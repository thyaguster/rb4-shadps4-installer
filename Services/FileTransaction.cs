using System.Text.Json;
using System.Security.Cryptography;

namespace RB4ShadPS4Installer.Services;

internal sealed class FileTransaction : IDisposable
{
    internal const string TransactionLogName = "transaction.jsonl";
    private readonly string _root;
    private readonly bool _persistent;
    private readonly string _titleId;
    private readonly List<BackupEntry> _entries = [];
    private readonly HashSet<string> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private bool _finished;

    public FileTransaction(string root, string titleId, bool persistent)
    {
        _root = root;
        _titleId = titleId;
        _persistent = persistent;
        Directory.CreateDirectory(Path.Combine(_root, "files"));
        WriteManifest("InProgress", _entries);
    }

    public void PrepareFile(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (!_prepared.Add(destination)) return;

        var existed = File.Exists(destination);
        string? relativeBackup = null;
        if (existed)
        {
            relativeBackup = Path.Combine("files", $"{_entries.Count:D8}.bak");
            var backup = Path.Combine(_root, relativeBackup);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(destination, backup, overwrite: false);
        }
        _entries.Add(new BackupEntry(destination, relativeBackup, existed));
        AppendPreparedEntry(_entries[^1]);
    }

    public void Commit()
    {
        if (_finished) return;
        if (_persistent)
        {
            var protectedEntries = _entries.Select(entry =>
            {
                if (!entry.Existed || string.IsNullOrWhiteSpace(entry.BackupRelativePath)) return entry;
                var path = Path.Combine(_root, entry.BackupRelativePath);
                using var stream = File.OpenRead(path);
                return entry with
                {
                    BackupLength = stream.Length,
                    BackupSha256 = Convert.ToHexString(SHA256.HashData(stream))
                };
            }).ToArray();
            WriteManifest("Complete", protectedEntries);
        }
        else
        {
            TryDeleteRoot();
        }
        _finished = true;
    }

    public void Rollback()
    {
        if (_finished) return;
        var failures = new List<Exception>();
        foreach (var entry in _entries.AsEnumerable().Reverse())
        {
            try
            {
                if (entry.Existed)
                {
                    var backup = Path.Combine(_root, entry.BackupRelativePath!);
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Destination)!);
                    File.Copy(backup, entry.Destination, overwrite: true);
                }
                else if (File.Exists(entry.Destination))
                {
                    File.Delete(entry.Destination);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new IOException($"Could not restore {entry.Destination}", ex));
            }
        }

        TryDeleteRoot();
        _finished = true;
        if (failures.Count > 0) throw new AggregateException("Some installation changes could not be rolled back.", failures);
    }

    public void Dispose()
    {
        if (!_finished) Rollback();
    }

    private void TryDeleteRoot()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void WriteManifest(string status, IReadOnlyList<BackupEntry> entries)
    {
        var manifest = new BackupManifest(2, _titleId, DateTimeOffset.Now, status, entries);
        var path = Path.Combine(_root, "manifest.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private void AppendPreparedEntry(BackupEntry entry)
    {
        var path = Path.Combine(_root, TransactionLogName);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            16 * 1024, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(JsonSerializer.Serialize(entry));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}

internal sealed record BackupEntry(
    string Destination,
    string? BackupRelativePath,
    bool Existed,
    long? BackupLength = null,
    string? BackupSha256 = null);
internal sealed record BackupManifest(
    int FormatVersion,
    string TitleId,
    DateTimeOffset CreatedAt,
    string Status,
    IReadOnlyList<BackupEntry> Files);
