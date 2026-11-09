// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.FSharp.Core;
using Morphir.Mep;

namespace Morphir.Mep.AotProbe;

/// <summary>The C# consumer shared by unit tests and the standalone JIT/Native AOT probe.</summary>
public static class MepConsumer
{
    /// <summary>Exercises the public protocol API and returns deterministic output.</summary>
    public static async Task<string> RunAsync()
    {
        var output = new StringBuilder();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (JsonSerializer.IsReflectionEnabledByDefault)
            throw new InvalidOperationException("The probe must use generated codecs.");

        if (!ProtocolVersion.TryParse("0.2.0-draft.1", out var version, out var failure))
            throw new InvalidOperationException(failure.ToString());
        output.AppendLine($"version {version.Value}");
        var wireVersion = JsonSerializer.Serialize(version, MepJson.Default.ProtocolVersion);
        var parsedVersion = JsonSerializer.Deserialize(wireVersion, MepJson.Default.ProtocolVersion)
            ?? throw new InvalidOperationException("No standalone protocol version.");
        if (!parsedVersion.Equals(version)) throw new InvalidOperationException("The scalar codec changed the version.");
        output.AppendLine($"wire-version {wireVersion}");

        if (ProtocolVersion.TryParse(null, out var rejected, out var refused))
            throw new InvalidOperationException($"Null was accepted as {rejected.Value}.");
        output.AppendLine($"parse-failure {refused}");

        var parameters = new DescribeParams([version]);
        MepJson.Default.Verify(MepJson.Default.Options);
        var json = JsonSerializer.Serialize(parameters, MepJson.Default.DescribeParams);
        var decoded = JsonSerializer.Deserialize(json, MepJson.Default.DescribeParams)
            ?? throw new InvalidOperationException("No describe params.");
        if (!decoded.ProtocolVersions[0].Equals(version))
            throw new InvalidOperationException("The generated codec changed the protocol version.");
        output.AppendLine($"describe {json}");
        output.AppendLine($"description {parameters}");

        using var stream = new MemoryStream();
        await ContentLengthFraming.WriteAsync(stream, Encoding.UTF8.GetBytes(json), deadline.Token);
        stream.Position = 0;
        var frame = await ContentLengthFraming.ReadAsync(stream, deadline.Token);
        var payload = frame.Match(
            body => Encoding.UTF8.GetString(body),
            () => throw new InvalidOperationException("Unexpected EOF."),
            error => throw new InvalidOperationException(error.ToString()));
        if (payload != json) throw new InvalidOperationException("The frame changed the JSON.");
        output.AppendLine($"frame {frame}");
        output.AppendLine($"payload {payload}");

        var eof = await ContentLengthFraming.ReadAsync(stream, deadline.Token);
        output.AppendLine(eof.Match(
            _ => throw new InvalidOperationException("Unexpected frame."),
            () => "clean-eof",
            error => throw new InvalidOperationException(error.ToString())));

        using var invalid = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: -1\r\n\r\n"));
        var bad = await ContentLengthFraming.ReadAsync(invalid, deadline.Token);
        output.AppendLine(bad.Match(
            _ => throw new InvalidOperationException("Invalid length was accepted."),
            () => throw new InvalidOperationException("Invalid header was EOF."),
            error => $"framing-failure {error}"));

        await RunSessionAsync(output, deadline.Token);
        await RunCSharpSurfaceAsync(output, deadline.Token);
        return output.ToString();
    }

    /// <summary>The C# surface of issue #544: no F# option, no union case tests, and C# guest methods.</summary>
    private static async Task RunCSharpSurfaceAsync(StringBuilder output, CancellationToken token)
    {
        var info = Decode(
            """{"id":"x","name":"X","version":"1.0.0","types":["validator"],"description":"d","min_sdk_version":"0.4.0"}""",
            MepJson.Default.ExtensionInfo);
        output.AppendLine($"csharp-info {info.DescriptionOrNull()} {info.AuthorOrNull() ?? "-"} {info.HomepageOrNull() ?? "-"} {info.LicenseOrNull() ?? "-"} {info.MinSdkVersionOrNull()}");

        var progress = Decode("""{"requestId":1,"kind":"report","message":"m","percentage":5}""", MepJson.Default.ProgressParams);
        var error = Decode("""{"code":-32003,"message":"m"}""", MepJson.Default.RpcError);
        output.AppendLine($"csharp-optional {progress.PercentageOrNull()} {error.DataOrNull().HasValue}");

        var capabilities = Decode(
            """{"modelTransport":{"tuples":[{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}],"limits":{"maxEncodedBytes":8,"maxDecodedBytes":4,"maxDepth":2,"maxNodes":3}}}""",
            MepJson.Default.ExtensionCapabilities);
        output.AppendLine($"csharp-capabilities {capabilities.FrontendOrNull() is null} {capabilities.ModelTransportOrNull()?.Tuples.Count}");
        output.AppendLine($"csharp-tuples {capabilities.AgreedTuples(ModelOperation.GenerateModel).Count} {capabilities.AgreedTuples(ModelOperation.CompileModel).Count}");
        output.AppendLine(capabilities.FirstTuple(ModelOperation.CompileModel).Match(
            tuple => $"csharp-first {tuple}",
            unavailable => $"csharp-first-unavailable {unavailable.Code}"));

        if (!Method.TryFind("morphir.ping", out var ping))
            throw new InvalidOperationException("morphir.ping is a MEP method.");
        output.AppendLine($"csharp-method {ping.WireValue} {ping.OperationOrNull() is null} {Method.TryFind("x.unknown", out _)}");
        output.AppendLine($"csharp-operation {Method.GenerateModel.OperationOrNull()}");

        var handlers = new Dictionary<string, MethodHandler>
        {
            ["test.echo"] = MethodHandler.Request(
                MepJson.Default.DescribeParams, MepJson.Default.DescribeParams, (parameters, _) => Task.FromResult(parameters)),
            ["test.refuse"] = MethodHandler.Request<DescribeParams, DescribeParams>(
                MepJson.Default.DescribeParams, MepJson.Default.DescribeParams,
                (_, _) => throw new RpcException(RpcErrors.WithMessage(ErrorCode.ValidationError, "Validation could not run."))),
            ["test.ok"] = MethodHandler.Constant("""{"ok":true}"""),
            ["test.data"] = MethodHandler.Request<DescribeParams, DescribeParams>(
                MepJson.Default.DescribeParams, MepJson.Default.DescribeParams,
                (_, _) => throw new RpcException(RpcErrors.WithData(
                    ErrorCode.ValidationError, "With data.", JsonDocument.Parse("1").RootElement.Clone()))),
            ["test.note"] = MethodHandler.Notification(
                MepJson.Default.CancelParams, (parameters, cancellation) => Task.CompletedTask),
        };
        var dispatcher = new Dispatcher(handlers, (method, kind) =>
            method == "test.blocked" ? Admission.RefuseWith(RpcErrors.Create(ErrorCode.NotInitialized)) : Admission.Proceed);

        foreach (var request in new[]
        {
            """{"jsonrpc":"2.0","id":1,"method":"test.echo","params":{"protocolVersions":["0.2.0-draft.1"]}}""",
            """{"jsonrpc":"2.0","id":2,"method":"test.refuse","params":{"protocolVersions":["0.2.0-draft.1"]}}""",
            """{"jsonrpc":"2.0","id":3,"method":"test.ok"}""",
            """{"jsonrpc":"2.0","id":4,"method":"test.blocked"}""",
            """{"jsonrpc":"2.0","id":5,"method":"test.data","params":{"protocolVersions":["0.2.0-draft.1"]}}""",
            """{"jsonrpc":"2.0","method":"test.note","params":{"id":1}}""",
        })
        {
            var reply = await dispatcher.DispatchAsync(Encoding.UTF8.GetBytes(request), token);
            output.AppendLine($"csharp-dispatch {(reply is null ? "none" : Encoding.UTF8.GetString(reply))}");
        }

        var refusal = new MessageRefusal(FSharpOption<RequestId>.None, RpcErrors.Create(ErrorCode.ParseError), true);
        output.AppendLine($"csharp-refusal {refusal.IdOrNull() is null}");
        output.AppendLine($"csharp-state {SessionState.Created.Match(() => "created", _ => "initialized", _ => "shut-down", () => "exited")}");
        output.AppendLine($"csharp-end {ConnectionEnd.EndOfStream.Match(() => "end-of-stream", () => "stopped", failure => $"framing {failure}")}");
        output.AppendLine($"csharp-failure {InitializeFailure.InvalidResult.Match(e => e.Message, () => "invalid-result", v => v.Value, reason => reason)}");
        output.AppendLine($"csharp-diagnostic {ConnectionDiagnostic.UnsolicitedResponseWith(null).Match(id => $"unsolicited {id?.ToString() ?? "null"}", r => r.ToString(), f => f.ToString())}");
        output.AppendLine($"csharp-member {ModelMember.PresentWith(Decode("""{"envelopeVersion":"0.1.0-draft.1","modelId":"m","revision":"r","irVersion":"4.0.0","profile":"p","payload":{"codec":"morphir-json","layout":"single-document","decodedByteLength":0,"sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855","content":""}}""", MepJson.Default.MorphirModel)).Match(() => "absent", () => "null", model => model.ModelId)}");
    }

    private const string Extension =
        """{"id":"org.finos.morphir.probe","name":"Probe","version":"1.0.0","types":["validator"]}""";

    private static T Decode<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        JsonSerializer.Deserialize(json, info) ?? throw new InvalidOperationException($"No {typeof(T).Name}.");

    private static T Check<T, TError>(FSharpResult<T, TError> result, string step) =>
        result.Match(value => value, error => throw new InvalidOperationException($"{step} failed: {error}"));

    /// <summary>Runs describe, initialize, ping, info, capabilities, shutdown and exit over in-memory pipes.</summary>
    private static async Task RunSessionAsync(StringBuilder output, CancellationToken token)
    {
        var capabilities = Decode("""{"cancellation":true}""", MepJson.Default.ExtensionCapabilities);
        var claims = Decode(
            $$$"""{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.1"],"extension":{{{Extension}}},"capabilities":{}}""",
            MepJson.Default.CapabilityClaimSet);
        var guest = new Guest(new GuestDefinition(claims, capabilities, new Dictionary<string, MethodHandler>()));

        var toGuest = new Pipe();
        var toHost = new Pipe();
        var guestRun = guest.RunAsync(toGuest.Reader.AsStream(), toHost.Writer.AsStream(), token);
        var host = new HostSession(
            toHost.Reader.AsStream(),
            toGuest.Writer.AsStream(),
            new PeerInfo(PeerKind.Cli, "morphir-probe", "0.0.0"));
        var hostRun = host.RunAsync(token);

        var described = Check(await host.DescribeAsync(token), "describe");
        output.AppendLine($"session-describe {JsonSerializer.Serialize(described, MepJson.Default.CapabilityClaimSet)}");
        var session = Check(await host.InitializeAsync(token), "initialize");
        output.AppendLine($"session-initialize {session}");
        Check(await host.PingAsync(token), "ping");
        output.AppendLine("session-ping ok");
        var info = Check(await host.InfoAsync(token), "info");
        output.AppendLine($"session-info {JsonSerializer.Serialize(info, MepJson.Default.ExtensionInfo)}");
        var current = Check(await host.CapabilitiesAsync(token), "capabilities");
        output.AppendLine($"session-capabilities {JsonSerializer.Serialize(current, MepJson.Default.ExtensionCapabilities)}");
        Check(await host.ShutdownAsync(token), "shutdown");
        var late = await host.PingAsync(token);
        output.AppendLine($"session-ping-after-shutdown {late.Match(_ => 0, error => error.Code)}");
        await host.ExitAsync();
        output.AppendLine($"session-guest-end {await guestRun}");
        await toHost.Writer.CompleteAsync();
        output.AppendLine($"session-host-end {await hostRun}");
    }
}
