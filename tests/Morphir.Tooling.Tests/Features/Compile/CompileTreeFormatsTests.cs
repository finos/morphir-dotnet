// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Files;
using Morphir.Frontend.FSharp;
using Morphir.Ion;
using Morphir.IR.Trees;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Composes <c>ir.layout = "document-tree"</c> with <c>ir.format = "yaml" | "ion"</c> across compile publication,
/// task-record metadata, <c>-o</c> installation, read-back equivalence, and version refusals.
/// </summary>
[Category("PlatformSpecific")]
public sealed class CompileTreeFormatsTests
{
    private const string TreeDirectory = ".morphir/out/compile.dest/morphir-ir";
    private const string CommandInputs = "--input src/Rates.fs src/Pricing.fs --language fsharp --package-name acme/car-rental";

    private static CompileWorkspace Project()
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        return workspace;
    }

    private static CompileOverrides TreeOverrides(string version, string format, string? output = null) => new()
    {
        Language = "fsharp",
        Inputs = ["src/Rates.fs", "src/Pricing.fs"],
        PackageName = "acme/car-rental",
        IrVersion = version,
        IrLayout = "document-tree",
        IrFormat = format,
        Output = output,
    };

    private static async Task<CompileResult> Run(CompileWorkspace workspace, CompileOverrides overrides) =>
        CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]), overrides, workspace.Context()));

    private static CompileSuccess Succeeded(CompileResult result)
    {
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        return ((CompileConclusion.Succeeded)result.Conclusion).Value;
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

    private static string[] Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private static LoadedTreeModel ReadDocumentTree(string directory, TreeProfileSelection profile)
    {
        VFileTree.TryLoadFromDisk(directory, out var vfiles, out var loadError).Should().BeTrue(loadError?.ToString() ?? "");
        var options = TreeReadOptions.Create(1_000_000, 256L * 1024L * 1024L, 4096, 1_000_000, profile)
            .WithMaxTotalBytes(256L * 1024L * 1024L);
        DocumentTreeStore.TryReadModel(options, vfiles!, CancellationToken.None, out var loaded, out var errors)
            .Should().BeTrue(errors is null ? "" : string.Join("\n", errors.Select(e => $"{e.Code}: {e.Message}")));
        return loaded!;
    }

    [Test]
    [Arguments("4", "4", "v4")]
    [Arguments("3", "3.1.0", "v3.1.0")]
    public async Task YamlDocumentTreeCompileWritesAYamlTreeThatReadsBackAsTheJsonTreeModel(string version, string storedVersion, string recorded)
    {
        using var jsonWorkspace = Project();
        var jsonSuccess = Succeeded(await Run(jsonWorkspace, TreeOverrides(version, "json")));
        var jsonFiles = Files(jsonSuccess.Publication.IrPath);
        var jsonLoaded = ReadDocumentTree(jsonSuccess.Publication.IrPath, TreeProfileSelection.JsonOnly);

        using var yamlWorkspace = Project();
        var yamlRun = await Command(yamlWorkspace, $"--json=document {CommandInputs} --ir-version {version} --ir-layout document-tree --ir-format yaml");
        yamlRun.Exit.Should().Be(0, yamlRun.Output.Text);

        var treeRoot = yamlWorkspace.PathOf(TreeDirectory);
        Directory.Exists(treeRoot).Should().BeTrue();
        File.Exists(yamlWorkspace.PathOf(".morphir/out/compile.dest/morphir-ir.yaml")).Should().BeFalse();
        File.Exists(yamlWorkspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();

        var yamlFiles = Files(treeRoot);
        yamlFiles.Should().Contain("manifest.yaml");
        yamlFiles.Should().OnlyContain(path => path.EndsWith(".yaml", StringComparison.Ordinal));
        yamlFiles.Should().Equal(jsonFiles.Select(CanonicalYaml.TreeLocation));

        var yamlLoaded = ReadDocumentTree(treeRoot, TreeProfileSelection.YamlOnly);
        yamlLoaded.Profile.Should().Be(TreeProfile.Yaml);
        yamlLoaded.Model.SourceVersion.Text.Should().Be(storedVersion);
        if (version == "4")
            yamlLoaded.Model.Distribution.Equals(jsonLoaded.Model.Distribution).Should().BeTrue("the v4 YAML tree holds the exact model of the JSON tree");
        foreach (var jsonRel in jsonFiles)
        {
            CanonicalYamlWriter.TryWriteJson(File.ReadAllBytes(Path.Combine(jsonSuccess.Publication.IrPath, jsonRel)), out var expectedYaml, out var yamlError)
                .Should().BeTrue(yamlError);
            File.ReadAllBytes(Path.Combine(treeRoot, CanonicalYaml.TreeLocation(jsonRel))).Should().Equal(expectedYaml!, jsonRel);
        }

        using var report = JsonDocument.Parse(yamlRun.Output.Text);
        report.RootElement.GetProperty("output").GetString().Should().Be(treeRoot);
        report.RootElement.GetProperty("origins").GetProperty("irLayout").GetProperty("flag").GetString().Should().Be("--ir-layout");
        report.RootElement.GetProperty("origins").GetProperty("irFormat").GetProperty("flag").GetString().Should().Be("--ir-format");

        using var record = JsonDocument.Parse(File.ReadAllText(yamlWorkspace.PathOf(".morphir/out/compile.json")));
        var root = record.RootElement;
        root.GetProperty("value").EnumerateArray().Select(e => e.GetString()).Should().Equal("morphir-ir");
        var ir = root.GetProperty("ir");
        ir.GetProperty("path").GetString().Should().Be("morphir-ir");
        ir.GetProperty("layout").GetString().Should().Be("document-tree");
        ir.GetProperty("format").GetString().Should().Be("yaml");
        ir.GetProperty("version").GetString().Should().Be(recorded);
    }

    [Test]
    [Arguments("4", "4.0.0", "v4")]
    [Arguments("3", "3.0.0", "v3")]
    [Arguments("3.1.0", "3.1.0", "v3.1.0")]
    public async Task IonDocumentTreeCompileWritesAnIonTreeEquivalentToSingleFileIon(string version, string expectedHeaderVersion, string recorded)
    {
        using var singleWorkspace = Project();
        var single = Succeeded(await Run(singleWorkspace, new CompileOverrides
        {
            Language = "fsharp",
            Inputs = ["src/Rates.fs", "src/Pricing.fs"],
            PackageName = "acme/car-rental",
            IrVersion = version,
            IrLayout = "single-file",
            IrFormat = "ion",
        }));
        IonIrReading.TryRead(File.ReadAllBytes(single.Publication.IrPath), out var singleRead, out var singleFailure)
            .Should().BeTrue(singleFailure?.Message ?? "");

        using var treeWorkspace = Project();
        var treeRun = await Command(treeWorkspace, $"--json=document {CommandInputs} --ir-version {version} --ir-layout document-tree --ir-format ion");
        treeRun.Exit.Should().Be(0, treeRun.Output.Text);

        var treeRoot = treeWorkspace.PathOf(TreeDirectory);
        Directory.Exists(treeRoot).Should().BeTrue();
        File.Exists(treeWorkspace.PathOf(".morphir/out/compile.dest/morphir-ir.ion")).Should().BeFalse();

        var ionFiles = Files(treeRoot);
        ionFiles.Should().Contain("manifest.ion");
        ionFiles.Should().OnlyContain(path => path.EndsWith(".ion", StringComparison.Ordinal));

        VFileTree.TryLoadFromDisk(treeRoot, out var vfiles, out var loadError).Should().BeTrue(loadError?.ToString() ?? "");
        var treeRead = IonIrTree.readDetailed(new IonReadLimits(256 * 1024 * 1024, 4096, 1_000_000), vfiles!);
        treeRead.IsOk.Should().BeTrue(treeRead.IsError ? treeRead.ErrorValue.ToString() : "");

        var treeTree = IonIrWriter.writeTreeFiles(4000, treeRead.ResultValue);
        treeTree.IsOk.Should().BeTrue();
        var singleTree = IonIrWriter.writeTreeFiles(4000, singleRead!);
        singleTree.IsOk.Should().BeTrue();
        var actualTree = treeTree.ResultValue.Select(pair => (pair.Item1, pair.Item2));
        var expectedTree = singleTree.ResultValue.Select(pair => (pair.Item1, pair.Item2));
        actualTree.Should().HaveSameCount(expectedTree);
        foreach (var ((actualPath, actualBytes), (expectedPath, expectedBytes)) in actualTree.Zip(expectedTree))
        {
            actualPath.Should().Be(expectedPath);
            actualBytes.Should().Equal(expectedBytes, actualPath);
        }

        File.ReadAllText(Path.Combine(treeRoot, "manifest.ion")).Should().Contain($"formatVersion: \"{expectedHeaderVersion}\"");

        using var record = JsonDocument.Parse(File.ReadAllText(treeWorkspace.PathOf(".morphir/out/compile.json")));
        var root = record.RootElement;
        root.GetProperty("value").EnumerateArray().Select(e => e.GetString()).Should().Equal("morphir-ir");
        var ir = root.GetProperty("ir");
        ir.GetProperty("path").GetString().Should().Be("morphir-ir");
        ir.GetProperty("layout").GetString().Should().Be("document-tree");
        ir.GetProperty("format").GetString().Should().Be("ion");
        ir.GetProperty("version").GetString().Should().Be(recorded);
    }

    [Test]
    public async Task OutputInstallsYamlAndIonTreesAndReinstallRetiresPriorFiles()
    {
        using var workspace = Project();
        var dist = workspace.PathOf("dist");

        var yamlTree = Succeeded(await Run(workspace, TreeOverrides("4", "yaml", output: "dist")));
        yamlTree.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir"));
        Files(Path.Combine(dist, "morphir-ir")).Should().Equal(Files(yamlTree.Publication.IrPath));
        File.Exists(Path.Combine(dist, "morphir-ir", "manifest.yaml")).Should().BeTrue();

        var ionTree = Succeeded(await Run(workspace, TreeOverrides("4", "ion", output: "dist")));
        ionTree.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir"));
        Files(Path.Combine(dist, "morphir-ir")).Should().Equal(Files(ionTree.Publication.IrPath));
        File.Exists(Path.Combine(dist, "morphir-ir", "manifest.ion")).Should().BeTrue();
        File.Exists(Path.Combine(dist, "morphir-ir", "manifest.yaml")).Should().BeFalse();

        var yamlSingle = Succeeded(await Run(workspace, TreeOverrides("4", "yaml", output: "dist") with { IrLayout = "single-file" }));
        yamlSingle.Publication.Installed.Should().Equal(Path.Combine(dist, "morphir-ir.yaml"));
        File.Exists(Path.Combine(dist, "morphir-ir.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(dist, "morphir-ir", "manifest.ion")).Should().BeFalse();
    }

    [Test]
    [Arguments("1", "ion", "compile.unsupported_setting")]
    [Arguments("2", "ion", "compile.unsupported_setting")]
    [Arguments("3.1.0", "yaml", "compile.unsupported_ir_version")]
    [Arguments("3.1.0", "json", "compile.unsupported_ir_version")]
    public async Task DocumentTreeWithFormatsRefusesUnsupportedVersions(string version, string format, string expectedCode)
    {
        using var workspace = Project();
        var refused = await Run(workspace, TreeOverrides(version, format));
        refused.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(refused).Should().Contain(expectedCode);
    }
}
