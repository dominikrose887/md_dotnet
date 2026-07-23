using System.IO;
using System.Windows;
using System.Windows.Threading;
using MdViewer.Services;

namespace MdViewer;

public partial class App : Application
{
    private bool _errorDialogShown;
    private string? _lastLoggedException;
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogger.Initialize();
        AppLogger.Info($"Startup args: [{string.Join(", ", e.Args)}]");

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        try
        {
            _singleInstance = new SingleInstanceService();
            if (!_singleInstance.IsPrimaryInstance)
            {
                AppLogger.Info("Secondary instance — forwarding args to primary");
                SingleInstanceService.TrySendToExisting(e.Args);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            AppLogger.Info("Creating MainWindow");
            var window = new MainWindow();
            MainWindow = window;
            AppLogger.Info("Showing MainWindow");
            window.Show();
            AppLogger.Info("MainWindow shown");

            _singleInstance.StartServer(Dispatcher, args =>
            {
                if (MainWindow is MainWindow main)
                    main.HandleExternalArgs(args);
            });

            if (e.Args.Length == 0) return;
            window.HandleExternalArgs(e.Args);
        }
        catch (Exception ex)
        {
            AppLogger.Fatal("Fatal error during startup", ex);
            MessageBox.Show(
                $"MdViewer failed to start.\n\n{ex.Message}\n\nLog:\n{AppLogger.CurrentLogPath}",
                "MdViewer Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info($"Application exit (code={e.ApplicationExitCode})");
        _singleInstance?.Dispose();
        AppLogger.Shutdown();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var signature = $"{e.Exception.GetType().FullName}:{e.Exception.Message}";
        if (!string.Equals(signature, _lastLoggedException, StringComparison.Ordinal))
        {
            _lastLoggedException = signature;
            AppLogger.Fatal("Unhandled UI (Dispatcher) exception", e.Exception);
        }

        if (e.Exception is InvalidOperationException &&
            e.Exception.Message.Contains("highlighting rule", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (MainWindow is MainWindow main)
                    main.DisableSyntaxHighlightingSafe();
            }
            catch (Exception recoveryEx)
            {
                AppLogger.Error("Failed to disable syntax highlighting after error", recoveryEx);
            }
        }

        if (_errorDialogShown) return;
        _errorDialogShown = true;

        try
        {
            MessageBox.Show(
                $"An unexpected error occurred.\n\n{e.Exception.Message}\n\nDetails were written to:\n{AppLogger.CurrentLogPath}",
                "MdViewer Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore secondary UI failures
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLogger.Fatal($"Unhandled AppDomain exception (IsTerminating={e.IsTerminating})", ex);
        else
            AppLogger.Fatal($"Unhandled AppDomain exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");

        AppLogger.Shutdown();
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
