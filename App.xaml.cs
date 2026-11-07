using System.Windows;
using System.Windows.Interop;

namespace RB4ShadPS4Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
}
