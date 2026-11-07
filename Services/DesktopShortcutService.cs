using System.Reflection;
using System.Runtime.InteropServices;

namespace RB4ShadPS4Installer.Services;

internal static class DesktopShortcutService
{
    public static string DesktopDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string CreateLauncherShortcut(string launcher)
    {
        ValidateLauncher(launcher);
        return CreateShortcut(
            "RB4 shadPS4",
            launcher,
            string.Empty,
            "Open the shadPS4 launcher configured for Rock Band 4");
    }

    public static string CreateGameShortcut(string launcher, string gameDirectory)
    {
        ValidateLauncher(launcher);
        var eboot = Path.Combine(gameDirectory, "eboot.bin");
        if (!File.Exists(eboot))
            throw new FileNotFoundException("The detected Rock Band 4 installation has no eboot.bin.", eboot);

        var isQtLauncher = Path.GetFileName(launcher).Contains("QtLauncher", StringComparison.OrdinalIgnoreCase);
        var arguments = isQtLauncher
            ? $"-d -g {Quote(eboot)}"
            : $"-g {Quote(eboot)}";
        return CreateShortcut(
            "Rock Band 4 (shadPS4)",
            launcher,
            arguments,
            "Launch Rock Band 4 directly in shadPS4");
    }

    private static string CreateShortcut(string name, string target, string arguments, string description)
    {
        var desktop = DesktopDirectory;
        if (string.IsNullOrWhiteSpace(desktop))
            throw new DirectoryNotFoundException("Windows could not locate the current user's Desktop folder.");
        Directory.CreateDirectory(desktop);
        var shortcutPath = Path.Combine(desktop, name + ".lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable, so a desktop shortcut could not be created.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows could not initialize its shortcut service.");
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath])
                ?? throw new InvalidOperationException("Windows could not create the shortcut object.");
            var shortcutType = shortcut.GetType();
            SetProperty(shortcutType, shortcut, "TargetPath", target);
            SetProperty(shortcutType, shortcut, "Arguments", arguments);
            SetProperty(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(target)!);
            SetProperty(shortcutType, shortcut, "Description", description);
            SetProperty(shortcutType, shortcut, "IconLocation", target + ",0");
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            return shortcutPath;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void SetProperty(Type type, object target, string name, string value) =>
        type.InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static void ValidateLauncher(string launcher)
    {
        if (string.IsNullOrWhiteSpace(launcher) || !File.Exists(launcher))
            throw new FileNotFoundException("Select or download a valid shadPS4 application before creating shortcuts.", launcher);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
