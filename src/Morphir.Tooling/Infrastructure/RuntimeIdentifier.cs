using System.Runtime.InteropServices;

namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// Provides utilities for detecting and working with .NET Runtime Identifiers (RIDs).
/// </summary>
public static class RuntimeIdentifier
{
    /// <summary>
    /// Gets the current platform's Runtime Identifier in .NET RID format.
    /// Examples: linux-x64, win-x64, osx-arm64
    /// </summary>
    /// <returns>The current platform's RID</returns>
    public static string GetCurrentRid()
    {
        var os = GetOsComponent();
        var arch = GetArchComponent();
        return $"{os}-{arch}";
    }

    /// <summary>
    /// Gets the OS component of the RID.
    /// </summary>
    private static string GetOsComponent()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "osx";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";

        // Fallback to best effort
        return RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ? "freebsd" : "unknown";
    }

    /// <summary>
    /// Gets the architecture component of the RID.
    /// </summary>
    private static string GetArchComponent()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Validates whether a RID string is in a valid format.
    /// </summary>
    /// <param name="rid">The RID to validate</param>
    /// <returns>True if the RID is valid, false otherwise</returns>
    public static bool IsValidRid(string rid)
    {
        if (string.IsNullOrWhiteSpace(rid))
            return false;

        var parts = rid.Split('-');
        if (parts.Length != 2)
            return false;

        var validOsParts = new[] { "win", "linux", "osx", "freebsd" };
        var validArchParts = new[] { "x64", "x86", "arm64", "arm" };

        return validOsParts.Contains(parts[0]) && validArchParts.Contains(parts[1]);
    }
}
