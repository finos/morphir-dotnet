// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation.Mep;
using Morphir.Mep;
using Morphir.TestSupport;
using Morphir.TestSupport.Drivers;
using Morphir.TestSupport.Fixtures;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Scenarios;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.TaskRecords;
using GenerationReport = Morphir.Tooling.Features.Generate.GenerationReport;
using Versioning = Morphir.IR.Versioning;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
public sealed class BoundedGenerationLifetimeTests
{
    private const string OutDir = "build-artifacts";
    private const string DescriptorPath = "recorded/orders-model";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);
    private static string GuestAssembly => Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");

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

    // Store tests invoke the handler in process to observe its producer lease.
    private static async Task<CommandResult> GenerateAsync(CompileWorkspace project, string output)
    {
        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var invocation = new GenerateOverrides(Target: "text", Output: project.PathOf(output))
        {
            OutDir = OutDir,
            Extension = GuestAssembly,
        };
        var outcome = await app.Services.GetRequiredService<IMediator>().InvokeAsync<Outcome<GenerationSuccess>>(
            new GenerateRequest(invocation, new InvocationContext(project.Root) { Environment = project.Context().Environment }));
        var report = GenerationReport.From(outcome, invocation);
        return new CommandResult("GenerateHandler", report.ExitCode, report.ToJson(), "", TimeSpan.Zero, []);
    }

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
