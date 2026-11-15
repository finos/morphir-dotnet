// Probe: C# generation with Roslyn syntax trees (SyntaxFactory + NormalizeWhitespace) under Native AOT.
// No Workspaces: no Formatter, no SyntaxGenerator.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

static ParameterSyntax Param(string name, TypeSyntax type) => Parameter(Identifier(name)).WithType(type);
static TypeSyntax T(string name) => ParseTypeName(name);
static SyntaxTokenList Mods(params SyntaxKind[] kinds) => TokenList(kinds.Select(Token));

// enum Side { Buy, Sell }
var side = EnumDeclaration("Side").WithModifiers(Mods(SyntaxKind.PublicKeyword))
    .AddMembers(EnumMemberDeclaration("Buy"), EnumMemberDeclaration("Sell"));

// public readonly record struct Quantity(decimal Value);
var quantity = RecordDeclaration(SyntaxKind.RecordStructDeclaration, Token(SyntaxKind.RecordKeyword), "Quantity")
    .WithClassOrStructKeyword(Token(SyntaxKind.StructKeyword))
    .WithModifiers(Mods(SyntaxKind.PublicKeyword, SyntaxKind.ReadOnlyKeyword))
    .WithParameterList(ParameterList(SingletonSeparatedList(Param("Value", T("decimal")))))
    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

// union: abstract record Order { sealed record Market(...) : Order; sealed record Limit(...) : Order; }
RecordDeclarationSyntax Case(string name, params ParameterSyntax[] ps) =>
    RecordDeclaration(SyntaxKind.RecordDeclaration, Token(SyntaxKind.RecordKeyword), name)
        .WithModifiers(Mods(SyntaxKind.PublicKeyword, SyntaxKind.SealedKeyword))
        .WithParameterList(ParameterList(SeparatedList(ps)))
        .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(T("Order")))))
        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

var order = RecordDeclaration(SyntaxKind.RecordDeclaration, Token(SyntaxKind.RecordKeyword), "Order")
    .WithModifiers(Mods(SyntaxKind.PublicKeyword, SyntaxKind.AbstractKeyword))
    .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
    .AddMembers(
        ConstructorDeclaration("Order").WithModifiers(Mods(SyntaxKind.PrivateKeyword)).WithBody(Block()),
        Case("Market", Param("Side", T("Side")), Param("Quantity", T("Quantity"))),
        Case("Limit", Param("Side", T("Side")), Param("Quantity", T("Quantity")), Param("Price", T("decimal"))))
    .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

// static decimal Notional(Order order, decimal last) => order switch { Order.Market m => ..., Order.Limit l => ..., _ => throw ... };
SwitchExpressionArmSyntax Arm(string type, string v, ExpressionSyntax body) =>
    SwitchExpressionArm(DeclarationPattern(T(type), SingleVariableDesignation(Identifier(v))), body);
ExpressionSyntax Mul(ExpressionSyntax l, ExpressionSyntax r) => BinaryExpression(SyntaxKind.MultiplyExpression, l, r);
ExpressionSyntax Member(string a, params string[] rest) =>
    rest.Aggregate((ExpressionSyntax)IdentifierName(a), (e, n) => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, e, IdentifierName(n)));

var notional = MethodDeclaration(T("decimal"), "Notional")
    .WithModifiers(Mods(SyntaxKind.PublicKeyword, SyntaxKind.StaticKeyword))
    .WithParameterList(ParameterList(SeparatedList(new[] { Param("order", T("Order")), Param("last", T("decimal")) })))
    .WithExpressionBody(ArrowExpressionClause(SwitchExpression(IdentifierName("order")).AddArms(
        Arm("Order.Market", "m", Mul(Member("m", "Quantity", "Value"), IdentifierName("last"))),
        Arm("Order.Limit", "l", Mul(Member("l", "Quantity", "Value"), Member("l", "Price"))),
        SwitchExpressionArm(DiscardPattern(), ThrowExpression(ObjectCreationExpression(T("System.InvalidOperationException")).WithArgumentList(ArgumentList()))))))
    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

// generic: static IReadOnlyList<TOut> Map<TIn, TOut>(IReadOnlyList<TIn> items, Func<TIn, TOut> f) => items.Select(f).ToList();
var map = MethodDeclaration(T("System.Collections.Generic.IReadOnlyList<TOut>"), "Map")
    .WithModifiers(Mods(SyntaxKind.PublicKeyword, SyntaxKind.StaticKeyword))
    .WithTypeParameterList(TypeParameterList(SeparatedList(new[] { TypeParameter("TIn"), TypeParameter("TOut") })))
    .WithParameterList(ParameterList(SeparatedList(new[] { Param("items", T("System.Collections.Generic.IReadOnlyList<TIn>")), Param("f", T("System.Func<TIn, TOut>")) })))
    .WithExpressionBody(ArrowExpressionClause(InvocationExpression(Member("System", "Linq", "Enumerable", "ToList")).AddArgumentListArguments(Argument(
        InvocationExpression(Member("System", "Linq", "Enumerable", "Select")).AddArgumentListArguments(Argument(IdentifierName("items")), Argument(IdentifierName("f")))))))
    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

var functions = ClassDeclaration("Trading").WithModifiers(Mods(SyntaxKind.PublicKeyword, SyntaxKind.StaticKeyword)).AddMembers(notional, map);

var unit = CompilationUnit()
    .AddMembers(FileScopedNamespaceDeclaration(ParseName("Acme.Trading")).AddMembers(side, quantity, order, functions))
    .WithLeadingTrivia(Comment("// <auto-generated/>"), CarriageReturnLineFeed, Trivia(NullableDirectiveTrivia(Token(SyntaxKind.EnableKeyword), true)))
    .NormalizeWhitespace(indentation: "    ", eol: "\n");

var text = unit.ToFullString();
Console.Out.Write(text);
Console.Out.Write("\n");

// Round trip: the generated text must parse with no syntax error.
var options = new CSharpParseOptions(LanguageVersion.CSharp14);
var tree = CSharpSyntaxTree.ParseText(text, options);
var syntaxErrors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
Console.Error.WriteLine($"syntax errors: {syntaxErrors.Length}");
foreach (var d in syntaxErrors) Console.Error.WriteLine(d.ToString());

#if PROBE_BIND
// Bind the generated text against the SDK targeting pack (path in argument 0).
var refs = Directory.GetFiles(args[0], "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
var compilation = CSharpCompilation.Create("Generated", [tree], refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
var bindErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
Console.Error.WriteLine($"bind errors: {bindErrors.Length}");
foreach (var d in bindErrors) Console.Error.WriteLine(d.ToString());
return syntaxErrors.Length + bindErrors.Length == 0 ? 0 : 1;
#else
return syntaxErrors.Length == 0 ? 0 : 1;
#endif
