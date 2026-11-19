// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>The samples and examples of src/Morphir.Diagrams/README.md.</summary>
public partial class ReadmeTests
{
    internal static string Readme()
    {
        using var stream = typeof(ReadmeTests).Assembly.GetManifestResourceStream("Morphir.Diagrams.README.md")
            ?? throw new InvalidOperationException("The README is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The fenced blocks of a language, in README order.</summary>
    internal static string[] Blocks(string language) =>
        Regex.Matches(Readme(), $"```{language}\n(?<code>.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups["code"].Value).ToArray();

    public static IEnumerable<Func<string>> CSharpBlocks() =>
        Blocks("csharp").Select(block => (Func<string>)(() => block));

    [Test]
    [Category("PlatformSpecific")]
    [MethodDataSource(nameof(CSharpBlocks))]
    public void TheCSharpSampleCompilesWithoutWarnings(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "ReadmeSample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString())
            .ShouldBeEmpty();
    }

    [Test]
    public void TheReferenceExampleMatchesTheTestFixture()
    {
        var mermaid = Blocks("mermaid").ShouldHaveSingleItem();
        mermaid.ShouldBe(DiagramMother.Checkout.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");

        var listed = Blocks("text").ShouldHaveSingleItem().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        listed.ShouldBe(DiagramElementRef.Enumerate(DiagramMother.Read(DiagramMother.Checkout))
            .Select(reference => reference.ToString()));
    }

    [Test]
    public void EveryReferenceInTheTableParsesAndRoundTrips()
    {
        var examples = Regex.Matches(Readme(), @"\| `(?<reference>[a-z]+=[^`<]+)` \|").Select(match => match.Groups["reference"].Value).ToArray();

        // The element reference table and the per-family tables.
        examples.Length.ShouldBeGreaterThanOrEqualTo(8);
        foreach (var example in examples)
        {
            DiagramElementRef.TryParse(example, out var reference, out _).ShouldBeTrue(example);
            reference.ToString().ShouldBe(example);
        }
    }
}
