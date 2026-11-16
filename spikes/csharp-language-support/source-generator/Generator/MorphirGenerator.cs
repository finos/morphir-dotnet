using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Morphir.Lang.CSharp;

namespace Morphir.Lang.CSharp.Generator;

/// <summary>The thin adapter: each *.morphir.json AdditionalFile goes through the shared F# core.</summary>
[Generator]
public sealed class MorphirGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Refused = new(
        "MORPHIR2001", "Morphir generation failed", "{0}", "Morphir.Generation", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".morphir.json", System.StringComparison.Ordinal))
            .Select(static (file, ct) => file.GetText(ct)?.ToString() ?? "");

        context.RegisterSourceOutput(models, static (output, irJson) =>
        {
            var result = GeneratorCore.Generate(irJson, output.CancellationToken);
            foreach (var message in result.Item2)
                output.ReportDiagnostic(Diagnostic.Create(Refused, Location.None, message));
            foreach (var file in result.Item1)
                output.AddSource(file.Item1.Replace('/', '.'), SourceText.From(file.Item2, Encoding.UTF8));
        });
    }
}
