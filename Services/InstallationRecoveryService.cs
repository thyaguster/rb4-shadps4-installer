using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal sealed record InstallationRecoveryJournal(
    int FormatVersion,
    DateTimeOffset StartedAt,
    string Stage,
    string StagingRoot,
    string? FinalBackupRoot,
    InstallRequest Request);

internal sealed record PendingInstallationRecovery(
    string Root,
    InstallationRecoveryJournal Journal,
    BackupManifest Manifest)
{
    public int ChangedFileCount => Manifest.Files.Count;
}

internal static class InstallationRecoveryService
{
    private const string JournalName = "recovery.json";

    public static PendingInstallationRecovery Begin(InstallRequest request, string stagingRoot, string? finalBackupRoot)
    {
        var root = Path.Combine(request.UserDirectory, "rb4-installer-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var journal = new InstallationRecoveryJournal(1, DateTimeOffset.Now, "Preparing", stagingRoot, finalBackupRoot, request);
        WriteJournal(root, journal);
        return new(root, journal, new BackupManifest(2, request.TitleId, journal.StartedAt, "InProgress", []));
    }

    public static void UpdateStage(PendingInstallationRecovery recovery, string stage) =>
        WriteJournal(recovery.Root, recovery.Journal with { Stage = stage });

    public static PendingInstallationRecovery? FindPending(string userDirectory)
    {
        var parent = Path.Combine(userDirectory, "rb4-installer-recovery");
        if (!Directory.Exists(parent)) return null;
        foreach (var root in Directory.EnumerateDirectories(parent).OrderByDescending(Directory.GetCreationTimeUtc))
        {
            var journalPath = Path.Combine(root, JournalName);
            var manifestPath = Path.Combine(root, "manifest.json");
            if (!File.Exists(journalPath) || !File.Exists(manifestPath)) continue;
            try
            {
                var journal = JsonSerializer.Deserialize<InstallationRecoveryJournal>(File.ReadAllText(journalPath));
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (journal is null || manifest is null) continue;
                if (manifest.Status.Equals("Complete", StringComparison.OrdinalIgnoreCase))
                {
                    FinalizeCompleted(root, journal);
                    continue;
                }
                var transactionLog = Path.Combine(root, FileTransaction.TransactionLogName);
                if (File.Exists(transactionLog))
                    manifest = manifest with { Files = ReadPreparedEntries(transactionLog) };
                return new(root, journal, manifest);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }

    public static void Complete(PendingInstallationRecovery recovery)
    {
        var journal = recovery.Journal;
        var journalPath = Path.Combine(recovery.Root, JournalName);
        if (File.Exists(journalPath)) File.Delete(journalPath);
        var transactionLog = Path.Combine(recovery.Root, FileTransaction.TransactionLogName);
        if (File.Exists(transactionLog)) File.Delete(transactionLog);
        if (!string.IsNullOrWhiteSpace(journal.FinalBackupRoot))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journal.FinalBackupRoot)!);
            if (Directory.Exists(journal.FinalBackupRoot)) Directory.Delete(journal.FinalBackupRoot, recursive: true);
            Directory.Move(recovery.Root, journal.FinalBackupRoot);
        }
        else if (Directory.Exists(recovery.Root)) Directory.Delete(recovery.Root, recursive: true);
    }

    public static void Rollback(PendingInstallationRecovery recovery)
    {
        var allowedRoots = new[]
        {
            recovery.Journal.Request.GameLibrary,
            recovery.Journal.Request.UserDirectory,
            recovery.Journal.Request.AddonDirectory
        }.Where(PathUtility.IsUsableAbsolutePath).Select(Path.GetFullPath).ToArray();

        foreach (var entry in recovery.Manifest.Files.Reverse())
        {
            var destination = Path.GetFullPath(entry.Destination);
            if (!allowedRoots.Any(root => IsWithin(destination, root)))
                throw new InvalidDataException("The recovery journal contains a destination outside the saved installation paths.");
            if (entry.Existed)
            {
                var backup = Path.GetFullPath(Path.Combine(recovery.Root, entry.BackupRelativePath!));
                if (!IsWithin(backup, recovery.Root) || !File.Exists(backup))
                    throw new InvalidDataException("A required recovery file is missing.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backup, destination, overwrite: true);
            }
            else if (File.Exists(destination)) File.Delete(destination);
        }
        Cleanup(recovery);
    }

    public static void CleanStaging(PendingInstallationRecovery recovery)
    {
        TryDeleteDirectory(recovery.Journal.StagingRoot);
        UpdateStage(recovery, "Staging cleaned; rollback still available");
    }

    private static void Cleanup(PendingInstallationRecovery recovery)
    {
        TryDeleteDirectory(recovery.Journal.StagingRoot);
        TryDeleteDirectory(recovery.Root);
    }

    private static void FinalizeCompleted(string root, InstallationRecoveryJournal journal)
    {
        var recovery = new PendingInstallationRecovery(root, journal,
            new BackupManifest(2, journal.Request.TitleId, journal.StartedAt, "Complete", []));
        Complete(recovery);
        TryDeleteDirectory(journal.StagingRoot);
    }

    private static void WriteJournal(string root, InstallationRecoveryJournal journal)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, JournalName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static IReadOnlyList<BackupEntry> ReadPreparedEntries(string path)
    {
        var entries = new List<BackupEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<BackupEntry>(line);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException)
            {
                // A torn final append means the process stopped before that
                // PrepareFile call returned, so its destination was not changed.
                break;
            }
        }
        return entries;
    }

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
