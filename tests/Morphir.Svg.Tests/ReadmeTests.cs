// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Morphir.Svg.Tests;

/// <summary>Compiles each C# block of src/Morphir.Svg/README.md against the library.</summary>
public partial class ReadmeTests
{
    private static string Readme()
    {
        using var stream = typeof(ReadmeTests).Assembly.GetManifestResourceStream("Morphir.Svg.README.md")
            ?? throw new InvalidOperationException("The README is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static IEnumerable<Func<string>> CSharpBlocks() =>
        CSharpBlock().Matches(Readme()).Select(match => (Func<string>)(() => match.Groups["code"].Value));

    [Test]
    public void TheReadmeHasASampleForEachSection() => CSharpBlocks().Count().ShouldBe(3);

    [Test]
    [Category("PlatformSpecific")]
    [MethodDataSource(nameof(CSharpBlocks))]
    public void TheSampleCompilesWithoutWarnings(string source)
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
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString())
            .ShouldBeEmpty();
    }

    [GeneratedRegex("```csharp\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();
}
