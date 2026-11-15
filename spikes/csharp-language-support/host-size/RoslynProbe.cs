// Spike: a Roslyn probe for the size measurement of the Native AOT host. Not product code.
// apply.py copies this file to src/Morphir for one publish; revert.sh removes it.
#pragma warning disable RS1036, RS1041, RS2008
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Morphir;

internal static class RoslynProbe
{
    private const string Source = """
        using System.Collections.Generic;
        namespace Acme.Trading;
        public enum Side { Buy, Sell }
        public closed record Order;
        public sealed record Market(Side Side, decimal Quantity) : Order;
        public sealed record Limit(Side Side, decimal Quantity, decimal Price) : Order;
        public static class Pricing
        {
            private static int counter;
            public static decimal Notional(Order order, decimal last) => order switch
            {
                Market m => m.Quantity * last,
                Limit l => l.Quantity * l.Price,
            };
            public static decimal Total(IReadOnlyList<Order> orders)
            {
                var total = 0m;
                foreach (var o in orders) { total += Notional(o, 1m); }
                counter++;
                return total;
            }
            public static int Factorial(int n)
            {
                var acc = 1;
                while (n > 1) { acc *= n; n--; }
                return acc;
            }
        }
        """;

    public static int Run(string referenceDirectory)
    {
        // Frontend path: parse, bind, analyzers, data flow, control flow.
        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp15), path: "Trading.cs");
        var refs = Directory.GetFiles(referenceDirectory, "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
        var compilation = CSharpCompilation.Create("Model", [tree], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        Console.Out.WriteLine($"compile errors: {errors}");
        var found = compilation.WithAnalyzers([new ProbeAnalyzer()]).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        foreach (var d in found.OrderBy(d => d.Location.SourceSpan.Start))
            Console.Out.WriteLine($"{d.Id} {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}");
        var model = compilation.GetSemanticModel(tree);
        foreach (var type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(type)!;
            Console.Out.WriteLine($"type {symbol.Name} closed={symbol.IsClosed} union={symbol.IsUnion} record={symbol.IsRecord}");
        }
        foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var cfg = ControlFlowGraph.Create(method, model)!;
            foreach (var loop in method.DescendantNodes().Where(n => n is ForEachStatementSyntax or WhileStatementSyntax).Cast<StatementSyntax>())
            {
                var data = model.AnalyzeDataFlow(loop)!;
                var operation = (ILoopOperation)model.GetOperation(loop)!;
                Console.Out.WriteLine($"loop {method.Identifier.Text} {operation.LoopKind} written=[{string.Join(",", data.WrittenInside.Select(s => s.Name).Order())}] blocks={cfg.Blocks.Length}");
            }
        }

        // Backend path: syntax trees and layout.
        var record = RecordDeclaration(SyntaxKind.RecordDeclaration, Token(SyntaxKind.RecordKeyword), "Quote")
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword)))
            .WithParameterList(ParameterList(SingletonSeparatedList(Parameter(Identifier("Value")).WithType(ParseTypeName("double")))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        var unit = CompilationUnit().AddMembers(FileScopedNamespaceDeclaration(ParseName("Acme.Generated")).AddMembers(record)).NormalizeWhitespace(eol: "\n");
        Console.Out.Write(unit.ToFullString());
        Console.Out.Write("\n");
        return errors == 0 ? 0 : 1;
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class ProbeAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Mutation = new("MORPHIR1002", "mutation", "A change of '{0}' is not in the Morphir subset", "Morphir", DiagnosticSeverity.Error, true);
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Mutation];
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(c =>
            {
                if (((IIncrementOrDecrementOperation)c.Operation).Target is IMemberReferenceOperation m)
                    c.ReportDiagnostic(Diagnostic.Create(Mutation, c.Operation.Syntax.GetLocation(), m.Member.Name));
            }, OperationKind.Increment, OperationKind.Decrement);
        }
    }
}
