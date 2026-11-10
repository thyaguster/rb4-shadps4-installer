using System.Text;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class InstalledContentCatalogService
{
    public static async Task ExportCsvAsync(
        string destination,
        string titleId,
        IReadOnlyList<InstalledContentItem> items,
        IProgress<InstallProgress>? progress,
        CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(destination))
            throw new ArgumentException("Choose an absolute destination for the content catalog.", nameof(destination));

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The content catalog destination is invalid.", nameof(destination));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteLineAsync("\"Game title ID\",\"Name\",\"Content ID\",\"Type\",\"State\",\"Version\",\"Size (bytes)\",\"Folder\"");

                for (var index = 0; index < items.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = items[index];
                    var size = await Task.Run(() => CalculateDirectorySize(item.Folder, token), token);
                    var fields = new[]
                    {
                        titleId,
                        item.Name,
                        item.ContentId,
                        item.IsCustomSong ? "Custom song / pack" : "DLC / unclassified",
                        item.IsEnabled ? "Enabled" : "Disabled",
                        item.Version ?? string.Empty,
                        size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        item.Folder
                    };
                    await writer.WriteLineAsync(string.Join(",", fields.Select(EscapeCsv)));
                    var completed = index + 1;
                    progress?.Report(new InstallProgress(100d * completed / Math.Max(1, items.Count),
                        $"Cataloging installed content {completed} of {items.Count}"));
                }

                await writer.FlushAsync(token);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception) { }
            throw;
        }
    }

    private static long CalculateDirectorySize(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) return 0;
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    token.ThrowIfCancellationRequested();
                    try { total = checked(total + new FileInfo(file).Length); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException) { }
                }
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        var trimmedStart = value.TrimStart();
        if (trimmedStart.Length > 0 && trimmedStart[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
