using System.Diagnostics;
using FluentAssertions;
using Morphir.Compilation;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Compile.Sources;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Compile.Sources;

/// <summary>
/// The MSBuild provider against real project files and the real .NET SDK. Each fixture is written to a new
/// temporary directory with empty <c>Directory.Build.*</c> files, so that no repository build file applies.
/// Every real MSBuild run holds a slot of the shared process gate.
/// </summary>
public sealed class MsBuildProjectFileProviderTests : IDisposable
{
    private const string FSharpProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Rates.fs" />
            <Compile Include="Pricing/Rules.fs" />
            <Compile Include="Pricing.fs" />
          </ItemGroup>
        </Project>
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("morphir-msbuild-").FullName;
    private readonly List<(ProcessStartInfo StartInfo, ProcessRun Run)> _runs = [];

    public MsBuildProjectFileProviderTests()
    {
        Write("Directory.Build.props", "<Project />");
        Write("Directory.Build.targets", "<Project />");
        Write("Directory.Packages.props", "<Project />");
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Project(string items, string properties = "") => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            {properties}
          </PropertyGroup>
          <ItemGroup>
            {items}
          </ItemGroup>
        </Project>
        """;

    private async Task<ProcessRun> GatedRun(ProcessStartInfo startInfo, ProcessLimits limits, CancellationToken ct)
    {
        using var slot = await ProcessGate.Shared.EnterAsync(ct);
        var run = await BoundedProcess.RunAsync(startInfo, limits, ct);
        lock (_runs) _runs.Add((startInfo, run));
        return run;
    }

    private MsBuildProjectFileProvider Provider(MsBuildProviderOptions? options = null) =>
        new(options ?? MsBuildProviderOptions.Default, GatedRun);

    private Task<Checked<SourceSelection>> Expand(string projectFile, MsBuildProviderOptions? options = null) =>
        Provider(options).ExpandAsync(projectFile, CancellationToken.None);

    private static SourceSelection Passed(Checked<SourceSelection> result)
    {
        result.TryGetValue(out var selection).Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return selection!;
    }

    private static IEnumerable<string> Codes(Checked<SourceSelection> result) =>
        result.Diagnostics.Select(diagnostic => diagnostic.CodeOrNull!.Value);

    [Test]
    public async Task FSharpProjectExpandsInDeclaredOrder()
    {
        var projectFile = Write("src/Acme.CarRental.fsproj", FSharpProject);
        Write("src/Rates.fs", "module Rates");
        Write("src/Pricing/Rules.fs", "module Rules");
        Write("src/Pricing.fs", "module Pricing");

        var result = await Expand(projectFile);

        var selection = Passed(result);
        result.Diagnostics.Should().BeEmpty("the SDK's implicit FSharp.Core reference is not the project's");
        selection.Root.Should().Be(Path.Combine(_root, "src"));
        selection.Documents.Should().Equal("Rates.fs", "Pricing/Rules.fs", "Pricing.fs");
        selection.ProjectFileOrNull.Should().Be(projectFile);
    }

    [Test]
    public async Task CSharpProjectExpandsTheSameWay()
    {
        var explicitProject = Write("explicit/Acme.csproj", Project(
            """<Compile Include="Second.cs" /><Compile Include="First.cs" />""",
            "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>"));
        Write("explicit/First.cs", "class First {}");
        Write("explicit/Second.cs", "class Second {}");
        var globbedProject = Write("globbed/Acme.csproj", Project(""));
        Write("globbed/Model.cs", "class Model {}");

        Passed(await Expand(explicitProject)).Documents.Should().Equal("Second.cs", "First.cs");
        Passed(await Expand(globbedProject)).Documents.Should().Equal("Model.cs");
    }

    [Test]
    public async Task ExpansionHasNoSideEffectsAndLeavesNoProcess()
    {
        var projectFile = Write("src/Acme.CarRental.fsproj", FSharpProject);

        Passed(await Expand(projectFile));

        Directory.Exists(Path.Combine(_root, "src", "obj")).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "src", "bin")).Should().BeFalse();
        var (startInfo, run) = _runs.Should().ContainSingle().Subject;
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.Environment["MSBUILDDISABLENODEREUSE"].Should().Be("1");
        startInfo.ArgumentList.Should().Contain(["-nodeReuse:false", "-noAutoResponse"]);
        startInfo.ArgumentList.Should().NotContain(argument =>
            argument.StartsWith("-t", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("-restore", StringComparison.OrdinalIgnoreCase));
        var exited = run.Should().BeOfType<ProcessRun.Exited>().Subject;
        // The pipes closed, so no descendant holds them; and the MSBuild process itself is gone.
        var gone = () => Process.GetProcessById(exited.ProcessId).HasExited.Should().BeTrue();
        try { gone(); } catch (ArgumentException) { /* No process has that identity any more. */ }
    }

    [Test]
    public async Task LinkedFileOutsideTheRootIsRefused()
    {
        Write("shared/Common.fs", "module Common");
        Write("src/Rates.fs", "module Rates");
        var projectFile = Write("src/Acme.fsproj", Project(
            """<Compile Include="Rates.fs" /><Compile Include="../shared/Common.fs" Link="Common.fs" />"""));

        var result = await Expand(projectFile);

        result.IsPassed.Should().BeFalse();
        Codes(result).Should().Equal("sources.outside_root");
        result.Diagnostics[0].Message.Should().Contain("../shared/Common.fs");
    }

    [Test]
    public async Task ProjectReferenceIsRefusedAndPackageReferenceIsAWarning()
    {
        Write("src/Rates.fs", "module Rates");
        var referencing = Write("src/Referencing.fsproj", Project(
            """<Compile Include="Rates.fs" /><ProjectReference Include="../other/Other.fsproj" />"""));
        var packaged = Write("src/Packaged.fsproj", Project(
            """<Compile Include="Rates.fs" /><PackageReference Include="Newtonsoft.Json" Version="13.0.3" />"""));

        var refused = await Expand(referencing);
        refused.IsPassed.Should().BeFalse();
        Codes(refused).Should().Equal("msbuild.unsupported_project_reference");
        refused.Diagnostics[0].Message.Should().Contain("../other/Other.fsproj");

        var warned = await Expand(packaged);
        Passed(warned).Documents.Should().Equal("Rates.fs");
        Codes(warned).Should().Equal("msbuild.ignored_package_reference");
        warned.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        warned.Diagnostics[0].Message.Should().Contain("Newtonsoft.Json");
    }

    [Test]
    public async Task MissingSdkIsUnavailable()
    {
        var projectFile = Write("src/Acme.fsproj", FSharpProject);
        var missing = Path.Combine(_root, "no-sdk", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        var result = await Expand(projectFile, MsBuildProviderOptions.Default with { DotnetPath = missing });

        Codes(result).Should().Equal("msbuild.unavailable");
        result.Diagnostics[0].Message.Should().Contain(".NET SDK 8.0 or later");
    }

    [Test]
    public async Task EvaluationErrorKeepsTheToolMessage()
    {
        var projectFile = Write("src/Acme.fsproj", """
            <Project Sdk="Microsoft.NET.Sdk"><Import Project="missing.props" /></Project>
            """);

        var result = await Expand(projectFile);

        Codes(result).Should().Equal("msbuild.evaluation_failed");
        result.Diagnostics[0].Message.Should().Contain("MSB4019").And.Contain("missing.props");
    }

    [Test]
    public async Task LinkedProjectItemIsRefusedByCapture()
    {
        var real = Write("real/Rates.fs", "module Rates");
        Write("src/Pricing.fs", "module Pricing");
        File.CreateSymbolicLink(Path.Combine(_root, "src", "Rates.fs"), real);
        var projectFile = Write("src/Acme.fsproj", Project(
            """<Compile Include="Rates.fs" /><Compile Include="Pricing.fs" />"""));

        var selection = Passed(await Expand(projectFile));
        var captured = await new SourceCapture().CaptureAsync(selection, SourceReadLimits.Default, CancellationToken.None);

        captured.IsPassed.Should().BeFalse();
        captured.Diagnostics.Select(d => (d.CodeOrNull!.Value, d.LocationOrNull!.Uri))
            .Should().Equal(("sources.unsupported_entry", "Rates.fs"));
    }

    [Test]
    public async Task CancellationStopsExpansion()
    {
        var projectFile = Write("src/Acme.fsproj", FSharpProject);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var expand = () => Provider().ExpandAsync(projectFile, cancellation.Token);

        await expand.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public void ProviderIsSelectedByMsBuildProjectFilePatterns()
    {
        var provider = new MsBuildProjectFileProvider();
        provider.Identity.Value.Should().Be("msbuild");
        provider.Patterns.Should().Equal("*.*proj");

        var registry = ProjectFileProviderRegistry.Create([provider]);
        foreach (var file in new[] { "Acme.fsproj", "Acme.csproj", "Acme.vbproj", "dirs.proj" })
            registry.Resolve(Path.Combine(_root, file)).IsPassed.Should().BeTrue(file);
        registry.Resolve(Path.Combine(_root, "build.gradle")).Diagnostics
            .Select(d => d.CodeOrNull!.Value).Should().Equal("sources.unknown_project_file");
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("output")]
    [Arguments("not-started")]
    public async Task RunnerFailuresBecomeDiagnostics(string failure)
    {
        ProcessRun run = failure switch
        {
            "deadline" => new ProcessRun.DeadlineExceeded(TimeSpan.FromSeconds(1)),
            "output" => new ProcessRun.OutputLimitExceeded(1024),
            _ => new ProcessRun.NotStarted("No such file."),
        };
        var provider = new MsBuildProjectFileProvider(MsBuildProviderOptions.Default, (_, _, _) => Task.FromResult(run));

        var result = await provider.ExpandAsync(Write("Acme.fsproj", FSharpProject), CancellationToken.None);

        Codes(result).Should().Equal(failure switch
        {
            "deadline" => "msbuild.deadline_exceeded",
            "output" => "msbuild.output_limit_exceeded",
            _ => "msbuild.unavailable",
        });
    }

    [Test]
    [Arguments(145, "", "No .NET SDKs were found.", "msbuild.unavailable")]
    [Arguments(1, "MSBUILD : error MSB1001: Unknown switch.\nSwitch: -getItem:Compile", "", "msbuild.unavailable")]
    [Arguments(1, "/p/A.fsproj(1,1): error MSB4025: The project file could not be loaded.", "", "msbuild.evaluation_failed")]
    [Arguments(0, "not json", "", "msbuild.evaluation_failed")]
    public async Task ToolOutputIsClassified(int exitCode, string output, string error, string code)
    {
        var provider = new MsBuildProjectFileProvider(MsBuildProviderOptions.Default,
            (_, _, _) => Task.FromResult<ProcessRun>(new ProcessRun.Exited(exitCode, output, error, 0)));

        var result = await provider.ExpandAsync(Write("Acme.fsproj", FSharpProject), CancellationToken.None);

        Codes(result).Should().Equal(code);
    }

    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
