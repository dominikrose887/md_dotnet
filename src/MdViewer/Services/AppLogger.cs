using System.Diagnostics;
using System.IO;
using System.Text;

namespace MdViewer.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}

/// <summary>
/// Thread-safe file logger that flushes after every write so crash evidence is preserved.
/// Logs live under %LocalAppData%\MdViewer\logs\
/// </summary>
public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MdViewer",
        "logs");

    private static string? _sessionLogPath;
    private static StreamWriter? _writer;
    private static bool _initialized;

    public static string LogDirectoryPath => LogDirectory;

    public static string? CurrentLogPath => _sessionLogPath;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized) return;

            Directory.CreateDirectory(LogDirectory);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            _sessionLogPath = Path.Combine(LogDirectory, $"mdviewer-{stamp}.log");

            _writer = new StreamWriter(
                new FileStream(_sessionLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                Encoding.UTF8)
            {
                AutoFlush = true
            };

            _initialized = true;
        }

        Info("MdViewer starting");
        Info($"OS: {Environment.OSVersion}");
        Info($".NET: {Environment.Version}");
        Info($"Process: {Environment.ProcessPath}");
        Info($"PID: {Environment.ProcessId}");
        Info($"Log file: {_sessionLogPath}");
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);
    public static void Fatal(string message, Exception? ex = null) => Write(LogLevel.Fatal, message, ex);

    public static void Write(LogLevel level, string message, Exception? ex = null)
    {
        try
        {
            lock (Sync)
            {
                if (!_initialized)
                    Initialize();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}";
                _writer!.WriteLine(line);

                if (ex is not null)
                {
                    _writer.WriteLine(ex.ToString());
                    if (ex.InnerException is not null)
                        _writer.WriteLine($"Inner: {ex.InnerException}");
                }

                _writer.Flush();
            }

            // Mirror to debug output for Visual Studio / debugger sessions
            Trace.WriteLine($"[MdViewer] [{level}] {message}");
            if (ex is not null)
                Trace.WriteLine(ex);
        }
        catch
        {
            // Never throw from the logger
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            if (!_initialized) return;

            try
            {
                _writer?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] MdViewer shutting down");
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // Ignore
            }
            finally
            {
                _writer = null;
                _initialized = false;
            }
        }
    }

    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Error("Failed to open log folder", ex);
        }
    }
}
