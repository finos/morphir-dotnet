// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Morphir.Diagrams.Canvas.Tests;

/// <summary>The C# examples of src/Morphir.Diagrams.Canvas/README.md compile without warnings.</summary>
public class ReadmeTests
{
    private static string Readme()
    {
        using var stream = typeof(ReadmeTests).Assembly.GetManifestResourceStream("Morphir.Diagrams.Canvas.README.md")
            ?? throw new InvalidOperationException("The README is not embedded.");
        using var reader = new StreamReader(stream);
        // A Windows checkout may give the README CRLF line endings.
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    public static IEnumerable<Func<string>> CSharpBlocks() =>
        Regex.Matches(Readme(), "```csharp\n(?<code>.*?)```", RegexOptions.Singleline).Select(match => (Func<string>)(() => match.Groups["code"].Value));

    [Test]
    [Category("PlatformSpecific")]
    [MethodDataSource(nameof(CSharpBlocks))]
    public void TheCSharpExampleCompilesWithoutWarnings(string source)
    {
        _ = typeof(DrawIoRenderer);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "ReadmeExample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString())
            .ShouldBeEmpty();
    }
}
