// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>Task 4.7: the compile command, its JSON report, JSON Lines (STRM-2), streaming and exit codes.</summary>
[NotInParallel(nameof(CompileCommandTests))]
public sealed class CompileCommandTests
{
    private const string IncompletePricing =
        "namespace Acme.CarRental\n\nmodule Pricing =\n    let total (seats: int) (days: int) : int =\n        match seats with\n        | 1 -> Rates.dailyRate seats * days\n";

    private static readonly Diagnostic PackageWarning = Diagnostic.Create(DiagnosticSeverity.Warning,
        DiagnosticCode.Create("msbuild.ignored_package_reference"), "PackageReference Newtonsoft.Json is ignored.",
        Location.Create("Acme.CarRental.fsproj", Morphir.Compilation.Range.Create(Position.Create(0, 0), Position.Create(0, 0))));

    /// <summary>A project whose configured project file the fake provider expands to Rates.fs then Pricing.fs.</summary>
    private static (CompileWorkspace Workspace, FakeProjectFileProvider Provider) Project(string pricing = CompileRunnerTests.Pricing)
    {
        var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        workspace.Write("src/Acme.CarRental.fsproj", "<Project />");
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", pricing);
        var provider = new FakeProjectFileProvider("Rates.fs", "Pricing.fs");
        provider.Warnings.Add(PackageWarning);
        return (workspace, provider);
    }

    private static async Task<Run> Compile(CompileWorkspace workspace, IEnumerable<IProjectFileProvider> providers, IFrontend frontend,
        string arguments, CancellationTokenSource? cancellation = null)
    {
        var output = new CapturedOutput();
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<CompilationScenario>()
            .ConfigureServices(services =>
            {
                // The fake providers replace the MSBuild provider that the scenario registers.
                services.RemoveAll<IProjectFileProvider>();
                services.AddSingleton<IFrontend>(frontend);
                foreach (var provider in providers) services.AddSingleton(provider);
                services.AddSingleton<ICompileOutput>(output);
                services.AddSingleton(new CompileContextSource(() => workspace.Context()));
            })
            .Build();
        var exit = await app.RunAsync(cancellation?.Token ?? CancellationToken.None);
        return new Run(exit, output);
    }

    private static Task<Run> Compile((CompileWorkspace Workspace, FakeProjectFileProvider Provider) project, string arguments,
        IFrontend? frontend = null, CancellationTokenSource? cancellation = null) =>
        Compile(project.Workspace, [project.Provider], frontend ?? FSharpFrontend.Registration, arguments, cancellation);

    [Test]
    public async Task TheOutputOptionInstallsTheIrAndTheReportListsTheInstalledFile()
    {
        var project = Project();
        using var _ = project.Workspace;
        var target = project.Workspace.PathOf("dist");
        var run = await Compile(project, $"--json -o {target}");
        run.Exit.Should().Be(0, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("output").GetString().Should().Be(project.Workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json"));
        report.RootElement.GetProperty("installed").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(Path.Combine(target, "morphir-ir.json"));
        report.RootElement.GetProperty("origins").GetProperty("output").GetProperty("flag").GetString().Should().Be("--output");
        File.Exists(Path.Combine(target, "morphir-ir.json")).Should().BeTrue();
    }

    [Test]
    public async Task AnInstallConflictFailsWithExitCodeTwoAndNamesTheForeignFile()
    {
        var project = Project();
        using var _ = project.Workspace;
        project.Workspace.Write("dist/morphir-ir.json", "foreign");
        var run = await Compile(project, $"--json --output {project.Workspace.PathOf("dist")}");
        run.Exit.Should().Be(2, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("installed").GetArrayLength().Should().Be(0);
        report.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString())
            .Should().Contain("install.foreign_content");
        File.ReadAllText(project.Workspace.PathOf("dist/morphir-ir.json")).Should().Be("foreign");
    }

    [Test]
    public async Task JsonModeWritesExactlyOneReportOnStdout()
    {
        var project = Project();
        using var _ = project.Workspace;
        var run = await Compile(project, "--json");
        run.Exit.Should().Be(0, run.Output.Error);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("frontend").GetString().Should().Be("morphir-dotnet.fsharp (in-process)");
        report.RootElement.GetProperty("sources").GetProperty("documents").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Rates.fs", "Pricing.fs");
        report.RootElement.GetProperty("modules").GetArrayLength().Should().Be(2);
        File.Exists(report.RootElement.GetProperty("output").GetString()).Should().BeTrue();
        run.Output.Error.Should().NotContain("\"success\"", "stderr holds no copy of the report");
    }

    [Test]
    public async Task TheReportGivesTheCommandLineAsTheOriginOfAnOverride()
    {
        var project = Project();
        using var _ = project.Workspace;
        project.Workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[ir]\nformat_version = 3\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        var run = await Compile(project, "--json --ir-version 4");
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("irVersion").GetString().Should().Be("4");
        var origin = report.RootElement.GetProperty("origins").GetProperty("irVersion");
        origin.GetProperty("kind").GetString().Should().Be("cli");
        origin.GetProperty("flag").GetString().Should().Be("--ir-version");
    }

    [Test]
    public async Task WarningsFromEveryStageAreInASuccessfulReportInStageAndPositionOrder()
    {
        var project = Project(IncompletePricing);
        using var _ = project.Workspace;
        var run = await Compile(project, "--json");
        run.Exit.Should().Be(0, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("exitCode").GetInt32().Should().Be(0);
        var diagnostics = report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d => (d.GetProperty("severity").GetString(), d.GetProperty("code").GetString())).ToArray();
        diagnostics.Should().Equal(("warning", "msbuild.ignored_package_reference"), ("warning", "fsharp.compiler"));
    }

    [Test]
    public async Task AnUnsupportedConstructGivesOneFailedReportWithItsRange()
    {
        var project = Project(CompileRunnerTests.MutablePricing);
        using var _ = project.Workspace;
        var run = await Compile(project, "--json");
        run.Exit.Should().Be(1);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        report.RootElement.GetProperty("failureKind").GetString().Should().Be("source");
        report.RootElement.GetProperty("frontend").GetString().Should().Be("morphir-dotnet.fsharp (in-process)");
        report.RootElement.GetProperty("sources").GetProperty("documents").GetArrayLength().Should().Be(2);
        var mutable = report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Single(d => d.GetProperty("code").GetString() == "fsharp.unsupported_mutable");
        mutable.GetProperty("location").GetProperty("uri").GetString().Should().Be("Pricing.fs");
        mutable.GetProperty("location").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(4);
        run.Output.Error.Should().NotContain("fsharp.unsupported_mutable", "JSON mode streams nothing to stderr");
    }

    [Test]
    public async Task HumanModeStreamsAProjectFileWarningBeforeASlowFrontendCompletes()
    {
        var project = Project();
        using var _ = project.Workspace;
        CapturedOutput? output = null;
        string? stderrWhileCompiling = null;
        var slow = new FakeFrontend("morphir-dotnet.fsharp", FakeFrontend.FSharp(), async (request, ct) =>
        {
            await Task.Delay(200, ct);
            stderrWhileCompiling = output!.Error;
            return await FSharpFrontend.Registration.CompileAsync(request, ct);
        });
        var run = await CompileObserved(project, "", slow, captured => output = captured);
        run.Exit.Should().Be(0, run.Output.Error);
        stderrWhileCompiling.Should().Contain("msbuild.ignored_package_reference", "the sources stage completed before the frontend");
        var summary = run.Output.Error.Split('\n').Single(line => line.StartsWith("compile succeeded", StringComparison.Ordinal));
        summary.Should().Contain("warning msbuild.ignored_package_reference");
        run.Output.Text.Trim().Should().EndWith("morphir-ir.json", "stdout holds only the published path");
    }

    [Test]
    public async Task JsonLinesWritesAndFlushesEachRecordAsItsEventOccurs()
    {
        var project = Project();
        using var _ = project.Workspace;
        CapturedOutput? output = null;
        string[]? flushedWhileCompiling = null;
        var slow = new FakeFrontend("morphir-dotnet.fsharp", FakeFrontend.FSharp(), async (request, ct) =>
        {
            await Task.Delay(100, ct);
            flushedWhileCompiling = output!.FlushedLines;
            return await FSharpFrontend.Registration.CompileAsync(request, ct);
        });
        var run = await CompileObserved(project, "--json-lines", slow, captured => output = captured);
        run.Exit.Should().Be(0, run.Output.Error);
        flushedWhileCompiling.Should().NotBeNull();
        flushedWhileCompiling!.Select(Event).Should().Equal(
            "stageStarted", "stageCompleted", "stageStarted", "diagnostics", "stageCompleted", "stageStarted", "stageCompleted", "stageStarted");

        var lines = run.Output.Lines;
        lines.Should().OnlyContain(line => Parses(line));
        lines.Count(line => Event(line) == "completed").Should().Be(1);
        Event(lines[^1]).Should().Be("completed");
        using var sources = JsonDocument.Parse(lines.First(line => Event(line) == "diagnostics"));
        sources.RootElement.GetProperty("stage").GetString().Should().Be("sources");
        sources.RootElement.GetProperty("document").GetString().Should().Be("Acme.CarRental.fsproj");

        // The completed record holds the report that --json writes, and the diagnostics records add up to it.
        var json = await Compile(project, "--json");
        using var completed = JsonDocument.Parse(lines[^1]);
        using var report = JsonDocument.Parse(json.Output.Text);
        JsonSerializer.Serialize(completed.RootElement.GetProperty("report")).Should().Be(JsonSerializer.Serialize(report.RootElement));
        var streamed = lines.Where(line => Event(line) == "diagnostics")
            .SelectMany(line => JsonDocument.Parse(line).RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetRawText()))
            .Order(StringComparer.Ordinal);
        var reported = report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d => JsonSerializer.Serialize(d)).Order(StringComparer.Ordinal);
        streamed.Should().Equal(reported);
    }

    [Test]
    public async Task JsonLinesEndsAFailureWithACompletedRecord()
    {
        var project = Project(CompileRunnerTests.MutablePricing);
        using var _ = project.Workspace;
        var run = await Compile(project, "--json-lines");
        run.Exit.Should().Be(1);
        run.Output.Lines.Should().OnlyContain(line => Parses(line));
        using var completed = JsonDocument.Parse(run.Output.Lines[^1]);
        completed.RootElement.GetProperty("event").GetString().Should().Be("completed");
        completed.RootElement.GetProperty("report").GetProperty("success").GetBoolean().Should().BeFalse();
        completed.RootElement.GetProperty("report").GetProperty("exitCode").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task JsonAndJsonLinesTogetherGiveOneInvocationFailureRecord()
    {
        var project = Project();
        using var _ = project.Workspace;
        var run = await Compile(project, "--json --json-lines");
        run.Exit.Should().Be(2);
        run.Output.Lines.Should().ContainSingle();
        using var completed = JsonDocument.Parse(run.Output.Lines[0]);
        completed.RootElement.GetProperty("event").GetString().Should().Be("completed");
        completed.RootElement.GetProperty("report").GetProperty("failureKind").GetString().Should().Be("invocation");
        project.Provider.Expanded.Should().BeEmpty();
    }

    [Test]
    public async Task AParseErrorInJsonLinesModeEndsWithACompletedRecord()
    {
        var project = Project();
        using var _ = project.Workspace;
        var run = await Compile(project, "--json-lines --no-such-option");
        run.Exit.Should().Be(2);
        run.Output.Lines.Should().ContainSingle();
        Event(run.Output.Lines[0]).Should().Be("completed");
    }

    [Test]
    public async Task CancellationInJsonLinesModeEndsWithACompletedRecordAndExitCode130()
    {
        var project = Project();
        using var _ = project.Workspace;
        using var cancellation = new CancellationTokenSource();
        var waiting = new FakeFrontend("morphir-dotnet.fsharp", FakeFrontend.FSharp(), async (_, ct) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var run = await Compile(project, "--json-lines", waiting, cancellation);
        run.Exit.Should().Be(130);
        using var completed = JsonDocument.Parse(run.Output.Lines[^1]);
        completed.RootElement.GetProperty("event").GetString().Should().Be("completed");
        completed.RootElement.GetProperty("report").GetProperty("failureKind").GetString().Should().Be("cancelled");
        File.Exists(project.Workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();
    }

    [Test]
    [Arguments("", 0)]
    [Arguments("--ir-version 4.1.0-draft.1", 2)]
    [Arguments("--types-only", 2)]
    [Arguments("--input src/Rates.fs --project-file src/Acme.CarRental.fsproj", 2)]
    [Arguments("--input src/Rates.fs", 0)]
    public async Task ExitCodesFollowTheGenerationConvention(string arguments, int exit)
    {
        var project = Project();
        using var _ = project.Workspace;
        var run = await Compile(project, "--json " + arguments);
        run.Exit.Should().Be(exit, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("exitCode").GetInt32().Should().Be(exit);
    }

    [Test]
    public async Task OutsideAnyProjectInputFilesCompileWithTheDefaults()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("A.fs", "module Acme.Demo.A\n\nlet answer : int = 42\n");
        var run = await Compile(workspace, [], FSharpFrontend.Registration, "--json --language fsharp --input A.fs --package-name acme/demo");
        run.Exit.Should().Be(0, run.Output.Text);
        using var report = JsonDocument.Parse(run.Output.Text);
        report.RootElement.GetProperty("irVersion").GetString().Should().Be("4");
        report.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).Should().Equal("A");
    }

    private static async Task<Run> CompileObserved((CompileWorkspace Workspace, FakeProjectFileProvider Provider) project, string arguments,
        IFrontend frontend, Action<CapturedOutput> observe)
    {
        var output = new CapturedOutput();
        observe(output);
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<CompilationScenario>()
            .ConfigureServices(services =>
            {
                services.AddSingleton(frontend);
                services.RemoveAll<IProjectFileProvider>();
                services.AddSingleton<IProjectFileProvider>(project.Provider);
                services.AddSingleton<ICompileOutput>(output);
                services.AddSingleton(new CompileContextSource(() => project.Workspace.Context()));
            })
            .Build();
        return new Run(await app.RunAsync(), output);
    }

    private static bool Parses(string line)
    {
        try { using var _ = JsonDocument.Parse(line); return true; }
        catch (JsonException) { return false; }
    }

    private static string Event(string line)
    {
        using var record = JsonDocument.Parse(line);
        return record.RootElement.GetProperty("event").GetString()!;
    }

    private sealed record Run(int Exit, CapturedOutput Output);

    /// <summary>Captured stdout and stderr. Stdout records what was flushed.</summary>
    public sealed class CapturedOutput : ICompileOutput
    {
        private readonly FlushRecordingStream _stdout = new();
        private readonly StringWriter _stderr = new();

        public Stream StandardOutput => _stdout;
        public TextWriter StandardError => TextWriter.Synchronized(_stderr);
        public string Text => Encoding.UTF8.GetString(_stdout.ToArray());
        public string Error => _stderr.ToString();
        public string[] Lines => Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        public string[] FlushedLines => Encoding.UTF8.GetString(_stdout.Flushed).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class FlushRecordingStream : MemoryStream
    {
        public byte[] Flushed { get; private set; } = [];

        public override void Flush()
        {
            base.Flush();
            Flushed = ToArray();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flush();
            return Task.CompletedTask;
        }
    }
}
