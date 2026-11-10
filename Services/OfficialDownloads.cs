using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class OfficialDownloads
{
    private const string ShadReleasesApi = "https://api.github.com/repos/shadps4-emu/shadps4-qtlauncher/releases?per_page=10";
    private const string Rb4ShadPs4ReleasesApi = "https://api.github.com/repos/LlysiX/shadps4-rb4-qtlauncher/releases?per_page=10";
    private const string Rb4ShadPs4Url = "https://github.com/LlysiX/shadps4-rb4-qtlauncher/releases/download/rb4-2026-11-01/shadPS4-RB4-win64-2026-11-01-cc7f732.zip";
    private const string Rb4ShadPs4Digest = "sha256:601c8080d4912bfca5717afb5f084a4865d9b240b36ae874deeaed494568e771";
    private const string Rb4ShadPs4Tag = "rb4-2026-11-01";
    private const string Rb4ShadPs4Asset = "shadPS4-RB4-win64-2026-11-01-cc7f732.zip";
    private const string Rb4BuildMarker = ".rb4-shadps4-build.json";
    private const string OfficialBuildMarker = ".rb4-shadps4-official-build.json";
    private const string DeluxeUrl = "https://nightly.link/hmxmilohax/rock-band-4-deluxe/workflows/build/main/RB4DX-PS4.zip";

    public static string DefaultShadPs4Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RB4ShadPS4Installer", "shadPS4");

    public static string Rb4ShadPs4Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RB4ShadPS4Installer", "shadPS4-RB4");

    public static async Task<string> DownloadShadPs4Async(
        string destination,
        EmulatorReleaseOption? selectedRelease,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        using var client = CreateClient();
        progress.Report(new InstallProgress(1, selectedRelease is null
            ? "Finding the latest official shadPS4 Windows build"
            : $"Preparing pinned official shadPS4 {selectedRelease.Tag}"));
        var selected = selectedRelease ?? (await FetchReleaseOptionsAsync(client, EmulatorReleaseChannel.Official, token)).FirstOrDefault()
            ?? throw new InvalidOperationException("The official releases do not currently contain a Windows x64 ZIP.");
        if (selected.Channel != EmulatorReleaseChannel.Official)
            throw new InvalidOperationException("The selected release belongs to the wrong emulator channel.");

        var parent = Directory.GetParent(destination)?.FullName
            ?? throw new InvalidOperationException("The managed official emulator folder has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $"shadPS4-staging-{Guid.NewGuid():N}");
        try
        {
            await DownloadAndExtractAsync(client, selected.Url, selected.Digest, staging,
                $"Downloading official shadPS4 {selected.Tag}: {selected.Asset}", progress, token);
            var stagedLauncher = FindLauncher(staging)
                ?? throw new InvalidDataException("The downloaded shadPS4 archive did not contain its Windows launcher.");
            var launcherRelativePath = Path.GetRelativePath(staging, stagedLauncher);
            await File.WriteAllTextAsync(Path.Combine(staging, OfficialBuildMarker), JsonSerializer.Serialize(new
            {
                tag = selected.Tag,
                asset = selected.Asset,
                digest = selected.Digest,
                installedAtUtc = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true }), token);
            EmulatorRollbackService.ReplaceWithStaging(destination, staging);
            progress.Report(new InstallProgress(100, "Official shadPS4 downloaded, verified, and installed"));
            return Path.GetDirectoryName(Path.Combine(destination, launcherRelativePath))!;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static async Task<Rb4ShadPs4DownloadResult> DownloadRb4ShadPs4Async(
        string destination,
        EmulatorReleaseOption? selectedRelease,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        using var client = CreateClient();
        progress.Report(new InstallProgress(1, "Checking LlysiX for the latest RB4-compatible shadPS4 build"));
        var release = selectedRelease is null
            ? await FindLatestRb4BuildAsync(client, progress, token)
            : ToRb4Release(selectedRelease);

        var installed = ReadRb4BuildMarker(destination);
        var existingLauncher = FindLauncher(destination);
        if (existingLauncher is not null && installed is not null &&
            string.Equals(installed.Tag, release.Tag, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(installed.Digest, release.Digest, StringComparison.OrdinalIgnoreCase))
        {
            progress.Report(new InstallProgress(100, $"RB4-compatible shadPS4 is already current ({release.Tag})"));
            return new Rb4ShadPs4DownloadResult(Path.GetDirectoryName(existingLauncher)!, release.Tag, false, release.IsPinnedFallback);
        }

        var parent = Directory.GetParent(destination)?.FullName
            ?? throw new InvalidOperationException("The managed RB4 emulator folder has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $"shadPS4-RB4-staging-{Guid.NewGuid():N}");
        try
        {
            await DownloadAndExtractAsync(client, release.Url, release.Digest, staging,
                $"Downloading RB4-compatible shadPS4 {release.Tag}", progress, token);
            var stagedLauncher = FindLauncher(staging)
                ?? throw new InvalidDataException("The downloaded RB4-compatible archive did not contain its Windows launcher.");
            var launcherRelativePath = Path.GetRelativePath(staging, stagedLauncher);
            string? existingPortableUser = null;
            if (existingLauncher is not null)
            {
                try
                {
                    var candidate = ManagedEmulatorProfile.GetPortableUserDirectory(Path.GetDirectoryName(existingLauncher)!);
                    if (Directory.Exists(candidate)) existingPortableUser = candidate;
                }
                catch (FileNotFoundException) { }
            }
            await ManagedEmulatorProfile.PreparePortableUserAsync(
                Path.GetDirectoryName(stagedLauncher)!, existingPortableUser, progress, token);
            await File.WriteAllTextAsync(Path.Combine(staging, Rb4BuildMarker), JsonSerializer.Serialize(new
            {
                tag = release.Tag,
                asset = release.Asset,
                digest = release.Digest,
                installedAtUtc = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true }), token);

            EmulatorRollbackService.ReplaceWithStaging(destination, staging);

            progress.Report(new InstallProgress(100, $"RB4-compatible shadPS4 {release.Tag} installed and verified"));
            return new Rb4ShadPs4DownloadResult(
                Path.GetDirectoryName(Path.Combine(destination, launcherRelativePath))!,
                release.Tag, true, release.IsPinnedFallback);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task<Rb4Release> FindLatestRb4BuildAsync(
        HttpClient client,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        try
        {
            var selected = (await FetchReleaseOptionsAsync(client, EmulatorReleaseChannel.LlysixRb4, token)).FirstOrDefault()
                ?? throw new InvalidDataException("The latest RB4 release has no verified combined Windows x64 ZIP.");
            return ToRb4Release(selected);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
        {
            progress.Report(new InstallProgress(2, "Latest RB4 release could not be resolved; using the pinned known-good build"));
            return new Rb4Release(Rb4ShadPs4Tag, Rb4ShadPs4Asset, Rb4ShadPs4Url, Rb4ShadPs4Digest, true);
        }
    }

    public static async Task<IReadOnlyList<EmulatorReleaseOption>> GetReleaseOptionsAsync(
        EmulatorReleaseChannel channel, CancellationToken token)
    {
        using var client = CreateClient();
        return await FetchReleaseOptionsAsync(client, channel, token);
    }

    private static async Task<IReadOnlyList<EmulatorReleaseOption>> FetchReleaseOptionsAsync(
        HttpClient client, EmulatorReleaseChannel channel, CancellationToken token)
    {
        var url = channel == EmulatorReleaseChannel.LlysixRb4 ? Rb4ShadPs4ReleasesApi : ShadReleasesApi;
        using var response = await client.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return ParseReleaseOptions(document.RootElement, channel);
    }

    internal static IReadOnlyList<EmulatorReleaseOption> ParseReleaseOptions(JsonElement releases, EmulatorReleaseChannel channel)
    {
        var options = new List<EmulatorReleaseOption>();
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            var published = release.TryGetProperty("published_at", out var publishedValue) &&
                            DateTimeOffset.TryParse(publishedValue.GetString(), out var parsed) ? parsed : DateTimeOffset.MinValue;
            var assets = release.GetProperty("assets").EnumerateArray().Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? string.Empty,
                Url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                Digest = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null
            });
            var selected = channel == EmulatorReleaseChannel.LlysixRb4
                ? assets.FirstOrDefault(asset => asset.Name.StartsWith("shadPS4-RB4-win64-", StringComparison.OrdinalIgnoreCase) &&
                                                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                                asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
                : assets.Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                        asset.Name.Contains("win64", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(asset => asset.Name.Contains("qt", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (selected is null || string.IsNullOrWhiteSpace(selected.Url)) continue;
            options.Add(new(channel, tag, selected.Name, selected.Url, selected.Digest, published, false));
        }
        var ordered = options.OrderByDescending(option => option.PublishedAt).ToArray();
        if (ordered.Length > 0) ordered[0] = ordered[0] with { IsLatest = true };
        return ordered;
    }

    private static Rb4Release ToRb4Release(EmulatorReleaseOption selected)
    {
        if (selected.Channel != EmulatorReleaseChannel.LlysixRb4)
            throw new InvalidOperationException("The selected release belongs to the wrong emulator channel.");
        if (string.IsNullOrWhiteSpace(selected.Digest) || !selected.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected RB4 build does not provide a SHA-256 digest.");
        return new(selected.Tag, selected.Asset, selected.Url, selected.Digest, false);
    }

    private static Rb4BuildMarkerData? ReadRb4BuildMarker(string destination)
    {
        var marker = Path.Combine(destination, Rb4BuildMarker);
        if (!File.Exists(marker)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            var root = document.RootElement;
            return new Rb4BuildMarkerData(
                root.GetProperty("tag").GetString() ?? string.Empty,
                root.GetProperty("digest").GetString() ?? string.Empty);
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private static string? FindLauncher(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "shadPS4QtLauncher.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(directory, "shadPS4.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    public static async Task<string> DownloadDeluxeAsync(
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        using var client = CreateClient();
        var destination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RB4ShadPS4Installer", "downloads", "RB4DX",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        await DownloadAndExtractAsync(client, DeluxeUrl, null, destination,
            "Downloading official RB4 Deluxe for PS4/ShadPS4", progress, token);

        var goldHen = Directory.EnumerateDirectories(destination, "GoldHEN", SearchOption.AllDirectories).FirstOrDefault();
        if (goldHen is null) throw new InvalidDataException("The official RB4 Deluxe artifact did not contain a GoldHEN folder.");
        var sourceRoot = Directory.GetParent(goldHen)!.FullName;
        progress.Report(new InstallProgress(100, "Official RB4 Deluxe downloaded and extracted"));
        return sourceRoot;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RB4ShadPS4Installer/0.3");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task DownloadAndExtractAsync(
        HttpClient client,
        string url,
        string? expectedDigest,
        string destination,
        string label,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        var archive = Path.Combine(Path.GetTempPath(), $"rb4-official-{Guid.NewGuid():N}.zip");
        try
        {
            var downloadProgress = new InlineDownloadProgress(value =>
            {
                var percent = value.TotalBytes > 0 ? 85.0 * value.BytesReceived / value.TotalBytes.Value : 5;
                var status = value.Status.StartsWith("Download interrupted", StringComparison.Ordinal)
                    ? value.Status
                    : value.Resumed ? label + " (resuming)" : label;
                progress.Report(new InstallProgress(percent, status));
            });
            await ResumableDownloadService.DownloadAsync(client, url, archive, downloadProgress, token);

            if (!string.IsNullOrWhiteSpace(expectedDigest) && expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = File.OpenRead(archive);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                var expected = expectedDigest[7..];
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded archive failed SHA-256 verification.");
            }

            progress.Report(new InstallProgress(88, "Extracting downloaded archive"));
            Directory.CreateDirectory(destination);
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true), token);
            progress.Report(new InstallProgress(98, "Validating extracted files"));
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (File.Exists(archive + ".part")) File.Delete(archive + ".part");
        }
    }

    private sealed class InlineDownloadProgress(Action<DownloadTransferProgress> report) : IProgress<DownloadTransferProgress>
    {
        public void Report(DownloadTransferProgress value) => report(value);
    }

    private sealed record Rb4Release(string Tag, string Asset, string Url, string Digest, bool IsPinnedFallback);
    private sealed record Rb4BuildMarkerData(string Tag, string Digest);
}

internal sealed record Rb4ShadPs4DownloadResult(
    string Directory,
    string Version,
    bool Downloaded,
    bool UsedPinnedFallback);

internal enum EmulatorReleaseChannel { LlysixRb4, Official }

internal sealed record EmulatorReleaseOption(
    EmulatorReleaseChannel Channel,
    string Tag,
    string Asset,
    string Url,
    string? Digest,
    DateTimeOffset PublishedAt,
    bool IsLatest)
{
    public string Display => $"{Tag}{(IsLatest ? "  •  Latest" : string.Empty)}  •  {PublishedAt:yyyy-MM-dd}" +
                             (Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? "  •  SHA-256" : "  •  No published digest");
}

internal sealed record EmulatorReleaseSelection(string Display, EmulatorReleaseOption? Option);
