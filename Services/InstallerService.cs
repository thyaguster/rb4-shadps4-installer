using System.IO.Compression;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal sealed class InstallerService
{
    private static readonly string[] SupportedTitleIds = ["CUSA02084", "CUSA02901"];

    public ValidationResult Validate(InstallRequest request)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!SupportedTitleIds.Contains(request.TitleId)) errors.Add("Choose a supported RB4 title ID.");
        if (!PathUtility.IsUsableAbsolutePath(request.GameLibrary)) errors.Add("Choose a valid absolute shadPS4 game-library path.");
        if (!PathUtility.IsUsableAbsolutePath(request.UserDirectory)) errors.Add("Choose a valid absolute shadPS4 user-directory path.");
        if (request.AddonSources.Count > 0 && !PathUtility.IsUsableAbsolutePath(request.AddonDirectory))
            errors.Add("Choose a valid absolute additional-content path.");

        if (errors.Count > 0) return new ValidationResult(false, errors, warnings);

        var installedBase = Path.Combine(request.GameLibrary, request.TitleId);
        if (!string.IsNullOrWhiteSpace(request.BaseGameSource))
            ValidateSource(request.BaseGameSource, request.TitleId, false, errors, warnings);
        else if (!Directory.Exists(installedBase))
            errors.Add("Select an extracted RB4 base-game folder, or install it in shadPS4 first.");

        if (!string.IsNullOrWhiteSpace(request.PatchSource))
            ValidateSource(request.PatchSource, request.TitleId, true, errors, warnings);

        if (request.Mode == InstallMode.Content && string.IsNullOrWhiteSpace(request.PatchSource) && request.AddonSources.Count == 0)
            errors.Add("Choose an update, DLC, or custom-song package to install.");

        if (PackageExtractor.IsPackage(request.BaseGameSource) || PackageExtractor.IsPackage(request.PatchSource))
        {
            if (string.IsNullOrWhiteSpace(request.PackageExtractor) || !File.Exists(request.PackageExtractor))
                errors.Add("Choose pkg_extractor.exe to install PKG files.");
        }

        if (request.AddonSources.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(request.AddonDirectory)) errors.Add("Choose the shadPS4 additional-content directory.");
            foreach (var source in request.AddonSources)
            {
                if (!File.Exists(source) && !Directory.Exists(source)) errors.Add($"DLC/custom-song source not found: {source}");
                else if (File.Exists(source) && !Path.GetExtension(source).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Unsupported add-on file: {Path.GetFileName(source)}. Choose a .pkg or extracted folder.");
                else if (Directory.Exists(source)) ValidateAddonFolder(source, request.TitleId, errors);
            }
            if (request.AddonSources.Any(PackageExtractor.IsPackage) &&
                (string.IsNullOrWhiteSpace(request.PackageExtractor) || !File.Exists(request.PackageExtractor)))
                errors.Add("Choose pkg_extractor.exe to install DLC or custom-song PKGs.");
        }

        if (request.Mode == InstallMode.Deluxe)
        {
            if (string.IsNullOrWhiteSpace(request.DeluxeSource)) errors.Add("Choose the RB4 Deluxe ZIP or extracted folder.");
            else if (!File.Exists(request.DeluxeSource) && !Directory.Exists(request.DeluxeSource)) errors.Add("The RB4 Deluxe source does not exist.");
        }
        if (!Directory.Exists(request.UserDirectory)) warnings.Add("The shadPS4 user directory will be created.");
        if (request.Mode == InstallMode.Deluxe && request.PatchSource is null && !Directory.Exists(Path.Combine(request.GameLibrary, request.TitleId + "-patch")))
            warnings.Add("RB4 Deluxe expects the official RB4 2.21 update (in-game 2.3.7).");

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    public async Task<InstallOutcome> InstallAsync(InstallRequest request, IProgress<InstallProgress> progress, CancellationToken token)
    {
        var validation = Validate(request);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

        Directory.CreateDirectory(request.GameLibrary);
        Directory.CreateDirectory(request.UserDirectory);
        var baseDestination = Path.Combine(request.GameLibrary, request.TitleId);
        var patchDestination = Path.Combine(request.GameLibrary, request.TitleId + "-patch");
        var operations = new List<(string Source, string Destination, string Label)>();
        var persistentBackupRoot = request.CreateBackup
            ? Path.Combine(request.UserDirectory, "rb4-installer-backups", request.TitleId, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))
            : null;

        var stagingRoot = Path.Combine(request.GameLibrary, ".rb4-installer-staging", Guid.NewGuid().ToString("N"));
        var transactionRoot = persistentBackupRoot ?? Path.Combine(
            Path.GetTempPath(), "RB4ShadPS4Installer", "rollback", Guid.NewGuid().ToString("N"));
        using var transaction = new FileTransaction(transactionRoot, request.TitleId, request.CreateBackup);
        if (!string.IsNullOrWhiteSpace(request.BaseGameSource) && !PackageExtractor.IsPackage(request.BaseGameSource))
            operations.Add((request.BaseGameSource, baseDestination, "Installing RB4 base game"));
        if (!string.IsNullOrWhiteSpace(request.PatchSource) && !PackageExtractor.IsPackage(request.PatchSource))
            operations.Add((request.PatchSource, patchDestination, "Installing RB4 update"));

        string? temporaryDeluxe = null;
        var installedContentCount = 0;
        var skippedExistingContentCount = 0;
        try
        {
            var packageCount = (PackageExtractor.IsPackage(request.BaseGameSource) ? 1 : 0) +
                (PackageExtractor.IsPackage(request.PatchSource) ? 1 : 0) +
                request.AddonSources.Count(PackageExtractor.IsPackage);
            var packageIndex = 0;
            (double Start, double End) NextExtractionRange()
            {
                var start = 2 + 58.0 * packageIndex / Math.Max(1, packageCount);
                packageIndex++;
                var end = 2 + 58.0 * packageIndex / Math.Max(1, packageCount);
                return (start, end);
            }

            if (PackageExtractor.IsPackage(request.BaseGameSource))
            {
                var extractedBase = Path.Combine(stagingRoot, "base");
                var range = NextExtractionRange();
                progress.Report(new InstallProgress(range.Start, "Unpacking the RB4 base PKG"));
                await PackageExtractor.ExtractAsync(request.PackageExtractor!, request.BaseGameSource!, extractedBase, progress, range.Start, range.End, token);
                var extractedBaseRoot = EnsureExtractedPackage(extractedBase, request.TitleId, false);
                operations.Add((extractedBaseRoot, baseDestination, "Installing extracted RB4 base game"));
            }
            if (PackageExtractor.IsPackage(request.PatchSource))
            {
                var extractedPatch = Path.Combine(stagingRoot, "patch");
                var range = NextExtractionRange();
                progress.Report(new InstallProgress(range.Start, "Unpacking the RB4 update PKG"));
                await PackageExtractor.ExtractAsync(request.PackageExtractor!, request.PatchSource!, extractedPatch, progress, range.Start, range.End, token);
                var extractedPatchRoot = EnsureExtractedPackage(extractedPatch, request.TitleId, true);
                operations.Add((extractedPatchRoot, patchDestination, "Installing extracted RB4 update"));
            }

            for (var index = 0; index < request.AddonSources.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var source = request.AddonSources[index];
                string addonRoot;
                if (PackageExtractor.IsPackage(source))
                {
                    var extractedAddon = Path.Combine(stagingRoot, $"addon-{index + 1}");
                    var range = NextExtractionRange();
                    progress.Report(new InstallProgress(range.Start, $"Unpacking add-on {index + 1} of {request.AddonSources.Count}"));
                    await PackageExtractor.ExtractAsync(request.PackageExtractor!, source, extractedAddon, progress, range.Start, range.End, token);
                    addonRoot = extractedAddon;
                }
                else addonRoot = source;

                var addon = ResolveAddon(addonRoot, request.TitleId);
                var destination = Path.Combine(request.AddonDirectory, request.TitleId, addon.ContentId);
                if (Directory.Exists(destination) && !request.ReplaceExistingContent)
                {
                    skippedExistingContentCount++;
                    progress.Report(new InstallProgress(ProgressForSkipped(packageCount, index), $"Skipping already-installed add-on: {addon.ContentId}"));
                    continue;
                }
                operations.Add((addon.Root, destination, $"Installing add-on: {addon.ContentId}"));
                installedContentCount++;
            }

            if (request.Mode == InstallMode.Deluxe)
            {
                var deluxeRoot = request.DeluxeSource!;
                if (File.Exists(deluxeRoot))
                {
                    temporaryDeluxe = Path.Combine(Path.GetTempPath(), "RB4DXInstaller", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(temporaryDeluxe);
                    progress.Report(new InstallProgress(3, "Extracting RB4 Deluxe"));
                    await Task.Run(() => ZipFile.ExtractToDirectory(deluxeRoot, temporaryDeluxe), token);
                    deluxeRoot = temporaryDeluxe;
                }

                var goldHen = FindDirectory(deluxeRoot, "GoldHEN")
                    ?? throw new InvalidDataException("The Deluxe package does not contain a GoldHEN folder.");
                var plugin = Directory.EnumerateFiles(goldHen, "RB4DX-Plugin.prx", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.EnumerateFiles(deluxeRoot, "RB4DX-Plugin.prx", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidDataException("The Deluxe package does not contain RB4DX-Plugin.prx.");
                var deluxeData = request.EmulatorProfile == ShadPs4Profile.LlysixRb4
                    ? Path.Combine(request.UserDirectory, "data", "GoldHEN")
                    : Path.Combine(request.UserDirectory, "data", request.TitleId, "GoldHEN");
                operations.Add((goldHen, deluxeData, "Installing Deluxe data"));
                var activeGame = Directory.Exists(patchDestination) || !string.IsNullOrWhiteSpace(request.PatchSource)
                    ? patchDestination : baseDestination;
                operations.Add((plugin, Path.Combine(activeGame, "sce_module", "RB4DX-Plugin.prx"), "Installing Deluxe plugin"));
            }

            var allFiles = operations.Sum(operation => CountFiles(operation.Source));
            var copyStart = packageCount > 0 ? 62.0 : 5.0;
            long completed = 0;
            foreach (var operation in operations)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(operation.Source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(operation.Destination)!);
                    transaction.PrepareFile(operation.Destination);
                    File.Copy(operation.Source, operation.Destination, true);
                    completed++;
                    progress.Report(new InstallProgress(copyStart + (99.0 - copyStart) * completed / Math.Max(1, allFiles), operation.Label));
                }
                else
                {
                    foreach (var sourceFile in Directory.EnumerateFiles(operation.Source, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(operation.Source, sourceFile);
                        var destinationFile = Path.Combine(operation.Destination, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                        transaction.PrepareFile(destinationFile);
                        File.Copy(sourceFile, destinationFile, true);
                        completed++;
                        progress.Report(new InstallProgress(copyStart + (99.0 - copyStart) * completed / Math.Max(1, allFiles), $"{operation.Label}: {relative}"));
                        if (completed % 32 == 0) await Task.Yield();
                    }
                }
            }
            transaction.Commit();
            progress.Report(new InstallProgress(100, "Installation complete"));
            return new InstallOutcome(installedContentCount, skippedExistingContentCount);
        }
        catch (Exception installError)
        {
            progress.Report(new InstallProgress(99, "Installation interrupted; rolling back file changes"));
            try
            {
                transaction.Rollback();
                progress.Report(new InstallProgress(99, "Rollback complete; the previous installation was restored"));
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Installation failed and some changes could not be rolled back. Check the installation log.",
                    installError, rollbackError);
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(temporaryDeluxe);
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static double ProgressForSkipped(int packageCount, int index) =>
        packageCount > 0 ? Math.Min(61, 2 + 58.0 * (index + 1) / packageCount) : 5;

    private static void ValidateSource(string path, string titleId, bool patch, List<string> errors, List<string> warnings)
    {
        if (File.Exists(path))
        {
            if (!Path.GetExtension(path).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Unsupported source file: {Path.GetFileName(path)}. Choose a .pkg file or extracted folder.");
            return;
        }
        ValidateGameFolder(path, titleId, patch, errors, warnings);
    }

    private static void ValidateAddonFolder(string path, string expectedTitleId, List<string> errors)
    {
        try { ResolveAddon(path, expectedTitleId); }
        catch (Exception ex) { errors.Add($"Invalid add-on folder {Path.GetFileName(path)}: {ex.Message}"); }
    }

    private static (string Root, string ContentId) ResolveAddon(string path, string expectedTitleId)
    {
        var sfoFiles = Directory.EnumerateFiles(path, "param.sfo", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "sce_sys", StringComparison.OrdinalIgnoreCase))
            .Prepend(Path.Combine(path, "sce_sys", "param.sfo"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var foundWrongTitle = new List<string>();
        foreach (var sfo in sfoFiles)
        {
            var values = ParamSfoReader.Read(sfo);
            if (!values.TryGetValue("TITLE_ID", out var titleId) ||
                !titleId.Equals(expectedTitleId, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(titleId)) foundWrongTitle.Add(titleId);
                continue;
            }
            if (!values.TryGetValue("CONTENT_ID", out var contentId) || string.IsNullOrWhiteSpace(contentId))
                continue;
            var root = Directory.GetParent(Path.GetDirectoryName(sfo)!)!.FullName;
            return (root, SanitizeDirectoryName(contentId));
        }
        var detail = foundWrongTitle.Count > 0
            ? $"It targets {string.Join(", ", foundWrongTitle.Distinct())}, not {expectedTitleId}."
            : "No matching sce_sys\\param.sfo with a CONTENT_ID was found.";
        throw new InvalidDataException(detail);
    }

    private static string SanitizeDirectoryName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Trim();
    }

    private static string EnsureExtractedPackage(string path, string expectedTitleId, bool patch)
    {
        var candidates = new List<string> { path };
        candidates.AddRange(Directory.EnumerateFiles(path, "param.sfo", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "sce_sys", StringComparison.OrdinalIgnoreCase))
            .Select(file => Directory.GetParent(Path.GetDirectoryName(file)!)!.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        var collectedErrors = new List<string>();
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            ValidateGameFolder(candidate, expectedTitleId, patch, errors, warnings);
            if (errors.Count == 0) return candidate;
            collectedErrors.AddRange(errors);
        }
        throw new InvalidDataException("The extracted PKG is not the selected Rock Band 4 release: " +
            string.Join(" ", collectedErrors.Distinct()));
    }

    private static void ValidateGameFolder(string path, string expectedTitleId, bool patch, List<string> errors, List<string> warnings)
    {
        if (!Directory.Exists(path)) { errors.Add($"Game folder not found: {path}"); return; }
        var sfo = Path.Combine(path, "sce_sys", "param.sfo");
        if (!File.Exists(sfo)) { errors.Add($"{Path.GetFileName(path)} is missing sce_sys\\param.sfo."); return; }
        if (!File.Exists(Path.Combine(path, "eboot.bin"))) warnings.Add($"{Path.GetFileName(path)} does not contain eboot.bin.");
        try
        {
            var values = ParamSfoReader.Read(sfo);
            if (values.TryGetValue("TITLE_ID", out var titleId) && !titleId.Equals(expectedTitleId, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Selected source is {titleId}, but {expectedTitleId} was chosen.");
            if (patch && values.TryGetValue("APP_VER", out var version) && version != "02.21")
                warnings.Add($"The selected update reports version {version}; RB4 Deluxe expects 02.21.");
        }
        catch (Exception ex) { errors.Add($"Could not read param.sfo: {ex.Message}"); }
    }

    private static string? FindDirectory(string root, string name) =>
        new DirectoryInfo(root).Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? root
            : Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories).FirstOrDefault();

    private static int CountFiles(string path) => File.Exists(path) ? 1 : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

}
