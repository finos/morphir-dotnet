// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Files;
using Morphir.Frontend.FSharp;
using Morphir.IR.Trees;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
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

    /// <summary>The plain single-file JSON of a tree model joined back into one file.</summary>
    private static byte[] SingleFileBytes(StoredModel model)
    {
        model.TryConvert(ModelLayout.SingleFile, out var single, out var problems).Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        single!.TryWriteSingleFile(false, out var bytes, out problems).Should().BeTrue(problems is null ? "" : string.Join("\n", problems));
        return bytes!;
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
        // The oracle is the file that the single-file compile published, read as JSON and not through the tree codecs.
        var singleBytes = File.ReadAllBytes(single.Publication.IrPath);

        var success = Succeeded(await Run(workspace, Inputs(version)));

        success.Publication.IrPath.Should().Be(workspace.PathOf(TreeDirectory));
        Directory.Exists(success.Publication.IrPath).Should().BeTrue();
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse("the run clears .dest and writes only the tree");
        var tree = ReadTree(success.Publication.IrPath);
        tree.IrVersion.Should().Be(storedVersion);
        // A tree keeps no module order, so the join lists the modules by name. JSON object members have no order, so
        // DeepEquals compares the members of each object by name and every array in order.
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

    private static string Dest(CompileWorkspace workspace) => workspace.PathOf(".morphir/out/compile.dest");

    private static string OutRoot(CompileWorkspace workspace) => workspace.PathOf(".morphir/out");

    private static TaskStoreOptions Quiet => new() { Diagnostics = TextWriter.Null };

    private static async Task<CompileResult> RunWith(CompileWorkspace workspace, CompileOverrides overrides, CompilePublicationHooks hooks,
        TaskStoreOptions? options = null, CancellationToken ct = default)
    {
        var publisher = new TaskRecordCompilePublisher(options ?? Quiet, hooks);
        return CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration], publisher: publisher),
            overrides, workspace.Context(), ct));
    }

    private static bool IsTombstone(CompileWorkspace workspace)
    {
        using var record = JsonDocument.Parse(File.ReadAllText(workspace.PathOf(".morphir/out/compile.json")));
        return record.RootElement.TryGetProperty("tombstone", out var tombstone) && tombstone.GetBoolean();
    }

    /// <summary>Neither the IR, a staged IR nor a staged record stays: <c>compile.dest</c> is empty, and only the record is beside it.</summary>
    private static void NothingIsLeft(CompileWorkspace workspace)
    {
        Directory.EnumerateFileSystemEntries(Dest(workspace)).Should().BeEmpty("neither the staged nor a partial IR stays");
        Directory.EnumerateFiles(OutRoot(workspace), ".compile.json.*").Should().BeEmpty("no staged record stays");
    }

    /// <summary>A stage that writes part of the IR and then fails, as a full disk does.</summary>
    private static CompilePublicationHooks FailingStage(string layout) => layout == "document-tree"
        ? new CompilePublicationHooks
        {
            StageTree = (staging, _) =>
            {
                Directory.CreateDirectory(Path.Combine(staging, "pkg", "acme"));
                File.WriteAllText(Path.Combine(staging, "manifest.json"), "{\"formatVersion\": 4");
                throw new IOException("The disk is full.");
            },
        }
        : new CompilePublicationHooks
        {
            StageFile = async (staging, bytes, ct) =>
            {
                await File.WriteAllBytesAsync(staging, bytes[..16], ct);
                throw new IOException("The disk is full.");
            },
        };

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task AFailureDuringStagingRemovesThePartlyStagedIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));

        var failed = await RunWith(workspace, Inputs(layout: layout), FailingStage(layout));

        failed.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(failed).Should().Equal("compile.publish_failed");
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("the record of the failed run stays a tombstone");
    }

    [Test]
    public async Task AFirstCompileThatFailsDuringStagingLeavesNoRecord()
    {
        using var workspace = Project();

        var failed = await RunWith(workspace, Inputs(), FailingStage("document-tree"));

        failed.ExitCode.Should().Be(2);
        NothingIsLeft(workspace);
        File.Exists(workspace.PathOf(".morphir/out/compile.json")).Should().BeFalse("no earlier record, so no tombstone");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ACancellationBeforeTheMoveLeavesNoPartialIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));
        using var cancellation = new CancellationTokenSource();

        var cancelled = await RunWith(workspace, Inputs(layout: layout), new CompilePublicationHooks { AfterStaged = _ => cancellation.Cancel() },
            ct: cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("a cancelled run completes no record");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ACancellationAfterTheMoveRemovesTheMovedIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));
        using var cancellation = new CancellationTokenSource();
        string? moved = null;

        var cancelled = await RunWith(workspace, Inputs(layout: layout), new CompilePublicationHooks
        {
            AfterMoved = target =>
            {
                moved = target;
                cancellation.Cancel();
            },
        }, ct: cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        moved.Should().StartWith(Dest(workspace));
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("a cancelled run completes no record");
    }

    [Test]
    public async Task ACancellationJustBeforeTheRecordReplacementKeepsTheTombstone()
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs()));
        using var cancellation = new CancellationTokenSource();

        var cancelled = await RunWith(workspace, Inputs(), new CompilePublicationHooks(),
            Quiet with { BeforeRecordReplace = cancellation.Cancel }, cancellation.Token);

        cancelled.ExitCode.Should().Be(130);
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("the record is not replaced after the cancellation");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ARecordReplacementFailureRemovesTheMovedIrAndKeepsTheTombstone(string layout)
    {
        using var workspace = Project();
        Succeeded(await Run(workspace, Inputs(layout: layout)));

        var failed = await RunWith(workspace, Inputs(layout: layout), new CompilePublicationHooks(),
            Quiet with { BeforeRecordReplace = () => throw new IOException("The disk is full.") });

        failed.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(failed).Should().Equal("compile.record_write_failed");
        NothingIsLeft(workspace);
        IsTombstone(workspace).Should().BeTrue("the tombstone of the run stays when its record cannot replace it");
    }

    /// <summary>
    /// The lock file that the tree publication of Morphir.Files takes for an output root: the SHA-256 of the full root
    /// (upper case on Windows) in the per-user temp directory <c>morphir-publish-locks</c>.
    /// </summary>
    private static string PublishLockOf(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var identity = OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Path.GetTempPath(), "morphir-publish-locks", name + ".lock");
    }

    [Test]
    public async Task RepeatedTreeCompilesStageInOneRootAndShareOnePublishLock()
    {
        using var workspace = Project();
        var staged = new List<string>();
        var hooks = new CompilePublicationHooks { AfterStaged = staged.Add };

        for (var run = 0; run < 3; run++) Succeeded(await RunWith(workspace, Inputs(), hooks));

        staged.Should().HaveCount(3).And.OnlyContain(path => path == staged[0], "a run holds compile.lock, so one staging root serves every run");
        Path.GetDirectoryName(staged[0]).Should().Be(Dest(workspace));
        File.Exists(PublishLockOf(staged[0])).Should().BeTrue("the publication of the tree takes the lock of its one staging root");
    }

    [Test]
    [Arguments("document-tree")]
    [Arguments("single-file")]
    public async Task ASecondPublicationThatCannotMoveLeavesTheFirstIrInPlace(string layout)
    {
        using var workspace = Project();
        var written = File.ReadAllBytes(Succeeded(await Run(workspace, Inputs(layout: "single-file"))).Publication.IrPath);
        var version = Morphir.IR.Versioning.parse("4").ResultValue;
        var request = layout == "document-tree"
            ? CompilePublishRequest.ForDocumentTree("fsharp", version, null, written, Passed(CompileDocumentTree.Write(written)))
            : new CompilePublishRequest("fsharp", version, null, written);
        var settings = Passed(new ProjectCompileSettingsResolver().Resolve(Inputs(layout: layout), workspace.Context()));

        using var publication = Passed(await new TaskRecordCompilePublisher(Quiet).BeginAsync(settings, default));
        var first = Passed(await publication.PublishAsync(request, default));
        var second = await publication.PublishAsync(request, default);

        second.IsPassed.Should().BeFalse("the first IR is already at the target");
        second.Diagnostics.Select(d => d.CodeOrNull?.Value).Should().Equal("compile.publish_failed");
        (File.Exists(first.IrPath) || Directory.Exists(first.IrPath)).Should().BeTrue("the second attempt did not move anything, so it removes nothing");
        if (layout == "document-tree") ReadTree(first.IrPath).IrVersion.Should().Be("4");
        else File.ReadAllBytes(first.IrPath).Should().Equal(written);
        IsTombstone(workspace).Should().BeFalse("the record of the first publication stays");
        Directory.EnumerateFileSystemEntries(Dest(workspace)).Should().ContainSingle("no staged IR stays");
    }

    private static T Passed<T>(Checked<T> result)
    {
        result.TryGetValue(out var value).Should().BeTrue(string.Join("\n", result.Diagnostics));
        return value!;
    }

    [Test]
    public async Task AClassicTreeIsRequestedAsThreeAndNotAsThreePointOne()
    {
        using var workspace = Project();
        var refused = await Run(workspace, Inputs("3.1.0"));
        refused.ExitCode.Should().Be(2);
        CompileRunners.ErrorCodes(refused).Should().Equal("compile.unsupported_ir_version");
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
    public async Task ABuiltInBackendRefusesARecordedJsonTree()
    {
        using var workspace = Project();
        var compiled = await Command(workspace, $"--json=document {CommandInputs} --ir-layout document-tree");
        compiled.Exit.Should().Be(0, compiled.Output.Text);

        using var app = MorphirCli.CreateBuilder([]).AddScenario<GenerationScenario>().Build();
        var invocation = new GenerateOverrides(Target: "fsharp", Output: workspace.PathOf("generated"));
        var outcome = await app.Services.GetRequiredService<IMediator>().InvokeAsync<Outcome<GenerationSuccess>>(
            new GenerateRequest(invocation, new InvocationContext(workspace.Root) { Environment = workspace.Context().Environment }));
        var report = GenerationReport.From(outcome, invocation);

        report.ExitCode.Should().Be(1, report.ToJson());
        report.Diagnostics.Select(d => d.Code).Should().Equal(["unsupported-format"], report.ToJson());
        Directory.Exists(workspace.PathOf("generated")).Should().BeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BoundedGenerationReadsTheRecordOfARealDocumentTreeCompile(bool overriddenLayout)
    {
        using var workspace = new CompileWorkspace();
        // An invalid ir.layout that --ir-layout overrides is a warning of the compile, and generate takes the layout from the record.
        workspace.Write("morphir.toml", overriddenLayout
            ? Morphir.TestSupport.Drivers.BoundedOrdersRecordMother.ProjectConfiguration + "\n[ir]\nlayout = \"folder\"\n"
            : Morphir.TestSupport.Drivers.BoundedOrdersRecordMother.DocumentTreeConfiguration);
        workspace.Write("src/Domain.fs", Morphir.TestSupport.Drivers.BoundedOrdersRecordMother.DomainSource);
        workspace.Write("bounded-orders.mode", "compiled-orders");

        var compiled = await Command(workspace, overriddenLayout ? "--json=document --ir-layout document-tree" : "--json=document");
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
