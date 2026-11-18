// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using Morphir.Mep;

namespace Morphir.Mep.AotProbe;

/// <summary>
/// A C# single-message channel, as the WASM adapter implements it: each call passes one message to a guest's
/// dispatcher and returns its reply. <paramref name="answer"/> may replace what the guest returned.
/// </summary>
internal sealed class DispatcherChannel(Dispatcher dispatcher, Func<string, byte[]?, byte[]?> answer) : ISingleMessageChannel
{
    public List<string> Methods { get; } = [];

    public Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
        CallAsync(method, message, cancellation);

    public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
        CallAsync(method, message, cancellation);

    private async Task<byte[]?> CallAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
    {
        Methods.Add(method);
        var reply = await dispatcher.DispatchAsync(message, cancellation);
        return answer(method, reply);
    }
}

/// <summary>Runs a host session on a C# single-message channel, and a session that the channel fails.</summary>
internal static class SingleMessageConsumer
{
    private const string Claims =
        """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{"id":"org.finos.morphir.probe","name":"Probe","version":"1.0.0","types":["validator"]},"capabilities":{}}""";

    private static Guest NewGuest() =>
        new(new GuestDefinition(
            JsonSerializer.Deserialize(Claims, MepJson.Default.CapabilityClaimSet) ?? throw new InvalidOperationException("No claims."),
            JsonSerializer.Deserialize("{}", MepJson.Default.ExtensionCapabilities) ?? throw new InvalidOperationException("No capabilities."),
            new Dictionary<string, MethodHandler>()));

    private static string Failure(HostSession session) =>
        session.FailureOrNull()?.Match(
            noReply: method => $"no-reply {method}",
            replyForAnotherRequest: (method, expected, actual) => $"another-id {method} {expected} {actual?.ToString() ?? "null"}",
            notAResponse: (method, returned) => $"not-a-response {method} {returned}",
            invalidReply: (method, refusal) => $"invalid {method} {refusal}",
            replyTooLarge: (method, bytes) => $"too-large {method} {bytes}",
            answeredNotification: method => $"answered {method}",
            cancelled: method => $"cancelled {method}",
            channelFailed: (method, error) => $"channel {method} {error.Message}") ?? "none";

    public static async Task RunAsync(StringBuilder output, CancellationToken token)
    {
        var host = new PeerInfo(PeerKind.Cli, "morphir-probe", "0.0.0");
        var guest = NewGuest();
        var channel = new DispatcherChannel(guest.Dispatcher, (_, reply) => reply);
        var session = new HostSession(channel, host);

        var initialized = await session.InitializeAsync(token);
        output.AppendLine($"single-initialize {initialized.Match(value => value.ToString(), failure => failure.ToString())}");
        var ping = await session.PingAsync(token);
        output.AppendLine($"single-ping {ping.Match(value => value.GetRawText(), error => error.ToString())}");
        var shutdown = await session.ShutdownAsync(token);
        output.AppendLine($"single-shutdown {shutdown.Match(value => value.GetRawText(), error => error.ToString())}");
        await session.ExitAsync();
        output.AppendLine($"single-state {session.State.Match(() => "created", _ => "initialized", _ => "shut-down", () => "exited")}");
        output.AppendLine($"single-calls {string.Join(' ', channel.Methods)}");
        output.AppendLine($"single-failure {Failure(session)}");

        var silent = new DispatcherChannel(NewGuest().Dispatcher, (method, reply) => method == "morphir.ping" ? null : reply);
        var failing = new HostSession(silent, host);
        await failing.InitializeAsync(token);
        var unanswered = await failing.PingAsync(token);
        output.AppendLine($"single-no-reply {Failure(failing)} {unanswered.Match(_ => 0, error => error.Code)}");
        var ended = await failing.RunAsync(token);
        output.AppendLine($"single-ended {ended.Match(() => "end-of-stream", () => "stopped", failure => $"framing {failure}")} {string.Join(' ', silent.Methods)}");

        await RunBoundedGenerationAsync(output, host, token);
    }

    private static async Task RunBoundedGenerationAsync(StringBuilder output, PeerInfo peer, CancellationToken token)
    {
        const string supportJson = """
            {"tuples":[{"protocolVersion":"0.2.0-draft.2","accessVersion":"0.1.0-draft.1","operation":"morphir.backend.generateModel","irVersion":"4.0.0","profile":"morphir-ir","codec":"morphir-json","layout":"document-tree","contextualEncoding":"plain"}],
             "limits":{"maxFrameBytes":2048,"maxEncodedBytesPerExchange":2048,"maxDecodedBytesPerExchange":1024,"maxDocumentsPerBatch":1,"maxDepth":32,"maxNodes":64,"maxDiagnosticsPerExchange":2,"maxOutstandingRequests":1,"maxGuestStateBytes":1024,"maxHostCacheBytes":1024,"maxArtifactChunkBytes":128,"maxArtifactBytes":256,"maxTotalArtifactBytes":256,"maxSessionDiskBytes":1024,"maxSessionWorkUnits":256,"maxRounds":2,"maxCallMilliseconds":500,"maxSessionMilliseconds":2000}}
            """;
        var support = JsonSerializer.Deserialize(supportJson, MepJson.Default.ModelAccessSupport)
            ?? throw new InvalidOperationException("No bounded offer.");
        var claims = JsonSerializer.Deserialize(
            Claims.Replace("0.2.0-draft.1", "0.2.0-draft.2", StringComparison.Ordinal),
            MepJson.Default.CapabilityClaimSet) ?? throw new InvalidOperationException("No bounded claims.");
        var capabilities = JsonSerializer.Deserialize(
            $$"""{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":{{supportJson}}}""",
            MepJson.Default.ExtensionCapabilities) ?? throw new InvalidOperationException("No bounded capabilities.");
        var methods = new Dictionary<string, MethodHandler>
        {
            [Method.GenerateModelStart.WireValue] = MethodHandler.Request(
                MepJson.Default.ModelAccessStart, MepJson.Default.ModelAccessStep,
                (request, _) => Task.FromResult(ModelAccessStep.NewContinue(request.SessionId, 1, [], [], []))),
            [Method.GenerateModelAdvance.WireValue] = MethodHandler.Constant(
                """{"state":"complete","sessionId":"probe","artifacts":[],"diagnostics":[]}"""),
            [Method.GenerateModelCancel.WireValue] = MethodHandler.Constant("""{"sessionId":"retry"}"""),
        };
        var guest = new Guest(new GuestDefinition(claims, capabilities, methods));
        var channel = new DispatcherChannel(guest.Dispatcher, (_, reply) => reply);
        var session = HostSession.CreateWithModelAccess(channel, peer, support, null);
        var initialized = await session.InitializeAsync(token);
        var selected = initialized.Match(
            value => value.Capabilities.ModelAccessOrNull() is { Tuples.Count: 1 }
                ? value.ProtocolVersion.Value
                : throw new InvalidOperationException("No model access was agreed."),
            failure => throw new InvalidOperationException(failure.ToString()));
        var snapshot = new ModelAccessSnapshot("s", "acme/probe", "r1", Sha256Digest.Create(new string('0', 64)), "root");
        using var options = JsonDocument.Parse("{}");
        var request = new ModelAccessStart("probe", snapshot, support.Tuples[0], "text", options.RootElement, [], support.Limits);
        var start = await session.StartGenerationAsync(request, token);
        var round = start.Match(
            step => step.Match((_, number, _, _, _) => number, (_, _, _) => 0, (_, _) => 0),
            error => throw new InvalidOperationException(error.ToString()));
        var advance = await session.AdvanceGenerationAsync(new ModelAccessAdvance("probe", round, []), token);
        var completed = advance.Match(
            step => step.Match((_, _, _, _, _) => "-", (id, _, _) => id, (_, _) => "-"),
            error => throw new InvalidOperationException(error.ToString()));
        var replay = await session.AdvanceGenerationAsync(new ModelAccessAdvance("probe", round, []), token);
        await session.StartGenerationAsync(
            new ModelAccessStart("retry", snapshot, support.Tuples[0], "text", options.RootElement, [], support.Limits),
            token);
        var cancel = await session.CancelGenerationAsync(new ModelAccessCancel("retry"), token);
        var cancelled = cancel.Match(ack => ack.SessionId, error => throw new InvalidOperationException(error.ToString()));
        output.AppendLine($"bounded-generation {selected} {round} {completed} {replay.Match(_ => 0, error => error.Code)} {cancelled}");
    }
}
