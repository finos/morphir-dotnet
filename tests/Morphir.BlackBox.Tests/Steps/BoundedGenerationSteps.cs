// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using FluentAssertions;
using Reqnroll.UnitTestProvider;
using Morphir.TestSupport.Fixtures;

namespace Morphir.BlackBox.Tests.Steps;

[Binding]
public sealed class BoundedGenerationSteps(IBoundedGenerationDriver bounded, IScenarioWorkspace workspace, ScenarioRun run,
    IUnitTestRuntimeProvider runtime)
{
    private const string OutDir = "build-artifacts";

    /// <summary>The out root that <c>When I compile the project</c> and generation from the last compile share: the default.</summary>
    private WorkspaceDirectory CompileOutRoot => workspace.Work.Directory(".morphir", "out");

    [Given("the Orders domain source in a document-tree project")]
    public void GivenTheOrdersDomainSourceInADocumentTreeProject() => bounded.PrepareCompiledOrders();

    [Then("the compile records the Orders document tree")]
    public void TheCompileRecordsTheOrdersDocumentTree()
    {
        run.Result.ShouldExitWith(0);
        var tree = CompileOutRoot.Directory("compile.dest", "morphir-ir");
        CompilationReport.Of(run.Result).ShouldSucceed().Output.Should().Be(tree.Path);
        workspace.Exists(tree.File("manifest.json")).Should().BeTrue();
        workspace.Exists(tree.File("pkg/acme/orders/domain/module.json")).Should().BeTrue();
        using var record = System.Text.Json.JsonDocument.Parse(workspace.ReadText(CompileOutRoot.File("compile.json")));
        record.RootElement.GetProperty("value").EnumerateArray().Select(item => item.GetString()).Should().Equal("morphir-ir");
        var ir = record.RootElement.GetProperty("ir");
        (ir.GetProperty("path").GetString(), ir.GetProperty("layout").GetString(), ir.GetProperty("format").GetString(),
            ir.GetProperty("version").GetString()).Should().Be(("morphir-ir", "document-tree", "json", "v4"));
    }

    [Then("bounded generation renders order {int} from the compile record")]
    public async Task BoundedGenerationRendersTheOrderFromTheCompileRecord(int order)
    {
        var result = await bounded.GenerateFromLastCompileAsync("generated");
        result.ShouldExitWith(0);
        var report = GenerationReport.Of(result).Document;
        report.GetProperty("input").GetString().Should().Be(CompileOutRoot.Directory("compile.dest", "morphir-ir").Path);
        report.GetProperty("representation").GetString().Should().Be("DocumentTree");
        report.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()).Should().Equal("order.txt");
        workspace.ReadBytes(workspace.File("generated/order.txt"))
            .Should().Equal(System.Text.Encoding.UTF8.GetBytes(order.ToString(CultureInfo.InvariantCulture) + "\n"));
        workspace.ReadText(workspace.File("bounded-orders.methods")).Should().Contain("morphir.backend.generateModel.start");
    }

    [Then("bounded generation reads order {int} from its recorded descriptor")]
    public async Task GeneratesFromTheRecordedTreeAtItsDescriptorPath(int order)
    {
        bounded.Prepare("orders");
        var ir = bounded.RecordTree(order);

        var result = await bounded.GenerateAsync("generated");
        result.ShouldExitWith(0);
        var report = GenerationReport.Of(result).Document;
        report.GetProperty("input").GetString().Should().Be(ir.Path);
        report.TryGetProperty("boundedAccess", out _).Should().BeFalse();
        report.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()).Should().Equal("order.txt");
        report.GetProperty("diagnostics").EnumerateArray()
            .Select(note => (note.GetProperty("severity").GetString(), note.GetProperty("code").GetString()))
            .Should().Equal(("warning", "orders.inventory"), ("warning", "orders.complete"));
        var bytes = workspace.ReadBytes(workspace.File("generated/order.txt"));
        bytes.Should().Equal(System.Text.Encoding.UTF8.GetBytes(order.ToString(CultureInfo.InvariantCulture) + "\n"));
        var methods = workspace.ReadText(workspace.File("bounded-orders.methods")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        methods.Should().ContainInOrder(
            "morphir.backend.generateModel.start",
            "morphir.backend.generateModel.advance", "morphir.backend.generateModel.advance",
            "morphir.backend.generateModel.advance", "morphir.backend.generateModel.advance",
            "morphir.backend.generateModel.advance", "morphir.shutdown", "morphir.exit");
        methods.Count(method => method == "morphir.backend.generateModel.advance").Should().Be(5);
        workspace.Exists(workspace.File("bounded-orders.exited")).Should().BeTrue();

        if (order != 73) return;
        var baseline = workspace.File("expanded.json");
        workspace.WriteBytes(baseline, FixtureCatalog.ReadBytes(FixtureCatalog.BoundedOrders.ExpandedV4));
        workspace.Delete(workspace.File("bounded-orders.methods"));
        (await bounded.GenerateAsync("expanded-generated", "--input", baseline.Path)).ShouldExitWith(0);
        workspace.ReadBytes(workspace.File("expanded-generated/order.txt")).Should().Equal(bytes);
        workspace.ReadText(workspace.File("bounded-orders.methods")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Should()
            .Contain("morphir.backend.generateModel").And.NotContain("morphir.backend.generateModel.start");
    }

    [Then("bounded generation refuses oversized request {string}")]
    public async Task RefusesFramesAboveTheAgreedWireLimit(string oversized)
    {
        var oversizedRequest = bool.Parse(oversized);
        bounded.Prepare(oversizedRequest ? "small-frame" : "large-reply");
        bounded.RecordTree(73);
        var result = await bounded.GenerateAsync("generated",
            oversizedRequest ? ["--option", "padding=" + new string('x', 6000)] : []);

        result.ShouldExitWith(2);
        result.StandardOutput.Should().Contain("maxFrameBytes");
        workspace.Exists(workspace.File("generated/order.txt")).Should().BeFalse();
        var methods = workspace.ReadText(workspace.File("bounded-orders.methods")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        methods.Should().Contain("morphir.initialized");
        if (oversizedRequest) methods.Should().NotContain("morphir.backend.generateModel.start");
        else methods.Count(method => method == "morphir.backend.generateModel.advance").Should().Be(5);
    }

    [Then("the recorded tree reaches the configured WASM loader")]
    public async Task ARecordedTreeReachesTheConfiguredWasmLoaderWithoutReadingUnselectedBodies()
    {
        bounded.Prepare("orders");
        bounded.ConfigureMemoryLimitGuest();
        // The mutation makes an unrelated split type invalid. Opening the recorded tree does not need that body.
        bounded.RecordTree(74);
        var result = await bounded.GenerateAsync("generated", configured: true);

        result.ShouldExitWith(2);
        GenerationReport.Of(result).Document.GetProperty("diagnostics").EnumerateArray()
            .Select(note => note.GetProperty("code").GetString()).Should().Contain("extension.memory_limit");
        workspace.Exists(workspace.Directory("generated")).Should().BeFalse();
        workspace.Exists(workspace.Directory(OutDir + "/generate/text.dest")).Should().BeFalse();
    }

    [Then("configured WASM enforces state {int} and frame {int} with diagnostic {string}")]
    public async Task ConfiguredWasmAppliesSelectedStateAndFullFrameLimits(int stateBytes, int frameBytes, string diagnostic)
    {
        bounded.Prepare("orders");
        bounded.ConfigureWasmLimits(stateBytes, frameBytes);
        bounded.RecordTree(73);

        var result = await bounded.GenerateAsync("generated", configured: true);

        result.ShouldExitWith(2);
        result.StandardOutput.Should().Contain(diagnostic);
        workspace.Exists(workspace.Directory("generated")).Should().BeFalse();
    }

    [Then("configured WASM generates order {int}")]
    public async Task ConfiguredWasmGeneratesExactOrdersOutput(int order)
    {
        var module = bounded.SharedOrdersModule;
        if (string.IsNullOrEmpty(module))
        {
            runtime.TestIgnore("Set MORPHIR_SHARED_ORDERS_WASM to the shared Orders module assembled from the parent kit.");
            return;
        }

        bounded.Prepare("orders");
        bounded.ConfigureSharedOrdersGuest(module!);
        bounded.RecordTree(order == 128 ? 73 : order,
            reserve: order == 128 ? 128 : null);
        var result = await bounded.GenerateAsync("generated", configured: true);

        result.ShouldExitWith(0);
        var report = GenerationReport.Of(result).Document;
        report.GetProperty("backendTransport").GetString().Should().Be("wasm");
        report.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()).Should().Equal("order.txt");
        workspace.ReadBytes(workspace.File("generated/order.txt")).Should().Equal(System.Text.Encoding.UTF8.GetBytes(order.ToString(CultureInfo.InvariantCulture) + "\n"));
    }

    [Then("the Orders scale profile generates original production payloads")]
    public async Task OrdersScaleProfileGeneratesProductionPayloads()
    {
        bounded.ConfigureOrdersScaleGuest();
        bounded.RecordScaleProductionTree();
        var result = await bounded.GenerateOrdersScaleAsync("generated");
        result.ShouldExitWith(0);
        var report = GenerationReport.Of(result).Document;
        report.GetProperty("backendTransport").GetString().Should().Be("wasm");
        report.GetProperty("artifacts").EnumerateArray().Select(value => value.GetString()).Should().Equal("orders/m000000.txt");
        var bytes = workspace.ReadBytes(workspace.File("generated/orders/m000000.txt"));
        bytes.Length.Should().Be(OrdersScaleProductionMother.ArtifactBytes);
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant().Should().Be(OrdersScaleProductionMother.ArtifactSha256);
        var access = report.GetProperty("boundedAccess");
        access.GetProperty("maxDocumentBytes").GetInt64().Should().Be(32_768);
        // Original JSON bytes include their LF; emitted payload bytes include the guest's LF.
        access.GetProperty("maxInputBatchBytes").GetInt64().Should().Be(16_560);
        access.GetProperty("maxOutputBatchBytes").GetInt64().Should().Be(16_385);
        var limits = access.GetProperty("limits");
        limits.GetProperty("maxDecodedBytesPerExchange").GetInt32().Should().Be(65_536);
        limits.GetProperty("maxFrameBytes").GetInt32().Should().Be(65_536);
        limits.GetProperty("maxArtifactChunkBytes").GetInt32().Should().Be(32_768);
        access.GetProperty("peakDiscoveryIndexChargedBytes").GetInt64().Should().BeInRange(
            1, limits.GetProperty("maxHostCacheBytes").GetInt64() / 4);

        var measured = access.GetProperty("linearMemory");
        measured.GetProperty("peakGuestLinearMemoryBytes").GetInt64().Should().Be(1_048_576);
        var ceiling = measured.GetProperty("perMemoryCeilingBytes").GetInt64();
        ceiling.Should().Be(limits.GetProperty("maxGuestStateBytes").GetInt64());
        var kernelBytes = measured.GetProperty("peakKernelLinearMemoryBytes").GetInt64();
        kernelBytes.Should().BeInRange(65_536L, ceiling);
        (kernelBytes % 65_536).Should().Be(0);
    }

    [Then("the Orders scale profile refuses a document one byte over")]
    public async Task OrdersScaleProfileRefusesDocumentOneOver()
    {
        bounded.ConfigureOrdersScaleGuest();
        bounded.RecordScaleDocumentOverTree();
        var record = workspace.ReadText(workspace.File("build-artifacts/compile.json"));
        var result = await bounded.GenerateOrdersScaleAsync("generated");
        result.ShouldExitWith(2);
        GenerationReport.Of(result).Document.GetProperty("diagnostics").EnumerateArray()
            .Select(note => note.GetProperty("code").GetString()).Should().Contain("tree.file_too_large");
        workspace.Exists(workspace.Directory("generated")).Should().BeFalse();
        workspace.ReadText(workspace.File("build-artifacts/compile.json")).Should().Be(record);
    }
    [Then("bounded generation records only completed products and preserves the prior install on late failure")]
    public async Task TombstonesTheResultAndPreservesInstalledFilesAfterALateReadFailure()
    {
        const string task = OutDir + "/generate/text";
        var recordFile = workspace.File(task + ".json");
        var installedFile = workspace.File("generated/order.txt");
        bounded.Prepare("orders");
        var input = bounded.RecordTree(73);

        (await bounded.GenerateAsync("generated")).ShouldExitWith(0);
        using var before = System.Text.Json.JsonDocument.Parse(workspace.ReadText(recordFile));
        var completed = before.RootElement;
        completed.GetProperty("task").GetString().Should().Be("generate/text");
        completed.GetProperty("value").EnumerateArray().Select(path => path.GetString()).Should().Equal("order.txt");
        completed.GetProperty("installed").EnumerateObject().Single().Value.EnumerateArray()
            .Select(path => path.GetString()).Should().Equal("order.txt");
        workspace.ReadText(workspace.File(task + ".dest/order.txt")).Should().Be("73\n");
        workspace.ReadText(installedFile).Should().Be("73\n");

        bounded.Prepare("stage-before-read");
        workspace.WriteText(input.File("pkg/acme/orders/domain/order.value.json"), "{");
        var methodsFile = workspace.File("bounded-orders.methods");
        workspace.Delete(methodsFile);

        (await bounded.GenerateAsync("generated")).ShouldExitWith(2);
        workspace.ReadText(methodsFile).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(method => method == "morphir.backend.generateModel.advance").Should().Be(4);
        using var after = System.Text.Json.JsonDocument.Parse(workspace.ReadText(recordFile));
        var tombstone = after.RootElement;
        tombstone.GetProperty("tombstone").GetBoolean().Should().BeTrue();
        tombstone.GetProperty("value").GetArrayLength().Should().Be(0);
        tombstone.GetProperty("completedAt").GetString().Should().Be(completed.GetProperty("completedAt").GetString());
        System.Text.Json.JsonElement.DeepEquals(completed.GetProperty("installed"), tombstone.GetProperty("installed")).Should().BeTrue();
        workspace.ReadText(installedFile).Should().Be("73\n");
        workspace.Exists(workspace.File("generated/prelude.txt")).Should().BeFalse();
        workspace.Exists(workspace.File(task + ".dest/prelude.txt")).Should().BeFalse();
    }

    [Then("bounded generation protects sibling task records")]
    public async Task RefusesPublicationInsideASiblingMembersTaskStore()
    {
        bounded.Prepare("orders");
        bounded.RecordTree(73);
        const string sibling = OutDir + "/sibling";
        const string record = "existing producer record";
        workspace.WriteText(workspace.File(sibling + "/compile.json"), record);

        var result = await bounded.GenerateAsync(sibling);

        result.ShouldExitWith(2);
        GenerationReport.Of(result).Document.GetProperty("diagnostics").EnumerateArray()
            .Select(note => note.GetProperty("code").GetString()).Should().Contain("extension.task_output_overlap");
        workspace.ReadText(workspace.File(sibling + "/compile.json")).Should().Be(record);
        workspace.Exists(workspace.File(sibling + "/order.txt")).Should().BeFalse();
        workspace.Exists(workspace.File("bounded-orders.methods")).Should().BeFalse();
    }

    [Then("bounded generation refuses a recorded FIFO")]
    public async Task ARecordedFifoIsRefusedBeforeOpeningIt()
    {
        if (bounded.IsWindows)
        {
            runtime.TestIgnore("A POSIX FIFO is required.");
            return;
        }
        bounded.Prepare("orders");
        var input = bounded.RecordTree(73);
        var manifest = input.File("manifest.json");
        workspace.Delete(manifest);
        bounded.ReplaceWithFifo(manifest);
        var result = await bounded.GenerateAsync("generated");
        result.ShouldExitWith(2);
        result.StandardOutput.Should().Contain("Only regular input files are supported.");
        workspace.Exists(workspace.File("bounded-orders.methods")).Should().BeFalse();
        workspace.Exists(workspace.Directory("generated")).Should().BeFalse();
    }

}
