// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.FSharp.Core;
using Morphir.Compilation.Mep;
using Morphir.IR.Trees;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class BoundedGenerationSessionTests
{
    [Test]
    [Arguments(73)]
    [Arguments(74)]
    public async Task FollowsBothMemberPagesAndTheFinalValueReference(int order)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, order, omitOrder: false);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync();

        var output = result.Should().BeOfType<Outcome<StagedGenerationOutput>.Success>().Which.Value;
        var artifact = output.Artifacts.Should().ContainSingle().Which;
        artifact.Path.Should().Be("order.txt");
        var bytes = File.ReadAllBytes(Path.Combine(output.Directory, artifact.Path));
        bytes.Should().Equal(Encoding.UTF8.GetBytes($"{order}\n"));
        if (order == 73)
        {
            artifact.Sha256.Value.Should().Be("c6ebc76be5dc1f8b433f8d6fd9bd85cd9325086038442db8614bb799fec6fd85");
            using var baseline = JsonDocument.Parse(FixtureCatalog.ReadBytes(FixtureCatalog.BoundedOrders.ExpandedV4));
            var values = baseline.RootElement.GetProperty("distribution").GetProperty("Library").GetProperty("def")
                .GetProperty("modules").GetProperty("domain").GetProperty("Public").GetProperty("values");
            var target = DefinitionBody(values.GetProperty("total-in-USD")).GetProperty("Reference").GetString()!;
            bytes.Should().Equal(Encoding.UTF8.GetBytes(RenderLiteral(DefinitionBody(values.GetProperty(target[(target.LastIndexOf('#') + 1)..])))));
        }
        fixture.Advances.Should().Equal(1, 2, 3, 4, 5);
        fixture.Cancelled.Should().BeFalse();
        fixture.DefinitionsOpened.Should().Equal(
            "pkg/acme/orders/domain/total-in-_usd.value.json", "pkg/acme/orders/domain/order.value.json");
    }

    [Test]
    [Arguments(false, 257L)]
    [Arguments(true, 503L)]
    public async Task ObservesAdmittedDocumentBatchesAndArtifactBytesIncludingLf(bool twoDocuments, long expected)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, twoDocuments: twoDocuments);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync();

        var output = result.Should().BeOfType<Outcome<StagedGenerationOutput>.Success>().Which.Value;
        output.MaxInputBatchBytes.Should().Be(expected);
        fixture.MaxOutputBatchBytes.Should().Be(3L);
    }

    [Test]
    [Arguments(5, false)]
    [Arguments(6, true)]
    public async Task EnforcesTheAgreedDocumentDepthBeforeDelivery(int maxDepth, bool accepted)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, maxDepth: maxDepth);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync();

        if (accepted)
        {
            result.Should().BeOfType<Outcome<StagedGenerationOutput>.Success>();
            fixture.Advances.Should().Equal(1, 2, 3, 4, 5);
        }
        else
        {
            var diagnostic = result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>()
                .Which.Error.Diagnostics.Should().ContainSingle().Which;
            diagnostic.Code.Should().Be("depth_limit_exceeded");
            diagnostic.Message.Should().Contain("5 levels");
            fixture.Advances.Should().Equal(1, 2, 3, 4);
            fixture.Cancelled.Should().BeTrue();
            Directory.Exists(fixture.StageDirectory).Should().BeFalse();
        }
    }

    [Test]
    [Arguments(10, false, false)]
    [Arguments(11, false, true)]
    [Arguments(15, true, false)]
    [Arguments(21, true, true)]
    public async Task EnforcesTheAgreedDecodedNodesBeforeDelivery(int maxNodes, bool twoDocuments, bool accepted)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false,
            maxNodes: maxNodes, twoDocuments: twoDocuments);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync();

        if (accepted)
        {
            result.Should().BeOfType<Outcome<StagedGenerationOutput>.Success>();
            fixture.Advances.Should().Equal(1, 2, 3, 4, 5);
        }
        else
        {
            var diagnostic = result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>()
                .Which.Error.Diagnostics.Should().ContainSingle().Which;
            diagnostic.Code.Should().Be("tree.document_node_limit");
            diagnostic.Message.Should().Contain("maxNodes");
            fixture.Advances.Should().Equal(1, 2, 3, 4);
            fixture.Cancelled.Should().BeTrue();
            Directory.Exists(fixture.StageDirectory).Should().BeFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CountsTheSameDocumentNodesInJsonAndYaml(bool yaml)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, yaml: yaml);
        var snapshot = fixture.OpenSnapshot();
        const string definition = "pkg/acme/orders/domain/order.value";
        var refused = DocumentTreeStore.ReadDocumentWithinCounted(definition, 8192, 10, snapshot);
        refused.IsError.Should().BeTrue();
        refused.ErrorValue.Select(error => error.Code).Should().Equal("tree.document_node_limit");

        foreach (var limit in new[] { 11, int.MaxValue })
        {
            var counted = DocumentTreeStore.ReadDocumentWithinCounted(definition, 8192, limit, snapshot);
            counted.IsOk.Should().BeTrue();
            counted.ResultValue.Item2.Should().Be(11);
        }
    }

    [Test]
    [Arguments(300L, false)]
    [Arguments(10_000L, true)]
    public async Task ChargesRepeatedSmallInventoryPagesToOneSessionBudget(long maxWork, bool accepted)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, repeatInventoryWithWork: maxWork);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync();

        fixture.DefinitionsOpened.Should().BeEmpty();
        if (accepted)
        {
            result.Should().BeOfType<Outcome<StagedGenerationOutput>.Success>();
            fixture.Advances.Should().Equal(Enumerable.Range(1, 16));
            fixture.Cancelled.Should().BeFalse();
        }
        else
        {
            var diagnostic = result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>()
                .Which.Error.Diagnostics.Should().ContainSingle().Which;
            diagnostic.Code.Should().Be("extension.bounded_work_limit");
            diagnostic.Message.Should().Contain("maxSessionWorkUnits");
            fixture.Advances.Count.Should().BeInRange(1, 15);
            fixture.Cancelled.Should().BeTrue();
            Directory.Exists(fixture.StageDirectory).Should().BeFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelsAndDiscardsOutputWhenTheReferencedDocumentIsMissing(bool stallCancel)
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: true, framed: stallCancel, stallCancel: stallCancel);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var codes = result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>().Which.Error.Diagnostics
            .Select(diagnostic => diagnostic.Code);
        if (stallCancel)
            codes.Should().Equal("tree.missing_member", "extension.bounded_cancel");
        else
            codes.Should().Equal("tree.missing_member");
        fixture.Advances.Should().Equal(1, 2, 3, 4);
        fixture.Cancelled.Should().BeTrue();
        fixture.TransportStopped.Should().Be(stallCancel);
        Directory.Exists(fixture.StageDirectory).Should().BeFalse();
    }

    [Test]
    public async Task DiscardsPartialOutputWhenAnExchangeStalls()
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, stallAdvance: true);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync();

        result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>().Which.Error.Diagnostics[0].Code
            .Should().Be("extension.bounded_deadline");
        fixture.Advances.Should().BeEmpty();
        Directory.Exists(fixture.StageDirectory).Should().BeFalse();
    }

    [Test]
    public async Task StopsTheFramedGuestBeforeReturningFromAnUnansweredAdvance()
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, stallAdvance: true, framed: true);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>().Which.Error.Diagnostics[0].Code
            .Should().Be("extension.bounded_deadline");
        fixture.TransportStopped.Should().BeTrue();
        Directory.Exists(fixture.StageDirectory).Should().BeFalse();
    }

    [Test]
    public async Task DiscardsPartialOutputWhenAFramedWriteFails()
    {
        using var sandbox = TestSandbox.Create();
        await using var fixture = new OrdersFixture(sandbox, 73, omitOrder: false, framed: true, closeHostOutput: true);
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().BeOfType<Outcome<StagedGenerationOutput>.Failure>().Which.Error.Diagnostics[0].Code
            .Should().Be("extension.bounded_exchange");
        fixture.TransportStopped.Should().BeTrue();
        fixture.Advances.Should().BeEmpty();
        Directory.Exists(fixture.StageDirectory).Should().BeFalse();
    }

    private static JsonElement DefinitionBody(JsonElement definition) =>
        definition.GetProperty("Public").GetProperty("ExpressionBody").GetProperty("body");

    private static string RenderLiteral(JsonElement body) =>
        body.GetProperty("Literal").GetProperty("IntegerLiteral").GetInt32().ToString(CultureInfo.InvariantCulture) + "\n";

    private sealed class OrdersFixture : IAsyncDisposable
    {
        private readonly PhysicalFileProvider _physical;
        private readonly RecordingProvider _provider;
        private HostSession? _host;
        private MepChannelGuest? _ownedChannel;
        private readonly GuestChannel? _channel;
        private readonly PeerInfo _peer = new(PeerKind.Cli, "orders-test", "1.0.0");
        private readonly ModelAccessStart _start;
        private readonly BoundedArtifactStage _stage;
        private readonly bool _omitOrder;
        private readonly FramedLink? _framed;
        private readonly CancellationTokenSource _peerLifetime = new();
        private string? _orderDocumentId;

        public OrdersFixture(TestSandbox sandbox, int order, bool omitOrder,
            bool stallAdvance = false, bool framed = false, bool closeHostOutput = false, bool stallCancel = false,
            int? maxDepth = null, int? maxNodes = null, bool twoDocuments = false, bool yaml = false,
            long? repeatInventoryWithWork = null)
        {
            _omitOrder = omitOrder;
            const string orderPath = "pkg/acme/orders/domain/order.value.json";
            var prefix = FixtureCatalog.BoundedOrders.Directory + (yaml ? "v4/plain-yaml/" : "v4/plain-json/");
            foreach (var fixture in FixtureCatalog.BoundedOrders.Files.Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var path = fixture.RelativePath[prefix.Length..];
                if (omitOrder && path == orderPath) continue;
                var file = Path.Combine(sandbox.Work, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var text = FixtureCatalog.ReadText(fixture);
                if (path == orderPath && order != 73)
                    text = text.Replace("IntegerLiteral\": 73", $"IntegerLiteral\": {order}", StringComparison.Ordinal);
                File.WriteAllText(file, text, new UTF8Encoding(false));
            }
            _physical = new(sandbox.Work);
            _provider = new(_physical);

            var transcript = FixtureCatalog.ReadText(FixtureCatalog.Mep.BoundedGenerationSession);
            var start = JsonNode.Parse(transcript.Split('\n').First(line =>
                line.Contains("\"method\":\"morphir.backend.generateModel.start\"", StringComparison.Ordinal)))!["params"]!;
            if (maxDepth is { } depth) start["limits"]!["maxDepth"] = depth;
            if (maxNodes is { } nodes) start["limits"]!["maxNodes"] = nodes;
            if (twoDocuments) start["limits"]!["maxDocumentsPerBatch"] = 2;
            if (repeatInventoryWithWork is { } work)
            {
                start["limits"]!["maxSessionWorkUnits"] = work;
                start["limits"]!["maxRounds"] = 16;
            }
            _start = start.Deserialize(MepJson.Default.ModelAccessStart)!;
            var support = new ModelAccessSupport([_start.Input], _start.Limits);
            var disk = new GenerationSessionDiskBudget(_start.Limits.MaxSessionDiskBytes);
            _stage = new(sandbox.Root, _start.Limits, disk);

            const string claimsJson = """{"claimsVersion":"0.1.0-draft.2","protocolVersions":["0.2.0-draft.2"],"extension":{"id":"org.finos.morphir.orders","name":"Orders generator","version":"0.1.0","types":["backend"]},"capabilities":{}}""";
            var claims = JsonSerializer.Deserialize(claimsJson, MepJson.Default.CapabilityClaimSet)!;
            var supportJson = JsonSerializer.Serialize(support, MepJson.Default.ModelAccessSupport);
            var capabilities = JsonSerializer.Deserialize(
                $$"""{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":{{supportJson}}}""",
                MepJson.Default.ExtensionCapabilities)!;
            var handlers = new Dictionary<string, MethodHandler>
            {
                [Method.GenerateModelStart.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessStart, MepJson.Default.ModelAccessStep,
                    (request, _) =>
                    {
                        if (closeHostOutput) _framed!.CloseHostOutput();
                        return Task.FromResult(ModelAccessStep.NewContinue(request.SessionId, 1,
                            [ModelAccessRequest.NewInventory("root", "root", repeatInventoryWithWork.HasValue ? 1 : 3, null)],
                            stallAdvance || closeHostOutput ? [Chunk("partial", false)] : [], []));
                    }),
                [Method.GenerateModelAdvance.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessAdvance, MepJson.Default.ModelAccessStep,
                    async (advance, _) =>
                    {
                        if (framed && stallAdvance) await Task.Delay(Timeout.Infinite, _peerLifetime.Token);
                        return Next(advance, twoDocuments, repeatInventoryWithWork.HasValue);
                    }),
                [Method.GenerateModelCancel.WireValue] = MethodHandler.Request(MepJson.Default.ModelAccessCancel, MepJson.Default.ModelAccessCancel,
                    async (cancel, _) =>
                    {
                        Cancelled = true;
                        if (stallCancel) await Task.Delay(Timeout.Infinite, _peerLifetime.Token);
                        return cancel;
                    }),
            };
            var guest = new Guest(new GuestDefinition(claims, capabilities, handlers));
            if (framed)
            {
                _framed = new FramedLink(guest, _peer, support, _peerLifetime.Token);
                _host = _framed.Host;
            }
            else
                _channel = new GuestChannel(guest.Dispatcher, stallAdvance);
        }

        public List<int> Advances { get; } = [];
        public bool Cancelled { get; private set; }
        public bool TransportStopped { get; private set; }
        public string StageDirectory => _stage.Directory;
        public long MaxOutputBatchBytes => _stage.MaxOutputBatchBytes;
        public IEnumerable<string> DefinitionsOpened => _provider.Reads.Where(path =>
            path.EndsWith(".value.json", StringComparison.Ordinal) || path.EndsWith(".type.json", StringComparison.Ordinal));

        public async Task InitializeAsync()
        {
            if (_channel is not null)
            {
                var offer = new ModelAccessSupport([_start.Input], _start.Limits);
                var opened = await MepChannelGuest.StartModelAccessAsync(_channel, _peer, offer, TimeSpan.FromSeconds(5), default);
                _ownedChannel = opened.Match(value => value, error => throw new InvalidOperationException(error.Message));
                _host = _ownedChannel.HostSession;
                return;
            }
            var result = await Host.InitializeAsync(default);
            if (result.IsError) throw new InvalidOperationException(result.ErrorValue.ToString());
        }

        private HostSession Host => _host ?? throw new InvalidOperationException("Initialize the fixture before running it.");

        public BoundedTreeSnapshot OpenSnapshot()
        {
            var snapshot = DocumentTreeStore.OpenSnapshot(new ReadOptions(100, 8192),
                FSharpFunc<Unit, string>.FromConverter(_ => _start.Snapshot.Revision), _provider);
            if (snapshot.IsError) throw new InvalidOperationException(snapshot.ErrorValue.ToString());
            return snapshot.ResultValue;
        }

        public Task<Outcome<StagedGenerationOutput>> RunAsync() =>
            BoundedGenerationSession.RunAsync(Host, OpenSnapshot(), _start, _stage, StopAsync, default);

        private ModelAccessStep Next(ModelAccessAdvance advance, bool twoDocuments, bool repeatInventory)
        {
            Advances.Add(advance.Round);
            if (repeatInventory)
                return advance.Round == 16
                    ? ModelAccessStep.NewComplete(advance.SessionId, [], [])
                    : ModelAccessStep.NewContinue(advance.SessionId, advance.Round + 1,
                        [ModelAccessRequest.NewInventory($"root-{advance.Round + 1}", "root", 1, null)], [], []);
            var deliveries = advance.Deliveries.Select(value => JsonSerializer.SerializeToElement(value, MepJson.Default.ModelAccessDelivery)).ToArray();
            var delivery = deliveries[0];
            ModelAccessStep Ask(ModelAccessRequest request, params ModelArtifactChunk[] artifacts) =>
                ModelAccessStep.NewContinue(advance.SessionId, advance.Round + 1, [request], artifacts, []);

            var opened = advance.Round switch
            {
                <= 3 => Array.Empty<string>(),
                4 => ["pkg/acme/orders/domain/total-in-_usd.value.json"],
                _ when twoDocuments => ["pkg/acme/orders/domain/total-in-_usd.value.json",
                    "pkg/acme/orders/domain/order.value.json", "pkg/acme/orders/domain/total-in-_usd.value.json"],
                _ => ["pkg/acme/orders/domain/total-in-_usd.value.json", "pkg/acme/orders/domain/order.value.json"],
            };
            if (!DefinitionsOpened.SequenceEqual(opened))
                throw new InvalidOperationException("Definitions were read outside their requested round.");

            switch (advance.Round)
            {
                case 1:
                    var module = delivery.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("module"))
                        .Single(entry => entry.GetProperty("documentId").GetString() == "pkg/acme/orders/domain/module");
                    return Ask(ModelAccessRequest.NewInventory("first", module.GetProperty("memberSelection").GetString()!, 2, null));
                case 2:
                    _orderDocumentId = delivery.GetProperty("entries").EnumerateArray().Single(entry => entry.TryGetProperty("value", out _))
                        .GetProperty("value").GetProperty("documentId").GetString();
                    return Ask(ModelAccessRequest.NewInventory("second", "pkg/acme/orders/domain/module", 2,
                        FSharpOption<string>.Some(delivery.GetProperty("nextCursor").GetString()!)));
                case 3:
                    var final = delivery.GetProperty("entries").EnumerateArray().Single().GetProperty("value");
                    return Ask(ModelAccessRequest.NewDocument("final", final.GetProperty("documentId").GetString()!));
                case 4:
                    if (ReadBody(delivery).GetProperty("Reference").GetString() != "acme/orders:domain#order")
                        throw new InvalidOperationException("The final value must reference the order from the first inventory page.");
                    if (twoDocuments)
                        return ModelAccessStep.NewContinue(advance.SessionId, 5,
                            [ModelAccessRequest.NewDocument("order", _orderDocumentId!),
                             ModelAccessRequest.NewDocument("final-again", delivery.GetProperty("documentId").GetString()!)], [], []);
                    return ModelAccessStep.NewContinue(advance.SessionId, 5,
                        [ModelAccessRequest.NewDocument("order", _orderDocumentId!), ModelAccessRequest.NewInventory("confirm", "root", 1, null)],
                        _omitOrder ? [Chunk("partial", false)] : [], []);
                case 5:
                    if (!deliveries.Select(item => item.GetProperty("requestId").GetString())
                            .SequenceEqual(twoDocuments ? ["order", "final-again"] : ["order", "confirm"]))
                        throw new InvalidOperationException("The host changed the order of the requests.");
                    return ModelAccessStep.NewComplete(advance.SessionId,
                        [Chunk(RenderLiteral(ReadBody(delivery)), true)], []);
                default:
                    throw new InvalidOperationException("Unexpected generation round.");
            }
        }

        private static JsonElement ReadBody(JsonElement delivery)
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(delivery.GetProperty("content").GetString()!));
            return DefinitionBody(document.RootElement.GetProperty("def")).Clone();
        }

        private static ModelArtifactChunk Chunk(string text, bool end)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return new("order.txt", 0, bytes.Length, Sha256Digest.Compute(bytes), Convert.ToBase64String(bytes), end);
        }

        private async Task StopAsync()
        {
            await _peerLifetime.CancelAsync();
            if (_framed is not null) await _framed.StopAsync();
            if (_ownedChannel is not null) await _ownedChannel.TerminateModelAccessAsync();
            TransportStopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _peerLifetime.Dispose();
            _stage.Dispose();
            _physical.Dispose();
        }
    }

    private sealed class FramedLink
    {
        private readonly CancellationToken _stop;
        private readonly Stream _hostOutput;
        private readonly Stream[] _streams;
        private readonly Task _running;
        private bool _closed;

        public FramedLink(Guest guest, PeerInfo peer, ModelAccessSupport support, CancellationToken stop)
        {
            _stop = stop;
            var fromGuest = new Pipe();
            var toGuest = new Pipe();
            var input = fromGuest.Reader.AsStream();
            var output = toGuest.Writer.AsStream();
            _hostOutput = output;
            var guestInput = toGuest.Reader.AsStream();
            var guestOutput = fromGuest.Writer.AsStream();
            _streams = [input, output, guestInput, guestOutput];

            Host = HostSession.CreateWithModelAccess(input, output, peer, support, null);
            _running = Task.WhenAll(Host.RunAsync(stop), guest.RunAsync(guestInput, guestOutput, stop));
        }

        public HostSession Host { get; }

        public void CloseHostOutput() => _hostOutput.Dispose();

        public async Task StopAsync()
        {
            if (_closed) return;
            _closed = true;
            try { await _running.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally
            {
                foreach (var stream in _streams) await stream.DisposeAsync();
            }
        }
    }

    private sealed class GuestChannel(Dispatcher dispatcher, bool stallAdvance) : ISingleMessageChannel, IDisposable
    {
        public async Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            if (stallAdvance && method == Method.GenerateModelAdvance.WireValue)
                await Task.Delay(Timeout.Infinite, cancellation);
            return await dispatcher.DispatchAsync(message, cancellation);
        }

        public Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation) =>
            dispatcher.DispatchAsync(message, cancellation);

        public void Dispose() { }
    }

    private sealed class RecordingProvider(IFileProvider source) : IFileProvider
    {
        public List<string> Reads { get; } = [];
        public IFileInfo GetFileInfo(string subpath) =>
            new RecordedFile(source.GetFileInfo(subpath), () => Reads.Add(subpath.Replace('\\', '/').TrimStart('/')));
        public IDirectoryContents GetDirectoryContents(string subpath) => source.GetDirectoryContents(subpath);
        public IChangeToken Watch(string filter) => source.Watch(filter);

        private sealed class RecordedFile(IFileInfo file, Action opened) : IFileInfo
        {
            public bool Exists => file.Exists;
            public long Length => file.Length;
            public string? PhysicalPath => file.PhysicalPath;
            public string Name => file.Name;
            public DateTimeOffset LastModified => file.LastModified;
            public bool IsDirectory => file.IsDirectory;
            public Stream CreateReadStream()
            {
                opened();
                return file.CreateReadStream();
            }
        }
    }
}
