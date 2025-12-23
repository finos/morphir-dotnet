using System.Runtime.InteropServices;

namespace Morphir.Tooling.Configuration;

/// <summary>
/// Provides OS-specific paths for configuration and cache directories.
/// </summary>
public static class OsPaths
{
    /// <summary>
    /// Gets the global configuration directory based on the operating system.
    /// - Windows: %APPDATA%\Morphir
    /// - Linux: $XDG_CONFIG_HOME/morphir or ~/.config/morphir
    /// - macOS: ~/Library/Application Support/morphir
    /// </summary>
    public static string GetGlobalConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Morphir");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "morphir");
        }

        // Linux/Unix
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, "morphir");
        }

        var homeLinux = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeLinux, ".config", "morphir");
    }

    /// <summary>
    /// Gets the global cache directory based on the operating system.
    /// - Windows: %LOCALAPPDATA%\Morphir\Cache
    /// - Linux: $XDG_CACHE_HOME/morphir or ~/.cache/morphir
    /// - macOS: ~/Library/Caches/morphir
    /// </summary>
    public static string GetGlobalCacheDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Morphir", "Cache");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Caches", "morphir");
        }

        // Linux/Unix
        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgCacheHome))
        {
            return Path.Combine(xdgCacheHome, "morphir");
        }

        var homeLinux = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeLinux, ".cache", "morphir");
    }
}
