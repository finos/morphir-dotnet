// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Compilation;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>An in-memory project-file provider. It records each expansion and returns fixed documents.</summary>
public sealed class FakeProjectFileProvider(params string[] documents) : IProjectFileProvider
{
    /// <summary>The project files expanded, in order.</summary>
    public List<string> Expanded { get; } = [];

    /// <summary>Warnings to add to each expansion.</summary>
    public List<Diagnostic> Warnings { get; } = [];

    public ComponentId Identity { get; } = ComponentId.Create("fake-msbuild");
    public IReadOnlyList<string> Patterns { get; } = ["*.*proj"];

    public Task<Checked<SourceSelection>> ExpandAsync(string projectFile, CancellationToken cancellationToken)
    {
        Expanded.Add(projectFile);
        var root = Path.GetDirectoryName(projectFile)!;
        return Task.FromResult(Checked<SourceSelection>.Of(SourceSelection.Create(root, documents, projectFile), Warnings));
    }
}

/// <summary>Task 4.3: neutral source selection (SRC-1) with an in-memory provider.</summary>
public sealed class SourceSelectionTests
{
    private static readonly IReadOnlySet<string> FSharp = new HashSet<string>(StringComparer.Ordinal) { ".fs", ".fsi" };
    private static readonly ProjectCompileSettingsResolver Resolver = new();

    private static CompileSettings Settings(CompileWorkspace workspace, CompileOverrides overrides)
    {
        var result = Resolver.Resolve(overrides, workspace.Context());
        result.TryGetValue(out var settings).Should().BeTrue(string.Join("\n", result.Diagnostics));
        return settings!;
    }

    private static async Task<Checked<SelectedSources>> Select(FakeProjectFileProvider provider, CompileSettings settings) =>
        await new SourceSelector(ProjectFileProviderRegistry.Create([provider])).SelectAsync(settings, FSharp, CancellationToken.None);

    private static SelectedSources Passed(Checked<SelectedSources> result)
    {
        result.TryGetValue(out var sources).Should().BeTrue(string.Join("\n", result.Diagnostics));
        return sources!;
    }

    private static string Code(Checked<SelectedSources> result) =>
        result.Diagnostics.Single(diagnostic => diagnostic.IsError).CodeOrNull!.Value;

    [Test]
    public async Task InputFilesKeepTheGivenOrder()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("src/Rates.fs", "");
        workspace.Write("src/Pricing.fs", "");
        var sources = Passed(await Select(new(), Settings(workspace, new() { Inputs = ["src/Rates.fs", "src/Pricing.fs"], PackageName = "acme/demo" })));
        sources.Selection.Documents.Should().Equal("Rates.fs", "Pricing.fs");
        sources.AbsoluteRoot.Should().Be(workspace.PathOf("src"));
    }

    [Test]
    public async Task InputFilesInSeveralDirectoriesAreRelativeToTheirCommonDirectory()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("src/core/Rates.fs", "");
        workspace.Write("src/app/Pricing.fs", "");
        var sources = Passed(await Select(new(), Settings(workspace, new() { Inputs = ["src/core/Rates.fs", "src/app/Pricing.fs"], PackageName = "acme/demo" })));
        sources.Selection.Documents.Should().Equal("core/Rates.fs", "app/Pricing.fs");
    }

    [Test]
    public async Task ADirectoryGivesItsFilesInLexicalOrder()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n\n[frontend]\nlanguage = \"fsharp\"\n");
        workspace.Write("src/b/Two.fs", "");
        workspace.Write("src/a/One.fs", "");
        workspace.Write("src/a/notes.md", "");
        var sources = Passed(await Select(new(), Settings(workspace, new())));
        sources.Selection.Documents.Should().Equal("a/One.fs", "b/Two.fs");
    }

    [Test]
    public async Task AnInputDirectoryReplacesTheSourceDirectory()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("lib/Z.fs", "");
        workspace.Write("lib/A.fs", "");
        var sources = Passed(await Select(new(), Settings(workspace, new() { Inputs = ["lib"], PackageName = "acme/demo", Language = "fsharp" })));
        sources.Selection.Documents.Should().Equal("A.fs", "Z.fs");
    }

    [Test]
    public async Task CommandLineInputReplacesTheConfiguredProjectFile()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/demo\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        workspace.Write("src/Acme.CarRental.fsproj", "<Project />");
        workspace.Write("src/Rates.fs", "");
        workspace.Write("src/Pricing.fs", "");
        var provider = new FakeProjectFileProvider("Pricing.fs", "Rates.fs");
        var sources = Passed(await Select(provider, Settings(workspace, new() { Inputs = ["src/Rates.fs", "src/Pricing.fs"] })));
        provider.Expanded.Should().BeEmpty("the configured project file is not evaluated");
        sources.Selection.Documents.Should().Equal("Rates.fs", "Pricing.fs");
    }

    [Test]
    public async Task TheConfiguredProjectFileGoesToItsProvider()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/demo\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        var projectFile = workspace.Write("src/Acme.CarRental.fsproj", "<Project />");
        var provider = new FakeProjectFileProvider("Rates.fs", "Pricing.fs");
        var sources = Passed(await Select(provider, Settings(workspace, new())));
        provider.Expanded.Should().Equal(projectFile);
        sources.Selection.Documents.Should().Equal("Rates.fs", "Pricing.fs");
        sources.Selection.ProjectFileOrNull.Should().Be(projectFile);
        sources.AbsoluteRoot.Should().Be(workspace.PathOf("src"));
    }

    [Test]
    public async Task AProjectFilePassedAsAnInputIsRefused()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Acme.CarRental.fsproj", "<Project />");
        var provider = new FakeProjectFileProvider("Rates.fs");
        var result = await Select(provider, Settings(workspace, new() { Inputs = ["Acme.CarRental.fsproj"], PackageName = "acme/demo" }));
        Code(result).Should().Be("sources.project_file_as_input");
        result.Diagnostics.Single().Message.Should().Contain("--project-file");
        provider.Expanded.Should().BeEmpty();
    }

    [Test]
    public async Task AnUnknownProjectFileNamesTheRegisteredPatterns()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/demo\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"build.gradle\"\n");
        workspace.Write("build.gradle", "");
        var result = await Select(new(), Settings(workspace, new()));
        Code(result).Should().Be("sources.unknown_project_file");
        result.Diagnostics.Single().Message.Should().Contain("build.gradle").And.Contain("*.*proj");
    }

    [Test]
    public async Task AProjectFileThatIsNotNamedIsNotEvaluated()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n\n[frontend]\nlanguage = \"fsharp\"\n");
        workspace.Write("src/Acme.fsproj", "<Project />");
        workspace.Write("src/Rates.fs", "");
        var provider = new FakeProjectFileProvider("Other.fs");
        var sources = Passed(await Select(provider, Settings(workspace, new())));
        provider.Expanded.Should().BeEmpty("the command never searches for project files");
        sources.Selection.Documents.Should().Equal("Rates.fs");
    }

    [Test]
    public async Task WithoutALanguageAConfiguredProjectFileIsNotReadAndTheLanguageIsRequired()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/demo\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        workspace.Write("src/Acme.CarRental.fsproj", "<Project />");
        var provider = new FakeProjectFileProvider("Rates.fs");
        var result = await Select(provider, Settings(workspace, new()));
        provider.Expanded.Should().BeEmpty();
        Code(result).Should().Be("compile.language_required");
        result.Diagnostics.Single().Message.Should().Contain("frontend.fsharp.project_file");
    }

    [Test]
    public async Task AMissingInputIsRefused()
    {
        using var workspace = new CompileWorkspace();
        Code(await Select(new(), Settings(workspace, new() { Inputs = ["Missing.fs"], PackageName = "acme/demo" })))
            .Should().Be("sources.missing");
    }

    [Test]
    public async Task ADirectoryAndFilesCannotBeCombined()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("lib/A.fs", "");
        workspace.Write("B.fs", "");
        Code(await Select(new(), Settings(workspace, new() { Inputs = ["lib", "B.fs"], PackageName = "acme/demo" })))
            .Should().Be("compile.invalid_setting");
    }
}
