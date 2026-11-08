using System.Text.Json;
using System.Security.Cryptography;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal sealed record BackupInfo(string Root, BackupManifest Manifest)
{
    public string DisplayTitle => $"{Manifest.TitleId}  •  {Manifest.CreatedAt.ToLocalTime():g}";
    public string DisplayDetails => $"{Manifest.Files.Count:N0} affected file(s)  •  " +
                                    (Manifest.FormatVersion >= 2 ? "SHA-256 protected" : "legacy backup");
}

internal sealed record BackupValidationResult(bool IsValid, bool IsCryptographicallyVerified, int CheckedFiles, string Message);

internal static class BackupService
{
    public static IReadOnlyList<BackupInfo> FindAll(string userDirectory)
    {
        var root = Path.Combine(userDirectory, "rb4-installer-backups");
        if (!Directory.Exists(root)) return [];
        try
        {
            return Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
                .Select(TryRead)
                .Where(info => info is not null && info.Manifest.Status == "Complete" && info.Manifest.FormatVersion is 1 or 2)
                .OrderByDescending(info => info!.Manifest.CreatedAt)
                .Cast<BackupInfo>()
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static void Delete(BackupInfo backup, string userDirectory)
    {
        var backupsRoot = Path.GetFullPath(Path.Combine(userDirectory, "rb4-installer-backups"));
        var target = Path.GetFullPath(backup.Root);
        if (string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), backupsRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !IsWithin(target, backupsRoot) ||
            !File.Exists(Path.Combine(target, "manifest.json")))
            throw new InvalidOperationException("The selected backup is outside the configured backup folder.");
        Directory.Delete(target, recursive: true);
    }

    public static async Task RestoreAsync(
        BackupInfo backup,
        IReadOnlyList<string> allowedRoots,
        IProgress<InstallProgress> progress,
        CancellationToken token)
    {
        var safeRoots = allowedRoots
            .Where(PathUtility.IsUsableAbsolutePath)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (safeRoots.Length == 0) throw new InvalidOperationException("No valid restore destinations are configured.");

        var validationProgress = new Progress<InstallProgress>(value =>
            progress.Report(new InstallProgress(value.Percent * 0.25, value.Message)));
        var validation = await ValidateAsync(backup, validationProgress, token);
        if (!validation.IsValid) throw new InvalidDataException(validation.Message);

        var entries = backup.Manifest.Files.ToArray();
        foreach (var entry in entries)
        {
            var destination = Path.GetFullPath(entry.Destination);
            if (!safeRoots.Any(root => IsWithin(destination, root)))
                throw new InvalidDataException($"The backup contains a destination outside the configured shadPS4 folders: {destination}");
            if (entry.Existed)
            {
                if (string.IsNullOrWhiteSpace(entry.BackupRelativePath))
                    throw new InvalidDataException("The backup manifest is missing a required file reference.");
                var source = Path.GetFullPath(Path.Combine(backup.Root, entry.BackupRelativePath));
                if (!IsWithin(source, backup.Root) || !File.Exists(source))
                    throw new InvalidDataException($"A required backup file is missing: {entry.BackupRelativePath}");
            }
        }

        var rollbackRoot = Path.Combine(Path.GetTempPath(), "RB4ShadPS4Installer", "restore-rollback", Guid.NewGuid().ToString("N"));
        using var transaction = new FileTransaction(rollbackRoot, backup.Manifest.TitleId, persistent: false);
        try
        {
            for (var index = entries.Length - 1; index >= 0; index--)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[index];
                transaction.PrepareFile(entry.Destination);
                if (entry.Existed)
                {
                    var source = Path.Combine(backup.Root, entry.BackupRelativePath!);
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Destination)!);
                    File.Copy(source, entry.Destination, overwrite: true);
                }
                else if (File.Exists(entry.Destination))
                {
                    File.Delete(entry.Destination);
                }

                var completed = entries.Length - index;
                progress.Report(new InstallProgress(25 + 75d * completed / Math.Max(1, entries.Length),
                    $"Restoring backup file {completed} of {entries.Length}"));
                if (completed % 32 == 0) await Task.Yield();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static async Task<BackupValidationResult> ValidateAsync(
        BackupInfo backup,
        IProgress<InstallProgress>? progress,
        CancellationToken token)
    {
        var entries = backup.Manifest.Files.Where(entry => entry.Existed).ToArray();
        var cryptographic = backup.Manifest.FormatVersion >= 2;
        for (var index = 0; index < entries.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[index];
            if (string.IsNullOrWhiteSpace(entry.BackupRelativePath))
                return new(false, false, index, "The backup manifest is missing a required file reference.");

            var source = Path.GetFullPath(Path.Combine(backup.Root, entry.BackupRelativePath));
            if (!IsWithin(source, backup.Root))
                return new(false, false, index, "The backup manifest references a file outside its backup folder.");
            if (!File.Exists(source))
                return new(false, false, index, $"A required backup file is missing: {entry.BackupRelativePath}");

            if (cryptographic)
            {
                if (entry.BackupLength is null || string.IsNullOrWhiteSpace(entry.BackupSha256))
                    return new(false, false, index, $"Integrity information is missing for {entry.BackupRelativePath}.");
                var info = new FileInfo(source);
                if (info.Length != entry.BackupLength.Value)
                    return new(false, false, index, $"Backup file size does not match its manifest: {entry.BackupRelativePath}");
                await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!actualHash.Equals(entry.BackupSha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, false, index, $"Backup file checksum failed: {entry.BackupRelativePath}");
            }

            var checkedFiles = index + 1;
            progress?.Report(new InstallProgress(100d * checkedFiles / Math.Max(1, entries.Length),
                $"Verifying backup file {checkedFiles} of {entries.Length}"));
        }

        if (!cryptographic)
            return new(true, false, entries.Length, "Legacy backup is structurally valid, but it predates SHA-256 integrity protection.");
        return new(true, true, entries.Length, $"Backup verified successfully ({entries.Length:N0} protected file(s)).");
    }

    private static BackupInfo? TryRead(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
            return manifest is null ? null : new BackupInfo(Path.GetDirectoryName(manifestPath)!, manifest);
        }
        catch (Exception) { return null; }
    }

    private static bool IsWithin(string candidate, string root)
    {
        candidate = Path.GetFullPath(candidate);
        root = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathFullyQualified(relative) &&
               relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
