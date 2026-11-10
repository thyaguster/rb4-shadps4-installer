using RB4ShadPS4Installer.Models;
using RB4ShadPS4Installer.Services;

var root = Path.Combine(Path.GetTempPath(), "rb4-recovery-profiles-" + Guid.NewGuid().ToString("N"));
try
{
    var user = Path.Combine(root, "user");
    var games = Path.Combine(root, "games");
    var addons = Path.Combine(root, "addcont");
    Directory.CreateDirectory(user);
    Directory.CreateDirectory(games);
    Directory.CreateDirectory(addons);
    var request = new InstallRequest(InstallMode.Content, user, games, null, null, null, null,
        addons, [], "CUSA02084", true);
    var staging = Path.Combine(games, ".rb4-installer-staging", "test");
    Directory.CreateDirectory(staging);
    File.WriteAllText(Path.Combine(staging, "partial.bin"), "partial");
    var recovery = InstallationRecoveryService.Begin(request, staging, null);
    var existing = Path.Combine(games, "existing.bin");
    var created = Path.Combine(games, "created.bin");
    File.WriteAllText(existing, "before");
    var transaction = new FileTransaction(recovery.Root, request.TitleId, persistent: true);
    transaction.PrepareFile(existing);
    File.WriteAllText(existing, "after");
    transaction.PrepareFile(created);
    File.WriteAllText(created, "new");
    InstallationRecoveryService.UpdateStage(recovery, "Applying file changes");

    var pending = InstallationRecoveryService.FindPending(user);
    Assert(pending is not null && pending.ChangedFileCount == 2, "an interrupted transaction should be discovered with every prepared file");
    InstallationRecoveryService.CleanStaging(pending!);
    Assert(!Directory.Exists(staging) && InstallationRecoveryService.FindPending(user) is not null,
        "staging cleanup must retain rollback data");
    InstallationRecoveryService.Rollback(InstallationRecoveryService.FindPending(user)!);
    Assert(File.ReadAllText(existing) == "before" && !File.Exists(created), "rollback should restore overwritten files and remove created files");
    Assert(InstallationRecoveryService.FindPending(user) is null, "a completed rollback should remove its journal");

    var activeRoot = Path.Combine(addons, request.TitleId);
    Directory.CreateDirectory(Path.Combine(activeRoot, "A"));
    Directory.CreateDirectory(Path.Combine(activeRoot, "B"));
    var items = ContentProfileService.FindAll(addons, request.TitleId);
    ContentProfileService.Save(addons, request.TitleId, "Main", items);
    ContentProfileService.SetEnabled(addons, request.TitleId, [items.Single(item => Path.GetFileName(item.Folder) == "B")], false);
    Directory.CreateDirectory(Path.Combine(activeRoot, "C"));
    ContentProfileService.Apply(addons, request.TitleId, "Main");
    var applied = ContentProfileService.FindAll(addons, request.TitleId);
    Assert(applied.Single(item => Path.GetFileName(item.Folder) == "A").IsEnabled, "profile should keep saved content enabled");
    Assert(applied.Single(item => Path.GetFileName(item.Folder) == "B").IsEnabled, "profile should re-enable saved content");
    Assert(!applied.Single(item => Path.GetFileName(item.Folder) == "C").IsEnabled, "profile should disable content outside its saved set");
    Assert(ContentProfileService.List(addons, request.TitleId).Single().EnabledCount == 2, "saved profile counts should be stable");
    ContentProfileService.Delete(addons, request.TitleId, "Main");
    Assert(ContentProfileService.List(addons, request.TitleId).Count == 0, "profiles should be deletable without changing content");

    Console.WriteLine("Installation recovery and content profile smoke tests passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
