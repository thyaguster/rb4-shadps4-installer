using System.Runtime.InteropServices;
using System.Text;

namespace RB4ShadPS4Installer.Services;

internal static class CrashReportService
{
    private const int RetainedReports = 10;

    public static string Create(Exception exception, string source, string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RB4ShadPS4Installer", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
        var version = typeof(CrashReportService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        var report = new StringBuilder()
            .AppendLine("RB4 shadPS4 Installer crash report")
            .AppendLine($"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}")
            .AppendLine($"Installer version: {version}")
            .AppendLine($"Source: {source}")
            .AppendLine($"Operating system: {RuntimeInformation.OSDescription}")
            .AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}")
            .AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}")
            .AppendLine()
            .AppendLine(exception.ToString())
            .ToString();
        File.WriteAllText(path, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Prune(directory, path);
        return path;
    }

    private static void Prune(string directory, string current)
    {
        try
        {
            foreach (var old in Directory.EnumerateFiles(directory, "crash-*.log")
                         .Where(path => !path.Equals(current, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(RetainedReports - 1))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A crash report must never fail because an older report could not be removed.
        }
    }
}
