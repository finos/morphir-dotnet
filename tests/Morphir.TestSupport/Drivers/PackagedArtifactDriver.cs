// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.TestSupport.Packaging;

namespace Morphir.TestSupport.Drivers;

/// <summary>A new empty working directory of a packaged run: below the work area, with a space in its path.</summary>
/// <param name="Folder">The directory.</param>
/// <param name="RelativePath">Its path relative to the work area, for <see cref="IScenarioWorkspace.CopyFixture"/>.</param>
public sealed record PackagedWorkDirectory(WorkspaceDirectory Folder, string RelativePath);

/// <summary>The result of a check that can be impossible on this machine.</summary>
public abstract record PackagedCheck
{
    private PackagedCheck() { }

    /// <summary>The check passed, or it failed with these problems (empty when it passed).</summary>
    public sealed record Done(IReadOnlyList<string> Problems) : PackagedCheck;

    /// <summary>This machine cannot make the check: the test is skipped with the reason, never passed.</summary>
    public sealed record NotPossible(string Reason) : PackagedCheck;
}

/// <summary>
/// The installed packaged offering of the run (design DRV-2 of the change consolidate-black-box-tests): its launch
/// metadata, runs of the installed executable from new empty directories in the workspace (with the .NET runtime of
/// the machine reachable or hidden, each with a host trace), the layout claim of the offering, its WASM engine files,
/// and a second installation of the test that is read-only before its first run. Only a packaged target has it.
/// </summary>
public interface IPackagedArtifactDriver
{
    /// <summary>The installation that the test uses: the installation of the run, or the own installation of the test.</summary>
    PackagedInstallation Installation { get; }

    /// <summary>The session driver of <see cref="Installation"/>, contained in the workspace.</summary>
    IProcessSessionDriver Sessions { get; }

    /// <summary>A new empty working directory below the work area, with a space in its path.</summary>
    PackagedWorkDirectory NewWorkDirectory();

    /// <summary>Runs the installed executable with <paramref name="arguments"/> in <paramref name="workDirectory"/>.</summary>
    Task<PackagedRunResult> RunAsync(IReadOnlyList<string> arguments, RuntimeAccess access, PackagedWorkDirectory workDirectory);

    /// <summary>The way this machine hides its .NET runtime, or why it cannot.</summary>
    RuntimeHidingPlan HidingPlan { get; }

    /// <summary>What contradicts the layout that the offering claims; empty when the layout agrees.</summary>
    IReadOnlyList<string> LayoutProblems();

    /// <summary>What contradicts the engines that the build contents of the artifact record; empty when the files agree.</summary>
    IReadOnlyList<string> EngineProblems();

    /// <summary>The check that <paramref name="run"/>, made with the runtime hidden, ran as the launch metadata says.</summary>
    Task<PackagedCheck> RunsWithoutARuntimeAsync(PackagedRunResult run);

    /// <summary>The check that <paramref name="run"/>, made with the runtime of the machine reachable, used the declared runtime.</summary>
    Task<PackagedCheck> UsesTheDeclaredRuntimeAsync(PackagedRunResult run, RuntimeAccess access);

    /// <summary>
    /// Makes a second installation of the offering in the workspace, read-only for this user before its first run, and
    /// makes it the installation of the test. The end of the test gives the permissions back.
    /// </summary>
    void InstallAReadOnlyCopy();

    /// <summary>The writes into the own installation that succeeded; empty when it is read-only.</summary>
    IReadOnlyList<string> WriteProblems();

    /// <summary>Each folder and file with its size of the installation, relative and in ordinal order.</summary>
    IReadOnlyList<string> Snapshot();
}

/// <summary>The packaged artifact driver on the installation of a <see cref="PackagedTarget"/>.</summary>
public sealed class PackagedArtifactDriver : IPackagedArtifactDriver, IAsyncDisposable
{
    private readonly PackagedTarget _target;
    private readonly TestTarget _testTarget;
    private readonly MorphirHost _host;
    private readonly IScenarioWorkspace _workspace;
    private ReadOnlyTree? _readOnly;
    private int _runs;

    public PackagedArtifactDriver(PackagedTarget target, TestTarget testTarget, MorphirHost host, IScenarioWorkspace workspace)
    {
        _target = target;
        _testTarget = testTarget;
        _host = host;
        _workspace = workspace;
        Installation = target.Installation;
        Sessions = new ProcessSessionDriver(testTarget, host, Installation.LaunchPath, workspace);
    }

    public PackagedInstallation Installation { get; private set; }

    public IProcessSessionDriver Sessions { get; private set; }

    public RuntimeHidingPlan HidingPlan => PackagedRun.HidingPlanForThisMachine();

    public PackagedWorkDirectory NewWorkDirectory()
    {
        var relative = $"run {Interlocked.Increment(ref _runs)} {PackagedRun.WorkDirectoryName}";
        return new PackagedWorkDirectory(_workspace.MakeDirectory(relative), relative);
    }

    public Task<PackagedRunResult> RunAsync(IReadOnlyList<string> arguments, RuntimeAccess access, PackagedWorkDirectory workDirectory)
    {
        var traces = System.IO.Directory.CreateDirectory(Path.Combine(_workspace.Root, "host traces")).FullName;
        return PackagedRun.RunAsync(
            Installation.LaunchPath, Installation.Directory, arguments, access, _workspace.Contain,
            _workspace.Root, workDirectory.Folder.Path, Path.Combine(traces, $"{Guid.NewGuid():N}.txt"));
    }

    public IReadOnlyList<string> LayoutProblems()
    {
        var installation = Installation;
        var files = System.IO.Directory.GetFiles(installation.Directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(installation.Directory, path).Replace('\\', '/'))
            .ToList();
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] runtime = [RuntimeLibrary("hostfxr"), RuntimeLibrary("coreclr"), "System.Private.CoreLib.dll"];
        var problems = new List<string>();
        void Contains(params string[] expected) =>
            problems.AddRange(expected.Where(name => !names.Contains(name)).Select(name => $"The installation has no {name}."));
        void ContainsNone(params string[] unexpected) =>
            problems.AddRange(unexpected.Where(names.Contains).Select(name => $"The installation has {name}."));

        switch (installation.Artifact.Deployment)
        {
            case "native-aot":
                // One native file. Not a .NET single-file bundle, which would also be one file: the executable has no
                // bundle signature and carries the Native AOT runtime's debug header export.
                if (files.Count != 1 || files[0] != installation.Artifact.LaunchPath)
                    problems.Add($"The installation has the files {string.Join(", ", files)}, not only {installation.Artifact.LaunchPath}.");
                ContainsNone(runtime);
                if (ExecutableStructure.Of(installation.LaunchPath) is var kind and not ExecutableKind.NativeAot)
                    problems.Add($"{installation.LaunchPath} is {kind}, not a Native AOT executable.");
                break;
            case "self-contained":
                // A folder that carries its runtime and the F# compiler service, untrimmed.
                Contains(["morphir.dll", "morphir.runtimeconfig.json", "FSharp.Compiler.Service.dll", .. runtime]);
                break;
            case "framework-dependent":
                // A folder without a runtime; its runtimeconfig names the shared framework.
                Contains("morphir.dll", "morphir.runtimeconfig.json", "FSharp.Compiler.Service.dll");
                ContainsNone(runtime);
                if (!File.ReadAllText(Path.Combine(installation.Directory, "morphir.runtimeconfig.json")).Contains("Microsoft.NETCore.App", StringComparison.Ordinal))
                    problems.Add("morphir.runtimeconfig.json does not name Microsoft.NETCore.App.");
                break;
            case "dotnet-tool":
                // A .NET tool installation: the dotnet-morphir apphost and the tool store with the managed composition.
                Contains("dotnet-morphir.dll", "Morphir.Tooling.Managed.dll", "FSharp.Compiler.Service.dll");
                ContainsNone(["morphir.dll", .. runtime]);
                break;
            default:
                problems.Add($"No layout claim for {installation.Artifact.Deployment}.");
                break;
        }

        return problems;
    }

    public IReadOnlyList<string> EngineProblems()
    {
        var recorded = Installation.Artifact.RecordedEngines();
        return PackagedEngineLayout.Problems(Installation.Directory, Installation.ArtifactRoot, Installation.Artifact.Deployment, _target.Locations.Rid, recorded);
    }

    public async Task<PackagedCheck> RunsWithoutARuntimeAsync(PackagedRunResult run)
    {
        var artifact = Installation.Artifact;
        if (artifact.BundledRuntime)
        {
            var problems = new List<string>();
            if (run.ExitCode != 0) problems.Add($"The run exits with {run.ExitCode}, not 0.\n{run.Describe()}");
            if (run.StandardOutput.Trim().Split('+')[0] != artifact.Release) problems.Add($"The run does not print the release {artifact.Release}.\n{run.Describe()}");
            problems.AddRange(await NoMachineRuntimeProblemsAsync(run, RuntimeAccess.Hidden).ConfigureAwait(false));
            return new PackagedCheck.Done(problems);
        }

        if (run.HostTrace is not { } trace) return new PackagedCheck.Done([$"The apphost wrote no host trace.\n{run.Describe()}"]);
        if (run.ExitCode == 0)
        {
            // The apphost found a runtime although the run hid each one that it knew. If it came from a registered or
            // default location, this machine's runtime could not be hidden after all: skipped, not passed.
            var fxr = HostTrace.ResolvedFxr(trace);
            var unhideable = PackagedRun.RegisteredRuntimeRoots().Concat(PackagedRun.DefaultRuntimeRoots()).ToList();
            if (fxr is not null && unhideable.Any(root => HostTrace.IsUnder(fxr, root)))
                return new PackagedCheck.NotPossible($"The apphost still found the runtime at {fxr}, a location this run could not hide.");
            return new PackagedCheck.Done([$"The runtime was hidden, yet the apphost started with hostfxr {fxr}.\n{run.Describe()}"]);
        }

        // The application cannot start, so the apphost's message is all a user sees. The installation guide repeats
        // what to do (docs/content/docs/getting-started/installation.md).
        var missing = new List<string>();
        if (run.ExitCode != FrameworkMissingExitCode) missing.Add($"The run exits with {run.ExitCode}, not {FrameworkMissingExitCode}.\n{run.Describe()}");
        if (!run.StandardError.Contains("You must install .NET to run this application", StringComparison.Ordinal)
            || !run.StandardError.Contains("https://aka.ms/dotnet-core-applaunch", StringComparison.Ordinal))
            missing.Add($"Standard error does not say how to install .NET.\n{run.Describe()}");
        if (OperatingSystem.IsWindows() && run.Environment.GetValueOrDefault("ProgramW6432") is { } emptyRoot)
        {
            // The default location the apphost looked in is the empty folder, so the hiding reached the host.
            var location = HostTrace.DefaultLocation(trace);
            if (location is null || !HostTrace.IsUnder(location, emptyRoot))
                missing.Add($"The default location {location ?? "(none)"} is not the empty {emptyRoot}.\n{trace}");
        }

        return new PackagedCheck.Done(missing);
    }

    public async Task<PackagedCheck> UsesTheDeclaredRuntimeAsync(PackagedRunResult run, RuntimeAccess access)
    {
        var artifact = Installation.Artifact;
        var problems = new List<string>();
        if (run.ExitCode != 0) return new PackagedCheck.Done([$"The run exits with {run.ExitCode}, not 0.\n{run.Describe()}"]);
        if (artifact.BundledRuntime)
        {
            // Even with a machine runtime reachable, a bundled offering never uses it.
            problems.AddRange(await NoMachineRuntimeProblemsAsync(run, access).ConfigureAwait(false));
            return new PackagedCheck.Done(problems);
        }

        if (artifact.RequiredRuntime != "Microsoft.NETCore.App 11.0") problems.Add($"The launch metadata declares {artifact.RequiredRuntime ?? "no runtime"}.");
        if (run.HostTrace is not { } trace) return new PackagedCheck.Done([.. problems, "The apphost wrote no host trace."]);
        var fxr = HostTrace.ResolvedFxr(trace);
        if (fxr is null) problems.Add($"The host trace names no hostfxr.\n{trace}");
        else if (!PackagedRun.MachineRuntimeRoots().Any(root => HostTrace.IsUnder(fxr, root)))
            problems.Add($"hostfxr {fxr} is not below a runtime of the machine: a framework-dependent offering runs on the machine's shared runtime.");
        // The runtimeconfig asks for Microsoft.NETCore.App 11.0.0 with the default roll-forward (Minor): the newest
        // 11.0 patch, or a higher 11.x minor when no 11.0 patch is installed, never 12.
        if (HostTrace.ChosenFramework(trace) is not { } chosen || !chosen.StartsWith("11.", StringComparison.Ordinal))
            problems.Add($"The host chose the framework {HostTrace.ChosenFramework(trace) ?? "(none)"}, not an 11.x version.\n{trace}");
        return new PackagedCheck.Done(problems);
    }

    /// <summary>
    /// The process loaded no runtime from the machine. native-aot ran no .NET host at all: no host trace file, no host
    /// tracing on standard error (where the host writes when it cannot open the file), an executable that is Native AOT
    /// and not a single-file bundle, and a control apphost run through the same containment that does write a trace.
    /// self-contained resolved hostfxr and CoreCLR inside its installation only.
    /// </summary>
    private async Task<IReadOnlyList<string>> NoMachineRuntimeProblemsAsync(PackagedRunResult run, RuntimeAccess access)
    {
        var installation = Installation;
        var problems = new List<string>();
        if (installation.Artifact.Execution == "native")
        {
            if (run.HostTrace is not null) problems.Add("A Native AOT executable runs no .NET host, yet the run wrote a host trace.");
            if (HostTrace.IsTracingOutput(run.StandardError)) problems.Add($"Standard error has .NET host tracing.\n{run.Describe()}");
            if (ExecutableStructure.Of(installation.LaunchPath) is var kind and not ExecutableKind.NativeAot)
                problems.Add($"{installation.LaunchPath} is {kind}, not a Native AOT executable.");
            var control = await PackagedRun.RunAsync(
                PackagedRun.ControlAppHost(), AppContext.BaseDirectory, ["--help"], access, _workspace.Contain,
                _workspace.Root, NewWorkDirectory().Folder.Path,
                Path.Combine(System.IO.Directory.CreateDirectory(Path.Combine(_workspace.Root, "host traces")).FullName, $"control {Guid.NewGuid():N}.txt")).ConfigureAwait(false);
            if (control.HostTrace is null || !control.HostTrace.Contains("--- Invoked apphost", StringComparison.Ordinal))
                problems.Add($"A known apphost run the same way writes no host trace, so the trace variables do not work here.\n{control.Describe()}");
            return problems;
        }

        if (run.HostTrace is not { } trace) return ["The apphost wrote no host trace."];
        return HostTrace.BundledRuntimeViolations(trace, installation.Directory, PackagedRun.MachineRuntimeRoots());
    }

    public void InstallAReadOnlyCopy()
    {
        var directory = Path.Combine(_workspace.Root, "read-only install");
        var installation = _target.InstallAnother(directory);
        Installation = installation;
        Sessions = new ProcessSessionDriver(_testTarget, _host, installation.LaunchPath, _workspace);
        _readOnly = ReadOnlyTree.Apply(installation.Directory);
    }

    public IReadOnlyList<string> WriteProblems() => ReadOnlyTree.WriteProblems(Installation.Directory);

    public IReadOnlyList<string> Snapshot() => ReadOnlyTree.Snapshot(Installation.Directory);

    /// <summary>Gives the read-only installation back its permissions, so the workspace can be removed.</summary>
    public ValueTask DisposeAsync()
    {
        _readOnly?.Dispose();
        _readOnly = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>What the apphost returns when it finds no .NET: 0x80008083 on Windows; a Unix shell sees its low byte, 131.</summary>
    private static int FrameworkMissingExitCode => OperatingSystem.IsWindows() ? unchecked((int)0x80008083) : 0x83;

    private static string RuntimeLibrary(string name) =>
        OperatingSystem.IsWindows() ? $"{name}.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.dylib" : $"lib{name}.so";
}
