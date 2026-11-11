// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>Whether a packaged run can reach the machine's .NET runtime.</summary>
public enum RuntimeAccess
{
    /// <summary>The test process's environment: the declared runtime is reachable as it is for this user.</summary>
    Visible,

    /// <summary>No .NET runtime reachable, by <see cref="PackagedRun.HidingPlanForThisMachine"/>.</summary>
    Hidden,
}

/// <summary>How a run hides the machine's .NET runtime.</summary>
public enum RuntimeHidingMode
{
    /// <summary>The child's environment: no <c>DOTNET_*</c>, no <c>dotnet</c> on PATH, and on Windows the default location moved.</summary>
    Environment,

    /// <summary>A clean Linux container with no .NET (<see cref="PackagedRun.ContainerVariable"/>).</summary>
    Container,

    /// <summary>This machine has a runtime that this run cannot hide; the scenario is skipped with the reason.</summary>
    NotPossible,
}

/// <summary>The way a hidden-runtime run hides the runtime on this machine, or why it cannot.</summary>
public sealed record RuntimeHidingPlan(RuntimeHidingMode Mode, string Reason);

/// <summary>The result of one packaged run, with the .NET host trace when the host wrote one.</summary>
public sealed record PackagedRunResult(int ExitCode, string StandardOutput, string StandardError, string? HostTrace, IReadOnlyDictionary<string, string?> Environment)
{
    public string Describe() =>
        $"exit {ExitCode}\nstdout:\n{StandardOutput}\nstderr:\n{StandardError}\nPATH={Environment.GetValueOrDefault("PATH")}";
}

/// <summary>
/// Runs an installed artifact in a new sandbox: a working directory whose path contains a space, a contained Morphir
/// home, argument lists without a shell, no window, and the process tree killed on timeout. Every run records a .NET
/// host trace.
/// </summary>
public static class PackagedRun
{
    /// <summary>The image of the clean Linux container for hidden-runtime runs, for example <c>mcr.microsoft.com/dotnet/runtime-deps:10.0</c>.</summary>
    public const string ContainerVariable = "MORPHIR_E2E_RUNTIME_CONTAINER";

    /// <summary>The working directory of a run, below the sandbox root; its name contains a space.</summary>
    public const string WorkDirectoryName = "work dir";

    internal static string CreateWorkingDirectory(string sandboxRoot) =>
        CanonicalPath.Of(Directory.CreateDirectory(Path.Combine(sandboxRoot, WorkDirectoryName)).FullName);

    public static Task<PackagedRunResult> RunAsync(
        PackagedInstallation installation, string[] arguments, RuntimeAccess access, Action<string>? prepare = null) =>
        RunLaunchAsync(installation.LaunchPath, installation.Directory, () => arguments, access, prepare);

    /// <summary>A run whose arguments are taken after <paramref name="prepare"/> has filled the working directory.</summary>
    public static Task<PackagedRunResult> RunAsync(
        PackagedInstallation installation, Func<string[]> arguments, RuntimeAccess access, Action<string>? prepare = null) =>
        RunLaunchAsync(installation.LaunchPath, installation.Directory, arguments, access, prepare);

    /// <summary>
    /// Runs the test host's own apphost with <c>--help</c> through the same sandbox, environment and trace settings as
    /// <see cref="RunAsync"/>. A run that writes a host trace shows that the trace variables work in this environment,
    /// so a Native AOT run without a trace means that no .NET host ran.
    /// </summary>
    public static Task<PackagedRunResult> TraceControlAsync(RuntimeAccess access)
    {
        var apphost = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Morphir.E2E.Tests.exe" : "Morphir.E2E.Tests");
        if (!File.Exists(apphost)) throw new InvalidOperationException($"The control apphost {apphost} does not exist.");
        return RunLaunchAsync(apphost, AppContext.BaseDirectory, () => ["--help"], access, prepare: null);
    }

    private static async Task<PackagedRunResult> RunLaunchAsync(
        string launchPath, string installationDirectory, Func<string[]> arguments, RuntimeAccess access, Action<string>? prepare)
    {
        await using var sandbox = TestSandbox.Create("morphir-packaged-");
        var work = CreateWorkingDirectory(sandbox.Root);
        prepare?.Invoke(work);
        var trace = Path.Combine(sandbox.Root, "host-trace.txt");

        var start = ChildProcess.StartInfo(launchPath, arguments());
        start.WorkingDirectory = work;
        sandbox.Apply(start, new SandboxOptions { Boundary = sandbox.Root });
        start.Environment["COREHOST_TRACE"] = "1";
        start.Environment["COREHOST_TRACEFILE"] = trace;
        if (access == RuntimeAccess.Hidden)
        {
            var plan = HidingPlanForThisMachine();
            switch (plan.Mode)
            {
                case RuntimeHidingMode.NotPossible:
                    throw new InvalidOperationException($"The runtime cannot be hidden here: {plan.Reason}");
                case RuntimeHidingMode.Container:
                    HideRuntime(start, Directory.CreateDirectory(Path.Combine(sandbox.Root, "no program files")).FullName);
                    start = InContainer(start, System.Environment.GetEnvironmentVariable(ContainerVariable)!,
                        readOnly: [installationDirectory], writable: [sandbox.Root], user: UnixUser());
                    break;
                default:
                    HideRuntime(start, Directory.CreateDirectory(Path.Combine(sandbox.Root, "no program files")).FullName);
                    break;
            }
        }

        var result = await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(3));
        return new PackagedRunResult(result.ExitCode, result.StandardOutput, result.StandardError,
            File.Exists(trace) ? await File.ReadAllTextAsync(trace) : null,
            start.Environment.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>
    /// How this machine's runtime can be hidden. The apphost searches, in order: <c>DOTNET_ROOT*</c>, the registered
    /// location (the Windows registry, or <c>/etc/dotnet/install_location</c> on Linux and macOS), and the default
    /// location. A run can drop the variables. On Windows it can move the default location, which the apphost reads
    /// from <c>ProgramW6432</c> (verified with the .NET 10 apphost on win-arm64), but not the registered one. On
    /// Linux and macOS it can move neither, so Linux uses a clean container when <see cref="ContainerVariable"/> names
    /// one.
    /// </summary>
    public static RuntimeHidingPlan HidingPlanForThisMachine()
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        return PlanHiding(os, RegisteredRuntimeRoots(), DefaultRuntimeRoots(), System.Environment.GetEnvironmentVariable(ContainerVariable) is { Length: > 0 } image ? image : null);
    }

    /// <summary>The plan for an operating system with these registered and default locations that hold a runtime.</summary>
    public static RuntimeHidingPlan PlanHiding(string os, IReadOnlyList<string> registered, IReadOnlyList<string> defaults, string? containerImage)
    {
        switch (os)
        {
            case "windows" when registered.Count > 0:
                return new(RuntimeHidingMode.NotPossible,
                    $"a .NET runtime is registered at {string.Join(", ", registered)} (HKLM\\SOFTWARE\\dotnet\\Setup\\InstalledVersions), and an environment cannot hide a registered location");
            case "windows":
                return new(RuntimeHidingMode.Environment, "no registered location; the default location is moved with ProgramW6432");
        }

        var unhideable = registered.Concat(defaults).Distinct(StringComparer.Ordinal).ToList();
        if (unhideable.Count == 0)
            return new(RuntimeHidingMode.Environment, "no registered or default location holds a runtime");
        if (os == "linux" && containerImage is not null)
            return new(RuntimeHidingMode.Container, $"a runtime at {string.Join(", ", unhideable)}; the run uses the clean container {containerImage}");
        return new(RuntimeHidingMode.NotPossible,
            $"a .NET runtime is at {string.Join(", ", unhideable)}, a registered or default location that an environment cannot hide" +
            (os == "linux" ? $"; set {ContainerVariable} to a container image without .NET, for example mcr.microsoft.com/dotnet/runtime-deps:10.0" : ""));
    }

    /// <summary>
    /// Hides the machine's runtime from the child: removes every <c>DOTNET_*</c> variable, sets
    /// <c>DOTNET_MULTILEVEL_LOOKUP=0</c>, drops every PATH entry that holds a <c>dotnet</c> executable, and on Windows
    /// points <c>ProgramW6432</c> at <paramref name="emptyDefaultRoot"/> so the apphost's default location is empty.
    /// </summary>
    public static void HideRuntime(ProcessStartInfo start, string emptyDefaultRoot)
    {
        foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(name);
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        var path = start.Environment.TryGetValue("PATH", out var value) ? value ?? "" : "";
        start.Environment["PATH"] = string.Join(Path.PathSeparator, path.Split(Path.PathSeparator).Where(entry => entry.Length > 0 && !ContainsDotnet(entry)));
        if (OperatingSystem.IsWindows()) start.Environment["ProgramW6432"] = emptyDefaultRoot;
    }

    /// <summary>
    /// The same run inside a Linux container: the installation mounted read-only and the sandbox writable, both at
    /// their own paths so the trace paths match; no network; only the sandbox, Morphir and host-trace variables.
    /// </summary>
    public static ProcessStartInfo InContainer(ProcessStartInfo start, string image, IEnumerable<string> readOnly, IEnumerable<string> writable, string? user)
    {
        var container = ChildProcess.StartInfo("docker", "run", "--rm", "--network", "none");
        if (user is not null)
        {
            container.ArgumentList.Add("--user");
            container.ArgumentList.Add(user);
        }

        foreach (var directory in readOnly)
        {
            container.ArgumentList.Add("--volume");
            container.ArgumentList.Add($"{directory}:{directory}:ro");
        }

        foreach (var directory in writable)
        {
            container.ArgumentList.Add("--volume");
            container.ArgumentList.Add($"{directory}:{directory}");
        }

        container.ArgumentList.Add("--workdir");
        container.ArgumentList.Add(start.WorkingDirectory);
        foreach (var (key, value) in start.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (value is null || !PassedToContainer(key)) continue;
            container.ArgumentList.Add("--env");
            container.ArgumentList.Add($"{key}={value}");
        }

        container.ArgumentList.Add(image);
        container.ArgumentList.Add(start.FileName);
        foreach (var argument in start.ArgumentList) container.ArgumentList.Add(argument);
        return container;
    }

    /// <summary>
    /// The machine's .NET installation roots that this process can see: <c>DOTNET_ROOT*</c>, the host that runs the
    /// tests, every <c>dotnet</c> on PATH, and the registered and default locations.
    /// </summary>
    public static IReadOnlyList<string> MachineRuntimeRoots()
    {
        var roots = new List<string?>();
        foreach (var name in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)" })
            roots.Add(System.Environment.GetEnvironmentVariable(name));
        roots.Add(Path.GetDirectoryName(System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? ""));
        roots.Add(Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")));
        roots.AddRange((System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(ContainsDotnet));
        roots.AddRange(RegisteredRuntimeRoots());
        roots.AddRange(DefaultRuntimeRoots());
        return [.. roots.Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)).Select(root => Path.GetFullPath(root!)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The registered locations that hold a runtime: on Windows <c>InstallLocation</c> under
    /// <c>HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\&lt;arch&gt;</c> (the 32-bit registry view, which the host reads),
    /// elsewhere the paths in <c>/etc/dotnet/install_location</c> and <c>install_location_&lt;arch&gt;</c>.
    /// </summary>
    public static IReadOnlyList<string> RegisteredRuntimeRoots()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            if (hklm.OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}")?.GetValue("InstallLocation") is string location)
                candidates.Add(location);
        }
        else if (Directory.Exists("/etc/dotnet"))
        {
            foreach (var file in Directory.GetFiles("/etc/dotnet", "install_location*"))
                candidates.AddRange(File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0));
        }

        return [.. candidates.Where(HoldsRuntime)];
    }

    /// <summary>
    /// The default locations that hold a runtime: <c>%ProgramW6432%\dotnet</c> on Windows, <c>/usr/local/share/dotnet</c>
    /// on macOS, <c>/usr/share/dotnet</c> and <c>/usr/lib/dotnet</c> on Linux, with the <c>x64</c> subfolder that an x64
    /// process uses under emulation on ARM64.
    /// </summary>
    public static IReadOnlyList<string> DefaultRuntimeRoots()
    {
        string[] bases = OperatingSystem.IsWindows()
            ? [Path.Combine(System.Environment.GetEnvironmentVariable("ProgramW6432") ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "dotnet")]
            : OperatingSystem.IsMacOS() ? ["/usr/local/share/dotnet"] : ["/usr/share/dotnet", "/usr/lib/dotnet"];
        var emulated = RuntimeInformation.ProcessArchitecture == Architecture.X64 && RuntimeInformation.OSArchitecture == Architecture.Arm64;
        return [.. bases.Select(root => emulated ? Path.Combine(root, "x64") : root).Where(HoldsRuntime)];
    }

    private static bool HoldsRuntime(string root) => Directory.Exists(Path.Combine(root, "host", "fxr"));

    private static bool PassedToContainer(string key) =>
        key is "HOME" or "DOTNET_MULTILEVEL_LOOKUP"
        || key.StartsWith("MORPHIR_", StringComparison.Ordinal)
        || key.StartsWith("XDG_", StringComparison.Ordinal)
        || key.StartsWith("COREHOST_", StringComparison.Ordinal);

    private static bool ContainsDotnet(string directory) =>
        File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    private static string? UnixUser() =>
        OperatingSystem.IsWindows() ? null : $"{getuid()}:{getgid()}";

    [DllImport("libc")]
    private static extern uint getuid();

    [DllImport("libc")]
    private static extern uint getgid();
}
