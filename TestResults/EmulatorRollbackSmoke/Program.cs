using System.Text.Json;
using RB4ShadPS4Installer.Services;

var temporary = Path.Combine(Path.GetTempPath(), "rb4-emulator-rollback-" + Guid.NewGuid().ToString("N"));
var officialRoot = Path.Combine(temporary, "shadPS4");
var rb4Root = Path.Combine(temporary, "shadPS4-RB4");
try
{
    CreateBuild(rb4Root, "rb4-old", "old-profile");
    var rb4Staging = Path.Combine(temporary, "rb4-staging");
    CreateBuild(rb4Staging, "rb4-new", "newest-profile");
    EmulatorRollbackService.ReplaceWithStaging(rb4Root, rb4Staging);

    Assert(ReadBuild(rb4Root) == "rb4-new", "the verified RB4 staging build should become current");
    Assert(ReadBuild(rb4Root + "-previous") == "rb4-old", "the old RB4 build should be retained");
    var rb4Status = EmulatorRollbackService.Inspect(
        Path.Combine(rb4Root, "launcher"), officialRoot, rb4Root);
    Assert(rb4Status.Available && rb4Status.CurrentVersion == "rb4-new" && rb4Status.PreviousVersion == "rb4-old",
        "the RB4 rollback status should describe both builds");

    var restored = EmulatorRollbackService.Restore(
        Path.Combine(rb4Root, "launcher"), officialRoot, rb4Root, CancellationToken.None);
    Assert(restored.Channel == ManagedEmulatorChannel.LlysixRb4 && ReadBuild(rb4Root) == "rb4-old",
        "restoring should activate the previous RB4 build");
    Assert(ReadProfile(rb4Root) == "newest-profile",
        "restoring RB4 binaries must preserve the newest portable user profile");
    Assert(ReadBuild(rb4Root + "-previous") == "rb4-new" && ReadProfile(rb4Root + "-previous") == "old-profile",
        "the replaced build and its former profile should remain available for undo");

    EmulatorRollbackService.Restore(
        Path.Combine(rb4Root, "launcher"), officialRoot, rb4Root, CancellationToken.None);
    Assert(ReadBuild(rb4Root) == "rb4-new" && ReadProfile(rb4Root) == "newest-profile",
        "restoring again should undo the rollback without changing the active profile");

    CreateBuild(officialRoot, "official-old", null);
    var officialStaging = Path.Combine(temporary, "official-staging");
    CreateBuild(officialStaging, "official-new", null);
    EmulatorRollbackService.ReplaceWithStaging(officialRoot, officialStaging);
    var officialStatus = EmulatorRollbackService.Inspect(officialRoot, officialRoot, rb4Root);
    Assert(officialStatus.Available && officialStatus.Channel == ManagedEmulatorChannel.Official,
        "official managed updates should also expose rollback");
    EmulatorRollbackService.Restore(officialRoot, officialRoot, rb4Root, CancellationToken.None);
    Assert(ReadBuild(officialRoot) == "official-old", "official rollback should restore the old build");

    var custom = Path.Combine(temporary, "custom-emulator");
    CreateBuild(custom, "custom", null);
    Assert(!EmulatorRollbackService.Inspect(custom, officialRoot, rb4Root).IsManaged,
        "custom emulator folders must never be treated as managed rollback targets");

    Console.WriteLine("Emulator rollback smoke tests passed.");
}
finally
{
    if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
}

static void CreateBuild(string root, string version, string? profile)
{
    var launcher = Path.Combine(root, "launcher");
    Directory.CreateDirectory(launcher);
    File.WriteAllText(Path.Combine(launcher, "shadPS4QtLauncher.exe"), version);
    File.WriteAllText(Path.Combine(root, "build.txt"), version);
    File.WriteAllText(Path.Combine(root, ".rb4-shadps4-build.json"), JsonSerializer.Serialize(new { tag = version }));
    if (profile is null) return;
    Directory.CreateDirectory(Path.Combine(launcher, "user"));
    File.WriteAllText(Path.Combine(launcher, "user", "profile.txt"), profile);
}

static string ReadBuild(string root) => File.ReadAllText(Path.Combine(root, "build.txt"));
static string ReadProfile(string root) => File.ReadAllText(Path.Combine(root, "launcher", "user", "profile.txt"));

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
