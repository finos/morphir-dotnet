// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

[Category("PlatformSpecific")]
public sealed class BoundedGenerationDriverTests
{
    private sealed class RecordingCli : ICliDriver
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public TestTarget Target => new TestTarget.DevelopmentBuild();
        public MorphirHost Host => MorphirHost.Native;

        public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
        {
            Arguments = arguments;
            return Task.FromResult(new CommandResult("generate", 0, "", "", TimeSpan.Zero, []));
        }

        public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default) =>
            throw new NotSupportedException();
    }

    private static BoundedGenerationDriver Driver(IScenarioWorkspace workspace, RecordingCli cli) =>
        new(workspace, new GenerationDriver(cli), new ExtensionDriver(cli,
            ExtensionArtifacts.Of(new Dictionary<string, string>
            {
                ["Morphir.TestSupport.Extension.BoundedOrders"] = "guest folder/orders.dll",
            }, _ => true)));

    [Test]
    public async Task RecordedInputUsesAnIndependentDescriptorAndKeepsUnselectedMutation()
    {
        await using var workspace = ScenarioWorkspace.Create("");
        var driver = Driver(workspace, new RecordingCli());
        driver.Prepare("orders");
        var tree = driver.RecordTree(74);
        using var record = JsonDocument.Parse(workspace.ReadText(workspace.File("build-artifacts/compile.json")));
        record.RootElement.GetProperty("module").GetString().Should().Be("");
        record.RootElement.GetProperty("ir").GetProperty("version").GetString().Should().Be("v4");
        record.RootElement.GetProperty("ir").GetProperty("path").GetString().Should().Be("recorded/orders-model");
        record.RootElement.GetProperty("value").EnumerateArray().Select(value => value.GetString()).Should().Equal("recorded/orders-model");
        workspace.ReadText(tree.File("pkg/acme/orders/domain/order.type.json")).Should().Be("{");
        workspace.ReadText(tree.File("pkg/acme/orders/domain/order.value.json")).Should().Contain("74");
    }

    [Test]
    public async Task TheCompiledOrdersProjectIsADocumentTreeProjectWithTheDomainSource()
    {
        await using var workspace = ScenarioWorkspace.Create("");
        Driver(workspace, new RecordingCli()).PrepareCompiledOrders();
        workspace.ReadText(workspace.File("morphir.toml")).Should().Be(BoundedOrdersRecordMother.DocumentTreeConfiguration)
            .And.Contain("layout = \"document-tree\"");
        workspace.ReadText(workspace.File("src/Domain.fs")).Should().Be(BoundedOrdersRecordMother.DomainSource);
        workspace.ReadText(workspace.File("bounded-orders.mode")).Should().Be("compiled-orders");
        workspace.Exists(workspace.File("build-artifacts/compile.json")).Should().BeFalse("the compile writes the record");
    }

    [Test]
    public async Task NativeGenerationPreservesPathsAndBackendOptionsAsSingleArguments()
    {
        await using var workspace = ScenarioWorkspace.Create("");
        var cli = new RecordingCli();
        await Driver(workspace, cli).GenerateAsync("output folder", "--option", "padding=two words");
        cli.Arguments.Should().Equal("generate", "--target", "text", "--output", workspace.Directory("output folder").Path,
            "--json=document", "--extension", "guest folder/orders.dll", "--out-dir", "build-artifacts", "--option", "padding=two words");
    }

    [Test]
    public async Task ConfiguredGenerationLeavesExtensionSelectionToTheProject()
    {
        await using var workspace = ScenarioWorkspace.Create("");
        var cli = new RecordingCli();
        await Driver(workspace, cli).GenerateAsync("generated", configured: true);
        cli.Arguments.Should().NotContain("--input").And.NotContain("--extension");
        cli.Arguments.Should().ContainInOrder("--out-dir", "build-artifacts");
    }
}
