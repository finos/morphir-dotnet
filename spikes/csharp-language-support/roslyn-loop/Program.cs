// Probe: classify C# loops for lowering to Morphir (List.map / List.filter / List.foldl / LetRecursion / refuse)
// with Roslyn data-flow and control-flow analysis, under Native AOT.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

const string Source = """
    using System.Collections.Generic;
    namespace Acme.Trading;
    public record Order(decimal Quantity, decimal Price);
    public static class Trading
    {
        private static int counter;
        public static decimal Notional(Order order) => order.Quantity * order.Price;

        public static decimal Total(IReadOnlyList<Order> orders)
        {
            var total = 0m;
            foreach (var o in orders) { total += Notional(o); }
            return total;
        }
        public static List<decimal> Notionals(IReadOnlyList<Order> orders)
        {
            var result = new List<decimal>();
            foreach (var o in orders) { result.Add(Notional(o)); }
            return result;
        }
        public static List<Order> Large(IReadOnlyList<Order> orders)
        {
            var result = new List<Order>();
            foreach (var o in orders) { if (o.Price > 10m) result.Add(o); }
            return result;
        }
        public static List<decimal> LargeNotionals(IReadOnlyList<Order> orders)
        {
            var result = new List<decimal>();
            foreach (var o in orders) { if (o.Price > 10m) result.Add(Notional(o)); }
            return result;
        }
        public static (decimal, int) SumAndCount(IReadOnlyList<Order> orders)
        {
            var sum = 0m; var count = 0;
            foreach (var o in orders) { sum += o.Price; count++; }
            return (sum, count);
        }
        public static int Factorial(int n)
        {
            var acc = 1;
            while (n > 1) { acc *= n; n--; }
            return acc;
        }
        public static int FirstIndexOver(IReadOnlyList<Order> orders, decimal limit)
        {
            for (var i = 0; i < orders.Count; i++) { if (orders[i].Price > limit) return i; }
            return -1;
        }
        public static decimal Counted(IReadOnlyList<Order> orders)
        {
            var total = 0m;
            foreach (var o in orders) { total += o.Price; counter++; }
            return total;
        }
        public static decimal UntilNegative(IReadOnlyList<Order> orders)
        {
            var total = 0m;
            foreach (var o in orders) { if (o.Price < 0m) break; total += o.Price; }
            return total;
        }
        public static void Fill(decimal[] target, decimal value)
        {
            for (var i = 0; i < target.Length; i++) { target[i] = value; }
        }
    }
    """;

var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp14), path: "Trading.cs");
var refs = Directory.GetFiles(args[0], "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
var compilation = CSharpCompilation.Create("Model", [tree], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
Console.Error.WriteLine($"compile errors: {errors.Length}");
foreach (var d in errors) Console.Error.WriteLine(d.ToString());
var model = compilation.GetSemanticModel(tree);

foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
{
    foreach (var loopSyntax in method.DescendantNodes().Where(n => n is ForEachStatementSyntax or WhileStatementSyntax or ForStatementSyntax).Cast<StatementSyntax>())
    {
        var loop = (ILoopOperation)model.GetOperation(loopSyntax)!;
        var data = model.AnalyzeDataFlow(loopSyntax)!;
        // Analyze the body: a break leaves the body, but it does not leave the region of the loop statement.
        var bodySyntax = loopSyntax switch { ForEachStatementSyntax f => f.Statement, WhileStatementSyntax w => w.Statement, ForStatementSyntax f => f.Statement, _ => throw new InvalidOperationException() };
        var control = model.AnalyzeControlFlow(bodySyntax)!;
        var names = (IEnumerable<ISymbol> s) => string.Join(",", s.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

        // Loop-carried state: locals and parameters that the loop writes and that live outside the loop.
        // The variables of a for initializer are loop state too.
        var initializerLocals = loop is IForLoopOperation f0 ? f0.Locals.Cast<ISymbol>().ToArray() : [];
        var state = data.WrittenInside.Where(s => !data.VariablesDeclared.Contains(s, SymbolEqualityComparer.Default)).Concat(initializerLocals).ToArray();
        // Writes that are not to a local or parameter: fields, properties, array elements.
        var nonLocalWrites = loop.Descendants().Select(o => o switch
        {
            IAssignmentOperation a => a.Target,
            IIncrementOrDecrementOperation i => i.Target,
            _ => null,
        }).Where(t => t is not null and not ILocalReferenceOperation and not IParameterReferenceOperation)
          .Select(t => t switch { IMemberReferenceOperation m => m.Member.Name, IArrayElementReferenceOperation => "array element", _ => "other" }).Distinct().ToArray();
        // Calls of Add on a local list that is declared outside the loop: a list builder.
        var adds = loop.Descendants().OfType<IInvocationOperation>()
            .Where(i => i.TargetMethod.Name == "Add" && i.Instance is ILocalReferenceOperation l && !data.VariablesDeclared.Contains(l.Local, SymbolEqualityComparer.Default)).ToArray();
        var hasCondition = loop.Body.Descendants().OfType<IConditionalOperation>().Any();
        var loopVariable = loop is IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation v } ? v.Symbol : null;
        var addsLoopVariable = adds.Length == 1 && adds[0].Arguments[0].Value is ILocalReferenceOperation r && SymbolEqualityComparer.Default.Equals(r.Local, loopVariable);
        var breaks = control.ExitPoints.Count(e => e is BreakStatementSyntax);
        var returns = control.ReturnStatements.Length;

        string decision;
        if (nonLocalWrites.Length > 0) decision = $"REFUSE (writes {string.Join(",", nonLocalWrites)})";
        else if (loop is IForEachLoopOperation && breaks + returns == 0 && adds.Length == 1 && state.Length == 0)
            decision = !hasCondition ? "List.map" : addsLoopVariable ? "List.filter" : "List.filterMap";
        else if (loop is IForEachLoopOperation && breaks + returns == 0 && adds.Length == 0 && state.Length > 0)
            decision = state.Length == 1 ? "List.foldl" : $"List.foldl (state is a tuple of {state.Length})";
        else if (adds.Length == 0)
            decision = $"LetRecursion (parameters: {names(state)}; exits: {breaks} break, {returns} return)";
        else decision = "REFUSE (no rule)";

        // The control-flow graph of the method also builds under AOT.
        var cfg = ControlFlowGraph.Create(method, model)!;
        Console.Out.WriteLine($"{method.Identifier.Text,-15} {loop.LoopKind,-8} state=[{names(state)}] declared=[{names(data.VariablesDeclared)}] flowsOut=[{names(data.DataFlowsOut)}] cfgBlocks={cfg.Blocks.Length} => {decision}");
    }
}
return errors.Length == 0 ? 0 : 1;
