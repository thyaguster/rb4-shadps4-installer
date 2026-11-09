using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class AppUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/thyaguster/rb4-shadps4-installer/releases/latest";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static AppEdition CurrentEdition =>
        Path.GetFileName(Environment.ProcessPath ?? string.Empty)
            .Contains("-Portable-", StringComparison.OrdinalIgnoreCase)
            ? AppEdition.Portable
            : AppEdition.Installed;

    public static async Task<AppRelease> CheckAsync(CancellationToken token)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(LatestReleaseApi, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("The latest GitHub release has no version tag.");
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException($"The latest GitHub release tag '{tag}' is not a valid version.");

        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new AppReleaseAsset(
                asset.GetProperty("name").GetString() ?? string.Empty,
                asset.GetProperty("browser_download_url").GetString() ?? string.Empty))
            .ToArray();
        var versionText = version.ToString(3);
        var setup = FindAsset(assets, $"RB4ShadPS4Installer-Setup-v{versionText}.exe");
        var portable = FindAsset(assets, $"RB4ShadPS4Installer-Portable-v{versionText}.zip");
        var checksums = FindAsset(assets, "SHA256SUMS.txt");
        return new AppRelease(
            tag,
            version,
            root.GetProperty("name").GetString() ?? $"RB4 shadPS4 Installer {tag}",
            root.GetProperty("body").GetString() ?? "No release notes were provided.",
            root.GetProperty("html_url").GetString() ??
                $"https://github.com/thyaguster/rb4-shadps4-installer/releases/tag/{tag}",
            setup,
            portable,
            checksums);
    }

    public static bool IsNewer(AppRelease release) => release.Version > CurrentVersion;

    public static async Task<PreparedAppUpdate> DownloadAsync(
        AppRelease release,
        AppEdition edition,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        var asset = edition == AppEdition.Portable ? release.PortableAsset : release.SetupAsset;
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RB4ShadPS4Installer", "updates", release.Tag);
        Directory.CreateDirectory(updateDirectory);
        var destination = Path.Combine(updateDirectory, asset.Name);

        using var client = CreateClient();
        progress.Report(new InstallProgress(2, $"Downloading checksums for {release.Tag}"));
        var checksumText = await client.GetStringAsync(release.ChecksumsAsset.Url, token);
        var expectedHash = FindExpectedHash(checksumText, asset.Name);
        await DownloadFileAsync(client, asset.Url, destination, asset.Name, progress, token);

        progress.Report(new InstallProgress(92, "Verifying the downloaded update"));
        await using (var stream = File.OpenRead(destination))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                throw new InvalidDataException("The downloaded application update failed SHA-256 verification.");
            }
        }

        if (edition == AppEdition.Installed)
        {
            progress.Report(new InstallProgress(100, "Update verified and ready to install"));
            return new PreparedAppUpdate(edition, release, destination, destination);
        }

        var extractDirectory = Path.Combine(updateDirectory, "portable");
        if (Directory.Exists(extractDirectory)) Directory.Delete(extractDirectory, recursive: true);
        Directory.CreateDirectory(extractDirectory);
        progress.Report(new InstallProgress(95, "Preparing the portable update"));
        ZipFile.ExtractToDirectory(destination, extractDirectory, overwriteFiles: true);
        var executable = Directory.EnumerateFiles(
                extractDirectory, "RB4ShadPS4Installer-Portable-v*.exe", SearchOption.AllDirectories)
            .SingleOrDefault()
            ?? throw new InvalidDataException("The portable update archive did not contain its versioned executable.");
        progress.Report(new InstallProgress(100, "Portable update verified and ready to restart"));
        return new PreparedAppUpdate(edition, release, destination, executable);
    }

    public static void Launch(PreparedAppUpdate update)
    {
        if (update.Edition == AppEdition.Installed)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = update.PreparedExecutable,
                UseShellExecute = true
            });
            return;
        }

        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The running portable executable could not be identified.");
        var currentDirectory = Path.GetDirectoryName(currentExecutable)!;
        VerifyDirectoryIsWritable(currentDirectory);
        var targetExecutable = Path.Combine(
            currentDirectory,
            Path.GetFileName(update.PreparedExecutable));
        var helper = Path.Combine(Path.GetTempPath(), $"rb4-app-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(helper, PortableUpdateScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-WindowStyle");
        start.ArgumentList.Add("Hidden");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(helper);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(update.PreparedExecutable);
        start.ArgumentList.Add(targetExecutable);
        start.ArgumentList.Add(currentExecutable);
        Process.Start(start);
    }

    private static void VerifyDirectoryIsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".rb4-update-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, string.Empty);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new IOException(
                "The Portable folder is not writable. Move the app to a folder you can modify, then try the update again.", ex);
        }
        finally
        {
            if (File.Exists(probe)) File.Delete(probe);
        }
    }

    private static AppReleaseAsset FindAsset(IEnumerable<AppReleaseAsset> assets, string name) =>
        assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"The latest release does not contain '{name}'.");

    private static string FindExpectedHash(string checksums, string assetName)
    {
        foreach (var line in checksums.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].Equals(assetName, StringComparison.OrdinalIgnoreCase) &&
                parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
                return parts[0];
        }
        throw new InvalidDataException($"SHA256SUMS.txt does not contain a valid digest for '{assetName}'.");
    }

    private static async Task DownloadFileAsync(
        HttpClient client,
        string url,
        string destination,
        string name,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        var transferProgress = new InlineDownloadProgress(value =>
        {
            var percent = value.TotalBytes > 0 ? 5 + 85.0 * value.BytesReceived / value.TotalBytes.Value : 10;
            var status = value.Status.StartsWith("Download interrupted", StringComparison.Ordinal)
                ? value.Status
                : value.Resumed ? $"Resuming {name}" : $"Downloading {name}";
            progress.Report(new InstallProgress(percent, status));
        });
        await ResumableDownloadService.DownloadAsync(client, url, destination, transferProgress, token);
    }

    private sealed class InlineDownloadProgress(Action<DownloadTransferProgress> report) : IProgress<DownloadTransferProgress>
    {
        public void Report(DownloadTransferProgress value) => report(value);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"RB4ShadPS4Installer/{CurrentVersion.ToString(3)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private const string PortableUpdateScript = """
param(
    [int]$ProcessId,
    [string]$SourceExecutable,
    [string]$TargetExecutable,
    [string]$PreviousExecutable
)
$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $SourceExecutable -Destination $TargetExecutable -Force
    Start-Process -FilePath $TargetExecutable
    if (-not [string]::Equals($PreviousExecutable, $TargetExecutable, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $PreviousExecutable)) {
        Remove-Item -LiteralPath $PreviousExecutable -Force
    }
}
catch {
    $logDirectory = Join-Path $env:LOCALAPPDATA 'RB4ShadPS4Installer\updates'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $_ | Out-File -LiteralPath (Join-Path $logDirectory 'update-helper-error.log') -Encoding utf8
    if (Test-Path -LiteralPath $PreviousExecutable) {
        Start-Process -FilePath $PreviousExecutable
    }
}
finally {
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
""";
}

internal enum AppEdition
{
    Installed,
    Portable
}

internal sealed record AppReleaseAsset(string Name, string Url);

internal sealed record AppRelease(
    string Tag,
    Version Version,
    string Name,
    string Notes,
    string PageUrl,
    AppReleaseAsset SetupAsset,
    AppReleaseAsset PortableAsset,
    AppReleaseAsset ChecksumsAsset);

internal sealed record PreparedAppUpdate(
    AppEdition Edition,
    AppRelease Release,
    string DownloadedAsset,
    string PreparedExecutable);
