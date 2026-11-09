// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Compilation;
using FormatVersion = Morphir.IR.Versioning.FormatVersion;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>Task 4.2: compile settings precedence, origins and exact format versions (CLI-1, VER-1, SRC-2).</summary>
public sealed class CompileSettingsResolverTests
{
    private static readonly ProjectCompileSettingsResolver Resolver = new();

    private static CompileSettings Passed(Checked<CompileSettings> result)
    {
        result.TryGetValue(out var settings).Should().BeTrue(string.Join("\n", result.Diagnostics));
        return settings!;
    }

    private static string[] Codes(Checked<CompileSettings> result) =>
        [.. result.Diagnostics.Where(d => d.IsError).Select(d => d.CodeOrNull?.Value ?? "")];

    [Test]
    public void TheCommandLineOverridesTheConfiguration()
    {
        using var workspace = new CompileWorkspace();
        var file = workspace.Write("morphir.toml", "[project]\nname = \"acme/car-rental\"\n\n[ir]\nformat_version = 3\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides { IrVersion = "4" }, workspace.Context()));
        settings.IrVersion.Value.Text.Should().Be("4");
        settings.IrVersion.Origin.Should().Be(new SettingOrigin.CommandLine("--ir-version"));
        settings.Package.Value.Text.Should().Be("acme/car-rental");
        settings.Package.Origin.Should().BeOfType<SettingOrigin.Configuration>()
            .Which.File.Should().EndWith("morphir.toml");
        Path.GetFullPath(((SettingOrigin.Configuration)settings.Package.Origin).File).Should().Be(Path.GetFullPath(file));
    }

    [Test]
    public void TheConfiguredIntegerVersionAppliesWithoutAFlag()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/car-rental\"\n\n[ir]\nformat_version = 3\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides(), workspace.Context()));
        settings.IrVersion.Value.Text.Should().Be("3");
        settings.IrVersion.Value.Version.Should().Be(FormatVersion.NewClassic(3));
        settings.IrVersion.Origin.Should().BeOfType<SettingOrigin.Configuration>().Which.Key.Should().Be("ir.format_version");
    }

    [Test]
    public void OutsideAnyProjectTheDefaultsApply()
    {
        using var workspace = new CompileWorkspace();
        var input = workspace.Write("A.fs", "module A\nlet x = 1\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides
        {
            Language = "fsharp",
            Inputs = ["A.fs"],
            PackageName = "acme/demo"
        }, workspace.Context()));
        settings.ProjectRoot.Should().BeNull();
        settings.IrVersion.Value.Text.Should().Be("4");
        settings.IrVersion.Origin.Should().Be(SettingOrigin.Default.Instance);
        settings.ExposedModules.Should().BeNull("every module is exposed");
        settings.Language!.Value.Value.Should().Be("fsharp");
        settings.Sources.Should().BeOfType<SourceRequest.Inputs>().Which.Paths.Should().Equal(input);
    }

    [Test]
    [Arguments("4.1.0-draft.1")]
    [Arguments("4.0.0")]
    [Arguments("4.0.0+build.5")]
    public void AFormatVersionKeepsItsExactSpelling(string version)
    {
        using var workspace = new CompileWorkspace();
        var settings = Passed(Resolver.Resolve(new CompileOverrides { IrVersion = version, PackageName = "acme/demo" }, workspace.Context()));
        settings.IrVersion.Value.Text.Should().Be(version);
    }

    [Test]
    public void AReleaseSpellingSelectsTheSameWriterAsTheInteger()
    {
        var writers = WriterRegistry.Create([new ClassicIrWriter(), new V4IrWriter()]);
        using var workspace = new CompileWorkspace();
        var release = Passed(Resolver.Resolve(new CompileOverrides { IrVersion = "4.0.0", PackageName = "acme/demo" }, workspace.Context()));
        var integer = Passed(Resolver.Resolve(new CompileOverrides { IrVersion = "4", PackageName = "acme/demo" }, workspace.Context()));
        writers.Resolve(release.IrVersion.Value.Version).TryGetValue(out var fromRelease).Should().BeTrue();
        writers.Resolve(integer.IrVersion.Value.Version).TryGetValue(out var fromInteger).Should().BeTrue();
        fromRelease!.Writer.Should().BeSameAs(fromInteger!.Writer);
        release.IrVersion.Value.Text.Should().Be("4.0.0", "the report records the requested spelling");
    }

    [Test]
    public void AConfiguredPreReleaseVersionIsKept()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n\n[ir]\nformat_version = \"4.1.0-draft.1\"\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides(), workspace.Context()));
        settings.IrVersion.Value.Text.Should().Be("4.1.0-draft.1");
    }

    [Test]
    public void TheConfiguredProjectFileOfTheSelectedLanguageIsTheSource()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides(), workspace.Context()));
        var source = settings.Sources.Should().BeOfType<SourceRequest.ProjectFile>().Which;
        source.Path.Should().Be(workspace.PathOf("src/Acme.CarRental.fsproj"));
        source.Origin.Should().BeOfType<SettingOrigin.Configuration>().Which.Key.Should().Be("frontend.fsharp.project_file");
        settings.Language!.Origin.Should().BeOfType<SettingOrigin.Configuration>();
    }

    [Test]
    public void AnotherLanguagesTableIsNotRead()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\nsource_directory = \"lib\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.csharp]\nproject_file = \"src/Acme.csproj\"\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides(), workspace.Context()));
        settings.Sources.Should().BeOfType<SourceRequest.SourceDirectory>().Which.Path.Should().Be(workspace.PathOf("lib"));
    }

    [Test]
    public void CommandLineSourcesReplaceTheConfiguredProjectFile()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides { Inputs = ["src/Rates.fs", "src/Pricing.fs"] }, workspace.Context()));
        settings.Sources.Should().BeOfType<SourceRequest.Inputs>().Which.Paths
            .Should().Equal(workspace.PathOf("src/Rates.fs"), workspace.PathOf("src/Pricing.fs"));
    }

    [Test]
    public void WithoutALanguageNoProjectFileKeyIsRead()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/car-rental\"\n\n[frontend.fsharp]\nproject_file = \"src/Acme.CarRental.fsproj\"\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides(), workspace.Context()));
        settings.Language.Should().BeNull("the upstream default language is not a selection");
        settings.Sources.Should().BeOfType<SourceRequest.SourceDirectory>().Which.Path.Should().Be(workspace.PathOf("src"));
        settings.UnreadProjectFileKeys.Should().Equal("frontend.fsharp.project_file");
    }

    [Test]
    public void ProjectFileAndInputCannotBeCombined()
    {
        using var workspace = new CompileWorkspace();
        var result = Resolver.Resolve(new CompileOverrides
        {
            ProjectFile = "Acme.CarRental.fsproj",
            Inputs = ["Extra.fs"],
            PackageName = "acme/demo"
        }, workspace.Context());
        Codes(result).Should().Equal("sources.conflicting_source_options");
    }

    [Test]
    public void TheProjectOptionSelectsAProjectDirectory()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("app/morphir.toml", "[project]\nname = \"acme/app\"\n\n[ir]\nformat_version = 2\n");
        var settings = Passed(Resolver.Resolve(new CompileOverrides { Project = "app" }, workspace.Context()));
        settings.Package.Value.Text.Should().Be("acme/app");
        settings.IrVersion.Value.Text.Should().Be("2");
        settings.ProjectRoot.Should().Be(workspace.PathOf("app"));
        settings.OutDirectory.Should().Be(workspace.PathOf("app/.morphir/out"));
    }

    [Test]
    public void AMissingPackageNameFails()
    {
        using var workspace = new CompileWorkspace();
        Codes(Resolver.Resolve(new CompileOverrides { Inputs = ["A.fs"] }, workspace.Context()))
            .Should().Equal("compile.package_required");
    }

    [Test]
    public void AnInvalidFormatVersionFails()
    {
        using var workspace = new CompileWorkspace();
        Codes(Resolver.Resolve(new CompileOverrides { IrVersion = "four", PackageName = "acme/demo" }, workspace.Context()))
            .Should().Equal("compile.invalid_setting");
    }

    [Test]
    public void TheDocumentTreeLayoutIsRefusedNamingTheKeyAndFile()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/demo\"\n\n[ir]\nlayout = \"document-tree\"\n");
        var result = Resolver.Resolve(new CompileOverrides(), workspace.Context());
        Codes(result).Should().Equal("compile.unsupported_setting");
        result.Diagnostics.Single(d => d.IsError).Message.Should().Contain("ir.layout").And.Contain("morphir.toml");
    }

    [Test]
    public void TypesOnlyIsRefused()
    {
        using var workspace = new CompileWorkspace();
        var result = Resolver.Resolve(new CompileOverrides { PackageName = "acme/demo", Inputs = ["A.fs"], TypesOnly = true }, workspace.Context());
        Codes(result).Should().Equal("compile.unsupported_setting");
        result.Diagnostics.Single(d => d.IsError).Message.Should().Contain("--types-only");
    }

    [Test]
    public void TheExtensionComesFromTheFlagElseTheSelectedLanguagesTable()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml",
            "[project]\nname = \"acme/demo\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[frontend.fsharp]\nextension = \"morphir-fsharp-frontend\"\n\n[frontend.csharp]\nextension = \"other\"\n");
        var configured = Passed(Resolver.Resolve(new CompileOverrides(), workspace.Context()));
        configured.Extension!.Value.Should().Be("morphir-fsharp-frontend");
        configured.Extension.Origin.Should().BeOfType<SettingOrigin.Configuration>().Which.Key.Should().Be("frontend.fsharp.extension");
        var flagged = Passed(Resolver.Resolve(new CompileOverrides { Extension = "/opt/guest" }, workspace.Context()));
        flagged.Extension!.Value.Should().Be("/opt/guest");
        flagged.Extension.Origin.Should().Be(new SettingOrigin.CommandLine("--extension"));
    }
}
