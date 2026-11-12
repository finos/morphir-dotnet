// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Reqnroll.UnitTestProvider;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>extension runtimes</c> and of a host that starts without its engine library (task 4.2 of
/// add-wasm-extension-host). The steps give the arguments of the report type to <see cref="ICliDriver"/>.
/// </summary>
[Binding]
public sealed class ExtensionRuntimesSteps(DriverSet drivers, IScenarioWorkspace workspace, ScenarioRun run, IUnitTestRuntimeProvider runtime)
{
    private (HostCopy Copy, ICliDriver Cli, IProcessSessionDriver Sessions)? _copy;
    private SessionExit? _worker;
    private ExtensionRuntimesReport? _platform;

    private ExtensionRuntimesReport Report => ExtensionRuntimesReport.Of(run.Result);

    private (HostCopy Copy, ICliDriver Cli, IProcessSessionDriver Sessions) Copy =>
        _copy ?? throw new InvalidOperationException("The scenario has no copy of the host before this step.");

    private SessionExit Worker => _worker ?? throw new InvalidOperationException("The copy served no worker before this step.");

    private string PinnedWasmtime => PinnedPackages.VersionOf(workspace.ReadText(workspace.CopyFixture("Directory.Packages.props")), "Wasmtime");

    /// <summary>The reason of a host with no engine: engine_missing, or the refusal of Windows x64 (#609).</summary>
    private string MissingEngineReason => (_platform ?? Report).IsWindowsX64 ? ExtensionRuntimesReport.WindowsX64Reason : "engine_missing";

    [Given("a copy of the host without its engine library")]
    public async Task ACopyOfTheHostWithoutItsEngineLibrary()
    {
        var copy = drivers.WithoutEngineLibraries();
        if (copy.Copy.Deleted.Count == 0)
            runtime.TestIgnore($"{copy.Copy.Source} carries no engine library file to delete (the engine is linked into the Native AOT executable, or a single-file bundle embeds it); run this scenario on a target that loads the engine from a file.");
        _copy = copy;
        _platform = ExtensionRuntimesReport.Of((await drivers.Cli.RunAsync(ExtensionRuntimesReport.Arguments)).ShouldExitWith(0));
    }

    [When("I ask for the runtimes of the host as JSON")]
    public async Task AskForTheRuntimesOfTheHost() => run.Result = await drivers.Cli.RunAsync(ExtensionRuntimesReport.Arguments);

    [When("I ask for the runtimes of the copy as JSON")]
    public async Task AskForTheRuntimesOfTheCopy() => run.Result = await Copy.Cli.RunAsync(ExtensionRuntimesReport.Arguments);

    [When("I run {string} with the copy")]
    public async Task RunWithTheCopy(string command)
    {
        // A model path of the command is a fixture that the workspace copies first.
        var arguments = command.Split(' ').Select(argument => argument.StartsWith("Models/", StringComparison.Ordinal) ? workspace.CopyFixture(argument).Path : argument).ToArray();
        run.Result = await Copy.Cli.RunAsync(arguments);
    }

    [When("the copy serves {string} with the frames {string}")]
    public async Task TheCopyServes(string module, string frames)
    {
        var identity = await ExtensionWorker.BuildIdentityAsync(Copy.Sessions);
        await using var worker = await Copy.Sessions.StartAsync(ExtensionWorker.ServeArguments(WorkerCorpus.ModulePath(module), identity));
        await worker.WriteAsync(ExtensionWorker.Frames(frames));
        _worker = await worker.WaitForExitAsync(TimeSpan.FromSeconds(60));
    }

    [Then("the runtime report names the host of the run")]
    public void TheReportNamesTheHost() => Expect(Report.Host == ExtensionListReport.HostName(drivers.Host), $"The runtime report names the host {Report.Host}.");

    [Then("the WASM runtime is available exactly when the platform can run guests")]
    public void TheRuntimeIsAvailableExactlyWhenThePlatformCanRunGuests()
    {
        Expect(Report.Wasm.Available == !Report.IsWindowsX64, $"The WASM runtime is {(Report.Wasm.Available ? "" : "not ")}available on {Report.Platform}.");
        if (Report.IsWindowsX64) Expect(Report.Wasm.Reason == ExtensionRuntimesReport.WindowsX64Reason, $"The reason is {Report.Wasm.Reason}.");
    }

    [Then("the WASM runtime has the engine of the pinned Wasmtime package, the profile {string} and {long} bytes of memory")]
    public void TheRuntimeHasThePinnedEngine(string profile, long memory)
    {
        var wasm = Report.Wasm;
        Expect(wasm.EngineName == "wasmtime" && wasm.EngineVersion == PinnedWasmtime, $"The engine is {wasm.EngineName} {wasm.EngineVersion}, not wasmtime {PinnedWasmtime}.");
        Expect(wasm.Profiles.SequenceEqual([profile]), $"The profiles are {string.Join(", ", wasm.Profiles)}, not {profile}.");
        Expect(wasm.MemoryBytes == memory, $"The memory bound is {wasm.MemoryBytes}, not {memory}.");
    }

    [Then("the WASM runtime is not available, with the reason of a missing engine on this platform")]
    public void TheRuntimeIsNotAvailable() =>
        Expect(!Report.Wasm.Available && Report.Wasm.Reason == MissingEngineReason, $"The WASM runtime is available, or its reason {Report.Wasm.Reason} is not {MissingEngineReason}.");

    [Then("the WASM runtime has the engine version of the pinned Wasmtime package")]
    public void TheRuntimeHasThePinnedEngineVersion() =>
        Expect(Report.Wasm.EngineVersion == PinnedWasmtime, $"The engine version is {Report.Wasm.EngineVersion}, not {PinnedWasmtime}.");

    [Then("the worker of the copy exits with status {int} and no bytes on standard output")]
    public void TheWorkerOfTheCopyExits(int status) =>
        Expect(Worker.ExitCode == status && Worker.StandardOutput.Length == 0, $"The worker of the copy: {Worker.Describe()}");

    [Then("the worker of the copy has the events {string}")]
    public void TheWorkerOfTheCopyHasTheEvents(string names) =>
        Expect(Worker.EventNames.SequenceEqual(names.Split(", ")), $"The worker of the copy has the events {string.Join(", ", Worker.EventNames)}: {Worker.Describe()}");

    [Then("the worker of the copy is refused with {string} and the reason of a missing engine on this platform")]
    public void TheWorkerOfTheCopyIsRefused(string code)
    {
        var refused = Worker.Events.Single(e => e.Name == "refused").Json.GetProperty("error");
        var message = refused.GetProperty("message").GetString() ?? "";
        Expect(refused.GetProperty("code").GetString() == code && message.Contains(MissingEngineReason, StringComparison.Ordinal)
            && !message.Contains("DllNotFoundException", StringComparison.Ordinal), $"The refusal is {refused.GetRawText()}.");
    }

    /// <summary>
    /// The help goes to either stream, as the redirection of the console of the CLI decides; each other command here
    /// writes its result to standard output, and a command with <c>--json</c> writes one JSON document.
    /// </summary>
    [Then("the command of the copy wrote its result with no word of the engine")]
    public void TheCommandWroteItsResult()
    {
        var output = run.Result.StandardOutput + run.Result.StandardError;
        if (run.Result.CommandLine.Contains("--help", StringComparison.Ordinal)) run.Result.OutputShouldContain("Commands:");
        else Expect(run.Result.StandardOutput.Length > 0, "Standard output is empty.");
        Expect(!output.Contains("DllNotFoundException", StringComparison.Ordinal) && !output.Contains("wasmtime", StringComparison.Ordinal), "The output names the engine.");
        if (run.Result.CommandLine.EndsWith("--json", StringComparison.Ordinal)) run.Result.ShouldWriteOneJsonDocument();
    }

    private void Expect(bool condition, string reason)
    {
        if (condition) return;
        CommandResult? result = null;
        try
        {
            result = run.Result;
        }
        catch (InvalidOperationException)
        {
            // The scenario ran no command, only a worker.
        }

        throw result is null ? new InvalidOperationException(reason) : new CommandAssertionException(reason, result);
    }
}
