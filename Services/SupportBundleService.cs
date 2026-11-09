using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class SupportBundleService
{
    private const int MaximumLogBytes = 4 * 1024 * 1024;
    private static readonly Regex Ipv4Address = new(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])", RegexOptions.Compiled);
    private static readonly Regex EmailAddress = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Create(
        string destination,
        InstallationHealthReport report,
        string applicationDirectory,
        string userDirectory,
        string? gameLibrary,
        string addonDirectory,
        string? packageExtractor)
    {
        var replacements = BuildReplacements(applicationDirectory, userDirectory, gameLibrary, addonDirectory, packageExtractor);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        AddText(archive, "README.txt", """
            RB4 shadPS4 Installer support bundle

            This archive was created locally at the user's request.
            Configurations, account data, keys, saves, firmware modules, game files,
            PKGs, DLC/custom-song files, and backup contents are intentionally excluded.
            Configured local paths and the Windows user name are replaced with labels.
            When available, the latest sanitized crash report is included for diagnosis.
            Review the text files before sharing the archive.
            """);
        AddText(archive, "health-report.txt", Sanitize(InstallationHealthService.Format(report), replacements));
        AddText(archive, "environment.txt", BuildEnvironmentReport());

        AddSanitizedLog(archive, Path.Combine(userDirectory, "log", "shad_log.txt"), "logs/shad_log.txt", replacements);
        var installerLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RB4ShadPS4Installer", "logs");
        var installerLog = Directory.Exists(installerLogDirectory)
            ? Directory.EnumerateFiles(installerLogDirectory, "install-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (installerLog is not null)
            AddSanitizedLog(archive, installerLog, "logs/latest-installer.log", replacements);
        var crashLog = Directory.Exists(installerLogDirectory)
            ? Directory.EnumerateFiles(installerLogDirectory, "crash-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (crashLog is not null)
            AddSanitizedLog(archive, crashLog, "logs/latest-crash.log", replacements);
    }

    private static string BuildEnvironmentReport()
    {
        var version = typeof(SupportBundleService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        return $"""
            Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}
            Installer version: {version}
            Operating system: {RuntimeInformation.OSDescription}
            OS architecture: {RuntimeInformation.OSArchitecture}
            Process architecture: {RuntimeInformation.ProcessArchitecture}
            .NET runtime: {RuntimeInformation.FrameworkDescription}
            """;
    }

    private static IReadOnlyList<(string Value, string Replacement)> BuildReplacements(
        string applicationDirectory,
        string userDirectory,
        string? gameLibrary,
        string addonDirectory,
        string? packageExtractor)
    {
        var values = new List<(string?, string)>
        {
            (applicationDirectory, "<EMULATOR>"),
            (userDirectory, "<SHADPS4_USER>"),
            (gameLibrary, "<GAME_LIBRARY>"),
            (addonDirectory, "<ADDON_DIRECTORY>"),
            (packageExtractor is null ? null : Path.GetDirectoryName(packageExtractor), "<PKG_EXTRACTOR>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<USER_PROFILE>"),
            (Environment.UserName, "<WINDOWS_USER>")
        };
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .Select(item => (item.Item1!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), item.Item2))
            .DistinctBy(item => item.Item1, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Item1.Length)
            .ToArray();
    }

    private static void AddSanitizedLog(
        ZipArchive archive,
        string source,
        string entryName,
        IReadOnlyList<(string Value, string Replacement)> replacements)
    {
        if (!File.Exists(source)) return;
        AddText(archive, entryName, Sanitize(ReadLogTail(source), replacements));
    }

    private static string ReadLogTail(string source)
    {
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumLogBytes) stream.Seek(-MaximumLogBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return stream.Length > MaximumLogBytes
            ? "[Earlier log content omitted; last 4 MiB included.]" + Environment.NewLine + text
            : text;
    }

    private static string Sanitize(string text, IReadOnlyList<(string Value, string Replacement)> replacements)
    {
        foreach (var (value, replacement) in replacements)
            text = text.Replace(value, replacement, StringComparison.OrdinalIgnoreCase);
        text = EmailAddress.Replace(text, "<EMAIL>");
        return Ipv4Address.Replace(text, "<IP_ADDRESS>");
    }

    private static void AddText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
