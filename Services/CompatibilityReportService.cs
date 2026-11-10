using System.Text.RegularExpressions;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static partial class CompatibilityReportService
{
    public static CompatibilityReport Analyze(InstallRequest request, IReadOnlyList<InstalledContentItem> installed, bool deluxeInstalled)
    {
        var items = new List<CompatibilityReportItem>();
        if (!string.IsNullOrWhiteSpace(request.BaseGameSource)) items.Add(AnalyzeOne("Base game", request.BaseGameSource, request.TitleId, "Base game", installed, false, deluxeInstalled));
        if (!string.IsNullOrWhiteSpace(request.PatchSource)) items.Add(AnalyzeOne("Game update", request.PatchSource, request.TitleId, "Game update", installed, false, deluxeInstalled));
        if (!string.IsNullOrWhiteSpace(request.DeluxeSource))
            items.Add(new("RB4 Deluxe", request.DeluxeSource, File.Exists(request.DeluxeSource) ? "ZIP" : "Folder",
                request.TitleId, "RB4DX mod", "Not applicable", "Not applicable", "Requires RB4 2.21",
                "Archive layout verified during installation", CompatibilitySeverity.Ready, "RB4DX source selected."));
        for (var index = 0; index < request.AddonSources.Count; index++)
            items.Add(AnalyzeOne($"Additional content {index + 1}", request.AddonSources[index], request.TitleId,
                "DLC / custom song", installed, true, deluxeInstalled));
        return new(items);
    }

    private static CompatibilityReportItem AnalyzeOne(string label, string source, string expectedTitleId,
        string expectedType, IReadOnlyList<InstalledContentItem> installed, bool addon, bool deluxeInstalled)
    {
        if (!File.Exists(source) && !Directory.Exists(source))
            return new(label, source, "Missing", "Unknown", expectedType, "Unknown", "Unknown",
                addon ? "RB4DX required" : "No RB4DX requirement", "Source unavailable",
                CompatibilitySeverity.Blocked, "The selected source no longer exists.");
        var isPackage = PackageExtractor.IsPackage(source);
        var sourceType = isPackage ? "PKG" : "Extracted folder";
        var region = "Unknown";
        var contentId = "Unknown";
        var contentType = expectedType;
        var confidence = isPackage ? "Filename evidence; metadata verified after extraction" : "Verified param.sfo metadata";
        var severity = isPackage ? CompatibilitySeverity.Attention : CompatibilitySeverity.Ready;
        var finding = isPackage ? "Encrypted PKG metadata will be validated after extraction." : "Metadata matches the selected game region.";

        if (isPackage)
        {
            var name = Path.GetFileNameWithoutExtension(source);
            var regionMatch = TitleIdRegex().Match(name);
            if (regionMatch.Success) region = regionMatch.Value.ToUpperInvariant();
            var contentMatch = ContentIdRegex().Match(name);
            if (contentMatch.Success) contentId = contentMatch.Value;
        }
        else
        {
            try
            {
                var candidates = Directory.EnumerateFiles(source, "param.sfo", SearchOption.AllDirectories)
                    .Where(file => string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "sce_sys", StringComparison.OrdinalIgnoreCase))
                    .Select(file => (File: file, Values: ParamSfoReader.Read(file))).ToArray();
                var sfo = candidates.FirstOrDefault(candidate =>
                    candidate.Values.GetValueOrDefault("TITLE_ID")?.Equals(expectedTitleId, StringComparison.OrdinalIgnoreCase) == true);
                if (sfo == default) sfo = candidates.FirstOrDefault();
                if (sfo == default) throw new InvalidDataException("No sce_sys\\param.sfo was found.");
                var values = sfo.Values;
                region = values.GetValueOrDefault("TITLE_ID") ?? "Unknown";
                contentId = values.GetValueOrDefault("CONTENT_ID") ?? "Unknown";
                contentType = DescribeCategory(values.GetValueOrDefault("CATEGORY"), expectedType);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                severity = CompatibilitySeverity.Blocked;
                confidence = "Metadata could not be verified";
                finding = ex.Message;
            }
        }

        if (region != "Unknown" && !region.Equals(expectedTitleId, StringComparison.OrdinalIgnoreCase))
        {
            severity = CompatibilitySeverity.Blocked;
            finding = $"Targets {region}, but the selected game is {expectedTitleId}.";
        }
        var duplicate = contentId != "Unknown" && installed.Any(item =>
            Normalize(item.ContentId).Equals(Normalize(contentId), StringComparison.OrdinalIgnoreCase) ||
            Normalize(Path.GetFileName(item.Folder)).Equals(Normalize(contentId), StringComparison.OrdinalIgnoreCase));
        var duplicateState = duplicate ? "Already installed" : contentId == "Unknown" ? "Unknown until extraction" : "Not installed";
        var requirement = addon ? (deluxeInstalled ? "RB4DX detected" : "RB4DX required") : "No RB4DX requirement";
        if (addon && !deluxeInstalled)
        {
            severity = CompatibilitySeverity.Blocked;
            finding = "Additional-content imports stay locked until RB4DX is installed.";
        }
        else if (duplicate && severity == CompatibilitySeverity.Ready)
            finding = "Already installed; it will be skipped unless reinstall is enabled.";

        return new(label, source, sourceType, region, contentType, contentId, duplicateState,
            requirement, confidence, severity, finding);
    }

    private static string DescribeCategory(string? category, string fallback) => category?.ToLowerInvariant() switch
    {
        "ac" => "Additional content",
        "gd" => "Base game",
        "gp" => "Game update",
        _ => fallback
    };

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());

    [GeneratedRegex(@"CUSA\d{5}", RegexOptions.IgnoreCase)]
    private static partial Regex TitleIdRegex();
    [GeneratedRegex(@"[A-Z]{2}\d{4}-CUSA\d{5}_\d{2}-[A-Z0-9_]+", RegexOptions.IgnoreCase)]
    private static partial Regex ContentIdRegex();
}
