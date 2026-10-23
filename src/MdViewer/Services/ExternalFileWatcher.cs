using System.IO;
using System.Text;
using System.Windows.Threading;

namespace MdViewer.Services;

public readonly record struct FileStamp(DateTime LastWriteTimeUtc, long Length)
{
    public static FileStamp Missing { get; } = new(DateTime.MinValue, -1);
}

public enum DiskChangeKind
{
    None,
    Modified,
    Deleted,
    /// <summary>The write is still landing. Check again shortly.</summary>
    Unstable,
    /// <summary>The file could not be read. Wait for the normal poll instead of spinning.</summary>
    Unavailable
}

public readonly record struct DiskChange(DiskChangeKind Kind, string? Content, FileStamp Stamp);

/// <summary>
/// Smart auto-reload policy: reload when the buffer matches the last save,
/// ask when the user has unsaved work. Newline differences alone are not a change,
/// because the editor normalizes line endings.
/// </summary>
public static class ExternalReloadPolicy
{
    public enum Decision
    {
        None,
        MarkClean,
        Reload,
        Prompt
    }

    public static Decision Decide(bool isDirty, string currentContent, string diskContent)
    {
        if (string.Equals(Normalize(currentContent), Normalize(diskContent), StringComparison.Ordinal))
            return isDirty ? Decision.MarkClean : Decision.None;

        // A UTF-16 or binary rewrite must not replace the open markdown on its own.
        if (diskContent.Contains('\0'))
            return Decision.Prompt;

        return isDirty ? Decision.Prompt : Decision.Reload;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}

/// <summary>
/// Watches open document directories. Each tab keeps its own baseline, so one
/// tab's save does not hide a change from another tab on the same path.
/// Renames are reported as a delete-plus-create at the original path: tools that
/// replace a file rename it aside first, and following that rename would open the backup.
/// </summary>
public sealed class ExternalFileWatcher : IDisposable
{
    private enum StatKind { Ok, Missing, Unavailable }

    private readonly record struct StatResult(StatKind Kind, FileStamp Stamp);

    private readonly Dispatcher _dispatcher;
    private readonly HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _directoryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer;
    private int _pollQueued;
    private bool _disposed;

    public ExternalFileWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, _) => RaisePoll();
        _timer.Start();
    }

    public event Action? PollRequested;

    public void SetWatchedPaths(IEnumerable<string> paths)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                wanted.Add(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                AppLogger.Warn($"Ignoring unwatchable path: {path}");
            }
        }

        _watched.Clear();
        foreach (var path in wanted)
            _watched.Add(path);

        SyncDirectoryWatchers(wanted);
    }

    /// <summary>
    /// Compare <paramref name="path"/> with one tab's baseline. A missing file is
    /// reported immediately; the caller decides how long it must stay missing.
    /// </summary>
    public DiskChange Observe(string path, FileStamp baseline)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DiskChange(DiskChangeKind.Unavailable, null, baseline);
        }

        var stat = Stat(full);
        if (stat.Kind == StatKind.Unavailable)
            return new DiskChange(DiskChangeKind.Unavailable, null, baseline);

        if (stat.Kind == StatKind.Missing)
        {
            if (baseline == FileStamp.Missing)
                return new DiskChange(DiskChangeKind.None, null, FileStamp.Missing);

            return new DiskChange(DiskChangeKind.Deleted, null, FileStamp.Missing);
        }

        var stamp = stat.Stamp;
        if (stamp == baseline)
            return new DiskChange(DiskChangeKind.None, null, stamp);

        // A write that just landed may still be flushing. Wait for the next pass.
        var age = DateTime.UtcNow - stamp.LastWriteTimeUtc;
        if (age < TimeSpan.FromMilliseconds(200) && age > TimeSpan.FromSeconds(-5))
            return new DiskChange(DiskChangeKind.Unstable, null, stamp);

        var content = TryRead(full);
        if (content is null)
            return new DiskChange(DiskChangeKind.Unavailable, null, stamp);

        var after = Stat(full);
        if (after.Kind == StatKind.Unavailable)
            return new DiskChange(DiskChangeKind.Unavailable, null, stamp);
        if (after.Kind != StatKind.Ok || after.Stamp != stamp)
            return new DiskChange(DiskChangeKind.Unstable, null, stamp);

        return new DiskChange(DiskChangeKind.Modified, content, stamp);
    }

    public static FileStamp CaptureStamp(string path)
    {
        return TryCaptureStamp(path, out var stamp) ? stamp : FileStamp.Missing;
    }

    public static bool TryCaptureStamp(string path, out FileStamp stamp)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            stamp = FileStamp.Missing;
            return false;
        }

        var stat = Stat(full);
        if (stat.Kind == StatKind.Unavailable)
        {
            stamp = default;
            return false;
        }

        stamp = stat.Kind == StatKind.Ok ? stat.Stamp : FileStamp.Missing;
        return true;
    }

    public static string? TryRead(string fullPath)
    {
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        PollRequested = null;
        foreach (var watcher in _directoryWatchers.Values)
            DisposeWatcher(watcher);
        _directoryWatchers.Clear();
    }

    private void SyncDirectoryWatchers(HashSet<string> paths)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    dirs.Add(dir);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // Skip a path we cannot map to a directory.
            }
        }

        foreach (var old in _directoryWatchers.Keys.Where(key => !dirs.Contains(key)).ToList())
        {
            DisposeWatcher(_directoryWatchers[old]);
            _directoryWatchers.Remove(old);
        }

        foreach (var dir in dirs)
        {
            if (_directoryWatchers.ContainsKey(dir)) continue;
            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                    Filter = "*"
                };
                watcher.Changed += OnWatcherEvent;
                watcher.Created += OnWatcherEvent;
                watcher.Deleted += OnWatcherEvent;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _directoryWatchers[dir] = watcher;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not watch directory {dir}: {ex.Message}");
            }
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e) => SchedulePoll();

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => SchedulePoll();

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        AppLogger.Warn($"File watcher reset: {e.GetException()?.Message}");
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RebuildWatchers));
    }

    private void RebuildWatchers()
    {
        if (_disposed) return;
        foreach (var watcher in _directoryWatchers.Values)
            DisposeWatcher(watcher);
        _directoryWatchers.Clear();
        SyncDirectoryWatchers(_watched.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private void SchedulePoll()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _pollQueued, 1) == 1) return;
        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _pollQueued, 0);
                if (_disposed) return;
                RaisePoll();
            }));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _pollQueued, 0);
            AppLogger.Debug($"File watcher poll skipped: {ex.Message}");
        }
    }

    private void RaisePoll()
    {
        if (_disposed) return;
        PollRequested?.Invoke();
    }

    private static StatResult Stat(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            info.Refresh();
            if (!info.Exists) return new StatResult(StatKind.Missing, FileStamp.Missing);
            return new StatResult(StatKind.Ok, new FileStamp(info.LastWriteTimeUtc, info.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StatResult(StatKind.Unavailable, default);
        }
    }

    private static void DisposeWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"File watcher dispose: {ex.Message}");
        }
    }
}
