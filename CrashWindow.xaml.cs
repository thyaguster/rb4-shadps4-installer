using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace RB4ShadPS4Installer;

public partial class CrashWindow : Window
{
    private readonly string? _reportPath;
    private readonly string _fallbackDetails;

    public CrashWindow(string? reportPath, Exception exception)
    {
        InitializeComponent();
        _reportPath = reportPath;
        _fallbackDetails = exception.ToString();
        ReportPathText.Text = string.IsNullOrWhiteSpace(reportPath)
            ? "The diagnostic report could not be written."
            : reportPath;
        OpenReportFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath);
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OpenReportFolder(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_reportPath)) return;
        TryAction(() => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_reportPath}\"")
        {
            UseShellExecute = true
        }));
    }

    private void CopyDetails(object sender, RoutedEventArgs e)
    {
        TryAction(() => Clipboard.SetText(
            !string.IsNullOrWhiteSpace(_reportPath) && File.Exists(_reportPath)
                ? File.ReadAllText(_reportPath)
                : _fallbackDetails));
    }

    private void TryAction(Action action)
    {
        try
        {
            action();
            ErrorText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Could not complete that action: " + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void CloseDialog(object sender, RoutedEventArgs e) => Close();
}
