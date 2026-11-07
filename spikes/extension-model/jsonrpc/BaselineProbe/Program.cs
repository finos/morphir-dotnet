// Baseline: no JSON-RPC library. It uses the same harness, System.Text.Json
// source generation and pipes, so its size is the floor for any MEP layer.
using System.Text.Json;
using System.Text.Json.Serialization;
using Probe;

var pair = new DuplexPair();
var client = new RawPeer(pair.ClientSend, pair.ClientReceive);
var server = new RawPeer(pair.ServerSend, pair.ServerReceive);
var report = new Report();
await report.Run("frame round trip with source-generated JSON", async () =>
{
    await client.SendAsync(JsonSerializer.Serialize(new EchoRequest("hi", 1), ProbeJson.Default.EchoRequest));
    using var doc = await server.ReceiveAsync(TimeSpan.FromSeconds(5));
    var req = doc.RootElement.Deserialize(ProbeJson.Default.EchoRequest)!;
    return (req.Text == "hi", doc.RootElement.GetRawText());
});
return report.ExitCode;

public sealed record EchoRequest(string Text, int Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EchoRequest))]
internal partial class ProbeJson : JsonSerializerContext;
