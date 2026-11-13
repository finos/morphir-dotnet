// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of <c>extension worker</c> on the session driver of the host (task 4.1 of add-wasm-extension-host).</summary>
[Binding]
public sealed class ExtensionWorkerSteps(IProcessSessionDriver sessions, DriverSet drivers, IScenarioWorkspace workspace, WorkerRun worker)
{
    private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(120);

    private IProcessSession? _running;

    private SessionExit Exit => worker.Exit;

    private string ModulePath(string reference) => reference.Split(':', 2) switch
    {
        ["corpus", var name] => workspace.CopyFixture(WorkerCorpus.ModuleFixture(name)).Path,
        ["guest", var name] => workspace.CopyFixture($"WorkerGuests/{name}.wasm").Path,
        _ => throw new ArgumentException($"A module is corpus:<name> or guest:<name>, not '{reference}'.", nameof(reference)),
    };

    private async Task<SessionExit> ServeAsync(IReadOnlyList<string> arguments, byte[][] frames)
    {
        await using var session = await sessions.StartAsync(arguments);
        await session.WriteAsync(frames);
        return await session.WaitForExitAsync(RunLimit);
    }

    private async Task<IReadOnlyList<string>> ServeArgumentsAsync(string module, params string[] more) =>
        ExtensionWorker.ServeArguments(module, await ExtensionWorker.BuildIdentityAsync(sessions), more);

    /// <summary>
    /// The WASM runtime of the host is available. A scenario that runs a guest has <c>@needs-wasm-guests</c>, so a run
    /// on Windows x64, where the host refuses each guest (#609), skips it before this step with that reason.
    /// </summary>
    private async Task RequireTheGuestRuntimeAsync()
    {
        var report = ExtensionRuntimesReport.Of((await drivers.Cli.RunAsync(ExtensionRuntimesReport.Arguments)).ShouldExitWith(0));
        if (!report.Wasm.Available) throw new CommandAssertionException("The WASM runtime of the host is not available.", report.Result);
    }

    [When("I ask the worker for its help as JSON")]
    public async Task AskTheWorkerForItsHelpAsJson() => worker.Exit = await ExtensionWorker.HelpAsync(sessions);

    [When("the worker serves {string} with the frames {string} and the arguments {string}")]
    public async Task TheWorkerServesWithTheArguments(string module, string frames, string arguments)
    {
        await RequireTheGuestRuntimeAsync();
        _running = drivers.Own(await sessions.StartAsync(await ServeArgumentsAsync(ModulePath(module), arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
        await _running.WriteAsync(ExtensionWorker.Frames(frames));
    }

    [When("the worker serves {string} with the frames {string}")]
    public async Task TheWorkerServes(string module, string frames)
    {
        await RequireTheGuestRuntimeAsync();
        worker.Exit = await ServeAsync(await ServeArgumentsAsync(ModulePath(module)), ExtensionWorker.Frames(frames));
    }

    [When("the worker is started with another build identity for a module file that is not there")]
    public async Task TheWorkerIsStartedWithAnotherBuildIdentity()
    {
        var other = ExtensionWorker.OtherIdentity(await ExtensionWorker.BuildIdentityAsync(sessions));
        worker.Exit = await ServeAsync(ExtensionWorker.ServeArguments(workspace.File("no-such-module.wasm").Path, other), ExtensionWorker.Frames("initialize"));
    }

    [When("the worker serves a text file named {string}")]
    public async Task TheWorkerServesATextFile(string name)
    {
        await RequireTheGuestRuntimeAsync();
        var file = workspace.File(name);
        workspace.WriteText(file, "This is a text file, not a module.\n");
        worker.Exit = await ServeAsync(await ServeArgumentsAsync(file.Path), ExtensionWorker.Frames("initialize"));
    }

    [When("the worker serves a module file that is not there")]
    public async Task TheWorkerServesAMissingModule()
    {
        await RequireTheGuestRuntimeAsync();
        worker.Exit = await ServeAsync(await ServeArgumentsAsync(workspace.File("missing.wasm").Path), ExtensionWorker.Frames("initialize"));
    }

    [When("the worker is started with the extra argument {string}")]
    public async Task TheWorkerIsStartedWithTheExtraArgument(string argument) =>
        worker.Exit = await ServeAsync(await ServeArgumentsAsync(ModulePath("corpus:extism-echo"), argument), ExtensionWorker.Frames("initialize"));

    [When("the guest has made its private workspace")]
    public async Task TheGuestHasMadeItsPrivateWorkspace()
    {
        var session = _running ?? throw new InvalidOperationException("No session is running.");
        var made = await session.WaitUntilAsync(
            () => workspace.EntriesIn(workspace.Temp).Any(entry => entry.Split('/').Any(part => part.StartsWith("morphir-wasm-session-", StringComparison.Ordinal))),
            TimeSpan.FromSeconds(60));
        if (!made)
            throw new InvalidOperationException("The guest asks for its workspace before it spins:\n" + string.Join("\n", session.StandardError.Select(line => line.Text)));
        // The call is inside handle now; give the spin a moment of its own.
        await session.WaitUntilAsync(() => false, TimeSpan.FromMilliseconds(300));
    }

    [When("I interrupt the worker")]
    public async Task InterruptTheWorker()
    {
        var session = _running ?? throw new InvalidOperationException("No session is running.");
        await session.InterruptAsync();
        worker.Exit = await session.WaitForExitAsync(TimeSpan.FromSeconds(30));
    }

    [Then("the worker exits with status {int}")]
    public void TheWorkerExitsWithStatus(int status) => Expect(Exit.ExitCode == status, $"The worker exits with {Exit.ExitCode?.ToString() ?? "no status"}, not {status}.");

    [Then("the worker exits with status {int} within one second of the interrupt")]
    public void TheWorkerExitsWithinOneSecond(int status)
    {
        TheWorkerExitsWithStatus(status);
        Expect(Exit.FromInterruptToExit is { } time && time < TimeSpan.FromSeconds(1),
            $"The worker exits {Exit.FromInterruptToExit} after the interrupt; the guest stops at the next epoch tick, within one second.");
    }

    [Then("standard output of the worker is one command result whose build identity is 64 hexadecimal digits")]
    public void StandardOutputIsOneCommandResult()
    {
        var text = Encoding.UTF8.GetString(Exit.StandardOutput);
        Expect(!text.TrimEnd('\n').Contains('\n'), "The command result is not one line.");
        using var document = JsonDocument.Parse(text);
        Expect(document.RootElement.GetProperty("profile").GetString() == "morphir/command-result", "Standard output is not a morphir/command-result.");
        Expect(System.Text.RegularExpressions.Regex.IsMatch(document.RootElement.GetProperty("result").GetProperty("buildIdentity").GetString() ?? "", "^[0-9a-f]{64}$"),
            "The build identity is not 64 hexadecimal digits.");
    }

    [Then("the help in the command result contains {string}")]
    public void TheHelpContains(string text)
    {
        using var document = JsonDocument.Parse(Exit.StandardOutput);
        Expect(document.RootElement.GetProperty("result").GetProperty("help").GetString()?.Contains(text, StringComparison.Ordinal) == true,
            $"The help does not contain '{text}'.");
    }

    [Then("standard output of the worker holds only these frames:")]
    public void StandardOutputHoldsOnlyTheseFrames(DataTable table)
    {
        var (frames, problem) = Exit.Frames;
        Expect(problem is null, problem ?? "");
        var expected = table.Rows.Select(row => row["frame"]).ToArray();
        var actual = frames.Select(frame => Encoding.UTF8.GetString(frame)).ToArray();
        Expect(actual.SequenceEqual(expected), $"Standard output holds the frames:\n{string.Join("\n", actual)}");
    }

    [Then("standard output of the worker holds only {int} frames that are each {}")]
    public void StandardOutputHoldsOnlyFramesThatAreEach(int count, string frame)
    {
        var (frames, problem) = Exit.Frames;
        Expect(problem is null, problem ?? "");
        Expect(frames.Select(body => Encoding.UTF8.GetString(body)).SequenceEqual(Enumerable.Repeat(frame, count)),
            $"Standard output does not hold only {count} frames {frame}: {frames.Count} frames.");
    }

    [Then("standard output of the worker is empty")]
    public void StandardOutputIsEmpty() => Expect(Exit.StandardOutput.Length == 0, "Standard output of the worker is not empty.");

    [Then("the guest output contains {string}")]
    public void TheGuestOutputContains(string text) => Expect(Exit.GuestOutput.Contains(text, StringComparison.Ordinal), $"The guest output does not contain '{text}'.");

    [Then("standard error of the worker has the events {string}")]
    public void StandardErrorHasTheEvents(string names) =>
        Expect(Exit.EventNames.SequenceEqual(names.Split(", ")), $"Standard error has the events {string.Join(", ", Exit.EventNames)}, not {names}.");

    [Then("standard error of the worker has exactly one {string} event with the code {string}")]
    public void StandardErrorHasExactlyOneEventWithTheCode(string name, string code)
    {
        var events = Exit.Events.Where(e => e.Name == name).ToArray();
        Expect(events.Length == 1, $"Standard error has {events.Length} {name} events, not one.");
        Expect(events[0].Json.GetProperty("error").GetProperty("code").GetString() == code, $"The {name} event does not have the code {code}.");
        Expect(Exit.StandardError.Count(line => line.Text.Contains("\"event\":\"refused\"", StringComparison.Ordinal)) == 1, "Standard error does not have exactly one refused line.");
    }

    [Then("standard error of the worker has no {string} event")]
    public void StandardErrorHasNoEvent(string name) => Expect(!Exit.EventNames.Contains(name), $"Standard error has a {name} event.");

    [Then("the fault has the kind {string} and a message that contains {string}")]
    public void TheFaultHasTheKindAndAMessage(string kind, string text)
    {
        var fault = Exit.Events.Single(e => e.Name == "fault").Json.GetProperty("fault");
        Expect(fault.GetProperty("kind").GetString() == kind && fault.GetProperty("message").GetString()?.Contains(text, StringComparison.Ordinal) == true,
            $"The fault is {fault.GetRawText()}, not the kind {kind} with '{text}'.");
    }

    [Then("each output event line of the worker is at most {int} bytes")]
    public void EachOutputEventLineIsAtMost(int bytes)
    {
        var lines = Exit.StandardError.Where(line => line.Text.Contains("\"event\":\"output\"", StringComparison.Ordinal)).ToArray();
        Expect(lines.Length > 0, "Standard error has no output event.");
        Expect(lines.All(line => Encoding.UTF8.GetByteCount(line.Text) <= bytes), $"An output event line is longer than {bytes} bytes.");
    }

    [Then("the output events state that more than {int} MiB of guest output were dropped")]
    public void TheOutputEventsStateWhatWasDropped(int mebibytes)
    {
        var dropped = Exit.Events.Where(e => e.Name == "output" && e.Json.TryGetProperty("droppedBytes", out _))
            .Select(e => e.Json.GetProperty("droppedBytes").GetInt64()).ToArray();
        Expect(dropped.Length == 5, $"{dropped.Length} output events state a dropped count; one for each call that overflowed its tail is 5.");
        Expect(dropped.Sum() > mebibytes * 1024L * 1024L, $"The output events state that {dropped.Sum()} bytes were dropped.");
    }

    [Then("standard error of the worker holds less than {int} KiB")]
    public void StandardErrorHoldsLessThan(int kibibytes) =>
        Expect(Exit.StandardError.Sum(line => Encoding.UTF8.GetByteCount(line.Text) + 1) < kibibytes * 1024, $"Standard error holds {kibibytes} KiB or more.");

    /// <summary>
    /// The worker does not hold the guest's output: its peak working set does not grow with what the guest writes.
    /// </summary>
    /// <remarks>
    /// Each call of the flood guest writes 20 MiB, and the host turns each 1 KiB write or log message into the text it
    /// read and the prefixed text of the output, which are dead when the call returns: about 100 MB of short-lived
    /// garbage for each call. The peak working set is the live data plus the garbage that the GC lets build up before
    /// it collects (its gen0 budget), and the runtime sizes that budget from the processor's cache. So the flood peak
    /// above that of an echo session measured the machine, not retention: 71 MB on the linux-arm64 release runner, 86
    /// to 90 MB for the Native AOT executable on linux-x64, 9 MB on a win-arm64 laptop, and 70 MB on that laptop with
    /// <c>DOTNET_GCgen0size</c> at 64 MiB. This step compares the worker with itself instead. By the end of the second
    /// call the GC has collected at least once whenever its budget is under about 200 MB, so the peak has its steady
    /// height. Calls three to five write 60 MiB more; a worker that kept a quarter of it would grow by more than the
    /// bound, and one that kept each output's text grew by more than 120 MB. The 64 KiB tail itself is checked by the
    /// steps on the output events and on standard error.
    /// </remarks>
    [Then("the peak working set of the worker grows by less than {int} MiB from its second call to its fifth")]
    public async Task ThePeakWorkingSetDoesNotGrowWithTheOutput(int mebibytes)
    {
        var peaks = await PeaksAfterEachCallAsync(ModulePath("guest:flood-output"), 5);
        if (peaks.Contains(0)) return; // The platform does not report a working set.
        Expect(peaks[4] - peaks[1] < mebibytes * 1024L * 1024L,
            $"The flood guest wrote 60 MiB of output through the worker in calls three to five; the peak working set after each call was {string.Join(", ", peaks)} bytes.");
    }

    /// <summary>The peak working set of the worker after each of <paramref name="calls"/> answered requests, each
    /// sent when the one before is answered; <c>exit</c> is sent after the last.</summary>
    private async Task<long[]> PeaksAfterEachCallAsync(string module, int calls)
    {
        await using var session = await sessions.StartAsync(await ServeArgumentsAsync(module));
        var peaks = new long[calls];
        for (var call = 1; call <= calls; call++)
        {
            await session.WriteAsync(ExtensionWorker.Frames("initialize"));
            var answered = await session.WaitUntilAsync(() => ContentLengthFrames.Read(session.StandardOutput).Frames.Count == call, RunLimit);
            if (!answered) throw new InvalidOperationException(string.Join("\n", session.StandardError.Select(line => line.Text).Take(20)));
            peaks[call - 1] = session.PeakWorkingSet;
        }

        await session.WriteAsync(ExtensionWorker.Frames("exit"));
        var exit = await session.WaitForExitAsync(RunLimit);
        if (exit.ExitCode != 0) throw new InvalidOperationException(exit.Describe());
        return peaks;
    }

    [Then("the temporary directory of the worker is empty")]
    public void TheTemporaryDirectoryIsEmpty()
    {
        var temp = worker.Temp ?? workspace.Temp;
        Expect(workspace.EntriesIn(temp).Count == 0,
            $"The worker did not remove its private directory, or something was extracted there: {string.Join(", ", workspace.EntriesIn(temp))}.");
    }

    private void Expect(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason + "\n" + (worker.ExitOrNull?.Describe() ?? ""));
    }
}
