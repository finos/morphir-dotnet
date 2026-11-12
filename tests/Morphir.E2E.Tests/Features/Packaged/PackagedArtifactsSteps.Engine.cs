// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Features.Packaged;

/// <summary>
/// The WASM engine of each packaged offering (add-wasm-extension-host, task 5.2): the engine files against the build's
/// record, the runtime report and a worker session from the installed artifact, and the same from a second installation
/// that is read-only before its first run.
/// </summary>
public sealed partial class PackagedArtifactsSteps
{
    private const string InstallationKey = "PackagedOwnInstallation";
    private static readonly TimeSpan WorkerLimit = TimeSpan.FromSeconds(120);

    private ReadOnlyTree? _readOnly;
    private IReadOnlyList<string>? _filesBeforeFirstRun;
    private TestSandbox? _workerSandbox;
    private string? _workerTemp;
    private WorkerRun? _workerRun;

    private WorkerRun WorkerRun => _workerRun ?? throw new InvalidOperationException("No packaged worker session in this scenario.");

    [Then("the engine libraries in the installed artifact are exactly the separate libraries its build contents record")]
    public void ThenTheEngineLibrariesAreThoseRecorded()
    {
        var installation = Installation;
        var recorded = installation.Artifact.RecordedEngines();
        var described = recorded is null ? "none" : string.Join("; ", recorded.Select(engine => $"{engine.Path} {engine.Platform} {engine.Delivery} {engine.Sha256}"));
        PackagedEngineLayout.Problems(installation.Directory, installation.ArtifactRoot, installation.Artifact.Deployment, new ExecutableLocator().CurrentRid, recorded)
            .Should().BeEmpty($"{installation.Artifact.BuildContentsPath} records the engines {described}");
    }

    [Then("the installed artifact reports its WASM runtime availability with the pinned engine on this platform")]
    public void ThenTheArtifactReportsTheWasmRuntime()
    {
        Result.ExitCode.Should().Be(0, Result.Describe());
        using var report = JsonDocument.Parse(Result.StandardOutput);
        var root = report.RootElement;
        root.GetProperty("host").GetString().Should().Be(ExecutableLocator.HostFor(ExecutableLocator.CurrentType));
        root.GetProperty("platform").GetString().Should().Be(new ExecutableLocator().CurrentRid, "the report comes from the running executable");
        var wasm = root.GetProperty("runtimes").EnumerateArray().Should().ContainSingle().Subject;
        wasm.GetProperty("kind").GetString().Should().Be("wasm");
        var windowsX64 = root.GetProperty("platform").GetString() == "win-x64";
        wasm.GetProperty("available").GetBoolean().Should().Be(!windowsX64, Result.StandardOutput);
        if (windowsX64)
            wasm.GetProperty("reason").GetString().Should().Be("win_x64_trap_unsupported");
        var version = ExecutableExtensionRuntimesTests.PackageVersion();
        wasm.GetProperty("engine").GetProperty("name").GetString().Should().Be("wasmtime");
        wasm.GetProperty("engine").GetProperty("version").GetString().Should().Be(version, "the engine is the pinned Wasmtime package's");
        wasm.GetProperty("profiles").EnumerateArray().Select(profile => profile.GetString()).Should().Equal("extism-kernel-v1");
        Installation.Artifact.RecordedEngines().Should().NotBeNull().And.OnlyContain(engine => engine.Version == version,
            "the artifact carries the engine of the version it reports");
    }

    [When("the installed artifact serves the pinned module \"(.*)\" with the frames \"(.*)\" in a worker session")]
    public async Task WhenTheArtifactServesAWorkerSession(string module, string frames)
    {
        var runtimeReport = await PackagedRun.RunAsync(Installation, ["extension", "runtimes", "--json"], RuntimeAccess.Visible);
        runtimeReport.ExitCode.Should().Be(0, runtimeReport.Describe());
        if (ExecutableExtensionRuntimesTests.IsWindowsX64(runtimeReport.StandardOutput))
            Skip.Test("win_x64_trap_unsupported: the installed executable cannot run guests; #609.");

        var sandbox = _workerSandbox = TestSandbox.Create("morphir-packaged-worker-");
        var work = Directory.CreateDirectory(Path.Combine(sandbox.Root, PackagedRun.WorkDirectoryName)).FullName;
        var temp = _workerTemp = Directory.CreateDirectory(Path.Combine(sandbox.Root, "temp dir")).FullName;
        var guest = Path.Combine(Directory.CreateDirectory(Path.Combine(work, "guest modules")).FullName, $"{module}.wasm");
        File.Copy(WorkerCorpus.ModulePath(module), guest);
        ReleaseFiles.Sha256(guest).Should().Be(WorkerCorpus.ModuleSha256(module), "the module is the pinned crafted one");

        // The same containment as every packaged run: a working directory whose path has a space, a sandboxed home, and
        // here also a temporary directory of the session's own, so its workspace can be seen and its removal checked.
        void Contain(ProcessStartInfo start)
        {
            start.WorkingDirectory = work;
            sandbox.Apply(start, new SandboxOptions { Boundary = sandbox.Root });
            start.Environment["TEMP"] = temp;
            start.Environment["TMP"] = temp;
            start.Environment["TMPDIR"] = temp;
        }

        var launch = Installation.LaunchPath;
        string identity;
        await using (var help = await WorkerProcess.StartAsync(launch, ["--help", "--json"], configure: Contain))
        {
            var result = await help.WaitForExitAsync(WorkerLimit);
            result.ExitCode.Should().Be(0, result.Describe());
            using var document = JsonDocument.Parse(result.Stdout);
            identity = document.RootElement.GetProperty("result").GetProperty("buildIdentity").GetString()!;
        }

        identity.Should().MatchRegex("^[0-9a-f]{64}$", "extension worker --help --json gives the build identity");
        await using var worker = await WorkerProcess.StartAsync(launch,
            ["--abi", "extism-kernel-v1", "--module", guest, "--build-identity", identity, "--json"], configure: Contain);
        await worker.WriteAsync([.. frames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => WorkerProcess.Frame(WorkerCorpus.FrameBody(name)))]);
        _workerRun = await worker.WaitForExitAsync(WorkerLimit);
    }

    [Then("the packaged worker session exits with status (\\d+)")]
    public void ThenThePackagedWorkerSessionExitsWith(int status) => WorkerRun.ExitCode.Should().Be(status, WorkerRun.Describe());

    [Then("the packaged worker session's stdout holds only these frames:")]
    public void ThenThePackagedWorkerSessionsStdoutHoldsOnly(DataTable table)
    {
        var (frames, problem) = WorkerRun.Frames;
        problem.Should().BeNull(WorkerRun.Describe());
        frames.Select(frame => Encoding.UTF8.GetString(frame)).Should().Equal(table.Rows.Select(row => row["frame"]));
    }

    [Then("the packaged worker session's stderr has the events \"(.*)\"")]
    public void ThenThePackagedWorkerSessionsStderrHasTheEvents(string names) =>
        WorkerRun.EventNames.Should().Equal(names.Split(", "), WorkerRun.Describe());

    [Then("the packaged worker session's temporary directory is empty")]
    public void ThenThePackagedWorkerSessionsTemporaryDirectoryIsEmpty() =>
        Directory.EnumerateFileSystemEntries(_workerTemp!).Should().BeEmpty("the worker removes its session workspace when it exits, and nothing is extracted there");

    [Given("a second installation of the artifact under test, made read-only before its first run")]
    public void GivenASecondReadOnlyInstallation()
    {
        var installation = PackagedInstallation.InstallAnother(ExecutableLocator.CurrentType, "read-only install");
        scenario[InstallationKey] = installation;
        installation.Directory.Should().Contain(" ", "the installation path contains a space");
        _filesBeforeFirstRun = ReadOnlyTree.Snapshot(installation.Directory);
        _readOnly = ReadOnlyTree.Apply(installation.Directory);
        ReadOnlyTree.WriteProblems(installation.Directory).Should().BeEmpty("the installation is read-only for this user before its first run");
    }

    [Then("the read-only installation holds the same files as before its first run")]
    public void ThenTheReadOnlyInstallationIsUnchanged() =>
        ReadOnlyTree.Snapshot(Installation.Directory).Should().Equal(_filesBeforeFirstRun, "nothing is extracted or written into the installation at first run");

    /// <summary>Gives the read-only installation back its permissions, so it can be deleted, whatever the scenario did.</summary>
    [AfterScenario]
    public void RestoreTheReadOnlyInstallation()
    {
        try
        {
            _readOnly?.Dispose();
        }
        finally
        {
            _workerSandbox?.Dispose();
        }
    }
}
