// CurlyRpc probe: one request/response method, one notification, and
// $/cancelRequest over Content-Length frames, with source-generated JSON.
// It uses only the typed AddLocalRpcMethod<...> overloads, which are
// reflection-free. The delegate and target overloads use reflection.
using System.Text.Json;
using System.Text.Json.Serialization;
using CurlyRpc;
using Probe;

var report = new Report();
int notifications = 0;
int cancelled = 0;
var options = new JsonRpcOptions { SerializerOptions = ProbeJson.Default.Options, OwnsStream = false };

JsonRpc NewServer(Stream send, Stream receive)
{
    var server = new JsonRpc(new HeaderDelimitedMessageHandler(send, receive), options);
    server.AddLocalRpcMethod<EchoRequest, EchoResult>("echo", req => Task.FromResult(new EchoResult("echo:" + req.Text)));
    server.AddLocalRpcMethod<LogNote>("log", note => { Interlocked.Increment(ref notifications); return Task.CompletedTask; });
    server.AddLocalRpcMethod<EchoRequest, EchoResult>("slow", async (req, ct) =>
    {
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
        return new EchoResult("never");
    });
    // A params object with one member, such as MEP DescribeParams, binds badly:
    // CurlyRpc reads it as a by-name wrapper and binds the member value.
    server.AddLocalRpcMethod<OneField, EchoResult>("one", req => Task.FromResult(new EchoResult("one:" + req.Text)));
    server.StartListening();
    return server;
}

// Part A: library client to library server.
var pairA = new DuplexPair();
await using (var server = NewServer(pairA.ServerSend, pairA.ServerReceive))
await using (var client = new JsonRpc(new HeaderDelimitedMessageHandler(pairA.ClientSend, pairA.ClientReceive), options))
{
    client.StartListening();
    await report.Run("library request/response", async () =>
    {
        var r = await client.InvokeWithParameterObjectAsync<EchoResult>("echo", new EchoRequest("a", 1));
        return (r!.Text == "echo:a", $"result {r.Text}");
    });
    await report.Run("library notification", async () =>
    {
        int before = notifications;
        await client.NotifyWithParameterObjectAsync("log", new LogNote("hello", 1));
        for (int i = 0; i < 50 && notifications == before; i++) await Task.Delay(10);
        return (notifications == before + 1, $"notifications {before} -> {notifications}");
    });
    await report.Run("library cancellation sends $/cancelRequest", async () =>
    {
        int before = cancelled;
        using var cts = new CancellationTokenSource(200);
        try { await client.InvokeWithParameterObjectAsync<EchoResult>("slow", new EchoRequest("x", 1), cts.Token); }
        catch (OperationCanceledException) { }
        for (int i = 0; i < 50 && cancelled == before; i++) await Task.Delay(10);
        return (cancelled == before + 1, $"server handler cancelled {before} -> {cancelled}");
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
await WireChecks.RunAll(report, Connect, () => notifications, () => cancelled);
foreach (var s in rawServers) await s.DisposeAsync();

Console.WriteLine("single-message dispatch API: none (JsonRpc needs an IJsonRpcMessageHandler connection)");
return report.ExitCode;

public sealed record EchoRequest(string Text, int Count);
public sealed record OneField(string Text);
public sealed record EchoResult(string Text);
public sealed record LogNote(string Message, int Level);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EchoRequest))]
[JsonSerializable(typeof(EchoResult))]
[JsonSerializable(typeof(LogNote))]
[JsonSerializable(typeof(OneField))]
internal partial class ProbeJson : JsonSerializerContext;
