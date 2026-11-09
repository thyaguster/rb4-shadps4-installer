using System.Diagnostics;
using System.Text.Json;

namespace RB4ShadPS4Installer.Services;

internal enum ManagedEmulatorChannel
{
    None,
    Official,
    LlysixRb4
}

internal sealed record EmulatorRollbackStatus(
    ManagedEmulatorChannel Channel,
    string Root,
    string PreviousRoot,
    string CurrentVersion,
    string PreviousVersion,
    bool Available)
{
    public bool IsManaged => Channel != ManagedEmulatorChannel.None;
}

internal sealed record EmulatorRollbackResult(
    ManagedEmulatorChannel Channel,
    string ApplicationDirectory,
    string RestoredVersion,
    string RetainedVersion);

internal static class EmulatorRollbackService
{
    private const string Rb4BuildMarker = ".rb4-shadps4-build.json";
    private const string OfficialBuildMarker = ".rb4-shadps4-official-build.json";

    public static EmulatorRollbackStatus Inspect(
        string applicationDirectory,
        string officialRoot,
        string rb4Root)
    {
        var managed = ResolveManagedRoot(applicationDirectory, officialRoot, rb4Root);
        if (managed.Channel == ManagedEmulatorChannel.None)
            return new(managed.Channel, string.Empty, string.Empty, string.Empty, string.Empty, false);

        var previous = managed.Root + "-previous";
        var currentLauncher = FindLauncher(managed.Root);
        var previousLauncher = FindLauncher(previous);
        return new(
            managed.Channel,
            managed.Root,
            previous,
            DescribeVersion(managed.Root, currentLauncher),
            DescribeVersion(previous, previousLauncher),
            currentLauncher is not null && previousLauncher is not null);
    }

    public static EmulatorRollbackResult Restore(
        string applicationDirectory,
        string officialRoot,
        string rb4Root,
        CancellationToken token)
    {
        var status = Inspect(applicationDirectory, officialRoot, rb4Root);
        if (!status.IsManaged)
            throw new InvalidOperationException("Rollback is available only for installer-managed shadPS4 folders.");
        if (!status.Available)
            throw new InvalidOperationException("No valid previous managed emulator build is available.");

        token.ThrowIfCancellationRequested();
        SwapRoots(status.Root, status.PreviousRoot);
        try
        {
            if (status.Channel == ManagedEmulatorChannel.LlysixRb4)
                PreserveNewestPortableUser(status.Root, status.PreviousRoot);
        }
        catch
        {
            // The user-profile exchange rolls itself back. Put the emulator
            // directories back as well so a failed restore changes nothing.
            SwapRoots(status.Root, status.PreviousRoot);
            throw;
        }

        var restoredLauncher = FindLauncher(status.Root)
            ?? throw new InvalidDataException("The restored emulator folder no longer contains a launcher.");
        return new(
            status.Channel,
            Path.GetDirectoryName(restoredLauncher)!,
            status.PreviousVersion,
            status.CurrentVersion);
    }

    public static void ReplaceWithStaging(string destination, string staging)
    {
        if (!Directory.Exists(staging))
            throw new DirectoryNotFoundException("The verified emulator staging folder is missing.");
        if (FindLauncher(staging) is null)
            throw new InvalidDataException("The verified emulator staging folder has no launcher.");

        var previous = destination + "-previous";
        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
        if (Directory.Exists(destination)) Directory.Move(destination, previous);
        try
        {
            Directory.Move(staging, destination);
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(previous))
                Directory.Move(previous, destination);
            throw;
        }
    }

    private static (ManagedEmulatorChannel Channel, string Root) ResolveManagedRoot(
        string applicationDirectory,
        string officialRoot,
        string rb4Root)
    {
        if (IsWithinOrEqual(applicationDirectory, rb4Root))
            return (ManagedEmulatorChannel.LlysixRb4, Path.GetFullPath(rb4Root));
        if (IsWithinOrEqual(applicationDirectory, officialRoot))
            return (ManagedEmulatorChannel.Official, Path.GetFullPath(officialRoot));
        return (ManagedEmulatorChannel.None, string.Empty);
    }

    private static bool IsWithinOrEqual(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullCandidate.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void SwapRoots(string current, string previous)
    {
        if (!Directory.Exists(current) || !Directory.Exists(previous))
            throw new DirectoryNotFoundException("Both the current and previous emulator folders are required for rollback.");

        var temporary = current + $"-rollback-{Guid.NewGuid():N}";
        var currentMoved = false;
        var previousMoved = false;
        try
        {
            Directory.Move(current, temporary);
            currentMoved = true;
            Directory.Move(previous, current);
            previousMoved = true;
            Directory.Move(temporary, previous);
        }
        catch
        {
            if (previousMoved && Directory.Exists(current) && !Directory.Exists(previous))
                Directory.Move(current, previous);
            if (currentMoved && Directory.Exists(temporary) && !Directory.Exists(current))
                Directory.Move(temporary, current);
            throw;
        }
    }

    private static void PreserveNewestPortableUser(string restoredRoot, string retainedRoot)
    {
        var restoredLauncher = FindLauncher(restoredRoot)
            ?? throw new InvalidDataException("The restored RB4 build has no launcher.");
        var retainedLauncher = FindLauncher(retainedRoot)
            ?? throw new InvalidDataException("The retained RB4 build has no launcher.");
        var restoredUser = Path.Combine(Path.GetDirectoryName(restoredLauncher)!, "user");
        var retainedUser = Path.Combine(Path.GetDirectoryName(retainedLauncher)!, "user");
        ExchangeDirectories(restoredUser, retainedUser, restoredRoot + $"-profile-swap-{Guid.NewGuid():N}");
    }

    private static void ExchangeDirectories(string first, string second, string temporary)
    {
        var firstExists = Directory.Exists(first);
        var secondExists = Directory.Exists(second);
        if (!firstExists && !secondExists) return;

        var firstMoved = false;
        var secondMoved = false;
        try
        {
            if (firstExists)
            {
                Directory.Move(first, temporary);
                firstMoved = true;
            }
            if (secondExists)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(first)!);
                Directory.Move(second, first);
                secondMoved = true;
            }
            if (firstMoved)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(second)!);
                Directory.Move(temporary, second);
            }
        }
        catch
        {
            if (secondMoved && Directory.Exists(first) && !Directory.Exists(second))
                Directory.Move(first, second);
            if (firstMoved && Directory.Exists(temporary) && !Directory.Exists(first))
                Directory.Move(temporary, first);
            throw;
        }
    }

    private static string? FindLauncher(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "shadPS4QtLauncher.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(directory, "shadPS4.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string DescribeVersion(string root, string? launcher)
    {
        if (launcher is null) return "unavailable";
        foreach (var markerName in new[] { Rb4BuildMarker, OfficialBuildMarker })
        {
            var marker = Path.Combine(root, markerName);
            if (!File.Exists(marker)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(marker));
                foreach (var property in new[] { "tag", "asset" })
                    if (document.RootElement.TryGetProperty(property, out var value) &&
                        !string.IsNullOrWhiteSpace(value.GetString()))
                        return value.GetString()!;
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(launcher).FileVersion;
            if (!string.IsNullOrWhiteSpace(version)) return version;
        }
        catch (FileNotFoundException) { }
        return new DirectoryInfo(root).LastWriteTime.ToString("g");
    }
}
