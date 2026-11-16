// Probe: one admission rule set as a Roslyn DiagnosticAnalyzer, run in process (as `morphir compile` would)
// under Native AOT. The same analyzer class is what the compiler and the IDE load from a package.
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

const string Source = """
    namespace Acme.Trading;
    public record Order(decimal Quantity, decimal Price);
    public static class Trading
    {
        private static int counter;
        public static decimal Notional(Order order) => order.Quantity * order.Price;
        public static decimal Total(System.Collections.Generic.IReadOnlyList<Order> orders)
        {
            var total = 0m;
            foreach (var o in orders) { total += Notional(o); }
            counter++;
            while (total > 1000m) { total -= 1m; }
            return total;
        }
        public static object Boxed(Order order) => (dynamic)order;
    }
    public class Mutable { public int Value { get; set; } }
    """;

var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp14), path: "Trading.cs");
var refs = Directory.GetFiles(args[0], "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
var compilation = CSharpCompilation.Create("Model", [tree], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
Console.Error.WriteLine($"compile errors: {compileErrors.Length}");
foreach (var d in compileErrors) Console.Error.WriteLine(d.ToString());

var withAnalyzers = compilation.WithAnalyzers([new MorphirAdmissionAnalyzer()]);
var found = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
foreach (var d in found.OrderBy(d => d.Location.SourceSpan.Start).ThenBy(d => d.Id, StringComparer.Ordinal))
{
    var line = d.Location.GetLineSpan();
    Console.Out.WriteLine($"{line.Path}({line.StartLinePosition.Line + 1},{line.StartLinePosition.Character + 1}): {d.Severity.ToString().ToLowerInvariant()} {d.Id}: {d.GetMessage()} [{d.Descriptor.HelpLinkUri}]");
}
return compileErrors.Length == 0 ? 0 : 1;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MorphirAdmissionAnalyzer : DiagnosticAnalyzer
{
    private static DiagnosticDescriptor Rule(string id, string slug, string message) => new(
        id, slug, message, "Morphir.Admission", DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: $"https://morphir.finos.org/rules/csharp/{slug}");

    private static readonly DiagnosticDescriptor Loop = Rule("MORPHIR1001", "unsupported_loop", "A '{0}' loop is not in the Morphir subset");
    private static readonly DiagnosticDescriptor Mutation = Rule("MORPHIR1002", "unsupported_mutation", "A change of '{0}' is not in the Morphir subset");
    private static readonly DiagnosticDescriptor Dynamic = Rule("MORPHIR1003", "unsupported_dynamic", "'dynamic' is not in the Morphir subset");
    private static readonly DiagnosticDescriptor Setter = Rule("MORPHIR1004", "unsupported_mutable_property", "Property '{0}' has a 'set' accessor");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Loop, Mutation, Dynamic, Setter];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(c =>
        {
            var loop = (ILoopOperation)c.Operation;
            var kind = loop.LoopKind switch { LoopKind.While => "while", LoopKind.For => "for", LoopKind.ForEach => "foreach", _ => "loop" };
            c.ReportDiagnostic(Diagnostic.Create(Loop, loop.Syntax.GetFirstToken().GetLocation(), kind));
        }, OperationKind.Loop);
        context.RegisterOperationAction(c =>
        {
            var target = c.Operation switch
            {
                ICompoundAssignmentOperation a => a.Target,
                IIncrementOrDecrementOperation i => i.Target,
                _ => null,
            };
            var name = target switch { ILocalReferenceOperation l => l.Local.Name, IMemberReferenceOperation m => m.Member.Name, _ => "a value" };
            c.ReportDiagnostic(Diagnostic.Create(Mutation, c.Operation.Syntax.GetLocation(), name));
        }, OperationKind.CompoundAssignment, OperationKind.Increment, OperationKind.Decrement);
        context.RegisterOperationAction(c =>
        {
            if (c.Operation.Type?.TypeKind == TypeKind.Dynamic)
                c.ReportDiagnostic(Diagnostic.Create(Dynamic, c.Operation.Syntax.GetLocation()));
        }, OperationKind.Conversion);
        context.RegisterSymbolAction(c =>
        {
            var property = (IPropertySymbol)c.Symbol;
            if (property.SetMethod is { IsInitOnly: false, IsImplicitlyDeclared: false })
                c.ReportDiagnostic(Diagnostic.Create(Setter, property.Locations[0], property.Name));
        }, SymbolKind.Property);
    }
}
