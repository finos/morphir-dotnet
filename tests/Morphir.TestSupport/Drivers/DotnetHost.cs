// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The dotnet host that starts an assembly of the product. A driver starts it by its full path: on Linux the process
/// runner starts each child through setsid, which looks up its program in the PATH of the child, and a scenario can
/// give the product a PATH with no dotnet.
/// </summary>
public static class DotnetHost
{
    /// <summary>
    /// <c>DOTNET_HOST_PATH</c> when it is set, else the executable of this process when it is the dotnet host, else the
    /// name <c>dotnet</c>.
    /// </summary>
    public static string Path { get; } = Resolve(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"), Environment.ProcessPath);

    public static string Resolve(string? hostPath, string? processPath)
    {
        if (!string.IsNullOrEmpty(hostPath)) return hostPath;
        var name = processPath?.Replace('\\', '/').Split('/')[^1];
        return name is "dotnet" or "dotnet.exe" ? processPath! : "dotnet";
    }
}
