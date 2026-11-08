using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class ContentAuditService
{
    public static ContentAuditReport Audit(
        string addonDirectory,
        string titleId,
        IProgress<InstallProgress>? progress,
        CancellationToken token)
    {
        var root = Path.GetFullPath(Path.Combine(addonDirectory, titleId));
        var findings = new List<ContentAuditFinding>();
        if (!Directory.Exists(root))
        {
            findings.Add(new(ContentAuditSeverity.Info, "No installed content folder",
                "No per-game add-on folder exists yet. This is normal when no DLC or custom songs are installed.", root));
            return new(titleId, root, 0, findings);
        }

        string[] folders;
        try
        {
            var discovered = new List<string>();
            foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                discovered.Add(folder);
            }
            folders = discovered.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            findings.Add(new(ContentAuditSeverity.Error, "Content directory cannot be read", ex.Message, root));
            return new(titleId, root, 0, findings);
        }

        var contentIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < folders.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var folder = folders[index];
            var displayName = Path.GetFileName(folder);
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                {
                    findings.Add(new(ContentAuditSeverity.Warning, "Linked content folder skipped",
                        "The audit does not follow directory junctions or symbolic links.", folder));
                    continue;
                }

                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    findings.Add(new(ContentAuditSeverity.Warning, "Empty content folder", displayName, folder));

                var sfo = FindParamSfo(folder, token);
                if (sfo is null)
                {
                    findings.Add(new(ContentAuditSeverity.Warning, "Package metadata is missing",
                        "No sce_sys/param.sfo file was found. shadPS4 may not recognize this content.", folder));
                    continue;
                }

                IReadOnlyDictionary<string, string> values;
                try { values = ParamSfoReader.Read(sfo); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    findings.Add(new(ContentAuditSeverity.Error, "Package metadata is unreadable", ex.Message, folder));
                    continue;
                }

                var metadataTitleId = values.GetValueOrDefault("TITLE_ID")?.Trim();
                if (!string.IsNullOrWhiteSpace(metadataTitleId) &&
                    !metadataTitleId.Equals(titleId, StringComparison.OrdinalIgnoreCase))
                    findings.Add(new(ContentAuditSeverity.Error, "Wrong-region content",
                        $"Metadata targets {metadataTitleId}, but this library is {titleId}.", folder));

                var category = values.GetValueOrDefault("CATEGORY")?.Trim();
                if (!string.IsNullOrWhiteSpace(category) && !category.Equals("ac", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new(ContentAuditSeverity.Warning, "Unexpected package category",
                        $"Metadata category is '{category}' instead of the expected add-on category 'ac'.", folder));

                var contentId = values.GetValueOrDefault("CONTENT_ID")?.Trim();
                if (string.IsNullOrWhiteSpace(contentId))
                    findings.Add(new(ContentAuditSeverity.Warning, "Content ID is missing",
                        "Package metadata does not contain CONTENT_ID.", folder));
                else
                {
                    if (!contentIds.TryGetValue(contentId, out var matches)) contentIds[contentId] = matches = [];
                    matches.Add(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                findings.Add(new(ContentAuditSeverity.Error, "Content folder cannot be read", ex.Message, folder));
            }
            finally
            {
                var completed = index + 1;
                progress?.Report(new InstallProgress(100d * completed / Math.Max(1, folders.Length),
                    $"Auditing installed content {completed} of {folders.Length}"));
            }
        }

        foreach (var duplicate in contentIds.Where(pair => pair.Value.Count > 1))
            findings.Add(new(ContentAuditSeverity.Error, "Duplicate content ID",
                $"{duplicate.Key} is used by {duplicate.Value.Count:N0} installed folders:\n" +
                string.Join("\n", duplicate.Value.Select(Path.GetFileName))));

        if (findings.Count == 0)
            findings.Add(new(ContentAuditSeverity.Info, "No issues found",
                "Every detected folder contains readable metadata for the selected region."));
        return new(titleId, root, folders.Length, findings);
    }

    private static string? FindParamSfo(string root, CancellationToken token)
    {
        var preferred = Path.Combine(root, "sce_sys", "param.sfo");
        if (File.Exists(preferred)) return preferred;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "param.sfo", SearchOption.TopDirectoryOnly)) return file;
            foreach (var child in Directory.EnumerateDirectories(current))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
        }
        return null;
    }
}
