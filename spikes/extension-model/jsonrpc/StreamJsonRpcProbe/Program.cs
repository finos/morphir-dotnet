// StreamJsonRpc probe: one request/response method, one notification, and
// $/cancelRequest over Content-Length frames, with source-generated JSON.
// It follows the "UTF-8 JSON" Native AOT sample of StreamJsonRpc, but it
// does not suppress IL2026 or IL3050, so the publish log shows them.
using System.Text.Json;
using System.Text.Json.Serialization;
using PolyType;
using Probe;
using StreamJsonRpc;

var report = new Report();
var impl = new Server();

IJsonRpcMessageFormatter NewFormatter() => new SystemTextJsonFormatter
{
    JsonSerializerOptions = { TypeInfoResolver = ProbeJson.Default, PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
};

JsonRpc NewServer(Stream send, Stream receive)
{
    var server = new JsonRpc(new HeaderDelimitedMessageHandler(send, receive, NewFormatter()));
    Watch(server, "server");
    if (Environment.GetEnvironmentVariable("PROBE_TRACE") == "1")
    {
        server.TraceSource.Switch.Level = System.Diagnostics.SourceLevels.All;
        server.TraceSource.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(true));
    }
    server.AddLocalRpcTarget(RpcTargetMetadata.FromShape<IServer>(), impl, null);
    server.StartListening();
    return server;
}

// Part A: library client to library server. The client sends each DTO as
// a by-name params object, as a MEP host does.
// - The source-generated proxy (Attach<IServer>) sends a positional array.
//   A UseSingleObjectParameterDeserialization method rejects it (-32602).
// - The trim-safe NamedArgs overloads write the CLR member names ("Text",
//   "Count") and ignore the JSON naming policy, so the server reads nulls.
// The probe therefore uses the object overloads, which give IL2026.
var pairA = new DuplexPair();
using (var server = NewServer(pairA.ServerSend, pairA.ServerReceive))
using (var client = new JsonRpc(new HeaderDelimitedMessageHandler(pairA.ClientSend, pairA.ClientReceive, NewFormatter())))
{
    Watch(client, "client");
    client.StartListening();
    await report.Run("library request/response", async () =>
    {
        var r = await client.InvokeWithParameterObjectAsync<EchoResult>("echo", (object)new EchoRequest("a", 1));
        return (r.Text == "echo:a", $"result {r.Text}");
    });
    await report.Run("library notification", async () =>
    {
        int before = impl.Notifications;
        await client.NotifyWithParameterObjectAsync("log", (object)new LogNote("hello", 1));
        for (int i = 0; i < 50 && impl.Notifications == before; i++) await Task.Delay(10);
        return (impl.Notifications == before + 1, $"notifications {before} -> {impl.Notifications}");
    });
    await report.Run("library cancellation sends $/cancelRequest", async () =>
    {
        int before = impl.Cancelled;
        using var cts = new CancellationTokenSource(200);
        try { await client.InvokeWithParameterObjectAsync<EchoResult>("slow", (object)new EchoRequest("x", 1), cts.Token); }
        catch (OperationCanceledException) { }
        for (int i = 0; i < 50 && impl.Cancelled == before; i++) await Task.Delay(10);
        return (impl.Cancelled == before + 1, $"server handler cancelled {before} -> {impl.Cancelled}");
    });
}

// Part B: raw wire peer to library server.
var rawServers = new List<JsonRpc>();
RawPeer Connect()
{
    var pair = new DuplexPair();
    rawServers.Add(NewServer(pair.ServerSend, pair.ServerReceive));
    return new RawPeer(pair.ClientSend, pair.ClientReceive);
}
await WireChecks.RunAll(report, Connect, () => impl.Notifications, () => impl.Cancelled);
rawServers.ForEach(s => s.Dispose());

static void Watch(JsonRpc rpc, string who) =>
    rpc.Disconnected += (_, e) =>
    {
        if (e.Reason != DisconnectedReason.LocallyDisposed)
            Console.WriteLine($"  [{who} disconnected: {e.Reason}: {e.Description} {e.Exception?.InnerException?.Message}]");
    };

Console.WriteLine("single-message dispatch API: none (JsonRpc needs an IJsonRpcMessageHandler connection)");
return report.ExitCode;

public sealed record EchoRequest(string Text, int Count);
public sealed record OneField(string Text);
public sealed record EchoResult(string Text);
public sealed record LogNote(string Message, int Level);

[JsonRpcContract, GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IServer
{
    [JsonRpcMethod("echo", UseSingleObjectParameterDeserialization = true)]
    Task<EchoResult> EchoAsync(EchoRequest request);

    [JsonRpcMethod("one", UseSingleObjectParameterDeserialization = true)]
    Task<EchoResult> OneAsync(OneField request);

    [JsonRpcMethod("log", UseSingleObjectParameterDeserialization = true)]
    Task LogAsync(LogNote note);

    [JsonRpcMethod("slow", UseSingleObjectParameterDeserialization = true)]
    Task<EchoResult> SlowAsync(EchoRequest request, CancellationToken cancellationToken);
}

public sealed class Server : IServer
{
    private int notifications;
    private int cancelled;
    public int Notifications => Volatile.Read(ref notifications);
    public int Cancelled => Volatile.Read(ref cancelled);

    public Task<EchoResult> EchoAsync(EchoRequest request) => Task.FromResult(new EchoResult("echo:" + request.Text));

    public Task<EchoResult> OneAsync(OneField request) => Task.FromResult(new EchoResult("one:" + request.Text));

    public Task LogAsync(LogNote note)
    {
        Interlocked.Increment(ref notifications);
        return Task.CompletedTask;
    }

    public async Task<EchoResult> SlowAsync(EchoRequest request, CancellationToken cancellationToken)
    {
        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
        return new EchoResult("never");
    }
}

[JsonSerializable(typeof(EchoRequest))]
[JsonSerializable(typeof(OneField))]
[JsonSerializable(typeof(EchoResult))]
[JsonSerializable(typeof(LogNote))]
// StreamJsonRpc serializes its own types through this context. Without the
// next line, the first error reply closes the connection (NotSupportedException).
[JsonSerializable(typeof(StreamJsonRpc.Protocol.CommonErrorData))]
// Without the next line, $/cancelRequest is dropped with a trace warning only.
[JsonSerializable(typeof(StreamJsonRpc.RequestId))]
internal partial class ProbeJson : JsonSerializerContext;
