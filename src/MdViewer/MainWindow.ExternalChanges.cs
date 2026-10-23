using System.IO;
using System.Windows;
using System.Windows.Threading;
using MdViewer.Models;
using MdViewer.Services;

namespace MdViewer;

public partial class MainWindow
{
    private static readonly TimeSpan ExternalFreshRetry = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ExternalModifySettle = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ExternalRiskySettle = TimeSpan.FromMilliseconds(1200);

    private ExternalFileWatcher? _externalFiles;
    private readonly Dictionary<Guid, DiskChange> _diskNotices = new();
    private readonly Dictionary<Guid, FileStamp> _diskBaseline = new();
    private readonly DiskChangeCoalescer _diskCoalescer = new();
    private readonly HashSet<Guid> _holdAfterDelete = new();
    private DispatcherTimer? _externalFollowUp;
    private bool _externalFollowUpArmed;
    private DateTime _externalFollowUpDue;
    private bool _externalPollRunning;
    private Guid _shownNoticeTab;
    private DiskChangeKind _shownNoticeKind;
    private FileStamp _shownNoticeStamp;

    private void InitializeExternalFileWatch()
    {
        _externalFiles = new ExternalFileWatcher(Dispatcher);
        _externalFiles.PollRequested += PollExternalFileChanges;
        _externalFollowUp = new DispatcherTimer();
        _externalFollowUp.Tick += (_, _) =>
        {
            _externalFollowUp.Stop();
            _externalFollowUpArmed = false;
            PollExternalFileChanges();
        };
        Activated += (_, _) => PollExternalFileChanges();
        SyncExternalWatches();
    }

    private void DisposeExternalFileWatch()
    {
        _externalFollowUp?.Stop();
        _externalFollowUp = null;
        if (_externalFiles is null) return;
        _externalFiles.PollRequested -= PollExternalFileChanges;
        _externalFiles.Dispose();
        _externalFiles = null;
    }

    private void SyncExternalWatches()
    {
        _externalFiles?.SetWatchedPaths(Tabs.Select(tab => tab.FilePath).OfType<string>());
    }

    private void SeedDiskBaseline(DocumentTab tab)
    {
        if (string.IsNullOrEmpty(tab.FilePath) || _diskBaseline.ContainsKey(tab.Id))
            return;
        if (ExternalFileWatcher.TryCaptureStamp(tab.FilePath, out var stamp))
            _diskBaseline[tab.Id] = stamp;
    }

    private void AcceptDiskBaseline(DocumentTab tab, FileStamp stamp)
    {
        _diskBaseline[tab.Id] = stamp;
        _diskCoalescer.Forget(tab.Id);
    }

    private void NoteExternalFileWritten(string path)
    {
        if (string.IsNullOrEmpty(path) || _activeTab is null) return;

        var overwroteConflict = _diskNotices.ContainsKey(_activeTab.Id);
        if (ExternalFileWatcher.TryCaptureStamp(path, out var stamp))
            _diskBaseline[_activeTab.Id] = stamp;
        _diskCoalescer.Forget(_activeTab.Id);
        _holdAfterDelete.Remove(_activeTab.Id);
        ClearDiskNotice(_activeTab);
        SyncExternalWatches();

        if (overwroteConflict)
            StatusText.Text = $"Saved your version: {path}";
    }

    private void ForgetExternalTab(DocumentTab tab)
    {
        _diskNotices.Remove(tab.Id);
        _diskBaseline.Remove(tab.Id);
        _diskCoalescer.Forget(tab.Id);
        _holdAfterDelete.Remove(tab.Id);
        if (_activeTab == tab)
            ExternalChangeBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// True when the open buffer is the only copy left, because the file was
    /// deleted or the user chose Keep after a delete.
    /// </summary>
    private bool HasExternalDiskHold(DocumentTab tab) =>
        _holdAfterDelete.Contains(tab.Id)
        || _diskCoalescer.IsPending(tab.Id, DiskChangeKind.Deleted)
        || (_diskNotices.TryGetValue(tab.Id, out var notice) && notice.Kind == DiskChangeKind.Deleted);

    private void PollExternalFileChanges()
    {
        if (_externalFiles is null || _externalPollRunning || _isClosing || _pdfExportInProgress)
            return;

        _externalPollRunning = true;
        try
        {
            SyncExternalWatches();
            foreach (var tab in Tabs.ToList())
            {
                if (string.IsNullOrEmpty(tab.FilePath) || !Tabs.Contains(tab))
                    continue;

                try
                {
                    if (!_diskBaseline.ContainsKey(tab.Id))
                    {
                        SeedDiskBaseline(tab);
                        continue;
                    }

                    var observed = _externalFiles.Observe(tab.FilePath, _diskBaseline[tab.Id]);
                    DispatchDiskChange(tab, observed);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"External file check failed: {tab.FilePath}", ex);
                }
            }
        }
        finally
        {
            _externalPollRunning = false;
        }
    }

    private void DispatchDiskChange(DocumentTab tab, DiskChange observed)
    {
        if (observed.Kind == DiskChangeKind.None)
        {
            _diskCoalescer.Forget(tab.Id);
            return;
        }

        if (observed.Kind == DiskChangeKind.Unstable)
        {
            ScheduleExternalFollowUp(ExternalFreshRetry);
            return;
        }

        if (observed.Kind == DiskChangeKind.Unavailable)
            return;

        // Already asking about this exact disk state. Reloading again would only
        // flash the bar. If the buffer is clean again (undo back to the last save),
        // apply the disk copy now — that snapshot already finished settling.
        if (IsPendingNotice(tab, observed))
        {
            if (observed.Kind == DiskChangeKind.Deleted || NeedsExternalPrompt(tab))
                return;

            HandleExternalModify(tab, observed);
            return;
        }

        if (observed.Kind == DiskChangeKind.Modified)
        {
            var decision = ExternalReloadPolicy.Decide(
                NeedsExternalPrompt(tab),
                CurrentTabText(tab),
                observed.Content ?? string.Empty);
            // Touch, or a rewrite of the same text, does not need a settle delay.
            if (decision is ExternalReloadPolicy.Decision.None or ExternalReloadPolicy.Decision.MarkClean)
            {
                HandleExternalModify(tab, observed);
                return;
            }
        }

        var delay = SettleDelay(tab, observed);
        if (!_diskCoalescer.TryStabilize(tab.Id, observed, DateTime.UtcNow, delay, out var stable))
        {
            ScheduleExternalFollowUp(delay + TimeSpan.FromMilliseconds(40));
            return;
        }

        if (stable.Kind == DiskChangeKind.Deleted)
            HandleExternalDelete(tab, stable);
        else
            HandleExternalModify(tab, stable);
    }

    private bool IsPendingNotice(DocumentTab tab, DiskChange observed) =>
        _diskNotices.TryGetValue(tab.Id, out var notice)
        && notice.Kind == observed.Kind
        && notice.Stamp == observed.Stamp;

    private bool NeedsExternalPrompt(DocumentTab tab) =>
        IsEffectivelyDirty(tab) || _holdAfterDelete.Contains(tab.Id);

    private TimeSpan SettleDelay(DocumentTab tab, DiskChange change)
    {
        if (change.Kind == DiskChangeKind.Deleted)
            return ExternalRiskySettle;

        // A brief truncate-to-empty is how a lot of tools replace a file.
        if (string.IsNullOrEmpty(change.Content) && CurrentTabText(tab).Length > 0)
            return ExternalRiskySettle;

        return ExternalModifySettle;
    }

    private void ScheduleExternalFollowUp(TimeSpan delay)
    {
        if (_externalFollowUp is null || _isClosing) return;
        if (delay < TimeSpan.FromMilliseconds(40))
            delay = TimeSpan.FromMilliseconds(40);

        var due = DateTime.UtcNow + delay;
        if (_externalFollowUpArmed && _externalFollowUpDue <= due.AddMilliseconds(15))
            return;

        _externalFollowUpDue = due;
        _externalFollowUpArmed = true;
        _externalFollowUp.Stop();
        _externalFollowUp.Interval = delay;
        _externalFollowUp.Start();
    }

    private void HandleExternalDelete(DocumentTab tab, DiskChange change)
    {
        // Leave the saved baseline alone so a file that reappears before the user
        // answers can still auto-reload when they have no local edits.
        RememberDiskNotice(tab, change);
    }

    private void HandleExternalModify(DocumentTab tab, DiskChange change)
    {
        var disk = change.Content ?? string.Empty;
        var decision = ExternalReloadPolicy.Decide(NeedsExternalPrompt(tab), CurrentTabText(tab), disk);
        switch (decision)
        {
            case ExternalReloadPolicy.Decision.None:
                _holdAfterDelete.Remove(tab.Id);
                AcceptDiskBaseline(tab, change.Stamp);
                ClearDiskNotice(tab);
                break;
            case ExternalReloadPolicy.Decision.MarkClean:
                _holdAfterDelete.Remove(tab.Id);
                MarkTabClean(tab);
                AcceptDiskBaseline(tab, change.Stamp);
                ClearDiskNotice(tab);
                if (tab == _activeTab)
                    StatusText.Text = "File on disk matches your version";
                break;
            case ExternalReloadPolicy.Decision.Reload:
                ApplyExternalContent(tab, disk, change.Stamp, quiet: true);
                break;
            case ExternalReloadPolicy.Decision.Prompt:
                RememberDiskNotice(tab, change);
                break;
        }
    }

    private void ApplyExternalContent(DocumentTab tab, string diskContent, FileStamp stamp, bool quiet)
    {
        if (string.IsNullOrEmpty(tab.FilePath)) return;

        var isActive = tab == _activeTab;
        var oldText = isActive ? Editor.Text : tab.Content;
        var normalized = ExternalReloadPolicy.Normalize(diskContent);
        if (string.Equals(ExternalReloadPolicy.Normalize(oldText), normalized, StringComparison.Ordinal))
        {
            MarkTabClean(tab);
            _holdAfterDelete.Remove(tab.Id);
            AcceptDiskBaseline(tab, stamp);
            ClearDiskNotice(tab);
            return;
        }

        var anchor = EditorAnchor.Map(oldText, normalized, new EditorAnchor(
            isActive ? Editor.CaretOffset : tab.CaretOffset,
            isActive ? Editor.SelectionStart : tab.CaretOffset,
            isActive && Editor.SelectionLength > 0 ? Editor.SelectionLength : 0,
            isActive ? CaptureCurrentScrollLine() : tab.ScrollLine));

        _holdAfterDelete.Remove(tab.Id);

        if (isActive)
        {
            _scrollSync.BeginReload(anchor.ScrollLine);
            _suppressTabEditorSync = true;
            try
            {
                Editor.Text = normalized;
                var length = Editor.Document.TextLength;
                var caret = Math.Clamp(anchor.Caret, 0, length);
                if (anchor.SelectionLength > 0 && anchor.SelectionStart >= 0 && anchor.SelectionStart + anchor.SelectionLength <= length)
                    Editor.Select(anchor.SelectionStart, anchor.SelectionLength);
                else
                    Editor.CaretOffset = caret;

                tab.AdoptDiskContent(Editor.Text);
                tab.CaretOffset = Editor.CaretOffset;
                tab.ScrollLine = anchor.ScrollLine;
                _savedContent = Editor.Text;
                _isDirty = false;
            }
            finally
            {
                _suppressTabEditorSync = false;
            }

            var scrollLine = anchor.ScrollLine;
            Dispatcher.BeginInvoke(() =>
            {
                if (_activeTab != tab) return;
                SetEditorVisibleLine(scrollLine);
                _scrollSync.RememberLine(scrollLine);
            }, DispatcherPriority.Loaded);
        }
        else
        {
            tab.AdoptDiskContent(normalized);
            tab.CaretOffset = anchor.Caret;
            tab.ScrollLine = anchor.ScrollLine;
        }

        AcceptDiskBaseline(tab, stamp);
        ClearDiskNotice(tab);

        if (!isActive)
        {
            AppLogger.Info($"Auto-reloaded background tab: {tab.FilePath}");
            return;
        }

        _previewTimer.Stop();
        UpdateTitle();
        UpdateStats();
        RebuildCodeBlockHighlighting();
        _focusMode?.Refresh();
        StatusText.Text = quiet ? "Reloaded — changed on disk" : "Reloaded from disk";
        if (_findBarOpen && !string.IsNullOrEmpty(FindTextBox.Text))
            RunFind();
        _ = UpdatePreviewAsync(scrollLine: anchor.ScrollLine);
        AppLogger.Info($"{(quiet ? "Auto-reloaded" : "Reloaded")} {tab.FilePath}");
    }

    private void MarkTabClean(DocumentTab tab)
    {
        var current = CurrentTabText(tab);
        tab.AdoptDiskContent(current);
        if (tab == _activeTab)
        {
            _savedContent = current;
            _isDirty = false;
            UpdateTitle();
        }
    }

    private void RememberDiskNotice(DocumentTab tab, DiskChange change)
    {
        var isNew = !IsPendingNotice(tab, change);
        _diskNotices[tab.Id] = change;
        if (tab == _activeTab)
            ShowExternalChangeBar(tab, change);
        if (isNew)
            AppLogger.Info($"External change needs a choice: {tab.FilePath} ({change.Kind})");
    }

    private void ClearDiskNotice(DocumentTab tab)
    {
        _diskNotices.Remove(tab.Id);
        if (tab == _activeTab)
            ExternalChangeBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateExternalChangeBar()
    {
        if (_activeTab is not null && _diskNotices.TryGetValue(_activeTab.Id, out var change))
            ShowExternalChangeBar(_activeTab, change);
        else
            ExternalChangeBar.Visibility = Visibility.Collapsed;
    }

    private void ShowExternalChangeBar(DocumentTab tab, DiskChange change)
    {
        if (ExternalChangeBar.Visibility == Visibility.Visible
            && _shownNoticeTab == tab.Id
            && _shownNoticeKind == change.Kind
            && _shownNoticeStamp == change.Stamp)
            return;

        _shownNoticeTab = tab.Id;
        _shownNoticeKind = change.Kind;
        _shownNoticeStamp = change.Stamp;

        ExternalChangeText.Text = DescribeExternalChange(tab, change);
        ExternalChangeReloadButton.Visibility = change.Kind == DiskChangeKind.Deleted
            ? Visibility.Collapsed
            : Visibility.Visible;
        ExternalChangeBar.Visibility = Visibility.Visible;
    }

    private static string DescribeExternalChange(DocumentTab tab, DiskChange change)
    {
        if (change.Kind == DiskChangeKind.Deleted)
            return $"{tab.Title} was deleted on disk. Keep your current version?";

        if (change.Content?.Contains('\0') == true)
            return $"{tab.Title} changed on disk, but the new copy isn't plain text. Reload it, or keep your current version?";

        return $"{tab.Title} changed on disk. Reload it, or keep your current version?";
    }

    private string CurrentTabText(DocumentTab tab) =>
        tab == _activeTab ? Editor.Text : tab.Content;

    private static bool IsEffectivelyDirty(DocumentTab tab, string current) =>
        !string.Equals(
            ExternalReloadPolicy.Normalize(current),
            ExternalReloadPolicy.Normalize(tab.SavedContent),
            StringComparison.Ordinal);

    private bool IsEffectivelyDirty(DocumentTab tab) =>
        IsEffectivelyDirty(tab, CurrentTabText(tab));

    private void ExternalChangeReload_Click(object sender, RoutedEventArgs e)
    {
        var tab = _activeTab;
        if (tab?.FilePath is null) return;

        var content = ExternalFileWatcher.TryRead(tab.FilePath);
        var stamp = ExternalFileWatcher.CaptureStamp(tab.FilePath);
        if (content is null)
        {
            if (_diskNotices.TryGetValue(tab.Id, out var pending) && pending.Content is not null)
            {
                content = pending.Content;
                stamp = pending.Stamp;
            }
            else
            {
                StatusText.Text = File.Exists(tab.FilePath)
                    ? "Could not reload — the file is in use"
                    : "File is no longer on disk";
                return;
            }
        }

        ApplyExternalContent(tab, content, stamp, quiet: false);
    }

    private void ExternalChangeKeep_Click(object sender, RoutedEventArgs e)
    {
        var tab = _activeTab;
        if (tab?.FilePath is null) return;
        if (!_diskNotices.TryGetValue(tab.Id, out var change))
        {
            ExternalChangeBar.Visibility = Visibility.Collapsed;
            return;
        }

        AcceptDiskBaseline(tab, change.Stamp);
        if (change.Kind == DiskChangeKind.Deleted)
            _holdAfterDelete.Add(tab.Id);
        else
            _holdAfterDelete.Remove(tab.Id);
        ClearDiskNotice(tab);
        StatusText.Text = "Kept your version";
        AppLogger.Info($"Kept editor version after external change: {tab.FilePath}");
    }
}
