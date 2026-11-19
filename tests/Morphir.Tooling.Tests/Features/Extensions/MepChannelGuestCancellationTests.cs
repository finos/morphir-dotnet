// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Compilation.Mep;
using Morphir.Extensions.Wasm;
using Morphir.Extensions.Wasm.Tests;
using Morphir.Mep;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

public sealed class MepChannelGuestCancellationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnedBoundedChannelNegotiatesAndSettlesBeforeDisposal(bool stallExit)
    {
        var offer = OrdersOffer();
        var channel = new ModelAccessChannel(offer, stallExit);
        var host = new PeerInfo(PeerKind.Cli, "test", "1.0.0");
        var connected = await MepChannelGuest.StartModelAccessAsync(channel, host, offer, TimeSpan.FromSeconds(5), default);
        await using var guest = connected.Match(value => value, failure => throw new InvalidOperationException(failure.Message));
        ((IMepGuestSession)guest).Session.Capabilities.ModelAccessOrNull().Should().NotBeNull();
        channel.Offered.Should().NotBeNull();
        channel.Offered!.Tuples.Should().Equal(offer.Tuples);

        using var stopping = new CancellationTokenSource();
        var closing = guest.CloseModelAccessAsync(stopping.Token);
        await channel.ExitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (stallExit)
        {
            channel.Disposals.Should().Be(0);
            stopping.Cancel();
        }
        var code = (await closing.WaitAsync(TimeSpan.FromSeconds(5))).Match(_ => "closed", error => error.Code);
        code.Should().Be(stallExit ? "extension.timeout" : "closed");
        channel.ExitSettled.Should().BeTrue();
        channel.Disposals.Should().Be(1);
        await guest.TerminateModelAccessAsync();
        channel.Disposals.Should().Be(1);
        channel.Methods.Should().Equal("morphir.initialize", "morphir.initialized", "morphir.shutdown", "morphir.exit");
    }

    [Test]
    public async Task OwnedBoundedChannelRefusesALegacyOnlyAgreementAndDisposesItsAdapter()
    {
        var offer = OrdersOffer();
        var channel = new ModelAccessChannel(offer, stallExit: false, legacy: true);
        var host = new PeerInfo(PeerKind.Cli, "test", "1.0.0");
        var connected = await MepChannelGuest.StartModelAccessAsync(channel, host, offer, TimeSpan.FromSeconds(5), default);
        connected.Match(_ => "connected", error => error.Code).Should().Be("extension.unavailable");
        channel.Methods.Should().NotContain("morphir.backend.generateModel.start");
        channel.Disposals.Should().Be(1);
    }

    [Test]
    public async Task TerminationWaitsForAdapterDisposalBeforeCompleting()
    {
        var offer = OrdersOffer();
        var disposalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new ModelAccessChannel(offer, stallExit: false, settle: async () =>
        {
            disposalEntered.SetResult();
            await allowDisposal.Task;
        });
        var host = new PeerInfo(PeerKind.Cli, "test", "1.0.0");
        var connected = await MepChannelGuest.StartModelAccessAsync(channel, host, offer, TimeSpan.FromSeconds(5), default);
        await using var guest = connected.Match(value => value, failure => throw new InvalidOperationException(failure.Message));

        var termination = guest.TerminateModelAccessAsync();
        try
        {
            var first = await Task.WhenAny(disposalEntered.Task, termination).WaitAsync(TimeSpan.FromSeconds(5));
            first.Should().BeSameAs(disposalEntered.Task);
            termination.IsCompleted.Should().BeFalse();
            channel.Disposals.Should().Be(0);
        }
        finally
        {
            allowDisposal.TrySetResult();
        }

        await termination.WaitAsync(TimeSpan.FromSeconds(5));
        channel.Disposals.Should().Be(1);
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task DisposingAWasmChannelWaitsForTheEngineToLeaveTheActiveCall()
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: guest instantiation is unavailable; #609.");

        var bytes = File.ReadAllBytes(MepWasmFixtures.FullPath("fixtures/wasm/extism-spin.wasm"));
        var module = WasmEngine.Shared.Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(
                admitted => admitted, refused => throw new InvalidOperationException(refused.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            notWasm => throw new InvalidOperationException(notWasm));
        await using var channel = WasmChannel.Open(module, SessionLimits.Default with
        {
            InstructionBudget = ulong.MaxValue,
            CallDeadline = TimeSpan.FromSeconds(5),
        }).Match(opened => opened, failed => throw new InvalidOperationException(failed.Message));

        using var caller = new CancellationTokenSource();
        var message = """{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{}}"""u8.ToArray();
        var active = channel.RequestAsync(Method.Initialize.WireValue, message, caller.Token);
        var disposal = channel.DisposeAsync().AsTask();
        try
        {
            disposal.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await caller.CancelAsync();
        }

        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        await FluentActions.Awaiting(async () => await active.WaitAsync(TimeSpan.FromSeconds(3)))
            .Should().ThrowAsync<OperationCanceledException>();
        channel.HandleCalls.Should().Be(1);
    }

    private static ModelAccessSupport OrdersOffer()
    {
        var transcript = FixtureCatalog.ReadText(FixtureCatalog.Mep.BoundedGenerationSession);
        using var start = JsonDocument.Parse(transcript.Split('\n').First(line =>
            line.Contains("\"method\":\"morphir.backend.generateModel.start\"", StringComparison.Ordinal)));
        var request = start.RootElement.GetProperty("params").Deserialize(MepJson.Default.ModelAccessStart)!;
        return new ModelAccessSupport([request.Input], request.Limits);
    }

    // Same contract as MepGuestProcess: caller cancellation throws; a deadline is extension.timeout.
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CancellationAndDeadlineKeepTheProcessTransportOutcome(bool describe, bool callerCancels)
    {
        var channel = new BlockingChannel();
        var host = new PeerInfo(PeerKind.Cli, "test", "1.0.0");
        var offer = new ModelTransportSupport(Array.Empty<ModelTransportTuple>(), new ModelTransportLimits(1024, 1024, 32, 1024), Array.Empty<string>());
        var started = await MepChannelGuest.StartCheckedAsync(channel, host, offer, TimeSpan.FromSeconds(1), default);
        await using var guest = started.Match(value => value, failure => throw new InvalidOperationException(failure.Message));
        using var caller = new CancellationTokenSource();
        using var parameters = JsonDocument.Parse("{}");
        async Task<string> Outcome()
        {
            if (describe)
            {
                var reply = await guest.DescribeAsync(caller.Token);
                return reply.IsError ? reply.ErrorValue.Code : "success";
            }
            var result = await guest.SendAsync(Method.Validate, parameters.RootElement, caller.Token);
            return result.Match(_ => "success", _ => "rpc_error", failure => failure.Code);
        }
        var pending = Outcome();
        await channel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels)
        {
            caller.Cancel();
            var exception = await FluentActions.Awaiting(async () => await pending).Should().ThrowAsync<OperationCanceledException>();
            exception.Which.CancellationToken.Should().Be(caller.Token);
        }
        else
        {
            (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("extension.timeout");
        }
    }

    private sealed class BlockingChannel : ISingleMessageChannel, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            using var request = JsonDocument.Parse(message);
            var id = request.RootElement.GetProperty("id").GetRawText();
            if (method == "morphir.initialize")
                return Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":ID,"result":{"protocolVersion":"0.2.0-draft.1","extension":{"id":"org.example.blocking","name":"Blocking","version":"1.0.0","types":["validator"]},"capabilities":{}}}""".Replace("ID", id));
            if (method == "morphir.shutdown")
                return Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":ID,"result":null}""".Replace("ID", id));
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return null;
        }

        public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) => Task.FromResult<byte[]?>(null);
        public void Dispose() { }
    }

    private sealed class ModelAccessChannel(ModelAccessSupport support, bool stallExit, bool legacy = false, Func<Task>? settle = null)
        : ISingleMessageChannel, IDisposable, IAsyncDisposable
    {
        private int disposals;
        public List<string> Methods { get; } = [];
        public TaskCompletionSource ExitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals => Volatile.Read(ref disposals);
        public ModelAccessSupport? Offered { get; private set; }
        public bool ExitSettled { get; private set; }

        public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            Methods.Add(method);
            using var request = JsonDocument.Parse(message);
            var id = request.RootElement.GetProperty("id").GetRawText();
            if (method == Method.Shutdown.WireValue)
                return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{}}}}"));

            var parameters = request.RootElement.GetProperty("params");
            if (parameters.TryGetProperty("modelAccess", out var access))
                Offered = access.Deserialize(MepJson.Default.ModelAccessSupport);
            var capabilities = legacy ? "{}" : "{\"modelAccess\":" + JsonSerializer.Serialize(support, MepJson.Default.ModelAccessSupport) + "}";
            var version = legacy ? "0.2.0-draft.1" : "0.2.0-draft.2";
            var initialized = $$"""
                {"jsonrpc":"2.0","id":{{id}},"result":{"protocolVersion":"{{version}}","extension":{"id":"org.example.orders","name":"Orders","version":"1.0.0","types":["backend"]},"capabilities":{{capabilities}} } }
                """;
            return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(initialized));
        }

        public async Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            Methods.Add(method);
            if (method != Method.Exit.WireValue) return null;
            ExitEntered.TrySetResult();
            try
            {
                if (stallExit) await Task.Delay(TimeSpan.FromSeconds(3), cancellation);
                return null;
            }
            finally
            {
                ExitSettled = true;
            }
        }

        public void Dispose() => Interlocked.Increment(ref disposals);

        public async ValueTask DisposeAsync()
        {
            if (settle is not null) await settle();
            Dispose();
        }
    }
}
