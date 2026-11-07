// Spike: Roslyn under Native AOT.
// See docs/content/contributing/design/spikes/extension-model/roslyn-aot.md.
//
// Modes (stdout carries the result, stderr carries timings and errors):
//   walk [--refs basic|sdk|tpa|location] [--scale N]   parse, bind, walk declarations and bodies
//   diag                                                 report a compile error (diagnostic text from resources)
//   emit                                                 emit IL to memory, then try Assembly.Load
//   analyzer                                             run an in-process DiagnosticAnalyzer
//   generator                                            run an in-process IIncrementalGenerator
//   analyzer-file <path.dll>                             load an analyzer assembly from disk
//
// Variants (MSBuild property ProbeVariant), so that each publish shows the warnings of one feature set:
//   frontend    walk, diag. What a C# frontend needs.            (default)
// ProbeRefs=sdk leaves out Basic.Reference.Assemblies (PROBE_NO_BASIC); the probe then reads the SDK targeting pack.
//   extensions  frontend + emit to memory, in-process analyzer and generator.   PROBE_EXTENSIONS
//   full        extensions + Assembly.Load, analyzer-file, --refs location.     PROBE_EXTENSIONS, PROBE_FULL
//   workspaces  frontend + an AdhocWorkspace (Microsoft.CodeAnalysis.CSharp.Workspaces).  PROBE_WORKSPACES
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynAotProbe;

public static class Program
{
    public static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "walk";
        try
        {
            return mode switch
            {
                "walk" => Walk(args),
                "diag" => Diag(),
#if PROBE_EXTENSIONS
                "emit" => Emit(),
                "analyzer" => RunAnalyzer(),
                "generator" => RunGenerator(),
#endif
#if PROBE_WORKSPACES
                "workspace" => RunWorkspace(),
#endif
#if PROBE_FULL
                "analyzer-file" => LoadAnalyzerFile(args.Length > 1 ? args[1] : throw new ArgumentException("analyzer-file needs a path")),
#endif
                _ => throw new ArgumentException($"unknown mode '{mode}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {mode}: {ex.GetType().FullName}: {ex.Message}");
            return 2;
        }
    }

    // ---------------------------------------------------------------- references

    static IReadOnlyList<MetadataReference> References(string kind)
    {
        switch (kind)
        {
#if !PROBE_NO_BASIC
            case "basic":
                // Reference assemblies embedded as resources in Basic.Reference.Assemblies.Net100.
                return Basic.Reference.Assemblies.Net100.References.All;
#endif

            case "sdk":
            {
                // The targeting pack of an installed .NET SDK. A frontend can find it from DOTNET_ROOT.
                var root = Environment.GetEnvironmentVariable("DOTNET_ROOT")
                           ?? throw new InvalidOperationException("DOTNET_ROOT is not set");
                var packs = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
                var version = Directory.GetDirectories(packs).Select(Path.GetFileName).OrderBy(v => v).Last()!;
                var dir = Path.Combine(packs, version, "ref", "net10.0");
                return Directory.GetFiles(dir, "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            }

            case "tpa":
            {
                // The JIT runtime gives the list of framework implementation assemblies. Native AOT does not.
                var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                          ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is null in this host");
                return tpa.Split(Path.PathSeparator).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            }

#if PROBE_FULL
            case "location":
            {
#pragma warning disable IL3000 // Intentional: this mode shows what Assembly.Location gives under AOT.
                var location = typeof(object).Assembly.Location;
#pragma warning restore IL3000
                if (string.IsNullOrEmpty(location))
                    throw new InvalidOperationException("typeof(object).Assembly.Location is empty in this host");
                var dir = Path.GetDirectoryName(location)!;
                return Directory.GetFiles(dir, "System*.dll").Append(Path.Combine(dir, "netstandard.dll"))
                    .Where(File.Exists)
                    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            }
#endif

            default:
                throw new ArgumentException($"unknown reference kind '{kind}'");
        }
    }

#if PROBE_NO_BASIC
    const string DefaultReferenceKind = "sdk";
#else
    const string DefaultReferenceKind = "basic";
#endif

    static IReadOnlyList<MetadataReference> DefaultReferences() => References(DefaultReferenceKind);

    static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees, IReadOnlyList<MetadataReference> refs, OutputKind kind = OutputKind.DynamicallyLinkedLibrary) =>
        CSharpCompilation.Create(
            "Acme.Orders",
            trees,
            refs,
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));

    // ---------------------------------------------------------------- walk

    static int Walk(string[] args)
    {
        var refsKind = Option(args, "--refs") ?? DefaultReferenceKind;
        var scale = int.Parse(Option(args, "--scale") ?? "1");
        var total = Stopwatch.StartNew();

        var sw = Stopwatch.StartNew();
        var refs = References(refsKind);
        var tRefs = sw.Elapsed;

        sw.Restart();
        var trees = Enumerable.Range(0, scale)
            .Select(i => CSharpSyntaxTree.ParseText(Sources.Domain(i), new CSharpParseOptions(LanguageVersion.Latest), path: $"Domain{i}.cs"))
            .ToList();
        var tParse = sw.Elapsed;

        sw.Restart();
        var compilation = Compile(trees, refs);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var tBind = sw.Elapsed;

        sw.Restart();
        var output = new StringBuilder();
        output.AppendLine($"references: {refs.Count}");
        output.AppendLine($"errors: {errors.Count}");
        foreach (var e in errors.Take(5)) output.AppendLine($"  {e}");

        // Report the model of the first tree only, so the output stays the same for every --scale.
        for (var i = 0; i < trees.Count; i++)
        {
            var model = compilation.GetSemanticModel(trees[i]);
            var sink = i == 0 ? output : new StringBuilder();
            new ModelWalker(sink).Visit(trees[i], model);
        }
        var tWalk = sw.Elapsed;

        Console.Write(output.ToString());
        Console.Error.WriteLine(
            $"timing refs={refsKind} scale={scale} loadRefs={tRefs.TotalMilliseconds:F0}ms parse={tParse.TotalMilliseconds:F0}ms " +
            $"bind={tBind.TotalMilliseconds:F0}ms walk={tWalk.TotalMilliseconds:F0}ms total={total.Elapsed.TotalMilliseconds:F0}ms");
        return errors.Count == 0 ? 0 : 1;
    }

    static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

#if PROBE_WORKSPACES
    // ---------------------------------------------------------------- workspace

    static int RunWorkspace()
    {
        // The Workspaces layer composes its services with MEF (System.Composition).
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "Acme.Orders", "Acme.Orders", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: DefaultReferences()));
        var document = workspace.AddDocument(project.Id, "Domain0.cs", Microsoft.CodeAnalysis.Text.SourceText.From(Sources.Domain(0)));
        var compilation = document.Project.GetCompilationAsync().GetAwaiter().GetResult()!;
        var errors = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        var formatted = Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync(document).GetAwaiter().GetResult();
        Console.WriteLine($"workspace errors: {errors}, formatted length: {formatted.GetTextAsync().GetAwaiter().GetResult().Length}");
        return errors == 0 ? 0 : 1;
    }
#endif

    // ---------------------------------------------------------------- diag

    static int Diag()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class C { public static int F() => missing + 1; }");
        var compilation = Compile(new[] { tree }, DefaultReferences());
        foreach (var d in compilation.GetDiagnostics()) Console.WriteLine(d.ToString());
        return 0;
    }

#if PROBE_EXTENSIONS
    // ---------------------------------------------------------------- emit

    static int Emit()
    {
        var tree = CSharpSyntaxTree.ParseText(Sources.Domain(0));
        var compilation = Compile(new[] { tree }, DefaultReferences());
        using var pe = new MemoryStream();
        var result = compilation.Emit(pe);
        Console.WriteLine($"emit success: {result.Success}, bytes: {pe.Length}");
        if (!result.Success) return 1;

#if PROBE_FULL
        // A frontend has no need to run the code. This step shows what breaks if a tool tries.
        try
        {
#pragma warning disable IL2026 // Intentional: this mode shows what Assembly.Load does under AOT.
            var assembly = Assembly.Load(pe.ToArray());
            Console.WriteLine($"Assembly.Load: {assembly.GetTypes().Length} types");
#pragma warning restore IL2026
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Assembly.Load: {ex.GetType().FullName}: {ex.Message}");
        }
#endif
        return 0;
    }

    // ---------------------------------------------------------------- analyzer

    static int RunAnalyzer()
    {
        var tree = CSharpSyntaxTree.ParseText(Sources.Domain(0));
        var compilation = Compile(new[] { tree }, DefaultReferences());
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MutableFieldAnalyzer()));
        var diagnostics = withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        Console.WriteLine($"analyzer diagnostics: {diagnostics.Length}");
        foreach (var d in diagnostics.OrderBy(d => d.Location.SourceSpan.Start)) Console.WriteLine($"  {d}");
        return 0;
    }

#if PROBE_FULL
    static int LoadAnalyzerFile(string path)
    {
        // The normal way to run third-party analyzers and generators: load them from a .dll.
        var reference = new AnalyzerFileReference(Path.GetFullPath(path), new NoOpLoader());
        Console.WriteLine($"reference: {reference.Display}");
        var errors = new List<string>();
        reference.AnalyzerLoadFailed += (_, e) => errors.Add($"{e.ErrorCode}: {e.Exception?.GetType().FullName}: {e.Exception?.Message}");
        var analyzers = reference.GetAnalyzers(LanguageNames.CSharp);
        var generators = reference.GetGenerators(LanguageNames.CSharp);
        Console.WriteLine($"analyzers: {analyzers.Length}, generators: {generators.Length}");
        foreach (var e in errors.Distinct()) Console.WriteLine($"  load failed: {e}");
        return 0;
    }

    /// <summary>Loads analyzer assemblies with Assembly.LoadFrom, as a simple host does.</summary>
    sealed class NoOpLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }

#pragma warning disable IL2026 // Intentional: this mode shows what loading from a path does under AOT.
        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
#pragma warning restore IL2026
    }
#endif

    // ---------------------------------------------------------------- generator

    static int RunGenerator()
    {
        var tree = CSharpSyntaxTree.ParseText(Sources.Domain(0));
        var compilation = Compile(new[] { tree }, DefaultReferences());
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RecordCounterGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
        var result = driver.GetRunResult();
        Console.WriteLine($"generated trees: {result.GeneratedTrees.Length}, diagnostics: {diagnostics.Length}");
        foreach (var t in result.GeneratedTrees) Console.WriteLine(t.GetText().ToString().TrimEnd());
        var errors = updated.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        Console.WriteLine($"updated compilation errors: {errors}");
        return 0;
    }
#endif
}

/// <summary>Walks declarations and method bodies, as a frontend does to build Morphir IR.</summary>
sealed class ModelWalker(StringBuilder output)
{
    static readonly SymbolDisplayFormat Format = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                                  SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public void Visit(SyntaxTree tree, SemanticModel model)
    {
        var root = tree.GetCompilationUnitRoot();
        foreach (var decl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(decl)!;
            output.AppendLine($"type {symbol.ToDisplayString(Format)} kind={Kind(symbol)}" +
                              (symbol.BaseType is { SpecialType: not SpecialType.System_Object and not SpecialType.System_ValueType and not SpecialType.System_Enum } b
                                  ? $" base={b.ToDisplayString(Format)}" : ""));
            foreach (var member in symbol.GetMembers().Where(m => !m.IsImplicitlyDeclared && m.CanBeReferencedByName).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                switch (member)
                {
                    case IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } f:
                        output.AppendLine($"  case {f.Name} = {f.ConstantValue}");
                        break;
                    case IPropertySymbol p:
                        output.AppendLine($"  field {p.Name}: {p.Type.ToDisplayString(Format)}");
                        break;
                    case IMethodSymbol { MethodKind: MethodKind.Ordinary } m:
                        var ps = string.Join(", ", m.Parameters.Select(x => $"{x.Name}: {x.Type.ToDisplayString(Format)}"));
                        output.AppendLine($"  value {m.Name}({ps}): {m.ReturnType.ToDisplayString(Format)}");
                        Body(m, model);
                        break;
                }
            }
        }
    }

    static string Kind(INamedTypeSymbol s) => s switch
    {
        { TypeKind: TypeKind.Enum } => "enum",
        { IsRecord: true, IsAbstract: true } => "union",
        { IsRecord: true, IsValueType: true } => "record-struct",
        { IsRecord: true } => "record",
        { IsStatic: true } => "module",
        _ => s.TypeKind.ToString().ToLowerInvariant(),
    };

    void Body(IMethodSymbol method, SemanticModel model)
    {
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax() as MethodDeclarationSyntax;
        SyntaxNode? body = (SyntaxNode?)syntax?.ExpressionBody?.Expression ?? syntax?.Body;
        if (body is null) return;
        var operation = model.GetOperation(body);
        if (operation is null) return;
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var op in operation.DescendantsAndSelf())
            kinds[op.Kind.ToString()] = kinds.GetValueOrDefault(op.Kind.ToString()) + 1;
        output.AppendLine($"    ops: {string.Join(" ", kinds.Select(k => $"{k.Key}={k.Value}"))}");

        // Resolve every pattern, call and member reference to a symbol, as lowering to IR must.
        foreach (var op in operation.DescendantsAndSelf())
        {
            switch (op)
            {
                case IInvocationOperation call:
                    output.AppendLine($"    call {call.TargetMethod.ContainingType.ToDisplayString(Format)}.{call.TargetMethod.Name}");
                    break;
                case IDeclarationPatternOperation { MatchedType: { } t }:
                    output.AppendLine($"    match {t.ToDisplayString(Format)}");
                    break;
                case ITypePatternOperation tp:
                    output.AppendLine($"    match {tp.MatchedType.ToDisplayString(Format)}");
                    break;
                case IRecursivePatternOperation { MatchedType: { } rt }:
                    output.AppendLine($"    match {rt.ToDisplayString(Format)}");
                    break;
                case IBinaryOperation bin:
                    output.AppendLine($"    binary {bin.OperatorKind} : {bin.Type?.ToDisplayString(Format)}");
                    break;
            }
        }
    }
}

#if PROBE_EXTENSIONS
/// <summary>An in-process analyzer, defined in this binary (no assembly loading).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
sealed class MutableFieldAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor Rule = new(
        "MORPHIR001", "Mutable property", "Property '{0}' has a setter; Morphir types are immutable",
        "Morphir", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(c =>
        {
            var p = (IPropertySymbol)c.Symbol;
            if (p.SetMethod is { IsInitOnly: false })
                c.ReportDiagnostic(Diagnostic.Create(Rule, p.Locations[0], p.Name));
        }, SymbolKind.Property);
    }
}

/// <summary>An in-process incremental generator, defined in this binary (no assembly loading).</summary>
sealed class RecordCounterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var records = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is RecordDeclarationSyntax,
            static (ctx, _) => ((RecordDeclarationSyntax)ctx.Node).Identifier.Text);
        context.RegisterSourceOutput(records.Collect(), static (spc, names) =>
            spc.AddSource("Records.g.cs",
                $"namespace Acme.Orders.Generated {{ public static class Records {{ public const int Count = {names.Length}; public const string Names = \"{string.Join(",", names.OrderBy(n => n, StringComparer.Ordinal))}\"; }} }}"));
    }
}

#endif

static class Sources
{
    // A small domain, in the style that a Morphir C# frontend reads.
    public static string Domain(int i) => $$"""
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Linq;

        namespace Acme.Orders{{(i == 0 ? "" : i.ToString())}};

        public readonly record struct OrderId(Guid Value);

        public enum Currency { Usd = 840, Eur = 978, Gbp = 826 }

        public sealed record Money(decimal Amount, Currency Currency);

        public sealed record Line(string Sku, int Quantity, Money Price);

        public abstract record Status
        {
            public sealed record Draft : Status;
            public sealed record Placed(DateOnly On) : Status;
            public sealed record Cancelled(string? Reason) : Status;
        }

        public sealed record Order(OrderId Id, IReadOnlyList<Line> Lines, Status Status)
        {
            public string? Note { get; set; }
        }

        public static class Pricing
        {
            public static decimal LineTotal(Line line) => line.Price.Amount * line.Quantity;

            public static decimal Total(Order order) => order.Lines.Sum(LineTotal);

            public static bool IsOpen(Order order) => order.Status switch
            {
                Status.Draft => true,
                Status.Placed { On: var on } when on.Year > 2000 => true,
                Status.Cancelled => false,
                _ => false,
            };

            public static Order Cancel(Order order, string? reason) =>
                order with { Status = new Status.Cancelled(reason ?? "none") };

            public static IReadOnlyList<T> Keep<T>(IEnumerable<T> items, Func<T, bool> predicate) =>
                items.Where(predicate).ToList();
        }
        """;
}
