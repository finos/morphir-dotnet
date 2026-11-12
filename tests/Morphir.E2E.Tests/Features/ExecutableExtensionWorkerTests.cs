// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;
using TUnit.Core.Enums;

namespace Morphir.E2E.Tests.Features;

/// <summary>
/// <c>extension worker</c> of the executable under test, driven through its process (task 4.1 of
/// add-wasm-extension-host): cases of the pinned crafted corpus, and Ctrl+Break on Windows. Run the assembly once with
/// <c>MORPHIR_EXECUTABLE_TYPE=tool</c> and once for <c>morphir</c> to cover both executables. The managed <c>morphir</c>
/// (<c>src/Morphir.Managed</c>), which is not published yet, is covered by pointing <c>MORPHIR_EXECUTABLE_PATH</c> at its
/// app host.
/// </summary>
public sealed class ExecutableExtensionWorkerTests
{
    /// <summary>
    /// The <c>worker/</c> cases and a sample of each other group. The complete corpus is run by
    /// <c>morphir mck wasm-host run</c> in task 4.3; set <c>MORPHIR_WORKER_CORPUS=all</c> to drive every case through
    /// this test instead.
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

    public static IEnumerable<string> Cases() =>
        Environment.GetEnvironmentVariable("MORPHIR_WORKER_CORPUS") == "all" ? WorkerCorpus.CaseIds : Sample;

    [Test]
    public void The_sample_has_every_worker_case_and_every_group()
    {
        var all = WorkerCorpus.CaseIds;
        Sample.Should().OnlyContain(id => all.Contains(id));
        Sample.Should().Contain(all.Where(id => id.StartsWith("worker/", StringComparison.Ordinal)));
        Sample.Select(Group).Distinct().Should().BeEquivalentTo(all.Select(Group).Distinct());
    }

    static string Group(string caseId) => caseId[..caseId.IndexOf('/', StringComparison.Ordinal)];

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task A_corpus_case_gives_its_expected_observation_through_the_worker(string caseId)
    {
        var executable = ExecutableExtensionWorkerSteps.Executable;
        var identity = await ExecutableExtensionWorkerSteps.IdentityAsync(executable);

        var outcome = await WorkerCorpus.RunAsync(executable, identity, caseId);

        outcome.State.Should().BeOneOf(["passed", "not_applicable"], outcome.ToString());
        if (caseId.StartsWith("bytes/", StringComparison.Ordinal)) outcome.State.Should().Be("not_applicable");
    }

    /// <summary>
    /// A runner can send a child in its own process group only Ctrl+Break on Windows, so the worker treats it as Ctrl+C
    /// (design WRK-2). .NET reports it as SIGQUIT.
    /// </summary>
    [Test]
    [RunOn(OS.Windows)]
    public async Task Ctrl_Break_stops_a_spinning_guest_within_one_second_with_130_and_no_fault()
    {
        using var sandbox = TestSandbox.Create("morphir-worker-e2e-");
        var temp = Directory.CreateDirectory(sandbox.PathOf("temp")).FullName;
        var executable = ExecutableExtensionWorkerSteps.Executable;
        var module = Path.Combine(AppContext.BaseDirectory, "TestData", "Wasm", "workspace-then-spin.wasm");
        await using var worker = await WorkerProcess.StartAsync(executable,
            ["--abi", "extism-kernel-v1", "--module", module, "--build-identity", await ExecutableExtensionWorkerSteps.IdentityAsync(executable),
             "--json", "--instructions", "9007199254740991", "--call-ms", "600000"],
            new Dictionary<string, string> { ["TEMP"] = temp, ["TMP"] = temp });
        await worker.WriteAsync(WorkerProcess.Frame(WorkerCorpus.FrameBody("initialize")));
        (await worker.WaitUntilAsync(() => Directory.EnumerateDirectories(temp, "morphir-wasm-session-*", SearchOption.AllDirectories).Any(),
            TimeSpan.FromSeconds(60))).Should().BeTrue("the guest asks for its workspace before it spins");
        await Task.Delay(300);

        var sent = await ConsoleInterrupt.SendAsync(worker.Id, ConsoleControlEvent.CtrlBreak);
        var run = await worker.WaitForExitAsync(TimeSpan.FromSeconds(30));

        run.ExitCode.Should().Be(130, run.Describe());
        (run.ExitTimeUtc - sent).Should().BeLessThan(TimeSpan.FromSeconds(1));
        run.EventNames.Should().Equal(["started", "exited"], run.Describe());
        run.Stdout.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(temp).Should().BeEmpty("the worker removes its private directory when it exits");
    }
}
