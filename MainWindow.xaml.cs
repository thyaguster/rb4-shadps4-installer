using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Text.Json;
using Microsoft.Win32;
using RB4ShadPS4Installer.Models;
using RB4ShadPS4Installer.Services;

namespace RB4ShadPS4Installer;

public partial class MainWindow : Window
{
    private readonly InstallerService _installer = new();
    private CancellationTokenSource? _cancellation;
    private string? _lastAutoAddonDirectory;
    private InstalledGame? _detectedGame;
    private bool _settingsVisible;
    private bool _creditsVisible;
    private bool _isFirstRun;
    private bool _showUpdatePanel;
    private bool _showDeluxePanel;
    private InstallationLog? _installationLog;
    private string? _lastErrorDetails;
    private BackupInfo? _latestBackup;
    private AppRelease? _latestAppRelease;
    private bool _checkingForAppUpdates;
    private bool _downloadingAppUpdate;

    public MainWindow()
    {
        InitializeComponent();
        AppVersionText.Text = $"VERSION {GetType().Assembly.GetName().Version?.ToString(3)}";
        var saved = PreferencesService.Load();
        _isFirstRun = saved is null;
        ShadPs4InstallText.Text = PathUtility.Normalize(saved?.ShadPs4Directory ?? OfficialDownloads.DefaultShadPs4Directory);
        ShadPs4PathSourceText.Text = !string.IsNullOrWhiteSpace(saved?.ShadPs4Directory)
            ? "Remembered from this installer's saved settings."
            : "Using the installer-managed default folder.";
        UserDirectoryText.Text = PathUtility.Normalize(saved?.UserDirectory ?? ShadPs4Locator.DefaultUserDirectory);
        if (!string.IsNullOrWhiteSpace(saved?.AddonDirectory)) AddonDirectoryText.Text = PathUtility.Normalize(saved.AddonDirectory);
        else SetAutoAddonDirectory();
        PackageExtractorText.Text = PathUtility.Normalize(saved?.PackageExtractor ?? PackageExtractor.FindBundled() ?? string.Empty);
        LauncherShortcutCheck.IsChecked = saved?.MaintainLauncherShortcut == true;
        GameShortcutCheck.IsChecked = saved?.MaintainGameShortcut == true;
        AutoCheckForUpdatesCheck.IsChecked = saved?.AutoCheckForUpdates != false;
        var appEdition = AppUpdateService.CurrentEdition == AppEdition.Portable ? "Portable" : "Installed";
        AppUpdateCurrentText.Text = $"Current version {AppUpdateService.CurrentVersion.ToString(3)}  •  {appEdition} edition";
        GameLibraryText.Text = PathUtility.Normalize(saved?.GameLibrary ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "shadPS4 Games"));
        EuTitleRadio.IsChecked = saved?.TitleId == "CUSA02901";
        UsaTitleRadio.IsChecked = saved?.TitleId != "CUSA02901";
        RefreshLibraries();
        if (_isFirstRun) ShowFirstRun();
        else
        {
            ShowWorkflow(true);
            TryDetectInstalledGame(showNotFound: false);
            UpdateSettingsVisibility();
        }
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isFirstRun && AutoCheckForUpdatesCheck.IsChecked == true)
            await CheckForAppUpdatesAsync(showCurrentStatus: false, promptForUpdate: true);
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || DeluxeSourcePanel is null) return;
        DeluxeSourcePanel.Visibility = DeluxeModeRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateSettingsVisibility();
    }

    private void ShowFirstRun()
    {
        FirstRunPanel.Visibility = Visibility.Visible;
        SavedPathActionsPanel.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Collapsed;
        CreditsButton.Visibility = Visibility.Collapsed;
        ShowWorkflow(false);
        _settingsVisible = true;
        UpdateSettingsVisibility();
    }

    private void ShowWorkflow(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        EditionPanel.Visibility = visibility;
        GameFilesPanel.Visibility = visibility;
        DeluxeMaintenancePanel.Visibility = Visibility.Collapsed;
        AddonPanel.Visibility = visibility;
        BackupCheck.Visibility = visibility;
        InstallButton.Visibility = visibility;
    }

    private void FinishFirstRun(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserDirectoryText.Text) || string.IsNullOrWhiteSpace(GameLibraryText.Text))
        {
            ShowStatus("Choose a shadPS4 user folder and game library before continuing.", true);
            return;
        }
        SavePreferences();
        _isFirstRun = false;
        _settingsVisible = false;
        FirstRunPanel.Visibility = Visibility.Collapsed;
        SavedPathActionsPanel.Visibility = Visibility.Visible;
        SettingsButton.Visibility = Visibility.Visible;
        CreditsButton.Visibility = Visibility.Visible;
        ShowWorkflow(true);
        TryDetectInstalledGame(showNotFound: false);
        UpdateSettingsVisibility();
    }

    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        _creditsVisible = false;
        _settingsVisible = !_settingsVisible;
        if (!_settingsVisible) SavePreferences();
        UpdateSettingsVisibility();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_settingsVisible) SettingsBanner.BringIntoView();
            else MainScrollViewer.ScrollToTop();
        }));
    }

    private void ToggleCredits(object sender, RoutedEventArgs e)
    {
        _settingsVisible = false;
        _creditsVisible = !_creditsVisible;
        UpdateSettingsVisibility();
        Dispatcher.BeginInvoke(new Action(MainScrollViewer.ScrollToTop));
    }

    private void UpdateSettingsVisibility()
    {
        if (LocationsPanel is null || ExtractorPanel is null) return;
        var settingsView = !_isFirstRun && _settingsVisible;
        var creditsView = !_isFirstRun && _creditsVisible;
        var show = _isFirstRun || settingsView;
        LocationsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExtractorPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShortcutPanel.Visibility = settingsView ? Visibility.Visible : Visibility.Collapsed;
        AppUpdatePanel.Visibility = settingsView ? Visibility.Visible : Visibility.Collapsed;
        BackupPanel.Visibility = settingsView ? Visibility.Visible : Visibility.Collapsed;
        SettingsBanner.Visibility = settingsView ? Visibility.Visible : Visibility.Collapsed;
        CreditsPanel.Visibility = creditsView ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Content = settingsView ? "←  Back to installer" : "⚙  Settings";
        SettingsButton.Style = (Style)FindResource(settingsView ? "PrimaryButton" : "SecondaryButton");
        CreditsButton.Content = creditsView ? "←  Back to installer" : "♥  Credits";
        CreditsButton.Style = (Style)FindResource(creditsView ? "PrimaryButton" : "SecondaryButton");
        if (settingsView) RefreshLatestBackup();

        if (settingsView || creditsView)
        {
            ShowWorkflow(false);
            DetectedInstallPanel.Visibility = Visibility.Collapsed;
        }
        else if (!_isFirstRun)
        {
            ShowWorkflow(true);
            var hasDetectedGame = _detectedGame is not null;
            var contentMode = ContentModeRadio.IsChecked == true;
            var deluxeMode = DeluxeModeRadio.IsChecked == true;

            DetectedInstallPanel.Visibility = hasDetectedGame ? Visibility.Visible : Visibility.Collapsed;
            StandardModeRadio.Visibility = hasDetectedGame ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumn(DeluxeModeRadio, hasDetectedGame ? 0 : 1);
            Grid.SetColumn(ContentModeRadio, hasDetectedGame ? 1 : 2);
            Grid.SetColumnSpan(ContentModeRadio, hasDetectedGame ? 2 : 1);
            DeluxeModeTitle.Text = hasDetectedGame
                ? (_detectedGame!.DeluxeInstalled ? "Repair RB4 Deluxe" : "Install RB4 Deluxe")
                : "RB4 Deluxe";
            DeluxeModeDescription.Text = hasDetectedGame
                ? (_detectedGame!.DeluxeInstalled ? "Repair or update the existing Deluxe mod" : "Add Deluxe to the detected base game")
                : "Install or repair the Deluxe mod";

            BaseSourcePanel.Visibility = hasDetectedGame || contentMode ? Visibility.Collapsed : Visibility.Visible;
            RegionPanel.Visibility = hasDetectedGame ? Visibility.Collapsed : Visibility.Visible;
            ShowGameUpdateButton.Visibility = hasDetectedGame && contentMode ? Visibility.Visible : Visibility.Collapsed;
            ShowDeluxeUpdateButton.Visibility = hasDetectedGame && deluxeMode && _detectedGame!.DeluxeInstalled
                ? Visibility.Visible : Visibility.Collapsed;
            GameFilesTitle.Text = deluxeMode ? "RB4 Deluxe files" : contentMode ? "Game update" : "Game files";
            var hideGameUpdateFields = hasDetectedGame && contentMode && !_showUpdatePanel;
            var hideInstalledDeluxeFields = hasDetectedGame && deluxeMode && _detectedGame!.DeluxeInstalled && !_showDeluxePanel;
            GameFilesPanel.Visibility = hideGameUpdateFields || hideInstalledDeluxeFields
                ? Visibility.Collapsed : Visibility.Visible;
            DeluxeMaintenancePanel.Visibility = hideInstalledDeluxeFields ? Visibility.Visible : Visibility.Collapsed;
            AddonPanel.Visibility = deluxeMode ? Visibility.Collapsed : Visibility.Visible;
            InstallButton.IsEnabled = !hideInstalledDeluxeFields;

            if (hasDetectedGame)
            {
                var version = string.IsNullOrWhiteSpace(_detectedGame!.Version) ? "Version unavailable" : $"Version {_detectedGame.Version}";
                DeluxeMaintenanceGameText.Text = $"{_detectedGame.TitleId}  •  {version}  •  RB4DX detected";
                DeluxeMaintenanceTargetText.Text = Path.Combine(_detectedGame.GameLibrary, _detectedGame.TitleId);
            }
        }
    }

    private async void CheckForAppUpdates(object sender, RoutedEventArgs e) =>
        await CheckForAppUpdatesAsync(showCurrentStatus: true, promptForUpdate: true);

    private async Task CheckForAppUpdatesAsync(bool showCurrentStatus, bool promptForUpdate)
    {
        if (_checkingForAppUpdates || _downloadingAppUpdate) return;
        _checkingForAppUpdates = true;
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "Checking…";
        try
        {
            var release = await AppUpdateService.CheckAsync(CancellationToken.None);
            _latestAppRelease = release;
            if (AppUpdateService.IsNewer(release))
            {
                AppUpdateTitleText.Text = $"{release.Name} is available";
                AppUpdateReleaseNotesText.Text = FormatReleaseNotes(release.Notes);
                InstallAppUpdateButton.Content = AppUpdateService.CurrentEdition == AppEdition.Portable
                    ? "Download & restart"
                    : "Download & install";
                AppUpdateAvailablePanel.Visibility = Visibility.Visible;
                ShowStatus($"Application update {release.Tag} is available.", false, success: true);
                if (promptForUpdate)
                {
                    var prompt = new AppUpdateWindow(
                        release, AppUpdateService.CurrentEdition, FormatReleaseNotes(release.Notes)) { Owner = this };
                    var accepted = prompt.ShowDialog() == true;
                    if (prompt.DisableAutomaticChecks)
                    {
                        AutoCheckForUpdatesCheck.IsChecked = false;
                        SavePreferences();
                    }
                    if (accepted) await DownloadAndInstallAppUpdateAsync();
                }
            }
            else
            {
                AppUpdateAvailablePanel.Visibility = Visibility.Collapsed;
                if (showCurrentStatus)
                    ShowStatus($"RB4 shadPS4 Installer {AppUpdateService.CurrentVersion.ToString(3)} is up to date.", false, success: true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
        {
            if (showCurrentStatus) ShowStatus("Could not check for application updates: " + ex.Message, true);
        }
        finally
        {
            _checkingForAppUpdates = false;
            CheckForUpdatesButton.IsEnabled = true;
            CheckForUpdatesButton.Content = "Check for updates";
        }
    }

    private async void InstallAppUpdate(object sender, RoutedEventArgs e)
        => await DownloadAndInstallAppUpdateAsync();

    private async Task DownloadAndInstallAppUpdateAsync()
    {
        if (_latestAppRelease is null || _downloadingAppUpdate) return;
        _downloadingAppUpdate = true;
        CheckForUpdatesButton.IsEnabled = false;
        InstallAppUpdateButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        var progress = new Progress<InstallProgress>(value =>
        {
            InstallProgressBar.Value = value.Percent;
            ShowStatus(value.Message, false);
        });
        try
        {
            var update = await AppUpdateService.DownloadAsync(
                _latestAppRelease, AppUpdateService.CurrentEdition, progress, CancellationToken.None);
            SavePreferences();
            ShowStatus(AppUpdateService.CurrentEdition == AppEdition.Portable
                ? "Update verified. Restarting into the new portable version…"
                : "Update verified. Opening the Windows installer…", false, success: true);
            AppUpdateService.Launch(update);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ShowStatus("Application update failed: " + ex.Message, true);
            _downloadingAppUpdate = false;
            CheckForUpdatesButton.IsEnabled = true;
            InstallAppUpdateButton.IsEnabled = true;
            InstallProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenAppUpdateRelease(object sender, RoutedEventArgs e)
    {
        if (_latestAppRelease is null) return;
        Process.Start(new ProcessStartInfo(_latestAppRelease.PageUrl) { UseShellExecute = true });
    }

    private void AutoCheckForUpdatesChanged(object sender, RoutedEventArgs e) => SavePreferences();

    private static string FormatReleaseNotes(string notes)
    {
        var lines = notes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('#').Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("![", StringComparison.Ordinal))
            .Select(line => line.Replace("**", string.Empty).Replace("`", string.Empty))
            .Take(8);
        var text = string.Join(Environment.NewLine, lines);
        return text.Length <= 900 ? text : text[..897] + "…";
    }

    private void DetectInstalledGame(object sender, RoutedEventArgs e) => TryDetectInstalledGame(showNotFound: true);

    private bool TryDetectInstalledGame(bool showNotFound)
    {
        var games = ShadPs4Locator.FindInstalledGames(UserDirectoryText.Text.Trim(), EmptyToNull(GameLibraryText.Text));
        var preferredTitle = EuTitleRadio.IsChecked == true ? "CUSA02901" : "CUSA02084";
        var detected = games.FirstOrDefault(game => game.TitleId == preferredTitle) ?? games.FirstOrDefault();
        if (detected is null)
        {
            _detectedGame = null;
            AddonPanelTitle.Text = "DLC & custom songs";
            DetectedInstallPathText.Text = string.Empty;
            DetectedInstallPanel.Visibility = Visibility.Collapsed;
            UpdateSettingsVisibility();
            if (showNotFound) ShowStatus("No installed USA or EU Rock Band 4 game was found in the configured libraries.", true);
            return false;
        }

        _detectedGame = detected;
        _showUpdatePanel = false;
        _showDeluxePanel = false;
        GameLibraryText.Text = detected.GameLibrary;
        EuTitleRadio.IsChecked = detected.TitleId == "CUSA02901";
        UsaTitleRadio.IsChecked = detected.TitleId == "CUSA02084";
        DetectedInstallTitle.Text = detected.DeluxeInstalled
            ? "Existing Rock Band 4 Deluxe installation detected"
            : "Existing Rock Band 4 installation detected";
        var versionText = string.IsNullOrWhiteSpace(detected.Version) ? "Version unavailable" : $"Game v{detected.Version}";
        var deluxeText = detected.DeluxeInstalled ? "RB4DX installed" : "Standard RB4";
        var addonText = detected.AddonCount == 1 ? "1 add-on installed" : $"{detected.AddonCount:N0} add-ons installed";
        DetectedInstallText.Text = detected.DeluxeInstalled
            ? $"{detected.TitleId}  •  {versionText}  •  {deluxeText}  •  {addonText}"
            : $"{detected.TitleId}  •  {versionText}  •  {addonText}";
        DetectedInstallPathText.Text = Path.Combine(detected.GameLibrary, detected.TitleId);
        AddonPanelTitle.Text = detected.AddonCount == 0
            ? "DLC & custom songs"
            : $"DLC & custom songs  •  {detected.AddonCount:N0} installed";
        ContentModeRadio.IsChecked = true;
        DetectedInstallPanel.Visibility = Visibility.Visible;
        _settingsVisible = false;
        UpdateSettingsVisibility();
        SavePreferences();
        return true;
    }

    private void ChangeDetectedPaths(object sender, RoutedEventArgs e)
    {
        _settingsVisible = true;
        UpdateSettingsVisibility();
    }

    private void ShowGameUpdate(object sender, RoutedEventArgs e)
    {
        _showUpdatePanel = true;
        UpdateSettingsVisibility();
        Dispatcher.BeginInvoke(new Action(() => GameFilesPanel.BringIntoView()));
    }

    private void ShowDeluxeUpdate(object sender, RoutedEventArgs e)
    {
        _showDeluxePanel = true;
        UpdateSettingsVisibility();
        Dispatcher.BeginInvoke(new Action(() => GameFilesPanel.BringIntoView()));
    }

    private void UserDirectoryChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        RefreshLibraries();
        if (string.IsNullOrWhiteSpace(AddonDirectoryText.Text) ||
            string.Equals(AddonDirectoryText.Text, _lastAutoAddonDirectory, StringComparison.OrdinalIgnoreCase))
            SetAutoAddonDirectory();
    }

    private void SetAutoAddonDirectory()
    {
        _lastAutoAddonDirectory = ShadPs4Locator.FindAddonDirectory(UserDirectoryText.Text.Trim());
        AddonDirectoryText.Text = _lastAutoAddonDirectory;
    }

    private void RefreshLibraries()
    {
        var current = GameLibraryText.Text;
        var libraries = ShadPs4Locator.FindGameLibraries(UserDirectoryText.Text.Trim());
        if (!string.IsNullOrWhiteSpace(current)) GameLibraryText.Text = current;
        else if (libraries.Count > 0) GameLibraryText.Text = libraries[0];
    }

    private static string? PickFolder(string title, string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void BrowseUserDirectory(object sender, RoutedEventArgs e)
    {
        var value = PickFolder("Choose the shadPS4 user folder", UserDirectoryText.Text);
        if (value is not null) UserDirectoryText.Text = value;
    }

    private void BrowseShadPs4Install(object sender, RoutedEventArgs e)
    {
        var value = PickFolder("Choose the shadPS4 application folder", ShadPs4InstallText.Text);
        if (value is not null)
        {
            ShadPs4InstallText.Text = value;
            ShadPs4PathSourceText.Text = "Custom application folder selected manually.";
        }
    }

    private void SelectExistingShadPs4(object sender, RoutedEventArgs e)
    {
        var selected = PickFolder("Select your existing shadPS4 application folder", ShadPs4InstallText.Text);
        if (selected is null) return;
        var applicationDirectory = ShadPs4Locator.FindLauncherDirectory(selected);
        if (applicationDirectory is null)
        {
            ShowStatus("That folder does not contain shadPS4QtLauncher.exe or shadPS4.exe.", true);
            return;
        }

        ShadPs4InstallText.Text = applicationDirectory;
        ShadPs4PathSourceText.Text = "Existing shadPS4 installation selected. This choice will be remembered.";
        UserDirectoryText.Text = ShadPs4Locator.InferUserDirectory(applicationDirectory);
        _lastAutoAddonDirectory = ShadPs4Locator.FindAddonDirectory(UserDirectoryText.Text.Trim());
        AddonDirectoryText.Text = _lastAutoAddonDirectory;
        var libraries = ShadPs4Locator.FindGameLibraries(UserDirectoryText.Text.Trim());
        if (libraries.Count > 0) GameLibraryText.Text = libraries[0];
        ShowStatus("Existing shadPS4 installation detected. Its configured paths were filled in below.", false, success: true);
    }

    private void UseManagedShadPs4(object sender, RoutedEventArgs e)
    {
        ShadPs4InstallText.Text = OfficialDownloads.DefaultShadPs4Directory;
        ShadPs4PathSourceText.Text = "Using the installer-managed default folder.";
        SavePreferences();
        ShowStatus("The managed shadPS4 folder is now selected. Existing files were not changed.", false, success: true);
    }

    private void ResetSavedPaths(object sender, RoutedEventArgs e)
    {
        ShadPs4InstallText.Text = OfficialDownloads.DefaultShadPs4Directory;
        ShadPs4PathSourceText.Text = "Using the installer-managed default folder.";
        UserDirectoryText.Text = ShadPs4Locator.DefaultUserDirectory;
        GameLibraryText.Text = PathUtility.Normalize(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "shadPS4 Games"));
        SetAutoAddonDirectory();
        PackageExtractorText.Text = PackageExtractor.FindBundled() ?? string.Empty;
        _detectedGame = null;
        _showUpdatePanel = false;
        _showDeluxePanel = false;
        StandardModeRadio.IsChecked = true;
        SavePreferences();
        UpdateSettingsVisibility();
        ShowStatus("Saved paths were reset. No emulator, game, or add-on files were deleted.", false, success: true);
    }

    private void BrowseGameLibrary(object sender, RoutedEventArgs e)
    {
        var value = PickFolder("Choose the shadPS4 game library", GameLibraryText.Text);
        if (value is not null) GameLibraryText.Text = value;
    }

    private void BrowseAddonDirectory(object sender, RoutedEventArgs e)
    {
        var value = PickFolder("Choose the shadPS4 additional-content folder", AddonDirectoryText.Text);
        if (value is not null) AddonDirectoryText.Text = value;
    }

    private void BrowseBaseSource(object sender, RoutedEventArgs e) => SetFolder(BaseSourceText, "Choose the extracted RB4 base-game folder");
    private void BrowsePatchSource(object sender, RoutedEventArgs e) => SetFolder(PatchSourceText, "Choose the extracted RB4 2.21 update folder");
    private void BrowseDeluxeFolder(object sender, RoutedEventArgs e) => SetFolder(DeluxeSourceText, "Choose the extracted RB4 Deluxe folder");

    private static void SetFolder(TextBox target, string title)
    {
        var value = PickFolder(title, target.Text);
        if (value is not null) target.Text = value;
    }

    private void BrowseDeluxeZip(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the RB4 Deluxe ZIP", Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) DeluxeSourceText.Text = dialog.FileName;
    }

    private void BrowseBasePackage(object sender, RoutedEventArgs e) => SetPackage(BaseSourceText, "Choose the RB4 base-game PKG");
    private void BrowsePatchPackage(object sender, RoutedEventArgs e) => SetPackage(PatchSourceText, "Choose the RB4 2.21 update PKG");

    private static void SetPackage(TextBox target, string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = "PlayStation 4 packages (*.pkg)|*.pkg|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) target.Text = dialog.FileName;
    }

    private void BrowsePackageExtractor(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose pkg_extractor.exe",
            Filter = "Package extractor (pkg_extractor.exe)|pkg_extractor.exe|Executables (*.exe)|*.exe"
        };
        if (dialog.ShowDialog() == true) PackageExtractorText.Text = dialog.FileName;
    }

    private void AddAddonPackages(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose DLC or custom-song PKGs",
            Filter = "PlayStation 4 packages (*.pkg)|*.pkg|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames)
            if (!AddonSourcesList.Items.Cast<string>().Contains(file, StringComparer.OrdinalIgnoreCase))
                AddonSourcesList.Items.Add(file);
    }

    private void AddAddonFolder(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder("Choose an extracted DLC or custom-song folder");
        if (folder is not null && !AddonSourcesList.Items.Cast<string>().Contains(folder, StringComparer.OrdinalIgnoreCase))
            AddonSourcesList.Items.Add(folder);
    }

    private void RemoveAddonSources(object sender, RoutedEventArgs e)
    {
        foreach (var item in AddonSourcesList.SelectedItems.Cast<string>().ToArray())
            AddonSourcesList.Items.Remove(item);
    }

    private void RefreshBackups(object sender, RoutedEventArgs e) => RefreshLatestBackup();

    private void RefreshLatestBackup()
    {
        if (BackupsList is null) return;
        var backups = BackupService.FindAll(PathUtility.Normalize(UserDirectoryText.Text));
        BackupsList.ItemsSource = backups;
        BackupsList.SelectedIndex = backups.Count > 0 ? 0 : -1;
        NoBackupsText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBackupSelection();
    }

    private void BackupSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBackupSelection();

    private void UpdateBackupSelection()
    {
        _latestBackup = BackupsList?.SelectedItem as BackupInfo;
        var selected = _latestBackup is not null;
        RestoreLatestBackupButton.IsEnabled = selected;
        OpenBackupFolderButton.IsEnabled = selected;
        DeleteBackupButton.IsEnabled = selected;
    }

    private void OpenBackupFolder(object sender, RoutedEventArgs e)
    {
        if (_latestBackup is null || !Directory.Exists(_latestBackup.Root)) return;
        Process.Start(new ProcessStartInfo(_latestBackup.Root) { UseShellExecute = true });
    }

    private void DeleteSelectedBackup(object sender, RoutedEventArgs e)
    {
        if (_latestBackup is null) return;
        var backup = _latestBackup;
        var confirmation = new ConfirmationWindow(
            "Delete recovery backup",
            "Delete this backup?",
            $"{backup.DisplayTitle}\n{backup.Manifest.Files.Count:N0} affected file(s)\n\nThis removes only the retained backup. It does not change the current Rock Band 4 installation.",
            "Delete backup") { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        try
        {
            BackupService.Delete(backup, PathUtility.Normalize(UserDirectoryText.Text));
            ShowStatus("Recovery backup deleted. The current game installation was not changed.", false, success: true);
        }
        catch (Exception ex)
        {
            ShowStatus("Could not delete the recovery backup: " + ex.Message, true);
        }
        RefreshLatestBackup();
    }

    private async void RestoreLatestBackup(object sender, RoutedEventArgs e)
    {
        if (_latestBackup is null) return;
        if (!EnsureShadPs4Closed("restore an installation backup")) return;
        var confirmation = new RestoreBackupWindow(_latestBackup) { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        RestoreLatestBackupButton.IsEnabled = false;
        OpenBackupFolderButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        var progress = new Progress<InstallProgress>(value =>
        {
            InstallProgressBar.Value = value.Percent;
            ShowStatus(value.Message, false);
        });

        try
        {
            await BackupService.RestoreAsync(_latestBackup,
                [GameLibraryText.Text, UserDirectoryText.Text, AddonDirectoryText.Text],
                progress, CancellationToken.None);
            InstallProgressBar.Value = 100;
            ShowStatus("Backup restored successfully. Restart shadPS4 so it can rescan the restored files.", false, success: true);
        }
        catch (Exception ex)
        {
            _lastErrorDetails = ex.ToString();
            CopyErrorButton.Visibility = Visibility.Visible;
            StatusActionsPanel.Visibility = Visibility.Visible;
            ShowStatus("Backup restore failed: " + ex.Message, true);
        }
        finally
        {
            InstallProgressBar.Visibility = Visibility.Collapsed;
            RefreshLatestBackup();
        }
    }

    private void SourceDragOver(object sender, DragEventArgs e)
    {
        var kind = (sender as FrameworkElement)?.Tag as string;
        var paths = GetDroppedPaths(e);
        e.Effects = paths.Length == 1 && IsAllowedSource(paths[0], kind)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropSource(object sender, DragEventArgs e)
    {
        if (sender is not TextBox target) return;
        var paths = GetDroppedPaths(e);
        var kind = target.Tag as string;
        if (paths.Length != 1 || !IsAllowedSource(paths[0], kind))
        {
            var expected = kind == "zip" ? "one ZIP file or extracted folder" : "one PKG file or extracted folder";
            ShowStatus($"Drop {expected} into this field.", true);
            return;
        }

        target.Text = paths[0];
        ShowStatus($"Selected {Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar))}.", false, success: true);
    }

    private void AddonDragOver(object sender, DragEventArgs e)
    {
        var paths = GetDroppedPaths(e);
        e.Effects = paths.Length > 0 && paths.All(IsAllowedAddon)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropAddons(object sender, DragEventArgs e)
    {
        var paths = GetDroppedPaths(e);
        if (paths.Length == 0 || paths.Any(path => !IsAllowedAddon(path)))
        {
            ShowStatus("Drop only DLC/custom-song PKGs or extracted folders into the content list.", true);
            return;
        }

        var added = 0;
        foreach (var path in paths)
        {
            if (AddonSourcesList.Items.Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            AddonSourcesList.Items.Add(path);
            added++;
        }
        ShowStatus(added == 0 ? "Those content sources are already in the queue." : $"Added {added} content source{(added == 1 ? string.Empty : "s")} to the queue.", false, success: added > 0);
    }

    private static string[] GetDroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (string[])e.Data.GetData(DataFormats.FileDrop)
            : [];

    private static bool IsAllowedSource(string path, string? kind) =>
        Directory.Exists(path) || File.Exists(path) && string.Equals(
            Path.GetExtension(path), kind == "zip" ? ".zip" : ".pkg", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedAddon(string path) =>
        Directory.Exists(path) || File.Exists(path) &&
        string.Equals(Path.GetExtension(path), ".pkg", StringComparison.OrdinalIgnoreCase);

    private async void DownloadPackageExtractor(object sender, RoutedEventArgs e)
    {
        DownloadExtractorButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<InstallProgress>(value =>
        {
            InstallProgressBar.Value = value.Percent;
            ShowStatus(value.Message, false);
        });
        try
        {
            PackageExtractorText.Text = await PackageExtractor.DownloadOfficialAsync(progress, CancellationToken.None);
            InstallProgressBar.Value = 100;
            ShowStatus("Official pkg_extractor 1.1 downloaded and SHA-256 verified.", false);
        }
        catch (Exception ex)
        {
            ShowStatus("Could not download the package extractor: " + ex.Message, true);
        }
        finally
        {
            DownloadExtractorButton.IsEnabled = true;
            InstallProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void DownloadShadPs4(object sender, RoutedEventArgs e)
    {
        await DownloadManagedShadPs4Async(rb4Compatible: false);
    }

    private async void DownloadRb4ShadPs4(object sender, RoutedEventArgs e)
    {
        await DownloadManagedShadPs4Async(rb4Compatible: true);
    }

    private async Task DownloadManagedShadPs4Async(bool rb4Compatible)
    {
        if (!EnsureShadPs4Closed("update the managed shadPS4 application")) return;
        var destination = rb4Compatible
            ? OfficialDownloads.Rb4ShadPs4Directory
            : OfficialDownloads.DefaultShadPs4Directory;
        ShadPs4InstallText.Text = destination;
        ShadPs4PathSourceText.Text = rb4Compatible
            ? "Using the separate managed folder for the tested RB4-compatible build."
            : "Using the managed folder for the latest official shadPS4 release.";
        DownloadShadPs4Button.IsEnabled = false;
        DownloadRb4ShadPs4Button.IsEnabled = false;
        InstallButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        var progress = new Progress<InstallProgress>(value =>
        {
            InstallProgressBar.Value = value.Percent;
            ShowStatus(value.Message, false);
        });
        try
        {
            Rb4ShadPs4DownloadResult? rb4Result = null;
            if (rb4Compatible)
            {
                rb4Result = await OfficialDownloads.DownloadRb4ShadPs4Async(destination, progress, CancellationToken.None);
                ShadPs4InstallText.Text = rb4Result.Directory;
            }
            else ShadPs4InstallText.Text = await OfficialDownloads.DownloadShadPs4Async(destination, progress, CancellationToken.None);
            SavePreferences();
            if (_isFirstRun)
            {
                UserDirectoryText.Text = ShadPs4Locator.InferUserDirectory(ShadPs4InstallText.Text);
                SetAutoAddonDirectory();
            }
            RefreshDesktopShortcuts(showSuccess: false);
            var message = !rb4Compatible
                ? "Latest official shadPS4 downloaded and extracted successfully."
                : rb4Result!.Downloaded
                    ? $"RB4-compatible shadPS4 {rb4Result.Version} downloaded, SHA-256 verified, and installed successfully."
                    : $"RB4-compatible shadPS4 is already up to date ({rb4Result.Version}).";
            if (rb4Result?.UsedPinnedFallback == true)
                message += " The pinned known-good release was used because the latest release could not be verified.";
            ShowStatus(message, false, success: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not download {(rb4Compatible ? "the RB4-compatible" : "official")} shadPS4 build: {ex.Message}", true);
        }
        finally
        {
            DownloadShadPs4Button.IsEnabled = true;
            DownloadRb4ShadPs4Button.IsEnabled = true;
            InstallButton.IsEnabled = true;
            InstallProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void DownloadDeluxe(object sender, RoutedEventArgs e)
    {
        DownloadDeluxeButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        InstallProgressBar.Visibility = Visibility.Visible;
        InstallProgressBar.Value = 0;
        var progress = new Progress<InstallProgress>(value =>
        {
            InstallProgressBar.Value = value.Percent;
            ShowStatus(value.Message, false);
        });
        try
        {
            DeluxeSourceText.Text = await OfficialDownloads.DownloadDeluxeAsync(progress, CancellationToken.None);
            ShowStatus("Official RB4 Deluxe downloaded, extracted, and selected.", false, success: true);
        }
        catch (Exception ex)
        {
            ShowStatus("Could not download RB4 Deluxe: " + ex.Message, true);
        }
        finally
        {
            DownloadDeluxeButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            InstallProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private InstallRequest BuildRequest()
    {
        var titleId = EuTitleRadio.IsChecked == true ? "CUSA02901" : "CUSA02084";
        var mode = ContentModeRadio.IsChecked == true
            ? InstallMode.Content
            : DeluxeModeRadio.IsChecked == true ? InstallMode.Deluxe : InstallMode.Standard;
        return new InstallRequest(
            mode,
            PathUtility.Normalize(UserDirectoryText.Text),
            PathUtility.Normalize(GameLibraryText.Text),
            mode == InstallMode.Content ? null : EmptyToNull(PathUtility.Normalize(BaseSourceText.Text)),
            EmptyToNull(PathUtility.Normalize(PatchSourceText.Text)),
            EmptyToNull(PathUtility.Normalize(DeluxeSourceText.Text)),
            EmptyToNull(PathUtility.Normalize(PackageExtractorText.Text)),
            PathUtility.Normalize(AddonDirectoryText.Text),
            AddonSourcesList.Items.Cast<string>().Select(PathUtility.Normalize).ToArray(),
            titleId,
            BackupCheck.IsChecked == true);
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async void StartInstall(object sender, RoutedEventArgs e)
    {
        if (!EnsureShadPs4Closed("change Rock Band 4 files")) return;
        if (NormalizePathFields())
        {
            SavePreferences();
            ShowStatus("One or more malformed paths were repaired. Review the corrected paths, then click Review & Install again.", true);
            return;
        }
        SavePreferences();
        var request = BuildRequest();
        var validation = _installer.Validate(request);
        if (!validation.IsValid)
        {
            ShowStatus("Please fix the following:\n\n• " + string.Join("\n• ", validation.Errors), true);
            return;
        }

        var disk = GetDiskSpaceEstimate(request);
        if (disk is { } space && space.Free < space.Required)
        {
            ShowStatus($"Not enough free space on the game-library drive. {FormatBytes(space.Free)} is available, but approximately {FormatBytes(space.Required)} is required.", true);
            return;
        }

        var actionName = request.Mode switch
        {
            InstallMode.Deluxe => "RB4 Deluxe",
            InstallMode.Content => "additional RB4 content",
            _ => "standard RB4"
        };
        var gameFolder = Path.Combine(request.GameLibrary, request.TitleId);
        var updateFolder = request.PatchSource is null
            ? null : Path.Combine(request.GameLibrary, request.TitleId + "-patch");
        var addonFolder = request.AddonSources.Count == 0
            ? null : Path.Combine(request.AddonDirectory, request.TitleId);
        var preflight = BuildPreflightSummary(request);
        var review = new ReviewWindow(actionName, request.TitleId, gameFolder, updateFolder,
            addonFolder, request.AddonSources.Count, validation.Warnings, preflight) { Owner = this };
        if (review.ShowDialog() != true) return;

        _installationLog?.Dispose();
        _installationLog = null;
        _lastErrorDetails = null;
        CopyErrorButton.Visibility = Visibility.Collapsed;
        try
        {
            _installationLog = new InstallationLog(request);
            StatusActionsPanel.Visibility = Visibility.Visible;
            OpenLogButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StatusActionsPanel.Visibility = Visibility.Collapsed;
            Debug.WriteLine("Could not create installation log: " + ex);
        }

        SetInstalling(true);
        _cancellation = new CancellationTokenSource();
        var completed = false;
        var progress = new Progress<InstallProgress>(value =>
        {
            InstallProgressBar.Value = value.Percent;
            ShowStatus(value.Message, false);
            _installationLog?.Write($"{value.Percent:0.0}%  {value.Message}");
        });

        try
        {
            await _installer.InstallAsync(request, progress, _cancellation.Token);
            _installationLog?.Write("Installation completed successfully.");
            ShowStatus("Installation complete. Restart shadPS4 so it can rescan the installed content.", false, success: true);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            _installationLog?.Write("Installation cancelled by the user.");
            ShowStatus("Installation cancelled. File changes were rolled back and the previous installation was restored.", true);
        }
        catch (Exception ex)
        {
            _lastErrorDetails = ex.ToString();
            _installationLog?.Write(ex);
            StatusActionsPanel.Visibility = Visibility.Visible;
            CopyErrorButton.Visibility = Visibility.Visible;
            ShowStatus("Installation failed: " + ex.Message, true);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetInstalling(false);
        }

        if (completed)
        {
            string details;
            if (request.Mode == InstallMode.Content)
            {
                var installed = new List<string>();
                if (request.PatchSource is not null) installed.Add("the selected update");
                if (request.AddonSources.Count > 0) installed.Add($"{request.AddonSources.Count} additional-content item(s)");
                details = "Installed successfully: " + string.Join(" and ", installed) + ".";
            }
            else details = request.AddonSources.Count > 0
                    ? $"Rock Band 4 and {request.AddonSources.Count} additional-content item(s) were installed successfully."
                    : "Rock Band 4 was installed successfully.";
            var launcher = ShadPs4Locator.FindLauncherExecutable(ShadPs4InstallText.Text.Trim());
            new CompletionWindow(details, launcher, gameFolder) { Owner = this }.ShowDialog();
            TryDetectInstalledGame(showNotFound: false);
            RefreshDesktopShortcuts(showSuccess: false);
        }
    }

    private void CancelInstall(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void OpenInstallLog(object sender, RoutedEventArgs e)
    {
        if (_installationLog is null || !File.Exists(_installationLog.Path)) return;
        Process.Start(new ProcessStartInfo(_installationLog.Path) { UseShellExecute = true });
    }

    private void CopyErrorDetails(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastErrorDetails)) return;
        Clipboard.SetText(_lastErrorDetails);
        ShowStatus("Error details copied. The complete installation log is also available.", false, success: true);
    }

    private void SetInstalling(bool installing)
    {
        InstallButton.IsEnabled = true;
        InstallButton.Visibility = installing ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
        InstallProgressBar.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
        if (installing) InstallProgressBar.Value = 0;
    }

    private void ShowStatus(string message, bool error, bool success = false)
    {
        StatusPanel.Visibility = Visibility.Visible;
        if (success)
        {
            StatusPanel.Background = (System.Windows.Media.Brush)FindResource("SuccessPanelBrush");
            StatusPanel.BorderBrush = (System.Windows.Media.Brush)FindResource("SuccessBorderBrush");
        }
        else if (error)
        {
            StatusPanel.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 20, 25));
            StatusPanel.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(152, 47, 67));
        }
        else
        {
            StatusPanel.Background = (System.Windows.Media.Brush)FindResource("PanelBrush");
            StatusPanel.BorderBrush = (System.Windows.Media.Brush)FindResource("PanelBorderBrush");
        }
        StatusText.Text = message;
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void CreateDesktopShortcuts(object sender, RoutedEventArgs e)
    {
        SavePreferences();
        RefreshDesktopShortcuts(showSuccess: true);
    }

    private void OpenDesktopFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DesktopShortcutService.DesktopDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus("Could not open the Desktop folder: " + ex.Message, true);
        }
    }

    private void RefreshDesktopShortcuts(bool showSuccess)
    {
        var createLauncher = LauncherShortcutCheck.IsChecked == true;
        var createGame = GameShortcutCheck.IsChecked == true;
        if (!createLauncher && !createGame)
        {
            if (showSuccess) ShowStatus("Select at least one desktop shortcut to create.", true);
            return;
        }

        var created = new List<string>();
        var errors = new List<string>();
        var launcher = ShadPs4Locator.FindLauncherExecutable(ShadPs4InstallText.Text.Trim());
        if (launcher is null)
        {
            errors.Add("Select or download a valid shadPS4 application first");
        }
        else
        {
            if (createLauncher)
            {
                try
                {
                    DesktopShortcutService.CreateLauncherShortcut(launcher);
                    created.Add("RB4 shadPS4");
                }
                catch (Exception ex) { errors.Add("Launcher shortcut: " + ex.Message); }
            }

            if (createGame)
            {
                var preferredTitle = EuTitleRadio.IsChecked == true ? "CUSA02901" : "CUSA02084";
                var game = _detectedGame ?? ShadPs4Locator
                    .FindInstalledGames(UserDirectoryText.Text.Trim(), EmptyToNull(GameLibraryText.Text))
                    .FirstOrDefault(value => value.TitleId == preferredTitle);
                if (game is null)
                {
                    errors.Add("Install or detect Rock Band 4 before creating its direct-launch shortcut");
                }
                else
                {
                    try
                    {
                        DesktopShortcutService.CreateGameShortcut(
                            launcher, Path.Combine(game.GameLibrary, game.TitleId));
                        created.Add("Rock Band 4 direct launch");
                    }
                    catch (Exception ex) { errors.Add("Game shortcut: " + ex.Message); }
                }
            }
        }

        if (!showSuccess) return;
        if (errors.Count > 0)
        {
            var prefix = created.Count > 0 ? $"Created {string.Join(" and ", created)}, but:\n\n" : string.Empty;
            ShowStatus(prefix + "• " + string.Join("\n• ", errors), true);
        }
        else
        {
            ShowStatus($"Created or refreshed {string.Join(" and ", created)} on the Desktop.", false, success: true);
        }
    }

    private void SavePreferences()
    {
        try
        {
            NormalizePathFields();
            PreferencesService.Save(new InstallerPreferences(
                EmptyToNull(ShadPs4InstallText.Text),
                EmptyToNull(UserDirectoryText.Text),
                EmptyToNull(GameLibraryText.Text),
                EmptyToNull(AddonDirectoryText.Text),
                EmptyToNull(PackageExtractorText.Text),
                EuTitleRadio.IsChecked == true ? "CUSA02901" : "CUSA02084",
                LauncherShortcutCheck.IsChecked == true,
                GameShortcutCheck.IsChecked == true,
                AutoCheckForUpdatesCheck.IsChecked == true));
        }
        catch (Exception ex)
        {
            ShowStatus("Could not save installer settings: " + ex.Message, true);
        }
    }

    private bool NormalizePathFields()
    {
        var changed = false;
        foreach (var textBox in new[]
        {
            ShadPs4InstallText, UserDirectoryText, GameLibraryText, AddonDirectoryText,
            PackageExtractorText, BaseSourceText, PatchSourceText, DeluxeSourceText
        })
        {
            var normalized = PathUtility.Normalize(textBox.Text);
            if (string.Equals(textBox.Text, normalized, StringComparison.Ordinal)) continue;
            textBox.Text = normalized;
            changed = true;
        }

        for (var index = 0; index < AddonSourcesList.Items.Count; index++)
        {
            var original = AddonSourcesList.Items[index]?.ToString() ?? string.Empty;
            var normalized = PathUtility.Normalize(original);
            if (string.Equals(original, normalized, StringComparison.Ordinal)) continue;
            AddonSourcesList.Items[index] = normalized;
            changed = true;
        }
        return changed;
    }

    private IReadOnlyList<string> BuildPreflightSummary(InstallRequest request)
    {
        var results = new List<string>();
        var launcherFound = HasShadPs4Launcher(ShadPs4InstallText.Text);
        results.Add(launcherFound ? "✓ shadPS4 application found" : "! shadPS4 application not found; game files can still be prepared");

        var gameVersion = _detectedGame?.Version is { Length: > 0 } version ? $" v{version}" : string.Empty;
        results.Add(_detectedGame is null
            ? $"✓ Target region: {request.TitleId} (new installation)"
            : $"✓ Installed game detected: {_detectedGame.TitleId}{gameVersion}; {_detectedGame.AddonCount:N0} add-on(s)");

        var packageCount = new[] { request.BaseGameSource, request.PatchSource }
            .Where(PackageExtractor.IsPackage).Count() + request.AddonSources.Count(PackageExtractor.IsPackage);
        results.Add(packageCount == 0
            ? "✓ PKG extractor not required for the selected sources"
            : $"✓ PKG extractor ready for {packageCount} package{(packageCount == 1 ? string.Empty : "s")}");

        var sourceCount = new[] { request.BaseGameSource, request.PatchSource, request.DeluxeSource }
            .Count(source => !string.IsNullOrWhiteSpace(source)) + request.AddonSources.Count;
        results.Add($"✓ {sourceCount} installation source{(sourceCount == 1 ? string.Empty : "s")} validated for {request.TitleId}");

        var disk = GetDiskSpaceEstimate(request);
        if (disk is { } space)
        {
            results.Add(space.Free >= space.Required
                ? $"✓ Disk space: {FormatBytes(space.Free)} free ({FormatBytes(space.Required)} estimated minimum)"
                : $"! Low disk space: {FormatBytes(space.Free)} free; approximately {FormatBytes(space.Required)} may be required");
        }
        else
        {
            results.Add("! Available disk space could not be determined");
        }
        return results;
    }

    private static bool HasShadPs4Launcher(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            return File.Exists(Path.Combine(directory, "shadPS4QtLauncher.exe")) ||
                   File.Exists(Path.Combine(directory, "shadPS4.exe")) ||
                   Directory.EnumerateFiles(directory, "shadPS4*.exe", SearchOption.AllDirectories).Any();
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool EnsureShadPs4Closed(string action)
    {
        var running = ShadPs4ProcessGuard.FindRunning();
        if (running.Count == 0) return true;
        ShowStatus($"Close {string.Join(" and ", running)} before trying to {action}, then try again.", true);
        return false;
    }

    private static (long Free, long Required)? GetDiskSpaceEstimate(InstallRequest request)
    {
        try
        {
            var root = Path.GetPathRoot(request.GameLibrary);
            if (string.IsNullOrWhiteSpace(root)) return null;
            var free = new DriveInfo(root).AvailableFreeSpace;
            var sourceBytes = new[] { request.BaseGameSource, request.PatchSource, request.DeluxeSource }
                .Concat(request.AddonSources)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Sum(path => new FileInfo(path!).Length);
            var required = Math.Max(512L * 1024 * 1024, sourceBytes * 2);
            return (free, required);
        }
        catch (Exception) { return null; }
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.0} GB";

    protected override void OnClosed(EventArgs e)
    {
        _installationLog?.Dispose();
        try { SavePreferences(); } catch { }
        base.OnClosed(e);
    }
}
