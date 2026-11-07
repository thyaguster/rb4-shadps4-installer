using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace RB4ShadPS4Installer;

public partial class CompletionWindow : Window
{
    private readonly string? _launcher;
    private readonly string _gameFolder;

    public CompletionWindow(string message, string? launcher, string gameFolder)
    {
        InitializeComponent();
        _launcher = launcher;
        _gameFolder = gameFolder;
        MessageText.Text = message;
        LaunchShadPs4Button.IsEnabled = !string.IsNullOrWhiteSpace(_launcher) && File.Exists(_launcher);
        OpenGameFolderButton.IsEnabled = Directory.Exists(_gameFolder);
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CloseDialog(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OpenGameFolder(object sender, RoutedEventArgs e)
    {
        TryStart(_gameFolder, closeAfter: false);
    }

    private void LaunchShadPs4(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_launcher)) return;
        TryStart(_launcher, closeAfter: true);
    }

    private void TryStart(string path, bool closeAfter)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = File.Exists(path) ? Path.GetDirectoryName(path)! : path
            });
            if (closeAfter) DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Could not open it: " + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
