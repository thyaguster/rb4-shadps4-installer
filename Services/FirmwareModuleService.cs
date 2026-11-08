namespace RB4ShadPS4Installer.Services;

internal sealed record FirmwareModuleStatus(
    string Directory,
    int SupportedInstalled,
    int SupportedTotal,
    int AdditionalInstalled,
    IReadOnlyList<string> MissingSupported);

internal sealed record FirmwareModuleImportResult(
    int Imported,
    int Recognized,
    int Additional,
    int SkippedInvalid);

internal static class FirmwareModuleService
{
    public const string OfficialGuideUrl =
        "https://github.com/shadps4-emu/shadPS4/wiki/I.-Quick-start-%5BUsers%5D#4-dumping-firmware-modules";

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "libSceAt9Enc.sprx",
        "libSceAudiodec.sprx",
        "libSceAudiodecCpu.sprx",
        "libSceAudiodecCpuDdp.sprx",
        "libSceAudiodecCpuDtsHdLbr.sprx",
        "libSceAudiodecCpuHevag.sprx",
        "libSceAudiodecCpuM4aac.sprx",
        "libSceAvPlayer.sprx",
        "libSceAvPlayerStreaming.sprx",
        "libSceBeisobmf.sprx",
        "libSceBemp2sys.sprx",
        "libSceCesCs.sprx",
        "libSceDepth.sprx",
        "libSceFont.sprx",
        "libSceFontFt.sprx",
        "libSceFreeTypeOl.sprx",
        "libSceFreeTypeOptOl.sprx",
        "libSceFreeTypeOt.sprx",
        "libSceJpegDec.sprx",
        "libSceJpegEnc.sprx",
        "libSceJson.sprx",
        "libSceJson2.sprx",
        "libSceLibcInternal.sprx",
        "libSceMoveTracker.sprx",
        "libSceNgs2.sprx",
        "libScePadTracker.sprx",
        "libScePngEnc.sprx",
        "libScePsmKitSystem.sprx",
        "libSceRtc.sprx",
        "libSceRudp.sprx",
        "libSceSystemGesture.sprx",
        "libSceUlt.sprx",
        "libSceWkFontConfig.sprx",
        "libSceXml.sprx"
    };

    public static FirmwareModuleStatus GetStatus(string userDirectory)
    {
        var directory = Path.Combine(userDirectory, "sys_modules");
        var installed = EnumerateModules(directory)
            .Select(path => Path.GetFileName(path))
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supportedInstalled = installed.Count(Supported.Contains);
        var missing = Supported.Where(name => !installed.Contains(name))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new FirmwareModuleStatus(directory, supportedInstalled, Supported.Count,
            installed.Count - supportedInstalled, missing);
    }

    public static IReadOnlyList<string> FindImportable(string sourceDirectory) =>
        EnumerateModules(sourceDirectory).Where(IsElf).ToArray();

    public static FirmwareModuleImportResult Import(
        string sourceDirectory,
        string userDirectory)
    {
        var destination = Path.Combine(userDirectory, "sys_modules");
        var candidates = EnumerateModules(sourceDirectory);
        Directory.CreateDirectory(destination);
        var imported = 0;
        var recognized = 0;
        var additional = 0;
        var skipped = 0;
        var copiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in candidates)
        {
            if (!IsElf(source))
            {
                skipped++;
                continue;
            }

            var name = Path.GetFileName(source);
            if (!copiedNames.Add(name)) continue;
            var target = Path.Combine(destination, name);
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, overwrite: true);
            imported++;
            if (Supported.Contains(name)) recognized++;
            else additional++;
        }

        return new FirmwareModuleImportResult(imported, recognized, additional, skipped);
    }

    private static string[] EnumerateModules(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];
        try
        {
            return Directory.EnumerateFiles(directory, "*.sprx", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            }).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static bool IsElf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == magic.Length &&
                   magic[0] == 0x7f && magic[1] == (byte)'E' &&
                   magic[2] == (byte)'L' && magic[3] == (byte)'F';
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
