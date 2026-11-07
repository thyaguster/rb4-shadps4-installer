using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class PackageExtractor
{
    private const string DownloadUrl = "https://github.com/AzaharPlus/shadPS4Plus/releases/download/PKG_EXTRACTOR_1_1/ShadPs4Plus-PkgExtractor-1.1-windows.zip";
    private const string DownloadSha256 = "A6A6DEE84C1283949245C868E580FF8D0049D0E73DF9DE3096460EE26C6F04D7";

    public static string UserToolsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RB4ShadPS4Installer", "tools", "pkg-extractor-1.1");

    public static bool IsPackage(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
        Path.GetExtension(path).Equals(".pkg", StringComparison.OrdinalIgnoreCase);

    public static string? FindBundled()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "pkg_extractor.exe"),
            Path.Combine(AppContext.BaseDirectory, "pkg_extractor.exe"),
            Path.Combine(UserToolsDirectory, "pkg_extractor.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? (Directory.Exists(UserToolsDirectory)
                ? Directory.EnumerateFiles(UserToolsDirectory, "pkg_extractor.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null);
    }

    public static async Task<string> DownloadOfficialAsync(IProgress<InstallProgress> progress, CancellationToken token)
    {
        Directory.CreateDirectory(UserToolsDirectory);
        var archive = Path.Combine(Path.GetTempPath(), $"rb4-pkg-extractor-{Guid.NewGuid():N}.zip");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RB4ShadPS4Installer/0.2");
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    received += read;
                    var percent = total > 0 ? 100.0 * received / total.Value : 0;
                    progress.Report(new InstallProgress(percent, "Downloading official PKG extractor"));
                }
            }

            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!hash.Equals(DownloadSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded extractor failed SHA-256 verification and was not installed.");
            }

            ZipFile.ExtractToDirectory(archive, UserToolsDirectory, overwriteFiles: true);
            var executable = FindBundled();
            if (executable is null) throw new InvalidDataException("The official archive did not contain pkg_extractor.exe.");
            return executable;
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    public static async Task ExtractAsync(
        string executable,
        string package,
        string destination,
        IProgress<InstallProgress> progress,
        double startPercent,
        double endPercent,
        CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(package);
        startInfo.ArgumentList.Add(destination);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var recentError = new Queue<string>();
        var currentPercent = startPercent;
        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            var message = args.Data.Trim();
            var match = Regex.Match(message, @"(?:Extracting\s+)?file\s+(\d+)\s+of\s+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, out var current) &&
                double.TryParse(match.Groups[2].Value, out var total) && total > 0)
            {
                currentPercent = startPercent + (endPercent - startPercent) * Math.Clamp(current / total, 0, 1);
            }
            progress.Report(new InstallProgress(currentPercent, "PKG extractor: " + message));
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            lock (recentError)
            {
                recentError.Enqueue(args.Data.Trim());
                while (recentError.Count > 8) recentError.Dequeue();
            }
        };

        try
        {
            if (!process.Start()) throw new InvalidOperationException("The package extractor could not be started.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string details;
            lock (recentError) details = string.Join(Environment.NewLine, recentError);
            throw new InvalidOperationException($"PKG extraction failed with exit code {process.ExitCode}." +
                (details.Length > 0 ? Environment.NewLine + details : string.Empty));
        }
        progress.Report(new InstallProgress(endPercent, $"Finished unpacking {Path.GetFileName(package)}"));
    }
}
