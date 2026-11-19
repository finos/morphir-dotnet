// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.Compilation.Mep;
using Morphir.Extensions.Wasm.Tests;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.TestSupport.Drivers;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.TaskRecords;
using Morphir.Tooling.Tests.Integration.CLI;
using Versioning = Morphir.IR.Versioning;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class BoundedGenerateCommandTests
{
    private const string OutDir = "build-artifacts";
    private const string DescriptorPath = "recorded/orders-model";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);
    private static string GuestAssembly => Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");

    [Test]
    [Arguments(73)]
    [Arguments(74)]
    public async Task GeneratesFromTheRecordedTreeAtItsDescriptorPath(int order)
    {
        using var project = Project("orders");
        var store = Store(project, new LineWriter());
        var ir = await RecordTreeAsync(store, order);

        var result = await GenerateAsync(project, "generated");
        result.ShouldExitWith(0);
        var report = result.ShouldWriteOneJsonDocument();
        report.GetProperty("input").GetString().Should().Be(ir);
        report.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()).Should().Equal("order.txt");
        report.GetProperty("diagnostics").EnumerateArray()
            .Select(note => (note.GetProperty("severity").GetString(), note.GetProperty("code").GetString()))
            .Should().Equal(("warning", "orders.inventory"), ("warning", "orders.complete"));
        var bytes = File.ReadAllBytes(project.PathOf("generated/order.txt"));
        bytes.Should().Equal(System.Text.Encoding.UTF8.GetBytes(order.ToString(CultureInfo.InvariantCulture) + "\n"));
        var methods = File.ReadAllLines(project.PathOf("bounded-orders.methods"));
        methods.Should().ContainInOrder(
            "morphir.backend.generateModel.start",
            "morphir.backend.generateModel.advance", "morphir.backend.generateModel.advance",
            "morphir.backend.generateModel.advance", "morphir.backend.generateModel.advance",
            "morphir.backend.generateModel.advance", "morphir.shutdown", "morphir.exit");
        methods.Count(method => method == "morphir.backend.generateModel.advance").Should().Be(5);
        File.Exists(project.PathOf("bounded-orders.exited")).Should().BeTrue();

        if (order != 73) return;
        var baseline = project.PathOf("expanded.json");
        File.WriteAllBytes(baseline, FixtureCatalog.ReadBytes(FixtureCatalog.BoundedOrders.ExpandedV4));
        File.Delete(project.PathOf("bounded-orders.methods"));
        (await GenerateAsync(project, "expanded-generated", "--input", baseline)).ShouldExitWith(0);
        File.ReadAllBytes(project.PathOf("expanded-generated/order.txt")).Should().Equal(bytes);
        File.ReadAllLines(project.PathOf("bounded-orders.methods")).Should()
            .Contain("morphir.backend.generateModel").And.NotContain("morphir.backend.generateModel.start");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RefusesFramesAboveTheAgreedWireLimit(bool oversizedRequest)
    {
        using var project = Project(oversizedRequest ? "small-frame" : "large-reply");
        await RecordTreeAsync(Store(project, new LineWriter()), 73);
        var result = await GenerateAsync(project, "generated",
            oversizedRequest ? ["--option", "padding=" + new string('x', 6000)] : []);

        result.ShouldExitWith(2);
        result.StandardOutput.Should().Contain("maxFrameBytes");
        File.Exists(project.PathOf("generated/order.txt")).Should().BeFalse();
        var methods = File.ReadAllLines(project.PathOf("bounded-orders.methods"));
        methods.Should().Contain("morphir.initialized");
        if (oversizedRequest) methods.Should().NotContain("morphir.backend.generateModel.start");
        else methods.Count(method => method == "morphir.backend.generateModel.advance").Should().Be(5);
    }

    [Test]
    public async Task ARecordedTreeReachesTheConfiguredWasmLoaderWithoutReadingUnselectedBodies()
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: guest instantiation is unavailable; #609.");
        using var project = Project("orders");
        project.Write("morphir.toml", """
            [project]
            name = "acme/orders"
            [frontend]
            language = "fsharp"
            [extensions.text]
            path = "orders.wasm"
            abi = "extism-kernel-v1"
            """);
        File.Copy(MepWasmFixtures.FullPath("fixtures/wasm/initial-memory-4097.wasm"), project.PathOf("orders.wasm"));
        // The mutation makes an unrelated split type invalid. Opening the recorded tree does not need that body.
        await RecordTreeAsync(Store(project, new LineWriter()), 74);
        var result = await DevelopmentCli.RunAsync(
            ["generate", "--target", "text", "--out-dir", OutDir, "--output", "generated", "--json=document"],
            project.Root, project.Context().Environment);

        result.ShouldExitWith(2);
        result.ShouldWriteOneJsonDocument().GetProperty("diagnostics").EnumerateArray()
            .Select(note => note.GetProperty("code").GetString()).Should().Contain("extension.memory_limit");
        Directory.Exists(project.PathOf("generated")).Should().BeFalse();
        Directory.Exists(project.PathOf(OutDir + "/generate/text.dest")).Should().BeFalse();
    }

    [Test]
    [Arguments(1, 32768, "maxGuestStateBytes")]
    [Arguments(1048576, 4119, "maxFrameBytes")]
    [Arguments(1048576, 4120, "with a response for the id 0")]
    public async Task ConfiguredWasmAppliesSelectedStateAndFullFrameLimits(int stateBytes, int frameBytes, string diagnostic)
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: guest instantiation is unavailable; #609.");

        using var project = Project("orders");
        project.Write("morphir.toml", """
            [project]
            name = "acme/orders"
            [frontend]
            language = "fsharp"
            [extensions.text]
            path = "orders.wasm"
            abi = "extism-kernel-v1"
            """);
        var transcript = FixtureCatalog.ReadText(FixtureCatalog.Mep.BoundedGenerationSession);
        var start = JsonNode.Parse(transcript.Split('\n').First(line =>
            line.Contains("\"method\":\"morphir.backend.generateModel.start\"", StringComparison.Ordinal)))!["params"]!;
        var limits = start["limits"]!.DeepClone();
        limits["maxGuestStateBytes"] = stateBytes;
        limits["maxFrameBytes"] = frameBytes;
        var selection = new JsonObject { ["tuples"] = new JsonArray(start["input"]!.DeepClone()), ["limits"] = limits };
        const string replyPrefix = """
            {"jsonrpc":"2.0","id":0,"result":{"protocolVersion":"0.2.0-draft.2","extension":{"id":"org.example.orders","name":"Orders","version":"1.0.0","types":["backend"]},"capabilities":{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":
            """;
        var reply = System.Text.Encoding.UTF8.GetBytes(replyPrefix + selection.ToJsonString() + "}}}");
        var escaped = string.Concat(reply.Select(value => $"\\{value:x2}"));
        File.WriteAllBytes(project.PathOf("orders.wasm"), Wasmtime.Module.ConvertText($$"""
            (module
              (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
              (import "extism:host/env" "store_u8" (func $store (param i64 i32)))
              (import "extism:host/env" "output_set" (func $out (param i64 i64)))
              (memory 1)
              (data (i32.const 0) "{{escaped}}")
              (global $initialized (mut i32) (i32.const 0))
              (func (export "handle") (result i32) (local $offset i64) (local $index i32) (local $length i32)
                ;; Once the 1 MiB selection is active, growth to 17 pages must be refused.
                (if (global.get $initialized)
                  (then
                    (if (i32.ne (memory.grow (i32.const 16)) (i32.const -1)) (then unreachable))))
                (local.set $length (select (i32.const 4096) (i32.const {{reply.Length}}) (global.get $initialized)))
                (global.set $initialized (i32.const 1))
                (local.set $offset (call $alloc (i64.extend_i32_u (local.get $length))))
                (loop $copy
                  (call $store (i64.add (local.get $offset) (i64.extend_i32_u (local.get $index)))
                    (select (i32.load8_u (local.get $index)) (i32.const 32)
                      (i32.lt_u (local.get $index) (i32.const {{reply.Length}}))))
                  (local.set $index (i32.add (local.get $index) (i32.const 1)))
                  (br_if $copy (i32.lt_u (local.get $index) (local.get $length))))
                (call $out (local.get $offset) (i64.extend_i32_u (local.get $length)))
                (i32.const 0)))
            """));
        await RecordTreeAsync(Store(project, new LineWriter()), 73);

        var result = await DevelopmentCli.RunAsync(
            ["generate", "--target", "text", "--out-dir", OutDir, "--output", "generated", "--json=document"],
            project.Root, project.Context().Environment);

        result.ShouldExitWith(2);
        result.StandardOutput.Should().Contain(diagnostic);
        Directory.Exists(project.PathOf("generated")).Should().BeFalse();
    }

    [Test]
    [Arguments(73, "73\n")]
    [Arguments(74, "74\n")]
    [Arguments(128, "128\n")]
    public async Task ConfiguredWasmGeneratesExactOrdersOutput(int order, string expected)
    {
        if (WasmRuntime.Process.Platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: guest instantiation is unavailable; #609.");
        var module = Environment.GetEnvironmentVariable("MORPHIR_SHARED_ORDERS_WASM");
        if (string.IsNullOrEmpty(module))
            Skip.Test("Set MORPHIR_SHARED_ORDERS_WASM to the shared Orders module assembled from the parent kit.");

        using var project = Project("orders");
        project.Write("morphir.toml", """
            [project]
            name = "acme/orders"
            [frontend]
            language = "fsharp"
            [extensions.text]
            path = "orders.wasm"
            abi = "extism-kernel-v1"
            sha256 = "d12319c81f125c41d4e073e10d11c61bde9e64ebe14671dcf7ffffc8cb2320e0"
            """);
        File.Copy(module!, project.PathOf("orders.wasm"));
        await RecordTreeAsync(Store(project, new LineWriter()), order == 128 ? 73 : order,
            reserve: order == 128 ? 128 : null);
        var result = await DevelopmentCli.RunAsync(
            ["generate", "--target", "text", "--out-dir", OutDir, "--output", "generated", "--json=document"],
            project.Root, project.Context().Environment);

        result.ShouldExitWith(0);
        var report = result.ShouldWriteOneJsonDocument();
        report.GetProperty("backendTransport").GetString().Should().Be("wasm");
        report.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()).Should().Equal("order.txt");
        File.ReadAllBytes(project.PathOf("generated/order.txt")).Should().Equal(System.Text.Encoding.UTF8.GetBytes(expected));
    }

    [Test]
    public async Task RefusesPublicationInsideASiblingMembersTaskStore()
    {
        using var project = Project("orders");
        await RecordTreeAsync(Store(project, new LineWriter()), 73);
        const string sibling = OutDir + "/sibling";
        const string record = "existing producer record";
        project.Write(sibling + "/compile.json", record);

        var result = await GenerateAsync(project, sibling);

        result.ShouldExitWith(2);
        result.ShouldWriteOneJsonDocument().GetProperty("diagnostics").EnumerateArray()
            .Select(note => note.GetProperty("code").GetString()).Should().Contain("extension.task_output_overlap");
        File.ReadAllText(project.PathOf(sibling + "/compile.json")).Should().Be(record);
        File.Exists(project.PathOf(sibling + "/order.txt")).Should().BeFalse();
        File.Exists(project.PathOf("bounded-orders.methods")).Should().BeFalse();
    }

    [Test]
    public async Task HoldsTheProducerLeaseWhileTheGuestReadsAndShutsDown()
    {
        using var project = Project("pause");
        var waits = new LineWriter();
        var store = Store(project, waits);
        var ir = await RecordTreeAsync(store, 73);
        var generation = GenerateAsync(project, "generated");
        using var writerBudget = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        Task<Outcome<CompileTaskRun>>? writing = null;
        try
        {
            await WaitForGuestAsync(project, "paused", generation);
            writing = store.BeginAsync(writerBudget.Token).AsTask();
            await waits.FirstLine.WaitAsync(Bound);
            writing.IsCompleted.Should().BeFalse("the guest still reads the producer's model");
            Directory.Exists(ir).Should().BeTrue();
            Directory.Exists(project.PathOf(OutDir + "/generate/text.dest")).Should().BeTrue();

            project.Write("bounded-orders.resume", "");
            await WaitForGuestAsync(project, "shutting-down", generation);
            writing.IsCompleted.Should().BeFalse("the guest has not finished teardown");
            Directory.Exists(ir).Should().BeTrue();
            File.Exists(project.PathOf("generated/order.txt")).Should().BeFalse();
        }
        finally
        {
            project.Write("bounded-orders.resume", "");
            project.Write("bounded-orders.finish-shutdown", "");
            try { await generation; }
            finally
            {
                if (writing is not null)
                {
                    var settled = await writing;
                    if (settled is Outcome<CompileTaskRun>.Success granted) granted.Value.Dispose();
                }
            }
        }
        (await generation).ShouldExitWith(0);
        writing!.Result.Should().BeOfType<Outcome<CompileTaskRun>.Success>();
        File.Exists(project.PathOf("bounded-orders.exited")).Should().BeTrue();
        File.ReadAllBytes(project.PathOf("generated/order.txt")).Should().Equal("73\n"u8.ToArray());
    }

    [Test]
    public async Task FailureSettlesAnExitedGuestRootsDescendantBeforeReleasingTheProducer()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows creation-time job ownership is required.");
        using var project = Project("root-exits");
        var waits = new LineWriter();
        var store = Store(project, waits);
        await RecordTreeAsync(store, 73);
        var generation = GenerateAsync(project, "generated");
        using var writerBudget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<Outcome<CompileTaskRun>>? writer = null;
        Process? descendant = null;
        try
        {
            await WaitForGuestAsync(project, "descendant", generation);
            var identity = File.ReadAllText(project.PathOf("bounded-orders.descendant")).Split('|');
            var monitored = Process.GetProcessById(int.Parse(identity[0], CultureInfo.InvariantCulture));
            if (monitored.StartTime.ToUniversalTime().Ticks != long.Parse(identity[1], CultureInfo.InvariantCulture))
            {
                monitored.Dispose();
                throw new InvalidOperationException("The descendant PID no longer belongs to the marked process.");
            }
            descendant = monitored;
            descendant.HasExited.Should().BeFalse();
            writer = store.BeginAsync(writerBudget.Token).AsTask();
            await waits.FirstLine.WaitAsync(Bound);
            writer.IsCompleted.Should().BeFalse();

            project.Write("bounded-orders.exit-root", "");
            (await writer.WaitAsync(Bound)).ShouldSucceed();
            descendant.HasExited.Should().BeTrue("the writer must wait for native guest settlement");
            var result = await generation.WaitAsync(Bound);
            result.ShouldExitWith(2);
            var diagnostics = result.ShouldWriteOneJsonDocument().GetProperty("diagnostics");
            var codes = diagnostics.EnumerateArray().Select(note => note.GetProperty("code").GetString()).ToArray();
            codes.Should().Contain(code => code == "extension.bounded_deadline" || code == "extension.bounded_exchange");
            codes.Should().NotContain("extension.bounded_termination", "the guest must settle cleanly: {0}", diagnostics);
            File.Exists(project.PathOf("generated/order.txt")).Should().BeFalse();
        }
        finally
        {
            project.Write("bounded-orders.exit-root", "");
            if (descendant is { HasExited: false })
            {
                descendant.Kill(entireProcessTree: true);
                await descendant.WaitForExitAsync().WaitAsync(Bound);
            }
            descendant?.Dispose();
            try { await generation; }
            finally
            {
                if (writer is not null && await writer is Outcome<CompileTaskRun>.Success granted)
                    granted.Value.Dispose();
            }
        }
    }

    [Test]
    public async Task NegotiatedSessionDeadlineCoversExitAfterShutdownAcknowledgement()
    {
        using var project = Project("stall-exit");
        var waits = new LineWriter();
        var store = Store(project, waits);
        await RecordTreeAsync(store, 73);
        var generation = GenerateAsync(project, "generated");
        using var writerDeadline = new CancellationTokenSource(Bound);
        Task<Outcome<CompileTaskRun>>? writing = null;
        try
        {
            await WaitForGuestAsync(project, "before-delayed-complete", generation);
            using var guest = ReadMarkedProcess(project.PathOf("bounded-orders.before-delayed-complete"));
            guest.Should().NotBeNull();
            writing = store.BeginAsync(writerDeadline.Token).AsTask();
            await waits.FirstLine.WaitAsync(Bound);
            writing.IsCompleted.Should().BeFalse("the guest has not returned its final batch");
            (await writing.WaitAsync(Bound)).ShouldSucceed();
            guest!.HasExited.Should().BeTrue("the writer acquires only after forced settlement");

            var result = await generation.WaitAsync(Bound);
            File.Exists(project.PathOf("bounded-orders.exit-received")).Should().BeTrue(
                "the guest must receive exit after acknowledging shutdown");
            result.ShouldExitWith(2);
            var report = result.ShouldWriteOneJsonDocument();
            report.GetProperty("diagnostics").EnumerateArray()
                .Select(note => note.GetProperty("code").GetString()).Should().Contain("extension.bounded_deadline");
            report.GetProperty("backend").GetString().Should().Be("org.finos.morphir.orders");
            report.GetProperty("backendVersion").GetString().Should().Be("0.1.0");
            report.GetProperty("backendTransport").GetString().Should().Be("mep");
            // Each delay fits a fresh deadline. Together they exceed the original session's 6 seconds.
            File.Exists(project.PathOf("bounded-orders.exited")).Should().BeFalse();
            File.Exists(project.PathOf("generated/order.txt")).Should().BeFalse();
        }
        finally
        {
            try { await generation; }
            finally
            {
                if (writing is not null && await writing is Outcome<CompileTaskRun>.Success granted)
                    granted.Value.Dispose();
            }
        }
    }

    [Test]
    public async Task AFailedInitializedWriteStopsTheNativeGuestBeforeReturning()
    {
        using var project = Project("mark-initialize");
        using var slot = await ProcessGate.Shared.EnterAsync();
        var options = MepGuestOptions.Create(DotnetHost.Path, [GuestAssembly], project.Root, TimeSpan.FromSeconds(5),
            TextWriter.Null, "orders-test", "1.0.0");
        var offer = new ModelTransportSupport([MepGenerationTransport.V4Tuple], new ModelTransportLimits(8192, 6144, 64, 256), []);
        var marker = project.PathOf("bounded-orders.initialized-reply");
        try
        {
            var started = await MepGuestProcess.StartUsingAsync(options,
                (input, output) => new HostSession(input, new FailInitializedWrite(output), options.Host, offer), default)
                .WaitAsync(Bound);
            File.Exists(marker).Should().BeTrue("the guest must have returned an initialize result");
            started.IsError.Should().BeTrue();
            started.ErrorValue.Code.Should().Be("extension.protocol_error");
            started.ErrorValue.Message.Should().Contain("initialize notification write");
            using var child = ReadMarkedProcess(marker);
            (child is null || child.HasExited).Should().BeTrue("the owner must settle the guest before returning");
        }
        finally
        {
            if (File.Exists(marker))
            {
                using var child = ReadMarkedProcess(marker);
                if (child is not null && !child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(Bound);
                }
            }
        }
    }

    [Test]
    public async Task ARecordedFifoIsRefusedBeforeOpeningIt()
    {
        if (OperatingSystem.IsWindows()) Skip.Test("A POSIX FIFO is required.");
        using var project = Project("orders");
        var input = await RecordTreeAsync(Store(project, new LineWriter()), 73);
        var manifest = Path.Combine(input, "manifest.json");
        File.Delete(manifest);
        MkFifo(manifest, 0x180).Should().Be(0);
        var result = await GenerateAsync(project, "generated");
        result.ShouldExitWith(2);
        result.StandardOutput.Should().Contain("Only regular input files are supported.");
        File.Exists(project.PathOf("bounded-orders.methods")).Should().BeFalse();
        Directory.Exists(project.PathOf("generated")).Should().BeFalse();
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    private static Process? ReadMarkedProcess(string marker)
    {
        var pid = int.Parse(File.ReadAllText(marker), CultureInfo.InvariantCulture);
        try { return Process.GetProcessById(pid); }
        catch (ArgumentException) { return null; }
    }

    private sealed class FailInitializedWrite(Stream output) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => output.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            buffer.Span.IndexOf("\"morphir.initialized\""u8) >= 0
                ? ValueTask.FromException(new IOException("Forced initialize notification write failure."))
                : output.WriteAsync(buffer, cancellationToken);
    }

    private static CompileWorkspace Project(string mode)
    {
        var project = new CompileWorkspace();
        project.Write("morphir.toml", "[project]\nname = \"acme/orders\"\n\n[frontend]\nlanguage = \"fsharp\"\n");
        project.Write("bounded-orders.mode", mode);
        return project;
    }

    private static CompileTaskStore Store(CompileWorkspace project, LineWriter waits)
    {
        var root = OutRoot.Resolve(new OutRootRequest(project.Root, project.Root) { OutDirFlag = OutDir }).ShouldSucceed();
        return new CompileTaskStore(root, MemberPath.Root, new TaskStoreOptions
        {
            Diagnostics = waits,
            PollInterval = TimeSpan.FromMilliseconds(10),
        });
    }

    private static async Task<string> RecordTreeAsync(CompileTaskStore store, int order, int? reserve = null)
    {
        using var run = (await store.BeginAsync(default)).ShouldSucceed();
        var descriptor = IrDescriptor.Create(RecordPath.Parse(DescriptorPath), IrLayout.DocumentTree,
            IrStorageFormat.Json, Versioning.parse("4").ResultValue);
        var root = descriptor.Path.Under(run.Dest);
        var prefix = FixtureCatalog.BoundedOrders.Directory + "v4/plain-json/";
        foreach (var fixture in FixtureCatalog.BoundedOrders.Files.Where(item => item.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = fixture.RelativePath[prefix.Length..];
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (order != 73 && relative == "pkg/acme/orders/domain/order.type.json")
                File.WriteAllText(path, "{");
            else if (order != 73 && relative == "pkg/acme/orders/domain/order.value.json")
            {
                var contents = FixtureCatalog.ReadText(fixture).Replace("IntegerLiteral\": 73",
                    "IntegerLiteral\": " + order.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                File.WriteAllText(path, contents);
            }
            else
                File.WriteAllBytes(path, FixtureCatalog.ReadBytes(fixture));
        }
        if (reserve is { } amount)
        {
            var domain = Path.Combine(root, "pkg/acme/orders/domain");
            var modulePath = Path.Combine(domain, "module.json");
            var module = JsonNode.Parse(File.ReadAllText(modulePath))!;
            module["values"]!.AsArray().Insert(1, JsonValue.Create("reserve"));
            File.WriteAllText(modulePath, module.ToJsonString());

            var referencePath = Path.Combine(domain, "total-in-_usd.value.json");
            var reference = JsonNode.Parse(File.ReadAllText(referencePath))!;
            reference["def"]!["Public"]!["ExpressionBody"]!["body"]!["Reference"] = "acme/orders:domain#reserve";
            File.WriteAllText(referencePath, reference.ToJsonString());

            var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(domain, "order.value.json")))!;
            definition["name"] = "reserve";
            definition["def"]!["Public"]!["ExpressionBody"]!["body"]!["Literal"]!["IntegerLiteral"] = amount;
            File.WriteAllText(Path.Combine(domain, "reserve.value.json"), definition.ToJsonString());
        }
        run.Complete(new CompileProduct("fsharp", descriptor, null)).ShouldSucceed();
        return root;
    }

    private static Task<CommandResult> GenerateAsync(CompileWorkspace project, string output, params string[] extra) =>
        DevelopmentCli.RunAsync(
            ["generate", "--target", "text", "--out-dir", OutDir, "--output", output,
                "--extension", GuestAssembly, "--json=document", .. extra],
            project.Root, project.Context().Environment);

    private static async Task WaitForGuestAsync(CompileWorkspace project, string marker, Task<CommandResult> generation)
    {
        using var timeout = new CancellationTokenSource(Bound);
        while (!File.Exists(project.PathOf("bounded-orders." + marker)))
        {
            if (generation.IsCompleted)
                throw new CommandAssertionException($"Generation ended before the guest signalled {marker}.", await generation);
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
