// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Conformance.GuestKit;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.Mep;

namespace Morphir.E2E.Tests.Features;

/// <summary>
/// Task 4.3 of add-wasm-extension-host: the generic guest kit's model tier, pinned under
/// <c>tests/Fixtures/MepGuestKit/</c>, for the published Python <c>0.6.0</c> guest, through <c>extension worker</c> of
/// the executable under test. Each session the kit's runner asks for is one worker process, started by
/// <see cref="WorkerSessions"/> with the wasm-host corpus's worker arguments. The host speaks MEP <c>0.2.0-draft.1</c>
/// only (design MEP-1), so the kit's legacy tier is not run, and this is never a complete run of the generic guest kit.
/// Who runs the kit's model tier against this host is the maintainer's open decision; this test is not that run.
/// </summary>
public sealed class ExecutableGuestKitModelTierTests
{
    static readonly PeerInfo Host = new(PeerKind.Cli, "morphir-dotnet-e2e", "0.0.0");

    static GuestKit Kit()
    {
        var file = Path.Combine(PublishedPythonGuest.RepositoryRoot(), "tests", "Fixtures", "MepGuestKit", "guest-generic-json.json");
        return GuestKit.TryParse(File.ReadAllBytes(file), out var kit, out var failure)
            ? kit
            : throw new InvalidOperationException($"The pinned kit does not load: {failure}");
    }

    [Test]
    public async Task The_model_tier_on_the_Python_guest_passes_through_the_worker_and_each_worker_exits_cleanly()
    {
        var module = await PublishedPythonGuest.PathAsync();
        var executable = ExecutableExtensionWorkerSteps.Executable;
        await using var sessions = await WorkerSessions.CreateAsync(executable, [], module, Host, CancellationToken.None);

        var run = await ModelTierRunner.RunAsync(Kit(), sessions.OpenAsync, CancellationToken.None);
        var records = await sessions.EndAsync(CancellationToken.None);

        Console.WriteLine($"host {executable}, build identity {sessions.BuildIdentity}");
        Console.WriteLine(run.Report());
        foreach (var record in records)
        {
            Console.WriteLine(record);
        }

        var notPassed = run.Steps.Where(step => step.Outcome.ToString() != "passed").Select(step => step.ToString()).ToList();
        notPassed.Should().BeEmpty();
        run.Guest.Should().Be("morphir-python 0.6.0");
        run.ModelTierOnly.Should().BeTrue();
        run.ModelIrVersions.Should().Equal("3.0.0", "4.0.0");
        run.Steps.Should().HaveCount(16);
        run.LegacyTier.Should().NotBeEmpty().And.OnlyContain(step => step.Outcome.ToString().StartsWith("not-run: ", StringComparison.Ordinal));

        // One worker for describe and one for the model tier. A handle call per request is not observable through a
        // worker; what is, is that each worker exited with 0, wrote started and exited and wrote no fault.
        records.Select(record => record.Purpose.WireValue).Should().Equal("describe", "model-tier");
        foreach (var record in records)
        {
            record.Problems.Should().BeEmpty(record.ToString());
            record.ExitCode.Should().Be(0);
            record.Events.Should().Equal(["started", "exited"], record.ToString());
            record.FaultOrNull().Should().BeNull();
            record.Arguments.Should().Equal(
                "extension", "worker", "--abi", "extism-kernel-v1", "--module", module, "--build-identity", sessions.BuildIdentity, "--json");
        }
    }
}
