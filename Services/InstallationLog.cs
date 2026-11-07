using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal sealed class InstallationLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public string Path { get; }

    public InstallationLog(InstallRequest request)
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RB4ShadPS4Installer", "logs");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"install-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(Path, append: false) { AutoFlush = true };
        Write("RB4 shadPS4 Installer log");
        Write($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Write($"Mode: {request.Mode}");
        Write($"Emulator profile: {request.EmulatorProfile}");
        Write($"Title ID: {request.TitleId}");
        Write($"Game library: {request.GameLibrary}");
        Write($"User directory: {request.UserDirectory}");
        Write($"Base source: {request.BaseGameSource ?? "(none)"}");
        Write($"Update source: {request.PatchSource ?? "(none)"}");
        Write($"Deluxe source: {request.DeluxeSource ?? "(none)"}");
        Write($"Additional content: {request.AddonSources.Count} item(s)");
        Write($"Backup enabled: {request.CreateBackup}");
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }

    public void Write(Exception exception) => Write(exception.ToString());

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
