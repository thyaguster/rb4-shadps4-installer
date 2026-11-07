using System.Diagnostics;

namespace RB4ShadPS4Installer.Services;

internal static class ShadPs4ProcessGuard
{
    private static readonly string[] ProcessNames = ["shadPS4", "shadPS4QtLauncher"];

    public static IReadOnlyList<string> FindRunning()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (!process.HasExited) found.Add(process.ProcessName);
                    }
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return found.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
