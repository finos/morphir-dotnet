// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Compilation.Mep;
using Morphir.Mep;

namespace Morphir.Tooling.Tests.Features.Extensions;

public sealed class MepChannelGuestCancellationTests
{
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
}
