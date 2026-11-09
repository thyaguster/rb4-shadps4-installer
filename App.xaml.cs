using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RB4ShadPS4Installer.Services;

namespace RB4ShadPS4Installer;

public partial class App : Application
{
    private int _handlingFatalError;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += HandleDomainException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        base.OnStartup(e);
        var window = new MainWindow();
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _ = new WindowInteropHelper(window).EnsureHandle();
                window.Close();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }
            return;
        }

        MainWindow = window;
        window.Show();
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (Interlocked.Exchange(ref _handlingFatalError, 1) != 0)
        {
            Shutdown(1);
            return;
        }

        var report = TryCreateReport(e.Exception, "WPF dispatcher");
        try
        {
            new CrashWindow(report, e.Exception).ShowDialog();
        }
        catch
        {
            // The crash UI is best effort; the report has already been written when possible.
        }
        Shutdown(1);
    }

    private static void HandleDomainException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown fatal error");
        _ = TryCreateReport(exception, "AppDomain");
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _ = TryCreateReport(e.Exception, "Unobserved task");
        e.SetObserved();
    }

    private static string? TryCreateReport(Exception exception, string source)
    {
        try { return CrashReportService.Create(exception, source); }
        catch { return null; }
    }
}
