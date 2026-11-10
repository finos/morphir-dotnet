// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;
using Reqnroll;

namespace Morphir.E2E.Tests.Features;

/// <summary>Steps for <c>extension worker</c> in the executable under test (task 4.1 of add-wasm-extension-host).</summary>
[Binding]
public sealed class ExecutableExtensionWorkerSteps : IDisposable
{
    static readonly ConcurrentDictionary<string, Task<string>> Identities = new();
    static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(120);

    readonly TestSandbox sandbox = TestSandbox.Create("morphir-worker-e2e-");
    WorkerRun? run;
    WorkerProcess? running;
    DateTime interruptedAt;

    /// <summary>The executable under test.</summary>
    public static string Executable =>
        new ExecutableLocator().GetExecutablePath() ?? throw new InvalidOperationException("Morphir executable not found. Build the executable first.");

    /// <summary>The build identity of <paramref name="executable"/>, read once from <c>extension worker --help --json</c>.</summary>
    public static Task<string> IdentityAsync(string executable) => Identities.GetOrAdd(executable, async path =>
    {
        await using var help = await WorkerProcess.StartAsync(path, ["--help", "--json"]);
        var result = await help.WaitForExitAsync(TimeSpan.FromSeconds(60));
        result.ExitCode.Should().Be(0, result.Describe());
        using var document = JsonDocument.Parse(result.Stdout);
        return document.RootElement.GetProperty("result").GetProperty("buildIdentity").GetString()!;
    });

    /// <summary>The worker's temporary directory: TEMP, TMP and TMPDIR of the worker process point here.</summary>
    string Temp => Directory.CreateDirectory(sandbox.PathOf("temp")).FullName;

    Dictionary<string, string> Environment => new() { ["TEMP"] = Temp, ["TMP"] = Temp, ["TMPDIR"] = Temp };

    WorkerRun Run => run ?? throw new InvalidOperationException("No worker has run in this scenario.");

    static string ModulePath(string reference) => reference.Split(':', 2) switch
    {
        ["corpus", var name] => WorkerCorpus.ModulePath(name),
        ["guest", var name] => Path.Combine(AppContext.BaseDirectory, "TestData", "Wasm", $"{name}.wasm"),
        _ => throw new ArgumentException($"A module is corpus:<name> or guest:<name>, not '{reference}'.", nameof(reference)),
    };

    static byte[][] Frames(string names) =>
        names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => WorkerProcess.Frame(WorkerCorpus.FrameBody(name))).ToArray();

    async Task<List<string>> ArgumentsAsync(string module, params string[] more) =>
        ["--abi", "extism-kernel-v1", "--module", module, "--build-identity", await IdentityAsync(Executable), "--json", .. more];

    async Task<WorkerRun> ServeAsync(IEnumerable<string> arguments, byte[][] frames)
    {
        await using var worker = await WorkerProcess.StartAsync(Executable, arguments, Environment);
        await worker.WriteAsync(frames);
        return await worker.WaitForExitAsync(RunLimit);
    }

    [When("I ask the worker for its help as JSON")]
    public async Task WhenIAskTheWorkerForItsHelpAsJson() => run = await ServeAsync(["--help", "--json"], []);

    [When("the worker serves \"(.*)\" with the frames \"(.*)\" and the arguments \"(.*)\"")]
    public async Task WhenTheWorkerServesWithTheFramesAndTheArguments(string module, string frames, string arguments)
    {
        running = await WorkerProcess.StartAsync(Executable,
            await ArgumentsAsync(ModulePath(module), arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)), Environment);
        await running.WriteAsync(Frames(frames));
    }

    [When("the worker serves \"([^\"]*)\" with the frames \"([^\"]*)\"$")]
    public async Task WhenTheWorkerServesWithTheFrames(string module, string frames) =>
        run = await ServeAsync(await ArgumentsAsync(ModulePath(module)), Frames(frames));

    [When("the worker is started with another build identity for a module file that does not exist")]
    public async Task WhenTheWorkerIsStartedWithAnotherBuildIdentity()
    {
        var identity = await IdentityAsync(Executable);
        var other = identity[..^1] + (identity[^1] == '0' ? '1' : '0');
        run = await ServeAsync(["--abi", "extism-kernel-v1", "--module", sandbox.PathOf("no-such-module.wasm"), "--build-identity", other, "--json"],
            Frames("initialize"));
    }

    [When("the worker serves a text file named \"(.*)\"")]
    public async Task WhenTheWorkerServesATextFileNamed(string name)
    {
        var path = sandbox.PathOf(name);
        await File.WriteAllTextAsync(path, "This is a text file, not a module.\n");
        run = await ServeAsync(await ArgumentsAsync(path), Frames("initialize"));
    }

    [When("the worker serves a module file that does not exist")]
    public async Task WhenTheWorkerServesAModuleFileThatDoesNotExist() =>
        run = await ServeAsync(await ArgumentsAsync(sandbox.PathOf("missing.wasm")), Frames("initialize"));

    [When("the worker is started with the extra argument \"(.*)\"")]
    public async Task WhenTheWorkerIsStartedWithTheExtraArgument(string argument) =>
        run = await ServeAsync(await ArgumentsAsync(WorkerCorpus.ModulePath("extism-echo"), argument), Frames("initialize"));

    [When("the guest has created its private workspace")]
    public async Task WhenTheGuestHasCreatedItsPrivateWorkspace()
    {
        var worker = running ?? throw new InvalidOperationException("No worker is running.");
        var created = await worker.WaitUntilAsync(
            () => Directory.EnumerateDirectories(Temp, "morphir-wasm-session-*", SearchOption.AllDirectories).Any(), TimeSpan.FromSeconds(60));
        created.Should().BeTrue("the guest asks for its workspace before it spins: {0}",
            string.Join(System.Environment.NewLine, worker.StderrLines.Select(line => line.Text)));
        // The call is inside handle now; give the spin a moment of its own.
        await Task.Delay(300);
    }

    [When("I interrupt the worker with Ctrl+C")]
    public async Task WhenIInterruptTheWorkerWithCtrlC()
    {
        var worker = running ?? throw new InvalidOperationException("No worker is running.");
        interruptedAt = await ConsoleInterrupt.SendAsync(worker.Id, ConsoleControlEvent.CtrlC);
        run = await worker.WaitForExitAsync(TimeSpan.FromSeconds(30));
    }

    [Then("the worker exits with status (\\d+)$")]
    public void ThenTheWorkerExitsWithStatus(int status) => Run.ExitCode.Should().Be(status, Run.Describe());

    [Then("the worker exits with status (\\d+) within one second of the interruption")]
    public void ThenTheWorkerExitsWithStatusWithinOneSecondOfTheInterruption(int status)
    {
        Run.ExitCode.Should().Be(status, Run.Describe());
        (Run.ExitTimeUtc - interruptedAt).Should().BeLessThan(TimeSpan.FromSeconds(1), "the guest stops at the next epoch tick");
    }

    [Then("stdout is one command result whose build identity is 64 hexadecimal digits")]
    public void ThenStdoutIsOneCommandResultWithABuildIdentity()
    {
        var text = Encoding.UTF8.GetString(Run.Stdout);
        text.TrimEnd('\n').Should().NotContain("\n", "the result is one line");
        using var document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("profile").GetString().Should().Be("morphir/command-result");
        document.RootElement.GetProperty("result").GetProperty("buildIdentity").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Then("the help in the command result contains \"(.*)\"")]
    public void ThenTheHelpInTheCommandResultContains(string text)
    {
        using var document = JsonDocument.Parse(Run.Stdout);
        document.RootElement.GetProperty("result").GetProperty("help").GetString().Should().Contain(text);
    }

    [Then("stdout holds only these frames:")]
    public void ThenStdoutHoldsOnlyTheseFrames(DataTable table)
    {
        var (frames, problem) = Run.Frames;
        problem.Should().BeNull(Run.Describe());
        frames.Select(frame => Encoding.UTF8.GetString(frame)).Should().Equal(table.Rows.Select(row => row["frame"]));
    }

    [Then("stdout holds only (\\d+) frames that are each (.*)")]
    public void ThenStdoutHoldsOnlyFramesThatAreEach(int count, string frame)
    {
        var (frames, problem) = Run.Frames;
        problem.Should().BeNull(Run.Describe());
        frames.Select(body => Encoding.UTF8.GetString(body)).Should().Equal(Enumerable.Repeat(frame, count));
    }

    [Then("stdout is empty")]
    public void ThenStdoutIsEmpty() => Run.Stdout.Should().BeEmpty(Run.Describe());

    [Then("the guest output contains \"(.*)\"")]
    public void ThenTheGuestOutputContains(string text) => Run.GuestOutput.Should().Contain(text, Run.Describe());

    [Then("stderr has the events \"(.*)\"")]
    public void ThenStderrHasTheEvents(string names) =>
        Run.EventNames.Should().Equal(names.Split(", "), Run.Describe());

    [Then("stderr has exactly one \"(.*)\" event with the code \"(.*)\"")]
    public void ThenStderrHasExactlyOneEventWithTheCode(string name, string code)
    {
        var events = Run.Events.Where(e => e.Name == name).ToArray();
        events.Should().ContainSingle(Run.Describe());
        events[0].Json.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
        Run.StderrLines.Count(line => line.Text.Contains("\"event\":\"refused\"", StringComparison.Ordinal)).Should().Be(1);
    }

    [Then("stderr has no \"(.*)\" event")]
    public void ThenStderrHasNoEvent(string name) => Run.EventNames.Should().NotContain(name, Run.Describe());

    [Then("the fault has the kind \"(.*)\" and a message that contains \"(.*)\"")]
    public void ThenTheFaultHasTheKindAndAMessage(string kind, string text)
    {
        var fault = Run.Events.Single(e => e.Name == "fault").Json.GetProperty("fault");
        fault.GetProperty("kind").GetString().Should().Be(kind);
        fault.GetProperty("message").GetString().Should().Contain(text);
    }

    [Then("every output event line is at most (\\d+) bytes")]
    public void ThenEveryOutputEventLineIsAtMost(int bytes)
    {
        var lines = Run.StderrLines.Where(line => line.Text.Contains("\"event\":\"output\"", StringComparison.Ordinal)).ToArray();
        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(line => Encoding.UTF8.GetByteCount(line.Text) <= bytes);
    }

    [Then("the output events state that more than (\\d+) MiB of guest output were dropped")]
    public void ThenTheOutputEventsStateWhatWasDropped(int mebibytes)
    {
        var dropped = Run.Events.Where(e => e.Name == "output" && e.Json.TryGetProperty("droppedBytes", out _))
            .Select(e => e.Json.GetProperty("droppedBytes").GetInt64()).ToArray();
        dropped.Should().HaveCount(5, "one dropped count for each call that overflowed its tail");
        dropped.Sum().Should().BeGreaterThan(mebibytes * 1024L * 1024L);
    }

    [Then("stderr holds less than (\\d+) KiB")]
    public void ThenStderrHoldsLessThan(int kibibytes) =>
        Run.StderrLines.Sum(line => Encoding.UTF8.GetByteCount(line.Text) + 1).Should().BeLessThan(kibibytes * 1024);

    [Then("the worker's peak working set is less than (\\d+) MiB above that of an echo session")]
    public async Task ThenThePeakWorkingSetIsBounded(int mebibytes)
    {
        var flood = await PeakAsync(ModulePath("guest:flood-output"));
        var echo = await PeakAsync(WorkerCorpus.ModulePath("extism-echo"));
        if (flood == 0 || echo == 0) return; // The platform does not report a working set.
        (flood - echo).Should().BeLessThan(mebibytes * 1024L * 1024L,
            $"the flood guest wrote 100 MiB of output through the worker; peak {flood} bytes against {echo} for an echo session");
    }

    /// <summary>The worker's peak working set after five answered requests, read before <c>exit</c> is sent.</summary>
    async Task<long> PeakAsync(string module)
    {
        await using var worker = await WorkerProcess.StartAsync(Executable, await ArgumentsAsync(module), Environment);
        await worker.WriteAsync(Frames("initialize, initialize, initialize, initialize, initialize"));
        var answered = await worker.WaitUntilAsync(() => StdoutFrames.Read(worker.Stdout).Frames.Count == 5, RunLimit);
        answered.Should().BeTrue(string.Join(System.Environment.NewLine, worker.StderrLines.Select(line => line.Text).Take(20)));
        var peak = worker.PeakWorkingSet;
        await worker.WriteAsync(Frames("exit"));
        var result = await worker.WaitForExitAsync(RunLimit);
        result.ExitCode.Should().Be(0, result.Describe());
        return peak;
    }

    [Then("the worker's temporary directory is empty")]
    public void ThenTheWorkersTemporaryDirectoryIsEmpty() =>
        Directory.EnumerateFileSystemEntries(Temp).Should().BeEmpty("the worker removes its private directory when it exits");

    public void Dispose()
    {
        running?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        sandbox.Dispose();
    }
}
