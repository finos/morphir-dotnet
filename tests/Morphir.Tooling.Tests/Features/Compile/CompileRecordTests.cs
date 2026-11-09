// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.Frontend.FSharp;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Tasks 5.1 to 5.3 connected to <c>morphir compile</c>: the compile runner publishes through the task record
/// store (record, tombstone, lock) and installs <c>-o</c> through the <c>installed</c> ledger.
/// </summary>
public sealed class CompileRecordTests
{
    private static CompileWorkspace Project(string pricing = CompileRunnerTests.Pricing)
    {
        var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", pricing);
        return workspace;
    }

    private static CompileOverrides Inputs(string? output = null) => new()
    {
        Language = "fsharp",
        Inputs = ["src/Rates.fs", "src/Pricing.fs"],
        PackageName = "acme/car-rental",
        IrVersion = "4",
        Output = output,
    };

    private static async Task<CompileResult> Run(CompileWorkspace workspace, CompileOverrides overrides,
        IEnumerable<Morphir.Compilation.IProjectFileProvider>? providers = null, CompileContext? context = null) =>
        CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration], providers: providers),
            overrides, context ?? workspace.Context()));

    private static JsonDocument Record(CompileWorkspace workspace, string outRoot = ".morphir/out") =>
        JsonDocument.Parse(File.ReadAllText(workspace.PathOf(outRoot + "/compile.json")));

    [Test]
    public async Task ASuccessfulCompileWritesASchemaOneRecordBesideTheIr()
    {
        using var workspace = Project();
        var result = await Run(workspace, Inputs());
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        var success = ((CompileConclusion.Succeeded)result.Conclusion).Value;
        success.Publication.IrPath.Should().Be(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json"));
        success.Publication.Installed.Should().BeEmpty();

        using var record = Record(workspace);
        var root = record.RootElement;
        root.GetProperty("schema").GetInt32().Should().Be(1);
        root.GetProperty("task").GetString().Should().Be("compile");
        root.GetProperty("module").GetString().Should().Be("");
        root.GetProperty("language").GetString().Should().Be("fsharp");
        root.GetProperty("inputs").GetArrayLength().Should().Be(0, "explicit input files are not a project file");
        root.GetProperty("value").EnumerateArray().Select(e => e.GetString()).Should().Equal("morphir-ir.json");
        var ir = root.GetProperty("ir");
        ir.GetProperty("path").GetString().Should().Be("morphir-ir.json");
        ir.GetProperty("layout").GetString().Should().Be("single-file");
        ir.GetProperty("format").GetString().Should().Be("json");
        ir.GetProperty("version").GetString().Should().Be("v4");
        root.GetProperty("installed").EnumerateObject().Should().BeEmpty();
        root.TryGetProperty("tombstone", out _).Should().BeFalse();
    }

    [Test]
    public async Task TheRecordVersionNamesTheProducedIrVersion()
    {
        using var workspace = Project();
        var result = await Run(workspace, Inputs() with { IrVersion = "3" });
        result.ExitCode.Should().Be(0);
        using var record = Record(workspace);
        record.RootElement.GetProperty("ir").GetProperty("version").GetString().Should().Be("v3");
    }

    [Test]
    public async Task AProjectFileCompileListsTheProjectFileInInputs()
    {
        using var workspace = Project();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        workspace.Write("src/Acme.CarRental.fsproj", "<Project />");
        var result = await Run(workspace, new CompileOverrides(), [new FakeProjectFileProvider("Rates.fs", "Pricing.fs")]);
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        using var record = Record(workspace);
        record.RootElement.GetProperty("inputs").EnumerateArray().Select(e => e.GetString()).Should().Equal("src/Acme.CarRental.fsproj");
    }

    [Test]
    public async Task AFailureAfterSuccessLeavesATombstoneThatKeepsTheLedgerAndNoIr()
    {
        using var workspace = Project();
        (await Run(workspace, Inputs("dist"))).ExitCode.Should().Be(0);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.MutablePricing);

        var failed = await Run(workspace, Inputs("dist"));
        failed.ExitCode.Should().Be(1);
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();
        using var record = Record(workspace);
        var root = record.RootElement;
        root.GetProperty("tombstone").GetBoolean().Should().BeTrue();
        root.TryGetProperty("ir", out _).Should().BeFalse();
        root.GetProperty("installed").EnumerateObject().Select(p => p.Name).Should().Equal(workspace.PathOf("dist"));
        File.Exists(workspace.PathOf("dist/morphir-ir.json")).Should().BeTrue("a failed compile installs nothing and removes nothing");
    }

    [Test]
    public async Task TheOutputOptionInstallsTheIrAndKeepsForeignFiles()
    {
        using var workspace = Project();
        workspace.Write("dist/README.md", "mine");
        var first = await Run(workspace, Inputs("dist"));
        first.ExitCode.Should().Be(0, string.Join("\n", first.Diagnostics.Select(d => d.Diagnostic)));
        var success = ((CompileConclusion.Succeeded)first.Conclusion).Value;
        success.Publication.Installed.Should().Equal(workspace.PathOf("dist/morphir-ir.json"));
        File.ReadAllBytes(workspace.PathOf("dist/morphir-ir.json")).Should().Equal(File.ReadAllBytes(success.Publication.IrPath));

        // Reinstall over own file: the IR is replaced and the other file stays.
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates.Replace("80", "90"));
        var second = await Run(workspace, Inputs("dist"));
        second.ExitCode.Should().Be(0, string.Join("\n", second.Diagnostics.Select(d => d.Diagnostic)));
        File.ReadAllText(workspace.PathOf("dist/morphir-ir.json")).Should().Contain("90");
        File.ReadAllText(workspace.PathOf("dist/README.md")).Should().Be("mine");
        using var record = Record(workspace);
        record.RootElement.GetProperty("installed").GetProperty(workspace.PathOf("dist")).EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("morphir-ir.json");
    }

    [Test]
    public async Task AnInstallConflictWithForeignContentFailsAndLeavesTheTargetUnchanged()
    {
        using var workspace = Project();
        workspace.Write("dist/morphir-ir.json", "foreign");
        var result = await Run(workspace, Inputs("dist"));
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Contain("install.foreign_content");
        File.ReadAllText(workspace.PathOf("dist/morphir-ir.json")).Should().Be("foreign");
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeTrue("the compile output stays published");
    }

    [Test]
    public async Task TheOutDirectoryVariableMovesTheOutRoot()
    {
        using var workspace = Project();
        var context = workspace.Context();
        var environment = new Dictionary<string, string>(context.Environment) { ["MORPHIR_OUT_DIR"] = "elsewhere" };
        var result = await Run(workspace, Inputs(), context: context with { Environment = environment });
        result.ExitCode.Should().Be(0);
        File.Exists(workspace.PathOf("elsewhere/compile.dest/morphir-ir.json")).Should().BeTrue();
        File.Exists(workspace.PathOf("elsewhere/compile.json")).Should().BeTrue();
    }

    [Test]
    public async Task TheOutDirOptionComesBeforeTheVariable()
    {
        using var workspace = Project();
        var context = workspace.Context();
        var environment = new Dictionary<string, string>(context.Environment) { ["MORPHIR_OUT_DIR"] = "elsewhere" };
        var result = await Run(workspace, Inputs() with { OutDir = "flagged" }, context: context with { Environment = environment });
        result.ExitCode.Should().Be(0);
        File.Exists(workspace.PathOf("flagged/compile.json")).Should().BeTrue();
        Directory.Exists(workspace.PathOf("elsewhere")).Should().BeFalse();
    }

    [Test]
    public async Task AWorkspaceMemberPublishesBelowItsMemberPath()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[workspace]\nmembers = [\"packages/orders\"]\n");
        workspace.Write("packages/orders/morphir.toml", "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n");
        workspace.Write("packages/orders/src/Rates.fs", CompileRunnerTests.Rates);
        var result = await Run(workspace, new CompileOverrides(), context: workspace.Context("packages/orders"));
        result.ExitCode.Should().Be(0, string.Join("\n", result.Diagnostics.Select(d => d.Diagnostic)));
        File.Exists(workspace.PathOf(".morphir/out/packages/orders/compile.dest/morphir-ir.json")).Should().BeTrue();
        using var record = Record(workspace, ".morphir/out/packages/orders");
        record.RootElement.GetProperty("module").GetString().Should().Be("packages/orders");
    }

    [Test]
    public async Task ALinkedSourceIsRefusedByTheSharedCapture()
    {
        using var workspace = Project();
        File.CreateSymbolicLink(workspace.PathOf("src/Linked.fs"), workspace.PathOf("src/Rates.fs"));
        var result = await Run(workspace, Inputs() with { Inputs = ["src/Linked.fs", "src/Pricing.fs"] });
        result.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(result).Should().Equal("sources.unsupported_entry");
    }
}
