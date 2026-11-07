// Shared probe harness. A raw JSON-RPC peer that writes and reads
// Content-Length frames, so the probe checks the bytes on the wire.
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace Probe;

/// <summary>Two one-way pipes: one for each direction.</summary>
public sealed class DuplexPair
{
    private readonly Pipe toServer = new();
    private readonly Pipe toClient = new();

    public Stream ServerSend => toClient.Writer.AsStream();
    public Stream ServerReceive => toServer.Reader.AsStream();
    public Stream ClientSend => toServer.Writer.AsStream();
    public Stream ClientReceive => toClient.Reader.AsStream();
}

public sealed class RawPeer(Stream send, Stream receive)
{
    public async Task SendAsync(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await send.WriteAsync(header);
        await send.WriteAsync(body);
        await send.FlushAsync();
    }

    public async Task<JsonDocument> ReceiveAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        int length = -1;
        while (true)
        {
            string line = await ReadLineAsync(cts.Token);
            if (line.Length == 0) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line["Content-Length:".Length..].Trim());
        }
        if (length < 0) throw new InvalidDataException("no Content-Length header");
        byte[] body = new byte[length];
        await receive.ReadExactlyAsync(body, cts.Token);
        return JsonDocument.Parse(body);
    }

    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        byte[] one = new byte[1];
        while (true)
        {
            await receive.ReadExactlyAsync(one, ct);
            if (one[0] == (byte)'\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)one[0]);
        }
    }
}

public sealed class Report
{
    private readonly List<(string Name, bool Pass, string Detail)> rows = [];

    public void Check(string name, bool pass, string detail)
    {
        rows.Add((name, pass, detail));
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    public async Task Run(string name, Func<Task<(bool, string)>> body)
    {
        try
        {
            var (pass, detail) = await body().WaitAsync(TimeSpan.FromSeconds(10));
            Check(name, pass, detail);
        }
        catch (Exception ex)
        {
            Check(name, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public int ExitCode => rows.All(r => r.Pass) ? 0 : 1;
}

/// <summary>The raw-wire checks that both probes run against a library server.</summary>
public static class WireChecks
{
    public const string U64Max = "18446744073709551615";

    /// <remarks>Each check uses a new connection, so one fatal error does not hide the next check.</remarks>
    public static async Task RunAll(Report report, Func<RawPeer> connect, Func<int> notificationCount, Func<int> cancelledCount)
    {
        var t = TimeSpan.FromSeconds(5);

        foreach (var (label, idToken) in new[] { ("string id", "\"req-abc\""), ("small number id", "42"), ("u64 max id", U64Max), ("long max + 1 id", "9223372036854775808") })
        {
            await report.Run($"raw request, {label}", async () =>
            {
                var raw = connect();
                await raw.SendAsync($$$"""{"jsonrpc":"2.0","id":{{{idToken}}},"method":"echo","params":{"text":"hi","count":1}}""");
                using var doc = await raw.ReceiveAsync(t);
                var root = doc.RootElement;
                string got = root.TryGetProperty("id", out var id) ? id.GetRawText() : "(none)";
                bool ok = got == idToken && root.TryGetProperty("result", out var r) && r.GetProperty("text").GetString() == "echo:hi";
                return (ok, $"sent id {idToken}, got {doc.RootElement.GetRawText()}");
            });
        }

        await report.Run("raw notification, no reply", async () =>
        {
            var raw = connect();
            int before = notificationCount();
            await raw.SendAsync("""{"jsonrpc":"2.0","method":"log","params":{"message":"raw","level":1}}""");
            await raw.SendAsync("""{"jsonrpc":"2.0","id":"after-note","method":"echo","params":{"text":"n","count":1}}""");
            using var doc = await raw.ReceiveAsync(t);
            string id = doc.RootElement.GetProperty("id").GetRawText();
            await Task.Delay(100);
            return (id == "\"after-note\"" && notificationCount() == before + 1, $"next reply id {id}, notifications {before} -> {notificationCount()}");
        });

        foreach (var (label, idToken) in new[] { ("string id", "\"slow-1\""), ("u64 max id", U64Max) })
        {
            await report.Run($"raw $/cancelRequest, {label}", async () =>
            {
                var raw = connect();
                int before = cancelledCount();
                await raw.SendAsync($$$"""{"jsonrpc":"2.0","id":{{{idToken}}},"method":"slow","params":{"text":"x","count":1}}""");
                await Task.Delay(200);
                await raw.SendAsync($$$"""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":{{{idToken}}}}}""");
                using var doc = await raw.ReceiveAsync(t);
                var root = doc.RootElement;
                string got = root.TryGetProperty("id", out var id) ? id.GetRawText() : "(none)";
                await Task.Delay(100);
                bool handlerSawCancel = cancelledCount() == before + 1;
                return (got == idToken && root.TryGetProperty("error", out _) && handlerSawCancel, $"handler token cancelled: {handlerSawCancel}; reply {root.GetRawText()}");
            });
        }

        await report.Run("raw single-member params object binds as one DTO", async () =>
        {
            var raw = connect();
            await raw.SendAsync("""{"jsonrpc":"2.0","id":8,"method":"one","params":{"text":"solo"}}""");
            using var doc = await raw.ReceiveAsync(t);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("result", out var r) && r.GetProperty("text").GetString() == "one:solo";
            return (ok, root.GetRawText());
        });

        await report.Run("raw unknown method", async () =>
        {
            var raw = connect();
            await raw.SendAsync("""{"jsonrpc":"2.0","id":7,"method":"nope","params":{}}""");
            using var doc = await raw.ReceiveAsync(t);
            int code = doc.RootElement.GetProperty("error").GetProperty("code").GetInt32();
            return (code == -32601, doc.RootElement.GetRawText());
        });
    }
}
