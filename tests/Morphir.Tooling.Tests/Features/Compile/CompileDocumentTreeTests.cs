// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Files;
using Morphir.Frontend.FSharp;
using Morphir.IR.Trees;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;
using GenerationReport = Morphir.Tooling.Features.Generate.GenerationReport;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// <c>ir.layout = "document-tree"</c> and <c>--ir-layout</c>: the compile writes a JSON document tree to
/// <c>compile.dest/morphir-ir</c>, records it in <c>compile.json</c>, installs it with <c>-o</c>, generates from it with
/// <c>--target</c>, and gives bounded <c>generate</c> a record that it reads.
/// </summary>
[Category("PlatformSpecific")]
public sealed class CompileDocumentTreeTests
{
    private const string TreeDirectory = ".morphir/out/compile.dest/morphir-ir";

    /// <summary>The Orders domain that the bounded orders guest reads: a type and a value <c>order</c>, and a value that references it.</summary>
    private const string OrdersDomain =
        "namespace Acme.Orders\n\nmodule Domain =\n    type Order = int\n    let order : int = 73\n    let totalInUSD : int = order\n";

    private static string GuestAssembly => Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");

    private static CompileWorkspace Project()
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        return workspace;
    }

    private static CompileOverrides Inputs(string version = "4", string layout = "document-tree", string? output = null) => new()
    {
        Language = "fsharp",
        Inputs = ["src/Rates.fs", "src/Pricing.fs"],
        PackageName = "acme/car-rental",
        IrVersion = version,
        IrLayout = layout,
        Output = output,
    };

    private static async Task<CompileResult> Run(CompileWorkspace workspace, CompileOverrides overrides) =>
        CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]), overrides, workspace.Context()));

    private static CompileSuccess Succeeded(CompileResult result)
    {
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        return ((CompileConclusion.Succeeded)result.Conclusion).Value;
    }

    /// <summary>The tree at <paramref name="directory"/> as the document-tree store reads it.</summary>
    private static StoredModel ReadTree(string directory)
    {
        VFileTree.TryLoadFromDisk(directory, out var files, out _).Should().BeTrue();
        var read = DocumentTreeStore.Read(ReadOptionsModule.defaults, files!);
        StoredModel.TryOfTreeRead(read, out var model, out _, out _, out var problems)
            .Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        return model!;
    }

    /// <summary>The plain single-file JSON of a model, for comparing a joined tree with a single-file compile.</summary>
    private static byte[] SingleFileBytes(StoredModel model)
    {
        model.TryConvert(ModelLayout.SingleFile, out var single, out var problems).Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        single!.TryWriteSingleFile(false, out var bytes, out problems).Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        return bytes!;
    }

    private static byte[] SingleFileBytes(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        StoredModel.TryReadSingleFile(document.RootElement, out var model, out _).Should().BeTrue();
        return SingleFileBytes(model!);
    }

    private static string[] Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    [Test]
    [Arguments("4", "4", "v4")]
    [Arguments("3", "3", "v3.1.0")]
    public async Task ADocumentTreeCompileWritesATreeThatJoinsToTheSingleFileModel(string version, string storedVersion, string recorded)
    {
        using var workspace = Project();
        var single = Succeeded(await Run(workspace, Inputs(version, "single-file")));
        var singleBytes = SingleFileBytes(single.Publication.IrPath);

        var success = Succeeded(await Run(workspace, Inputs(version)));

        success.Publication.IrPath.Should().Be(workspace.PathOf(TreeDirectory));
        Directory.Exists(success.Publication.IrPath).Should().BeTrue();
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse("the run clears .dest and writes only the tree");
        var tree = ReadTree(success.Publication.IrPath);
        tree.IrVersion.Should().Be(storedVersion);
        // A tree keeps no module order, so the join lists the modules by name: the models are equal as JSON objects.
        using (var joined = JsonDocument.Parse(SingleFileBytes(tree)))
        using (var expected = JsonDocument.Parse(singleBytes))
            JsonElement.DeepEquals(joined.RootElement, expected.RootElement).Should().BeTrue("the tree holds the model of the single file");

        using var record = JsonDocument.Parse(File.ReadAllText(workspace.PathOf(".morphir/out/compile.json")));
        var root = record.RootElement;
        root.GetProperty("value").EnumerateArray().Select(e => e.GetString()).Should().Equal("morphir-ir");
        var ir = root.GetProperty("ir");
        ir.GetProperty("path").GetString().Should().Be("morphir-ir");
        ir.GetProperty("layout").GetString().Should().Be("document-tree");
        ir.GetProperty("format").GetString().Should().Be("json");
        ir.GetProperty("version").GetString().Should().Be(recorded);
    }

    [Test]
    public async Task EveryFileOfAClassicTreeSaysThreePointOne()
    {
        using var workspace = Project();
        var success = Succeeded(await Run(workspace, Inputs("3")));
        var files = Files(success.Publication.IrPath);
        files.Should().Contain("manifest.json");
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(success.Publication.IrPath, file)));
            document.RootElement.GetProperty("formatVersion").GetString().Should().Be("3.1.0", file);
        }
    }

    [Test]
    public async Task OutputInstallsTheTreeAndAReinstallReplacesIt()
    {
        using var workspace = Project();
        var dist = workspace.PathOf("dist");
        var first = Succeeded(await Run(workspace, Inputs(output: "dist")));
        first.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir"));
        Files(Path.Combine(dist, "morphir-ir")).Should().Equal(Files(first.Publication.IrPath));
        ReadTree(Path.Combine(dist, "morphir-ir")).IrVersion.Should().Be("4");

        var again = Succeeded(await Run(workspace, Inputs(output: "dist")));
        again.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir"));

        // A single-file compile into the same target retires the files of the tree that the earlier install wrote.
        var single = Succeeded(await Run(workspace, Inputs(layout: "single-file", output: "dist")));
        single.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir.json"));
        File.Exists(Path.Combine(dist, "morphir-ir", "manifest.json")).Should().BeFalse();
        File.Exists(Path.Combine(dist, "morphir-ir.json")).Should().BeTrue();
    }

    private static async Task<(int Exit, CompileCommandTests.CapturedOutput Output)> Command(CompileWorkspace workspace, string arguments)
    {
        var output = new CompileCommandTests.CapturedOutput();
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<GenerationScenario>()
            .AddScenario<CompilationScenario>()
            .ConfigureServices(services =>
            {
                services.AddSingleton(FSharpFrontend.Registration);
                services.AddSingleton<ICompileOutput>(output);
                services.AddSingleton(new CompileContextSource(() => workspace.Context()));
            })
            .Build();
        return (await app.RunAsync(), output);
    }

    private const string CommandInputs = "--input src/Rates.fs src/Pricing.fs --language fsharp --package-name acme/car-rental";

    [Test]
    public async Task TheIrLayoutOptionSelectsTheDocumentTree()
    {
        using var workspace = Project();
        var run = await Command(workspace, $"--json=document {CommandInputs} --ir-layout document-tree");
        run.Exit.Should().Be(0, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("output").GetString().Should().Be(workspace.PathOf(TreeDirectory));
        ReadTree(workspace.PathOf(TreeDirectory)).IrVersion.Should().Be("4");
    }

    [Test]
    public async Task AnInvalidIrLayoutOptionIsAnInvocationOfTheSettings()
    {
        using var workspace = Project();
        var run = await Command(workspace, $"--json=document {CommandInputs} --ir-layout folder");
        run.Exit.Should().Be(2, run.Output.Text);
        run.Output.Text.Should().Contain("compile.invalid_setting").And.Contain("--ir-layout");
    }

    [Test]
    [NotInParallel(nameof(CompileTargetTests))]
    [Arguments("4")]
    [Arguments("3")]
    public async Task CompileWithATargetGeneratesTheSameFilesFromATree(string version)
    {
        using var workspace = Project();
        var tree = await Command(workspace, $"--json=document {CommandInputs} --ir-version {version} --ir-layout document-tree --target fsharp --generate-output gen");
        tree.Exit.Should().Be(0, tree.Output.Text);
        using (var report = JsonDocument.Parse(tree.Output.Text))
            report.RootElement.GetProperty("output").GetString().Should().Be(workspace.PathOf(TreeDirectory));

        var single = await Command(workspace, $"--json=document {CommandInputs} --ir-version {version} --target fsharp --generate-output gen2");
        single.Exit.Should().Be(0, single.Output.Text);

        string Content(string root, string file) => File.ReadAllText(Path.Combine(root, file));
        var generated = Files(workspace.PathOf("gen"));
        generated.Should().NotBeEmpty().And.Equal(Files(workspace.PathOf("gen2")));
        foreach (var file in generated)
            Content(workspace.PathOf("gen"), file).Should().Be(Content(workspace.PathOf("gen2"), file), file);
    }

    [Test]
    public async Task BoundedGenerationReadsTheRecordOfARealDocumentTreeCompile()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/orders\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[ir]\nlayout = \"document-tree\"\n");
        workspace.Write("src/Domain.fs", OrdersDomain);
        workspace.Write("bounded-orders.mode", "orders");

        var compiled = await Command(workspace, "--json=document");
        compiled.Exit.Should().Be(0, compiled.Output.Text);

        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var invocation = new GenerateOverrides(Target: "text", Output: workspace.PathOf("generated")) { Extension = GuestAssembly };
        var outcome = await app.Services.GetRequiredService<IMediator>().InvokeAsync<Outcome<GenerationSuccess>>(
            new GenerateRequest(invocation, new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment }));
        var report = GenerationReport.From(outcome, invocation);

        report.ExitCode.Should().Be(0, report.ToJson());
        report.Representation.Should().Be("DocumentTree");
        File.ReadAllBytes(workspace.PathOf("generated/order.txt")).Should().Equal("73\n"u8.ToArray());
    }
}
