using System.Text.Json;

namespace RB4ShadPS4Installer.Services;

internal sealed class FileTransaction : IDisposable
{
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
    }

    public void Commit()
    {
        if (_finished) return;
        if (_persistent)
        {
            var manifest = new BackupManifest(
                1, _titleId, DateTimeOffset.Now, "Complete", _entries);
            File.WriteAllText(Path.Combine(_root, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
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
}

internal sealed record BackupEntry(string Destination, string? BackupRelativePath, bool Existed);
internal sealed record BackupManifest(
    int FormatVersion,
    string TitleId,
    DateTimeOffset CreatedAt,
    string Status,
    IReadOnlyList<BackupEntry> Files);
