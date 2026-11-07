using System.Windows;
using System.Windows.Input;
using RB4ShadPS4Installer.Services;

namespace RB4ShadPS4Installer;

public partial class RestoreBackupWindow : Window
{
    internal RestoreBackupWindow(BackupInfo backup)
    {
        InitializeComponent();
        TitleText.Text = backup.Manifest.TitleId;
        CreatedText.Text = backup.Manifest.CreatedAt.ToLocalTime().ToString("g");
        FileCountText.Text = backup.Manifest.Files.Count.ToString("N0");
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void ConfirmRestore(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelRestore(object sender, RoutedEventArgs e) => DialogResult = false;
}
