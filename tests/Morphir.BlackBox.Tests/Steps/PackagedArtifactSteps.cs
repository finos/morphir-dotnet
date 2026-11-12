// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Morphir.TestSupport.Packaging;
using Reqnroll.UnitTestProvider;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of the four packaged offerings, run from their installed release artifacts, and of the WASM engine in
/// each offering (add-deployment-and-acquisition task 2.4, add-wasm-extension-host task 5.2).
/// </summary>
[Binding]
public sealed class PackagedArtifactSteps(DriverSet drivers, IScenarioWorkspace workspace, IUnitTestRuntimeProvider runtime, WorkerRun worker)
{
    private static readonly TimeSpan WorkerLimit = TimeSpan.FromSeconds(120);

    private PackagedRunResult? _result;
    private RuntimeAccess _access = RuntimeAccess.Visible;
    private IReadOnlyList<string>? _filesBeforeFirstRun;

    private IPackagedArtifactDriver Packaged => drivers.Packaged;

    private PackagedInstallation Installation => Packaged.Installation;

    /// <summary>The installation directory of the test.</summary>
    private string InstallFolder => Installation.Directory;

    private PackagedRunResult Result => _result ?? throw new InvalidOperationException("No packaged run in this scenario.");

    [Given("the packaged artifact of the run is installed into a new directory whose path contains a space")]
    public void TheArtifactIsInstalled()
    {
        var launch = System.IO.Path.GetRelativePath(InstallFolder, Installation.LaunchPath).Replace('\\', '/');
        Expect(InstallFolder.Contains(' ', StringComparison.Ordinal), $"The installation path {InstallFolder} contains no space.");
        Expect(Packaged.Snapshot().Any(entry => entry.StartsWith(launch + " ", StringComparison.Ordinal)), $"{Installation.LaunchPath} is the launch path, and it is not installed.");
    }

    [Then("the archive matches the SHA-256 in its launch metadata")]
    public void TheArchiveMatches() =>
        Expect(ReleaseFiles.Sha256(Installation.Artifact.ArchivePath) == Installation.Artifact.Sha256, $"{Installation.Artifact.ArchivePath} does not have the SHA-256 {Installation.Artifact.Sha256}.");

    [Then("the launch path of the launch metadata is in the installed directory")]
    public void TheLaunchPathIsInstalled() =>
        Expect(System.IO.Path.GetFullPath(Installation.LaunchPath).StartsWith(System.IO.Path.GetFullPath(InstallFolder), StringComparison.Ordinal),
            $"{Installation.LaunchPath} is not in {InstallFolder}.");

    [Then("the installed layout is what the offering claims")]
    public void TheLayoutIsWhatTheOfferingClaims() => ExpectNone(Packaged.LayoutProblems());

    [When("I run the installed artifact with {string} from a new empty directory")]
    public async Task RunFromAnEmptyDirectory(string arguments) => await RunAsync(arguments.Split(' '), RuntimeAccess.Visible);

    [When("I run the installed artifact with {string} with no .NET runtime reachable")]
    public async Task RunWithoutARuntime(string arguments)
    {
        // A runtime at a location that this run cannot hide would make the scenario prove nothing, so it is skipped
        // with the reason, never passed.
        if (Packaged.HidingPlan is { Mode: RuntimeHidingMode.NotPossible } plan)
            runtime.TestIgnore($"No .NET runtime can be hidden on this machine: {plan.Reason}.");
        await RunAsync(arguments.Split(' '), RuntimeAccess.Hidden);
    }

    [When("I run the installed artifact with {string} with the .NET runtime of the machine reachable")]
    public async Task RunWithTheMachineRuntime(string arguments) => await RunAsync(arguments.Split(' '), RuntimeAccess.Visible);

    [When("I compile an F# source file with the installed artifact")]
    public async Task CompileFSharp()
    {
        var work = Packaged.NewWorkDirectory();
        workspace.WriteText(work.Folder.File("my sources", "Rates.fs"), "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n");
        await RunAsync(["compile", "--language", "fsharp", "--input", "my sources/Rates.fs", "--package-name", "acme/demo", "--json"], RuntimeAccess.Visible, work);
    }

    [When("I validate the model fixture {string} with the installed artifact")]
    public async Task ValidateTheModelFixture(string fixture)
    {
        // The input file is given by an absolute path that contains spaces.
        var work = Packaged.NewWorkDirectory();
        var input = workspace.CopyFixture(fixture, $"{work.RelativePath}/model inputs/{System.IO.Path.GetFileName(fixture)}");
        await RunAsync(["validate", "--input", input.Path, "--json"], RuntimeAccess.Visible, work);
    }

    private async Task RunAsync(string[] arguments, RuntimeAccess access, PackagedWorkDirectory? work = null)
    {
        var directory = work ?? Packaged.NewWorkDirectory();
        if (work is null) Expect(workspace.EntriesIn(directory.Folder).Count == 0, $"{directory.Folder} is not empty.");
        Expect(directory.Folder.Path.Contains(' ', StringComparison.Ordinal), "The working directory path contains no space.");
        _access = access;
        _result = await Packaged.RunAsync(arguments, access, directory);
    }

    [Then("the installed artifact exits with code {int}")]
    public void TheArtifactExitsWith(int code) => Expect(Result.ExitCode == code, $"The installed artifact exits with {Result.ExitCode}, not {code}.\n{Result.Describe()}");

    [Then("the installed artifact prints the release of its launch metadata")]
    public void TheArtifactPrintsItsRelease() =>
        Expect(Result.StandardOutput.Trim().Split('+')[0] == Installation.Artifact.Release, $"The installed artifact does not print {Installation.Artifact.Release}.\n{Result.Describe()}");

    [Then("the installed artifact lists the extensions of its host")]
    public void TheArtifactListsItsHost()
    {
        using var report = JsonDocument.Parse(Result.StandardOutput);
        var ids = report.RootElement.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()).ToHashSet();
        Expect(report.RootElement.GetProperty("host").GetString() == ExtensionListReport.HostName(drivers.Host)
            && new[] { "morphir.csharp", "morphir.fsharp", "morphir.type-validator" }.All(ids.Contains), $"The extension list is not the list of the host.\n{Result.Describe()}");
    }

    [Then("the F# compile follows the in-process F# frontend of the launch metadata")]
    public void TheFSharpCompileFollowsTheFrontend()
    {
        using var report = JsonDocument.Parse(Result.StandardOutput);
        if (Installation.Artifact.InProcessFSharpFrontend)
        {
            Expect(Result.ExitCode == 0 && report.RootElement.GetProperty("success").GetBoolean()
                && report.RootElement.GetProperty("frontend").GetString() == "morphir-dotnet.fsharp (in-process)", Result.Describe());
        }
        else
        {
            // The refusal names where an F# frontend is available.
            var diagnostic = report.RootElement.GetProperty("diagnostics").EnumerateArray().Single();
            var message = diagnostic.GetProperty("message").GetString() ?? "";
            Expect(Result.ExitCode == 2 && diagnostic.GetProperty("code").GetString() == "compile.frontend_unavailable"
                && message.Contains("dotnet-morphir", StringComparison.Ordinal) && message.Contains("--extension", StringComparison.Ordinal), Result.Describe());
        }
    }

    [Then("the installed artifact runs without a runtime as its launch metadata says")]
    public async Task TheArtifactRunsWithoutARuntime() => Accept(await Packaged.RunsWithoutARuntimeAsync(Result));

    [Then("the installed artifact uses the runtime that its launch metadata declares")]
    public async Task TheArtifactUsesTheDeclaredRuntime() => Accept(await Packaged.UsesTheDeclaredRuntimeAsync(Result, _access));

    [Then("the engine libraries in the installed artifact are exactly the separate libraries that its build contents record")]
    public void TheEngineLibrariesAreThoseRecorded() => ExpectNone(Packaged.EngineProblems());

    [Then("the installed artifact reports its WASM runtime availability with the pinned engine on this platform")]
    public void TheArtifactReportsTheWasmRuntime()
    {
        Expect(Result.ExitCode == 0, Result.Describe());
        var report = ExtensionRuntimesReport.Of(new CommandResult(Installation.LaunchPath, Result.ExitCode, Result.StandardOutput, Result.StandardError, TimeSpan.Zero, []));
        var version = PinnedPackages.VersionOf(workspace.ReadText(workspace.CopyFixture("Directory.Packages.props")), "Wasmtime");
        Expect(report.Host == ExtensionListReport.HostName(drivers.Host), $"The runtime report names the host {report.Host}.");
        Expect(report.Platform == RuntimeIdentifiers.Current, "The report comes from the running executable, so it names the platform of this machine.");
        Expect(report.Wasm.Kind == "wasm" && report.Wasm.Available == !report.IsWindowsX64, Result.StandardOutput);
        if (report.IsWindowsX64) Expect(report.Wasm.Reason == ExtensionRuntimesReport.WindowsX64Reason, Result.StandardOutput);
        Expect(report.Wasm.EngineName == "wasmtime" && report.Wasm.EngineVersion == version, $"The engine is not the engine {version} of the pinned Wasmtime package.");
        Expect(report.Wasm.Profiles.SequenceEqual(["extism-kernel-v1"]), Result.StandardOutput);
        Expect(Installation.Artifact.RecordedEngines() is { } recorded && recorded.All(engine => engine.Version == version),
            "The artifact does not carry the engine of the version that it reports.");
    }

    [When("the installed artifact serves the pinned module {string} with the frames {string} in a worker session")]
    public async Task TheArtifactServesAWorkerSession(string module, string frames)
    {
        var runtimes = await Packaged.RunAsync(["extension", "runtimes", "--json"], RuntimeAccess.Visible, Packaged.NewWorkDirectory());
        Expect(runtimes.ExitCode == 0, runtimes.Describe());
        if (runtimes.StandardOutput.Contains("\"platform\":\"win-x64\"", StringComparison.Ordinal))
            runtime.TestIgnore($"{ExtensionRuntimesReport.WindowsX64Reason}: the installed executable cannot run guests; #609.");

        // The same containment as each packaged run: a working directory whose path has a space, a contained home, and
        // here also a temporary directory of the session of its own, so its workspace can be seen and its removal checked.
        var work = Packaged.NewWorkDirectory();
        var temp = worker.Temp = workspace.MakeDirectory("temp dir");
        var guest = workspace.CopyFixture(WorkerCorpus.ModuleFixture(module), $"{work.RelativePath}/guest modules/{module}.wasm");
        Expect(Convert.ToHexStringLower(SHA256.HashData(workspace.ReadBytes(guest))) == WorkerCorpus.ModuleSha256(module), "The module is not the pinned crafted one.");
        var options = new ProcessSessionOptions
        {
            WorkingDirectory = work.Folder.Path,
            Environment = new Dictionary<string, string?> { ["TEMP"] = temp.Path, ["TMP"] = temp.Path, ["TMPDIR"] = temp.Path },
        };

        var help = await ExtensionWorker.HelpAsync(Packaged.Sessions, options);
        Expect(help.ExitCode == 0, help.Describe());
        using var document = JsonDocument.Parse(help.StandardOutput);
        var identity = document.RootElement.GetProperty("result").GetProperty("buildIdentity").GetString()!;
        Expect(System.Text.RegularExpressions.Regex.IsMatch(identity, "^[0-9a-f]{64}$"), "extension worker --help --json does not give the build identity.");

        await using var session = await Packaged.Sessions.StartAsync(ExtensionWorker.ServeArguments(guest.Path, identity), options);
        await session.WriteAsync(ExtensionWorker.Frames(frames));
        worker.Exit = await session.WaitForExitAsync(WorkerLimit);
    }

    [Given("a second installation of the artifact of the run, made read-only before its first run")]
    public void ASecondReadOnlyInstallation()
    {
        Packaged.InstallAReadOnlyCopy();
        Expect(InstallFolder.Contains(' ', StringComparison.Ordinal), "The installation path contains no space.");
        _filesBeforeFirstRun = Packaged.Snapshot();
        ExpectNone(Packaged.WriteProblems());
    }

    [Then("the read-only installation holds the same files as before its first run")]
    public void TheReadOnlyInstallationIsUnchanged() =>
        Expect(Packaged.Snapshot().SequenceEqual(_filesBeforeFirstRun!), "Something was extracted or written into the installation at its first run.");

    private void Accept(PackagedCheck check)
    {
        switch (check)
        {
            case PackagedCheck.NotPossible notPossible:
                runtime.TestIgnore(notPossible.Reason);
                break;
            case PackagedCheck.Done done:
                ExpectNone(done.Problems);
                break;
        }
    }

    private static void ExpectNone(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0) throw new InvalidOperationException(string.Join("\n", problems));
    }

    private static void Expect(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
