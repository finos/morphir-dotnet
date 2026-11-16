// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Morphir.Lang.CSharp;
using Morphir.Lang.CSharp.Generator;
using Shouldly;

namespace Morphir.Lang.CSharp.Generator.Tests;

/// <summary>
/// The source generator in a Roslyn generator driver: it gives the paths and bytes of the pipeline function, and a
/// diagnostic for a model that is not valid (csharp-build-generation).
/// </summary>
public sealed class GeneratorTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static GeneratorRunResult Run(IEnumerable<(string Path, byte[] Bytes)> models, IReadOnlyDictionary<string, string>? properties = null)
    {
        var compilation = CSharpCompilation.Create("Consumer", options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(
            [new MorphirCSharpGenerator().AsSourceGenerator()],
            [.. models.Select(model => (AdditionalText)new ModelText(model.Path, model.Bytes))],
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            new Options(properties ?? new Dictionary<string, string>()));
        return driver.RunGenerators(compilation).GetRunResult().Results.Single();
    }

    private static GeneratorRunResult Run(string path, byte[] bytes, IReadOnlyDictionary<string, string>? properties = null) =>
        Run([(path, bytes)], properties);

    private static CSharpPipelineResult Pipeline(byte[] bytes) =>
        CSharpPipeline.Generate(CSharpPipelineRequest.Create(bytes), CancellationToken.None);

    [Test]
    [Arguments("daily-rate-v4.json")]
    [Arguments("daily-rate-v3.json")]
    [Arguments("car-rental-v2.json")]
    public void TheGeneratorAddsThePathsAndBytesOfThePipelineFunction(string fixture)
    {
        var bytes = Fixture(fixture);
        var result = Run("/models/model.morphir.json", bytes);
        var piped = Pipeline(bytes);

        piped.Succeeded.ShouldBeTrue(string.Join("; ", piped.Diagnostics));
        result.Exception.ShouldBeNull();
        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedSources.Select(source => source.HintName).ShouldBe(piped.Files.Select(file => file.RelativePath));
        foreach (var (source, file) in result.GeneratedSources.Zip(piped.Files))
            Encoding.UTF8.GetBytes(source.SourceText.ToString()).ShouldBe(Encoding.UTF8.GetBytes(file.Text), file.RelativePath);
    }

    [Test]
    [Arguments("malformed-model.txt", "invalid-json")]
    [Arguments("invalid-version.json", "unsupported-version")]
    public void AnInvalidModelGivesAnErrorWithTheReasonAndNoFile(string fixture, string code)
    {
        var bytes = fixture == "invalid-version.json" ? Encoding.UTF8.GetBytes("""{"formatVersion":"x"}""") : Fixture(fixture);
        var result = Run("/models/broken.morphir.json", bytes);

        result.Exception.ShouldBeNull();
        result.GeneratedSources.ShouldBeEmpty();
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("MORPHIR2001");
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.GetMessage().ShouldStartWith($"/models/broken.morphir.json: {code}");
        diagnostic.Location.GetLineSpan().Path.ShouldBe("/models/broken.morphir.json");
    }

    [Test]
    public void AFileThatIsNotAModelIsNotRead()
    {
        var result = Run("/models/model.json", Fixture("daily-rate-v4.json"));
        result.GeneratedSources.ShouldBeEmpty();
        result.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void TheGeneratorDoesNothingWhenTheMSBuildTargetWritesTheFiles()
    {
        var result = Run("/models/model.morphir.json", Fixture("daily-rate-v4.json"),
            new Dictionary<string, string> { [MorphirCSharpGenerator.FilesProperty] = "true" });
        result.GeneratedSources.ShouldBeEmpty();
        result.Diagnostics.ShouldBeEmpty();
    }

    [Test]
    [Arguments(".NETCoreApp", "v10.0", true)]
    [Arguments(".NETStandard", "v2.0", true)]
    [Arguments(".NETCoreApp", "v11.0", false)]
    public void ThePolyfillFileIsOnlyForAFrameworkWithNoRuntimeTypesOfCSharp15(string identifier, string version, bool expected)
    {
        var result = Run("/models/model.morphir.json", Fixture("daily-rate-v4.json"), new Dictionary<string, string>
        {
            ["build_property.TargetFrameworkIdentifier"] = identifier,
            ["build_property.TargetFrameworkVersion"] = version,
        });
        result.GeneratedSources.Any(source => source.HintName == CSharpPipeline.PolyfillPath).ShouldBe(expected);
    }

    [Test]
    public void TheNamespacePropertyIsTheNamespaceOfTheFiles()
    {
        var result = Run("/models/model.morphir.json", Fixture("daily-rate-v4.json"),
            new Dictionary<string, string> { [MorphirCSharpGenerator.NamespaceProperty] = "Pricing.Generated" });
        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedSources.ShouldContain(source => source.SourceText.ToString().Contains("namespace Pricing.Generated"));
    }

    [Test]
    public void AnInvalidNamespacePropertyGivesAnError()
    {
        var result = Run("/models/model.morphir.json", Fixture("daily-rate-v4.json"),
            new Dictionary<string, string> { [MorphirCSharpGenerator.NamespaceProperty] = "class" });
        result.GeneratedSources.ShouldBeEmpty();
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("MORPHIR2001");
        diagnostic.GetMessage().ShouldContain("invalid-namespace");
    }

    [Test]
    public void TwoModelsAddTheSamePolyfillFileOneTime()
    {
        var current = Fixture("daily-rate-v4.json");
        var other = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current).Replace("car/rental", "car/pricing"));
        var result = Run([("/models/a.morphir.json", current), ("/models/b.morphir.json", other)]);

        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedSources.Count(source => source.HintName == CSharpPipeline.PolyfillPath).ShouldBe(1);
        result.GeneratedSources.Select(source => source.HintName).ShouldBeUnique();
    }

    [Test]
    public void TwoModelsThatWriteOneFileWithDifferentTextGiveAnError()
    {
        var current = Fixture("daily-rate-v4.json");
        var other = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(current).Replace("5000", "6000"));
        var result = Run([("/models/a.morphir.json", current), ("/models/b.morphir.json", other)]);

        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("MORPHIR2003");
        diagnostic.GetMessage().ShouldContain("/models/a.morphir.json");
    }

    [Test]
    public void TheCodeThatTheGeneratorWroteInTheBuildOfThisProjectRuns() =>
        Car.Rental.Inventory.DailyRate().ShouldBe(5000);

    private sealed class ModelText(string path, byte[] bytes) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(bytes, bytes.Length, Encoding.UTF8, canBeEmbedded: true);
    }

    private sealed class Options(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptionsProvider
    {
        private readonly Values _global = new(properties);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Values.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Values.Empty;

        private sealed class Values(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly Values Empty = new(ImmutableDictionary<string, string>.Empty);

            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
                values.TryGetValue(key, out value);
        }
    }
}
