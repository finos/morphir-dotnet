// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Conformance.GuestKit;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.TestSupport.Drivers;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 4.3 of add-wasm-extension-host: the generic guest kit's model tier, pinned under
/// <c>tests/Fixtures/MepGuestKit/</c>, for the published Python <c>0.6.0</c> guest, through <c>extension worker</c> of
/// the executable of the target of the run (the testing library: <c>MORPHIR_TEST_TARGET</c>, <c>MORPHIR_TEST_HOST</c>
/// and <c>MORPHIR_TEST_EXECUTABLE</c>; the default is the native host of the development build). Each session that the
/// kit's runner asks for is one worker process, started by <see cref="WorkerSessions"/> with the worker arguments of
/// the wasm-host corpus. It judges the run with the kit library in the test process, so it is a conformance test and
/// not a black-box test. The host speaks MEP <c>0.2.0-draft.1</c> only (design MEP-1), so the kit's legacy tier is not
/// run, and this is never a complete run of the generic guest kit.
/// </summary>
public class WorkerGuestKitModelTierTests
{
    static readonly PeerInfo Host = new(PeerKind.Cli, "morphir-dotnet-e2e", "0.0.0");

    static GuestKit Kit() =>
        GuestKit.TryParse(File.ReadAllBytes(MepGuestKitFixtures.KitFile), out var kit, out var failure)
            ? kit
            : throw new InvalidOperationException($"The pinned kit does not load: {failure}");

    [Test]
    public async Task The_model_tier_on_the_Python_guest_passes_through_the_worker_and_each_worker_exits_cleanly()
    {
        var cli = TargetCli.OfTestRun(typeof(WorkerGuestKitModelTierTests).Assembly, MorphirHost.Native);
        await using var sandbox = TestSandbox.Create("morphir-worker-kit-");
        var module = sandbox.PathOf("morphir-python-binding-0.6.0.wasm");
        await File.WriteAllBytesAsync(module, await PublishedGuest.BytesAsync());
        await using var sessions = await WorkerSessions.CreateAsync(cli.FileName, [.. cli.PrefixArguments], module, Host, CancellationToken.None);

        Console.WriteLine($"host {cli.Artifact} ({cli.Target.Name}, {cli.Host.Name()}), build identity {sessions.BuildIdentity}");

        // The records are printed even when the run throws, so a failure shows each worker's exit status, fault
        // line and problems.
        ModelTierRun run;
        IReadOnlyList<WorkerSessionRecord> records;
        try
        {
            run = await ModelTierRunner.RunAsync(Kit(), sessions.OpenAsync, CancellationToken.None);
            Console.WriteLine(run.Report());
        }
        finally
        {
            records = await sessions.EndAsync(CancellationToken.None);
            foreach (var record in records)
            {
                Console.WriteLine(record);
            }
        }

        var notPassed = run.Steps.Where(step => step.Outcome.WireValue != "passed").Select(step => step.ToString()).ToList();
        await Assert.That(notPassed).IsEmpty();
        await Assert.That(run.Guest).IsEqualTo("morphir-python 0.6.0");
        await Assert.That(run.ModelTierOnly).IsTrue();
        await Assert.That(run.ModelIrVersions.SequenceEqual(["3.0.0", "4.0.0"])).IsTrue();
        await Assert.That(run.Steps.Count).IsEqualTo(16);
        await Assert.That(run.LegacyTier.Count).IsGreaterThan(0);
        await Assert.That(run.LegacyTier.All(step => step.Outcome.WireValue == "not-run")).IsTrue();

        // One worker for describe and one for the model tier. A handle call per request is not observable through a
        // worker; what is, is that each worker exited with 0, wrote started and exited and wrote no fault.
        await Assert.That(records.Select(record => record.Purpose.WireValue).SequenceEqual(["describe", "model-tier"])).IsTrue();
        foreach (var record in records)
        {
            await Assert.That(record.Problems).IsEmpty().Because(record.ToString());
            await Assert.That(record.ExitCode).IsEqualTo(0);
            await Assert.That(record.Events.SequenceEqual(["started", "exited"])).IsTrue().Because(record.ToString());
            await Assert.That(record.FaultOrNull()).IsNull();
            await Assert.That(record.Arguments.SequenceEqual(
                ["extension", "worker", "--abi", "extism-kernel-v1", "--module", module, "--build-identity", sessions.BuildIdentity, "--json"])).IsTrue();
        }
    }
}
