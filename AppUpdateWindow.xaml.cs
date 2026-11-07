using System.Windows;
using System.Windows.Input;
using RB4ShadPS4Installer.Services;

namespace RB4ShadPS4Installer;

public partial class AppUpdateWindow : Window
{
    public bool DisableAutomaticChecks => DisableAutomaticChecksCheck.IsChecked == true;

    internal AppUpdateWindow(AppRelease release, AppEdition edition, string releaseNotes)
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppUpdateService.CurrentVersion.ToString(3)}  →  {release.Version.ToString(3)}";
        ReleaseNotesText.Text = releaseNotes;
        var portable = edition == AppEdition.Portable;
        InstallUpdateButton.Content = portable ? "Update & restart" : "Open installer";
        UpdateActionText.Text = portable
            ? "After you confirm, the app downloads the Portable ZIP. A hidden one-time helper waits for this window to close, places the new version beside it, removes the old executable, and starts the update."
            : "After you confirm, the app downloads and opens the new Windows Setup. The installer updates the existing application while keeping your saved paths and settings.";
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void AcceptUpdate(object sender, RoutedEventArgs e) => DialogResult = true;
    private void UpdateLater(object sender, RoutedEventArgs e) => DialogResult = false;
}
