using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class OfficialDownloads
{
    private const string ShadReleasesApi = "https://api.github.com/repos/shadps4-emu/shadps4-qtlauncher/releases?per_page=10";
    private const string Rb4ShadPs4ReleaseApi = "https://api.github.com/repos/LlysiX/shadps4-rb4-qtlauncher/releases/latest";
    private const string Rb4ShadPs4Url = "https://github.com/LlysiX/shadps4-rb4-qtlauncher/releases/download/rb4-2026-11-01/shadPS4-RB4-win64-2026-11-01-cc7f732.zip";
    private const string Rb4ShadPs4Digest = "sha256:601c8080d4912bfca5717afb5f084a4865d9b240b36ae874deeaed494568e771";
    private const string Rb4ShadPs4Tag = "rb4-2026-11-01";
    private const string Rb4ShadPs4Asset = "shadPS4-RB4-win64-2026-11-01-cc7f732.zip";
    private const string Rb4BuildMarker = ".rb4-shadps4-build.json";
    private const string DeluxeUrl = "https://nightly.link/hmxmilohax/rock-band-4-deluxe/workflows/build/main/RB4DX-PS4.zip";

    public static string DefaultShadPs4Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RB4ShadPS4Installer", "shadPS4");

    public static string Rb4ShadPs4Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RB4ShadPS4Installer", "shadPS4-RB4");

    public static async Task<string> DownloadShadPs4Async(
        string destination,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        using var client = CreateClient();
        progress.Report(new InstallProgress(1, "Finding the latest official shadPS4 Windows build"));
        using var response = await client.GetAsync(ShadReleasesApi, token);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: token);

        var candidates = document.RootElement.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean())
            .SelectMany(release => release.GetProperty("assets").EnumerateArray())
            .Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? string.Empty,
                Url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                Digest = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null
            })
            .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            asset.Name.Contains("win64", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => asset.Name.Contains("qt", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selected = candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("The official release does not currently contain a Windows x64 ZIP.");

        await DownloadAndExtractAsync(client, selected.Url, selected.Digest, destination,
            $"Downloading official shadPS4: {selected.Name}", progress, token);
        var launcher = Directory.EnumerateFiles(destination, "shadPS4QtLauncher.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(destination, "shadPS4.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (launcher is null) throw new InvalidDataException("The downloaded shadPS4 archive did not contain its Windows launcher.");
        progress.Report(new InstallProgress(100, "Official shadPS4 downloaded and extracted"));
        return Path.GetDirectoryName(launcher)!;
    }

    public static async Task<Rb4ShadPs4DownloadResult> DownloadRb4ShadPs4Async(
        string destination,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        using var client = CreateClient();
        progress.Report(new InstallProgress(1, "Checking LlysiX for the latest RB4-compatible shadPS4 build"));
        var release = await FindLatestRb4BuildAsync(client, progress, token);

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
        var previous = destination + "-previous";
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

            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            if (Directory.Exists(destination)) Directory.Move(destination, previous);
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (!Directory.Exists(destination) && Directory.Exists(previous)) Directory.Move(previous, destination);
                throw;
            }

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
            using var response = await client.GetAsync(Rb4ShadPs4ReleaseApi, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString()
                ?? throw new InvalidDataException("The latest RB4 release has no tag.");
            var asset = root.GetProperty("assets").EnumerateArray()
                .Select(value => new
                {
                    Name = value.GetProperty("name").GetString() ?? string.Empty,
                    Url = value.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    Digest = value.TryGetProperty("digest", out var digest) ? digest.GetString() : null
                })
                .FirstOrDefault(value => value.Name.StartsWith("shadPS4-RB4-win64-", StringComparison.OrdinalIgnoreCase) &&
                                         value.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The latest RB4 release has no combined Windows x64 ZIP.");
            if (string.IsNullOrWhiteSpace(asset.Digest) || !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The latest RB4 Windows build does not provide a SHA-256 digest.");
            return new Rb4Release(tag, asset.Name, asset.Url, asset.Digest, false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
        {
            progress.Report(new InstallProgress(2, "Latest RB4 release could not be resolved; using the pinned known-good build"));
            return new Rb4Release(Rb4ShadPs4Tag, Rb4ShadPs4Asset, Rb4ShadPs4Url, Rb4ShadPs4Digest, true);
        }
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
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[131072];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    received += read;
                    var percent = total > 0 ? 85.0 * received / total.Value : 5;
                    progress.Report(new InstallProgress(percent, label));
                }
            }

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
        }
    }

    private sealed record Rb4Release(string Tag, string Asset, string Url, string Digest, bool IsPinnedFallback);
    private sealed record Rb4BuildMarkerData(string Tag, string Digest);
}

internal sealed record Rb4ShadPs4DownloadResult(
    string Directory,
    string Version,
    bool Downloaded,
    bool UsedPinnedFallback);
