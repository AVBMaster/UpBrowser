using System.IO;
using System.Runtime.InteropServices;

namespace UpBrowser.Core;

/// <summary>
/// Resolves the per-user application data directory. The Windows path stays on
/// the WinXP-safe <see cref="WindowsFolderProvider"/>; Linux/macOS use the
/// platform-standard config locations, since
/// <see cref="Environment.SpecialFolder.ApplicationData"/> returns an empty
/// string on some Linux distributions (no XDG_CONFIG_HOME set).
/// </summary>
public static class AppPaths
{
    public static string AppDataDir => GetAppDataDirectory("UpBrowser");

    public static string GetAppDataDirectory(string appName)
    {
        string root;
#if WINDOWS
        root = WindowsFolderProvider.GetAppDataPath();
#else
        root = GetNonWindowsConfigRoot();
#endif
        return string.IsNullOrEmpty(root)
            ? Path.Combine(Path.GetTempPath(), appName)
            : Path.Combine(root, appName);
    }

    private static string GetNonWindowsConfigRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? FallbackRoot() : Path.Combine(home, "Library", "Application Support");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return xdg;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            return appData;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(userProfile) ? FallbackRoot() : Path.Combine(userProfile, ".config");
    }

    private static string FallbackRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local) ? Path.GetTempPath() : local;
    }
}
