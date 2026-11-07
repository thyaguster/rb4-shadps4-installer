using System.Windows;
using System.Windows.Input;

namespace RB4ShadPS4Installer;

public partial class ReviewWindow : Window
{
    public ReviewWindow(string action, string titleId, string gameFolder, string? updateFolder,
        string? addonFolder, int additionalContentCount, IReadOnlyList<string> warnings,
        IReadOnlyList<string> preflight)
    {
        InitializeComponent();
        ActionText.Text = action;
        RegionText.Text = titleId == "CUSA02901" ? "Europe  •  CUSA02901" : "USA  •  CUSA02084";
        GameFolderText.Text = gameFolder;
        UpdateFolderText.Text = updateFolder ?? string.Empty;
        UpdateFolderLabel.Visibility = UpdateFolderText.Visibility = updateFolder is null ? Visibility.Collapsed : Visibility.Visible;
        AddonFolderText.Text = addonFolder ?? string.Empty;
        AddonFolderLabel.Visibility = AddonFolderText.Visibility = addonFolder is null ? Visibility.Collapsed : Visibility.Visible;
        ContentText.Text = additionalContentCount == 0 ? "None" : $"{additionalContentCount} item(s)";
        PreflightText.Text = string.Join("\n", preflight);
        if (warnings.Count > 0)
        {
            WarningsPanel.Visibility = Visibility.Visible;
            WarningsText.Text = "• " + string.Join("\n• ", warnings);
        }
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void ConfirmInstall(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelInstall(object sender, RoutedEventArgs e) => DialogResult = false;
}
