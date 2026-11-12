// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using TUnit.Core.Enums;

namespace Morphir.BlackBox.Tests.Integration;

/// <summary>
/// <c>extension worker</c> of the host, driven through its process (task 4.1 of add-wasm-extension-host): cases of
/// the pinned crafted corpus, and Ctrl+Break on Windows. A loop over a corpus and a test with a time budget are
/// framework tests (design FORM-1). Run them on each host and target to cover each executable.
/// </summary>
[Category("integration")]
public sealed class ExtensionWorkerCorpusTests : BlackBoxTest
{
    /// <summary>
    /// The <c>worker/</c> cases and a sample of each other group. The complete corpus is run by
    /// <c>morphir mck wasm-host run</c>; set <c>MORPHIR_WORKER_CORPUS=all</c> to drive every case through this test.
    /// </summary>
    static readonly string[] Sample =
    [
        "worker/missing-abi",
        "worker/other-build-identity",
        "classification/unknown-namespace",
        "classification/missing-handle",
        "classification/declared-abi-mismatch",
        "classification/bytes-profile-unsupported",
        "classification/shared-memory",
        "bytes/echo",
        "extism/echo",
        "extism/response-not-object",
        "extism/notifications-not-delivered",
        "extism/notifications-accepted",
        "extism/notification-answered",
        "limits/request-over-limit",
        "limits/response-over-limit",
        "bounds/small-budget",
        "bounds/trap",
        "bounds/initial-memory-over-ceiling",
        "sandbox/http-request",
        "sandbox/workspace-info",
        "sandbox/logging",
    ];

    public static IEnumerable<string> Cases() => WorkerCorpus.Selected(Sample);

    [Test]
    public async Task The_sample_has_every_worker_case_and_every_group()
    {
        var all = WorkerCorpus.CaseIds;
        await Assert.That(Sample.All(all.Contains)).IsTrue();
        await Assert.That(all.Where(id => id.StartsWith("worker/", StringComparison.Ordinal)).All(Sample.Contains)).IsTrue();
        await Assert.That(Sample.Select(Group).Distinct().Order().SequenceEqual(all.Select(Group).Distinct().Order())).IsTrue();
    }

    static string Group(string caseId) => caseId[..caseId.IndexOf('/', StringComparison.Ordinal)];

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task A_corpus_case_gives_its_expected_observation_through_the_worker(string caseId)
    {
        var identity = await ExtensionWorker.BuildIdentityAsync(Drivers.Sessions);

        var outcome = await WorkerCorpus.RunAsync(Drivers.Sessions, identity, caseId);

        await Assert.That(outcome.State is "passed" or "not_applicable").IsTrue().Because(outcome.ToString());
        if (caseId.StartsWith("bytes/", StringComparison.Ordinal)) await Assert.That(outcome.State).IsEqualTo("not_applicable");
    }

    /// <summary>
    /// A runner can send a child in its own process group only Ctrl+Break on Windows, so the worker treats it as Ctrl+C
    /// (design WRK-2 of add-wasm-extension-host). .NET reports it as SIGQUIT.
    /// </summary>
    [Test]
    [RunOn(OS.Windows)]
    public async Task Ctrl_Break_stops_a_spinning_guest_within_one_second_with_130_and_no_fault()
    {
        var module = Workspace.CopyFixture("WorkerGuests/workspace-then-spin.wasm");
        var identity = await ExtensionWorker.BuildIdentityAsync(Drivers.Sessions);
        await using var worker = await Drivers.Sessions.StartAsync(
            ExtensionWorker.ServeArguments(module.Path, identity, "--instructions", "9007199254740991", "--call-ms", "600000"));
        await worker.WriteAsync(ExtensionWorker.Frames("initialize"));
        var made = await worker.WaitUntilAsync(
            () => Workspace.EntriesIn(Workspace.Temp).Any(entry => entry.Split('/').Any(part => part.StartsWith("morphir-wasm-session-", StringComparison.Ordinal))),
            TimeSpan.FromSeconds(60));
        await Assert.That(made).IsTrue().Because("the guest asks for its workspace before it spins");
        await worker.WaitUntilAsync(() => false, TimeSpan.FromMilliseconds(300));

        await worker.InterruptAsync(Morphir.TestSupport.ConsoleControlEvent.CtrlBreak);
        var exit = await worker.WaitForExitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(exit.ExitCode).IsEqualTo(130).Because(exit.Describe());
        await Assert.That(exit.FromInterruptToExit!.Value).IsLessThan(TimeSpan.FromSeconds(1));
        await Assert.That(exit.EventNames.SequenceEqual(["started", "exited"])).IsTrue().Because(exit.Describe());
        await Assert.That(exit.StandardOutput.Length).IsEqualTo(0);
        await Assert.That(Workspace.EntriesIn(Workspace.Temp).Count).IsEqualTo(0).Because("the worker removes its private directory when it exits");
    }
}
