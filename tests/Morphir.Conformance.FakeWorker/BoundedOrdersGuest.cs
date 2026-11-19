// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Morphir.Conformance.FakeWorker;

/// <summary>A process guest for the pinned orders fixture, using the MEP wire format directly.</summary>
internal sealed class BoundedOrdersGuest(string mode)
{
    internal const string ModeFile = "bounded-orders.mode";
    private const string LegacyVersion = "0.2.0-draft.1";
    private const string BoundedVersion = "0.2.0-draft.2";
    private const string Generate = "morphir.backend.generateModel";
    private const string Package = "acme/orders";
    private const string Module = "domain";
    private const string FinalMember = "total-in-USD";
    private const string Artifact = "order.txt";

    private static readonly JsonElement LegacyTuple = JsonSerializer.SerializeToElement(new
    {
        operation = Generate,
        profile = "morphir-ir",
        codec = "morphir-json",
        envelopeVersion = "0.1.0-draft.1",
        irVersion = "4.0.0",
    });

    private static readonly JsonElement BoundedTuple = JsonSerializer.SerializeToElement(new
    {
        protocolVersion = BoundedVersion,
        accessVersion = "0.1.0-draft.1",
        operation = Generate,
        irVersion = "4.0.0",
        profile = "morphir-ir",
        codec = "morphir-json",
        layout = "document-tree",
        contextualEncoding = "plain",
    });

    private static readonly IReadOnlyDictionary<string, long> LegacyLimits = new Dictionary<string, long>
    {
        ["maxEncodedBytes"] = 8192,
        ["maxDecodedBytes"] = 6144,
        ["maxDepth"] = 64,
        ["maxNodes"] = 256,
    };

    private static readonly IReadOnlyDictionary<string, long> BoundedLimits = new Dictionary<string, long>
    {
        ["maxFrameBytes"] = 32768,
        ["maxEncodedBytesPerExchange"] = 8192,
        ["maxDecodedBytesPerExchange"] = 6144,
        ["maxDocumentsPerBatch"] = 8,
        ["maxDepth"] = 64,
        ["maxNodes"] = 256,
        ["maxDiagnosticsPerExchange"] = 16,
        ["maxOutstandingRequests"] = 8,
        ["maxGuestStateBytes"] = 1048576,
        ["maxHostCacheBytes"] = 1048576,
        ["maxArtifactChunkBytes"] = 4096,
        ["maxArtifactBytes"] = 4294967296,
        ["maxTotalArtifactBytes"] = 4294967296,
        ["maxSessionDiskBytes"] = 8589934592,
        ["maxSessionWorkUnits"] = 4096,
        ["maxRounds"] = 16,
        ["maxCallMilliseconds"] = 30000,
        ["maxSessionMilliseconds"] = 60000,
    };

    private static readonly object Extension = new
    {
        id = "org.finos.morphir.orders",
        name = "Orders generator",
        version = "0.1.0",
        types = new[] { "backend" },
    };

    private static readonly object Backend = new { targets = new[] { "text" }, irVersions = new[] { "4.0.0" }, generate = false };

    private enum Phase { BeforeInitialize, AwaitingInitialized, Ready, ShuttingDown }
    private Phase _phase;
    private IReadOnlyDictionary<string, long>? _boundedLimits;
    private bool _legacy;
    private OrdersRun? _run;

    private sealed record Member(string Name, string DocumentId);

    private sealed record OrdersRun(string SessionId, string SnapshotId)
    {
        internal int Round { get; set; } = 1;
        internal string Selection { get; set; } = "";
        internal Member? Order { get; set; }
        internal Member? Final { get; set; }
    }

    internal static int Run()
    {
        try
        {
            var mode = File.ReadAllText(ModeFile).Trim();
            Require(mode is "orders" or "pause" or "mark-initialize" or "large-reply" or "small-frame" or "root-exits" or "stall-exit" or "stage-before-read",
                "Unknown orders mode.");
            return new BoundedOrdersGuest(mode).Serve();
        }
        catch (Exception error) when (error is IOException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException or TimeoutException)
        {
            Console.Error.WriteLine($"orders guest: {error.Message}");
            return 1;
        }
    }

    private int Serve()
    {
        using var input = Console.OpenStandardInput();
        while (Program.ReadFrame(input) is { } frame)
        {
            using var message = JsonDocument.Parse(frame);
            var request = message.RootElement;
            Require(Text(request, "jsonrpc") == "2.0", "Expected JSON-RPC 2.0.");
            var method = Text(request, "method");
            File.AppendAllText("bounded-orders.methods", method + "\n");

            if (method == "morphir.initialized")
            {
                Require(_phase == Phase.AwaitingInitialized && !request.TryGetProperty("id", out _), "Unexpected initialized notification.");
                _phase = Phase.Ready;
                continue;
            }
            if (method == "morphir.exit")
            {
                Require(_phase == Phase.ShuttingDown && !request.TryGetProperty("id", out _), "Unexpected exit notification.");
                if (mode == "stall-exit")
                {
                    Mark("bounded-orders.exit-received");
                    Thread.Sleep(TimeSpan.FromMilliseconds(3900));
                }
                Mark("bounded-orders.exited");
                return 0;
            }

            request.TryGetProperty("params", out var parameters);
            var id = request.GetProperty("id");
            if (method == Generate + ".advance" && _phase == Phase.Ready && _run is { } run
                && parameters.GetProperty("round").GetInt32() != run.Round)
            {
                Reply(new { jsonrpc = "2.0", id, error = new { code = -32602, message = $"Expected round {run.Round}" } });
                continue;
            }

            var result = method switch
            {
                "morphir.extension.describe" when _phase != Phase.ShuttingDown => Describe(),
                "morphir.initialize" when _phase == Phase.BeforeInitialize => Initialize(parameters),
                Generate when _phase == Phase.Ready && _legacy => GenerateInline(parameters),
                Generate + ".start" when _phase == Phase.Ready && _boundedLimits is { } bounds && _run is null => Start(parameters, bounds),
                Generate + ".advance" when _phase == Phase.Ready && _run is { } active => Advance(parameters, active),
                Generate + ".cancel" when _phase == Phase.Ready && _run is { } active => Cancel(parameters, active),
                "morphir.shutdown" when _phase == Phase.Ready => Shutdown(),
                _ => throw new InvalidOperationException($"Unexpected {method} in {_phase}."),
            };
            if (mode == "mark-initialize" && method == "morphir.initialize") Mark("bounded-orders.initialized-reply");
            Reply(new { jsonrpc = "2.0", id, result });
        }
        return 1;
    }

    private static object Describe() => new
    {
        claimsVersion = "0.1.0-draft.2",
        protocolVersions = new[] { BoundedVersion, LegacyVersion },
        extension = Extension,
        capabilities = new { backend = Backend, modelTransport = Transport(LegacyLimits), modelAccess = Access(BoundedLimits) },
    };

    private object Initialize(JsonElement parameters)
    {
        var versions = parameters.GetProperty("protocolVersions").EnumerateArray().Select(version => version.GetString()).ToArray();
        var capabilities = new Dictionary<string, object> { ["backend"] = Backend };
        if (versions.Contains(BoundedVersion) && Offers(parameters, "modelAccess", BoundedTuple, out var access))
        {
            var limits = Intersect(access.GetProperty("limits"), BoundedLimits);
            if (mode is "large-reply" or "small-frame")
                limits["maxFrameBytes"] = Math.Min(4096, limits["maxFrameBytes"]);
            if (mode == "root-exits")
                limits["maxCallMilliseconds"] = Math.Min(6000, limits["maxCallMilliseconds"]);
            if (mode == "stall-exit")
            {
                limits["maxSessionMilliseconds"] = Math.Min(6000, limits["maxSessionMilliseconds"]);
                limits["maxCallMilliseconds"] = Math.Min(9000, limits["maxCallMilliseconds"]);
            }
            _boundedLimits = limits;
            capabilities["modelAccess"] = Access(_boundedLimits);
        }
        if (Offers(parameters, "modelTransport", LegacyTuple, out var transport))
        {
            _legacy = true;
            capabilities["modelTransport"] = Transport(Intersect(transport.GetProperty("limits"), LegacyLimits));
        }

        var version = _boundedLimits is not null ? BoundedVersion : LegacyVersion;
        Require(versions.Contains(version), "The host offered no supported generation protocol version.");
        _phase = Phase.AwaitingInitialized;
        return new { protocolVersion = version, extension = Extension, capabilities };
    }

    private object Start(JsonElement parameters, IReadOnlyDictionary<string, long> bounds)
    {
        Require(Text(parameters, "target") == "text" && JsonElement.DeepEquals(parameters.GetProperty("input"), BoundedTuple),
            "Expected the v4 plain JSON document-tree tuple and text target.");
        var limits = parameters.GetProperty("limits");
        var admitted = Intersect(limits, bounds);
        Require(admitted.All(bound => limits.GetProperty(bound.Key).GetInt64() == bound.Value), "Start increased a negotiated limit.");
        var snapshot = parameters.GetProperty("snapshot");
        Require(Text(snapshot, "modelId") == Package, "Expected the orders model identity.");
        var run = new OrdersRun(Text(parameters, "sessionId"), Text(snapshot, "snapshotId"));
        _run = run;
        // The real tree includes an earlier private module as well as the dependency spec.
        return Continue(run, Inventory("root", "root", 3));
    }

    private object Advance(JsonElement parameters, OrdersRun run)
    {
        Require(Text(parameters, "sessionId") == run.SessionId, "Advance changed the generation session.");
        var delivery = parameters.GetProperty("deliveries").EnumerateArray().Single();
        Require(Text(delivery, "snapshotId") == run.SnapshotId, "Delivery changed the snapshot identity.");
        run.Round++;
        switch (run.Round - 1)
        {
            case 1:
                var module = Entries(delivery, "root").Select(entry => entry.GetProperty("module"))
                    .Single(entry => Text(entry, "packageName") == Package && Text(entry, "modulePath") == Module);
                Require(Text(module, "role") == "definition", "The domain module must be a definition.");
                run.Selection = Text(module, "memberSelection");
                return Continue(run, Inventory("first", run.Selection, 2));
            case 2:
                var firstPage = Entries(delivery, "first").ToArray();
                Require(firstPage.Length == 2 && Text(firstPage[0].GetProperty("type"), "name") == "order",
                    "Expected the order type before the same-named value.");
                run.Order = OwnValue(firstPage[1]);
                return Continue(run, Inventory("second", run.Selection, 2, Text(delivery, "nextCursor")));
            case 3:
                var final = OwnValue(Entries(delivery, "second").Single());
                Require(final.Name == FinalMember && !delivery.TryGetProperty("nextCursor", out _), "Expected the final value on the final page.");
                run.Final = final;
                return Continue(run, Document("final", final));
            case 4:
                var order = run.Order!;
                var reference = Text(ReadBody(delivery, "final", run.Final!), "Reference");
                Require(reference == $"{Package}:{Module}#{order.Name}", "The final value must reference the earlier value.");
                if (mode == "stage-before-read")
                {
                    var prelude = "privately staged\n"u8.ToArray();
                    return Continue(run, Document("order", order),
                    [
                        new { path = "prelude.txt", offset = 0, decodedByteLength = prelude.Length, sha256 = Digest(prelude),
                            content = Convert.ToBase64String(prelude), end = true },
                    ]);
                }
                return Continue(run, Document("order", order));
            case 5:
                var text = RenderLiteral(ReadBody(delivery, "order", run.Order!));
                if (mode == "pause") WaitAt("bounded-orders.paused", "bounded-orders.resume");
                if (mode == "root-exits") ExitWithPipeDescendant();
                if (mode == "stall-exit")
                {
                    Mark("bounded-orders.before-delayed-complete");
                    Thread.Sleep(TimeSpan.FromMilliseconds(3300));
                }
                var bytes = Encoding.UTF8.GetBytes(text);
                _run = null;
                return new
                {
                    state = "complete",
                    sessionId = run.SessionId,
                    diagnostics = new[] { new { severity = "warning", code = "orders.complete",
                        message = mode == "large-reply" ? new string('x', 6000) : "Rendered the order." } },
                    artifacts = new[] { new { path = Artifact, offset = 0, decodedByteLength = bytes.Length, sha256 = Digest(bytes), content = Convert.ToBase64String(bytes), end = true } },
                };
            default:
                throw new InvalidOperationException("Unexpected generation round.");
        }
    }

    private object Cancel(JsonElement parameters, OrdersRun run)
    {
        Require(Text(parameters, "sessionId") == run.SessionId, "Cancel changed the generation session.");
        _run = null;
        return new { sessionId = run.SessionId };
    }

    private object Shutdown()
    {
        if (mode == "pause") WaitAt("bounded-orders.shutting-down", "bounded-orders.finish-shutdown");
        _phase = Phase.ShuttingDown;
        return new { };
    }

    private static object GenerateInline(JsonElement parameters)
    {
        Require(Text(parameters, "target") == "text" && JsonElement.DeepEquals(parameters.GetProperty("input"), LegacyTuple),
            "Expected the v4 inline JSON tuple and text target.");
        var payload = parameters.GetProperty("model").GetProperty("payload");
        Require(Text(payload, "codec") == "morphir-json" && Text(payload, "layout") == "single-document", "Expected a single JSON document.");
        using var document = JsonDocument.Parse(DecodeContent(payload));
        var root = document.RootElement;
        var library = root.GetProperty("distribution").GetProperty("Library");
        Require(root.GetProperty("formatVersion").GetInt32() == 4 && Text(library, "packageName") == Package, "Expected the v4 orders library.");
        var values = library.GetProperty("def").GetProperty("modules").GetProperty(Module).GetProperty("Public").GetProperty("values");
        var reference = Text(DefinitionBody(values.GetProperty(FinalMember)), "Reference");
        var modulePrefix = $"{Package}:{Module}#";
        Require(reference.StartsWith(modulePrefix, StringComparison.Ordinal), "The final value must reference the orders domain.");
        var content = RenderLiteral(DefinitionBody(values.GetProperty(reference[modulePrefix.Length..])));
        return new { success = true, artifacts = new[] { new { path = Artifact, content } }, diagnostics = Array.Empty<object>() };
    }

    private static JsonElement ReadBody(JsonElement delivery, string id, Member member)
    {
        Require(Text(delivery, "requestId") == id && Text(delivery, "kind") == "document" && Text(delivery, "documentId") == member.DocumentId,
            "Unexpected document delivery.");
        using var document = JsonDocument.Parse(DecodeContent(delivery));
        var root = document.RootElement;
        Require(root.GetProperty("formatVersion").GetInt32() == 4 && Text(root, "name") == member.Name, "Unexpected value document.");
        return DefinitionBody(root.GetProperty("def")).Clone();
    }

    private static byte[] DecodeContent(JsonElement payload)
    {
        var content = Text(payload, "content");
        var bytes = Convert.FromBase64String(content);
        Require(bytes.Length == payload.GetProperty("decodedByteLength").GetInt32()
                && Convert.ToBase64String(bytes) == content && Digest(bytes) == Text(payload, "sha256"),
            "Payload length, encoding or SHA-256 did not match.");
        return bytes;
    }

    private static JsonElement DefinitionBody(JsonElement definition) =>
        definition.GetProperty("Public").GetProperty("ExpressionBody").GetProperty("body");

    private static string RenderLiteral(JsonElement body) =>
        body.GetProperty("Literal").GetProperty("IntegerLiteral").GetInt64().ToString(CultureInfo.InvariantCulture) + "\n";

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static IEnumerable<JsonElement> Entries(JsonElement delivery, string id)
    {
        Require(Text(delivery, "requestId") == id && Text(delivery, "kind") == "inventory", "Unexpected inventory delivery.");
        return delivery.GetProperty("entries").EnumerateArray();
    }

    private static Member OwnValue(JsonElement entry)
    {
        var value = entry.GetProperty("value");
        Require(Text(value, "packageName") == Package && Text(value, "modulePath") == Module && Text(value, "role") == "definition",
            "Expected a value defined by the orders domain.");
        return new(Text(value, "name"), Text(value, "documentId"));
    }

    private static object Continue(OrdersRun run, object request, object[]? artifacts = null) => new
    {
        state = "continue",
        sessionId = run.SessionId,
        round = run.Round,
        requests = new[] { request },
        artifacts = artifacts ?? [],
        diagnostics = run.Round == 1
            ? new[] { new { severity = "warning", code = "orders.inventory", message = "Reading the package inventory." } }
            : Array.Empty<object>(),
    };

    private static object Inventory(string requestId, string selection, int pageSize, string? cursor = null)
    {
        if (cursor is null) return new { requestId, kind = "inventory", selection, pageSize };
        return new { requestId, kind = "inventory", selection, pageSize, cursor };
    }

    private static object Document(string requestId, Member member) => new { requestId, kind = "document", documentId = member.DocumentId };

    private static bool Offers(JsonElement parameters, string name, JsonElement tuple, out JsonElement support) =>
        parameters.TryGetProperty(name, out support)
        && support.GetProperty("tuples").EnumerateArray().Any(offered => JsonElement.DeepEquals(offered, tuple));

    private static Dictionary<string, long> Intersect(JsonElement offer, IReadOnlyDictionary<string, long> supported)
    {
        var result = new Dictionary<string, long>(supported.Count);
        foreach (var (name, maximum) in supported)
        {
            var offered = offer.GetProperty(name).GetInt64();
            Require(offered > 0, $"Expected a positive {name}.");
            result.Add(name, Math.Min(offered, maximum));
        }
        return result;
    }

    private static object Transport(IReadOnlyDictionary<string, long> limits) => new { tuples = new[] { LegacyTuple }, limits, guarantees = Array.Empty<string>() };

    private static object Access(IReadOnlyDictionary<string, long> limits) => new { tuples = new[] { BoundedTuple }, limits };

    private static string Text(JsonElement element, string key) =>
        element.GetProperty(key).GetString() ?? throw new InvalidOperationException($"Expected a string at {key}.");

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private static void Reply(object body) => Program.WriteStdout(Program.Frame(JsonSerializer.SerializeToUtf8Bytes(body)));

    private static void Mark(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
        File.Move(temporary, path, overwrite: true);
    }

    private static void ExitWithPipeDescendant()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing guest executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(BoundedOrdersGuest).Assembly.Location);
        start.ArgumentList.Add("--bounded-orders-pipe-descendant");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The pipe descendant did not start.");
        WaitAt("bounded-orders.root-ready", "bounded-orders.exit-root");
        Environment.Exit(0);
    }

    internal static int RunPipeDescendant()
    {
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime().Ticks;
        if (OperatingSystem.IsLinux())
        {
            var stat = File.ReadAllText($"/proc/{self.Id}/stat");
            start = long.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[19], CultureInfo.InvariantCulture);
        }
        var identity = FormattableString.Invariant($"{self.Id}|{start}");
        File.WriteAllText("bounded-orders.descendant.tmp", identity);
        File.Move("bounded-orders.descendant.tmp", "bounded-orders.descendant");
        // Keep the inherited output handles open until the owner terminates the job.
        Thread.Sleep(TimeSpan.FromSeconds(35));
        return 0;
    }

    private static void WaitAt(string marker, string resume)
    {
        Mark(marker);
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(resume))
        {
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(30)) throw new TimeoutException($"Timed out waiting for {resume}.");
            Thread.Sleep(20);
        }
    }
}
