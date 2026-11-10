// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>Whether a packaged run can reach the machine's .NET runtime.</summary>
public enum RuntimeAccess
{
    /// <summary>The test process's environment: the declared runtime is reachable as it is for this user.</summary>
    Visible,

    /// <summary>
    /// No <c>dotnet</c> on PATH, no <c>DOTNET_*</c> variable, and <c>DOTNET_MULTILEVEL_LOOKUP=0</c>. A runtime at the
    /// apphost's registered or default location stays reachable; <see cref="PackagedRun.UnhideableRuntimeRoots"/> names it.
    /// </summary>
    Hidden,
}

/// <summary>The result of one packaged run, with the .NET host trace when the host wrote one.</summary>
public sealed record PackagedRunResult(int ExitCode, string StandardOutput, string StandardError, string? HostTrace, IReadOnlyDictionary<string, string?> Environment)
{
    public string Describe() =>
        $"exit {ExitCode}\nstdout:\n{StandardOutput}\nstderr:\n{StandardError}\nPATH={Environment.GetValueOrDefault("PATH")}";
}

/// <summary>
/// Runs an installed artifact in a new sandbox: an empty working directory, a contained Morphir home, argument lists
/// without a shell, no window, and the process tree killed on timeout. Every run records a .NET host trace.
/// </summary>
public static class PackagedRun
{
    public static async Task<PackagedRunResult> RunAsync(
        PackagedInstallation installation, string[] arguments, RuntimeAccess access, Action<string>? prepare = null)
    {
        await using var sandbox = TestSandbox.Create("morphir-packaged-");
        prepare?.Invoke(sandbox.Work);
        var trace = Path.Combine(sandbox.Root, "host-trace.txt");

        var start = ChildProcess.StartInfo(installation.LaunchPath, arguments);
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start, new SandboxOptions { Boundary = sandbox.Root });
        start.Environment["COREHOST_TRACE"] = "1";
        start.Environment["COREHOST_TRACEFILE"] = trace;
        if (access == RuntimeAccess.Hidden) HideRuntime(start);

        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(3));
        return new PackagedRunResult(result.ExitCode, result.StandardOutput, result.StandardError,
            File.Exists(trace) ? await File.ReadAllTextAsync(trace) : null,
            start.Environment.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>
    /// The machine's .NET installation roots that this process can see: <c>DOTNET_ROOT*</c>, the host that runs the
    /// tests, every <c>dotnet</c> on PATH, and the registered and default locations.
    /// </summary>
    public static IReadOnlyList<string> MachineRuntimeRoots()
    {
        var roots = new List<string?>();
        foreach (var name in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)" })
            roots.Add(Environment.GetEnvironmentVariable(name));
        roots.Add(Path.GetDirectoryName(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? ""));
        roots.Add(Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")));
        roots.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(ContainsDotnet));
        roots.AddRange(UnhideableRuntimeRoots());
        return [.. roots.Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)).Select(root => Path.GetFullPath(root!)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Runtime locations that an apphost searches whatever the environment says: the default install location and,
    /// on Linux and macOS, the location that <c>/etc/dotnet/install_location</c> registers. On Windows the registry
    /// entry is not read here; its usual target is the default location.
    /// </summary>
    public static IReadOnlyList<string> UnhideableRuntimeRoots()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
        }
        else
        {
            candidates.AddRange(OperatingSystem.IsMacOS() ? ["/usr/local/share/dotnet"] : ["/usr/share/dotnet", "/usr/lib/dotnet"]);
            foreach (var file in Directory.Exists("/etc/dotnet") ? Directory.GetFiles("/etc/dotnet", "install_location*") : [])
                candidates.AddRange(File.ReadAllLines(file).Where(line => line.Trim().Length > 0).Select(line => line.Trim()));
        }
        return [.. candidates.Where(candidate => Directory.Exists(Path.Combine(candidate, "host", "fxr")))];
    }

    private static void HideRuntime(ProcessStartInfo start)
    {
        foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(name);
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        var path = start.Environment.TryGetValue("PATH", out var value) ? value ?? "" : "";
        start.Environment["PATH"] = string.Join(Path.PathSeparator, path.Split(Path.PathSeparator).Where(entry => entry.Length > 0 && !ContainsDotnet(entry)));
    }

    private static bool ContainsDotnet(string directory) =>
        File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
}
