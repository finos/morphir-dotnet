// Spike: how Roslyn shows a C# 15 union to a frontend.
// Usage: dotnet run -c Release -- inputs/shapes.cs
// The probe compiles the input in memory and prints the diagnostics, the union symbols and the operation trees.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(args[0]), new CSharpParseOptions(LanguageVersion.Preview), path: args[0]);
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
var compilation = CSharpCompilation.Create("model", [tree], refs, options);
foreach (var d in compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning))
    Console.WriteLine("DIAG " + d);

var model = compilation.GetSemanticModel(tree);
Console.WriteLine("== types");
foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
{
    var type = (INamedTypeSymbol)model.GetDeclaredSymbol(declaration)!;
    Console.WriteLine($"{type.ToDisplayString()} kind={type.TypeKind} valueType={type.IsValueType} isUnion={type.IsUnion} isClosed={type.IsClosed} record={type.IsRecord}");
    if (!type.IsUnion) continue;
    Console.WriteLine("   cases: " + string.Join(", ", type.UnionCaseTypes.Select(c => c.ToDisplayString() + ":" + c.TypeKind)));
    Console.WriteLine("   members: " + string.Join(", ", type.GetMembers().Select(m => m.Kind + " " + m.Name + (m.IsImplicitlyDeclared ? "*" : ""))));
    Console.WriteLine("   interfaces: " + string.Join(", ", type.AllInterfaces.Select(i => i.ToDisplayString())));
}

Console.WriteLine("== operations");
foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
{
    Console.WriteLine("-- " + method.Identifier.Text);
    var body = (SyntaxNode?)method.ExpressionBody?.Expression ?? method.Body;
    var operation = body is null ? null : model.GetOperation(body);
    if (operation is null) { Console.WriteLine("  (no operation)"); continue; }
    // An expression body can have an implicit conversion above it; print from that parent.
    while (operation.Parent is IConversionOperation parent) operation = parent;
    Dump(operation, 1);
}

static void Dump(IOperation operation, int depth)
{
    var extra = operation switch
    {
        IConversionOperation c => $" [implicit={c.IsImplicit} conversion={c.GetConversion()} method={c.OperatorMethod?.ToDisplayString()}] {c.Operand.Type?.ToDisplayString()} -> {c.Type?.ToDisplayString()}",
        IDeclarationPatternOperation p => $" input={p.InputType.ToDisplayString()} narrowed={p.NarrowedType.ToDisplayString()} matched={p.MatchedType?.ToDisplayString()}",
        IRecursivePatternOperation p => $" input={p.InputType.ToDisplayString()} narrowed={p.NarrowedType.ToDisplayString()} matched={p.MatchedType.ToDisplayString()}",
        ITypePatternOperation p => $" input={p.InputType.ToDisplayString()} matched={p.MatchedType.ToDisplayString()}",
        IObjectCreationOperation o => $" type={o.Type?.ToDisplayString()}",
        IPropertyReferenceOperation p => $" property={p.Property.ToDisplayString()}",
        IDefaultValueOperation d => $" type={d.Type?.ToDisplayString()}",
        _ => "",
    };
    Console.WriteLine(new string(' ', depth * 2) + operation.Kind + extra);
    foreach (var child in operation.ChildOperations) Dump(child, depth + 1);
}
