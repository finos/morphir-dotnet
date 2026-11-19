using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
Console.WriteLine("LanguageVersion: " + string.Join(",", Enum.GetNames<LanguageVersion>().Where(n => n.Contains("1") && n.Length >= 8)) + " | Latest=" + LanguageVersion.Latest.MapSpecifiedToEffectiveVersion() + " Default=" + LanguageVersion.Default.MapSpecifiedToEffectiveVersion());
Console.WriteLine("SyntaxKind: " + string.Join(",", Enum.GetNames<SyntaxKind>().Where(n => n.Contains("Union") || n.Contains("Closed"))));
Console.WriteLine("SyntaxFactory: " + string.Join(",", typeof(SyntaxFactory).GetMethods().Select(m => m.Name).Where(n => n.Contains("Union") || n.Contains("Closed")).Distinct()));
foreach (var t in new[] { typeof(INamedTypeSymbol), typeof(ITypeSymbol), typeof(ISymbol) })
    Console.WriteLine(t.Name + ": " + string.Join(",", t.GetProperties().Select(p => p.Name).Where(n => n.Contains("Union") || n.Contains("Closed") || n.Contains("Case"))));
Console.WriteLine("TypeKind: " + string.Join(",", Enum.GetNames<TypeKind>()));
var source = File.ReadAllText(args[0]);
var tree = CSharpSyntaxTree.ParseText(source);
var refs = Directory.GetFiles(args[1], "*.dll").Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
var comp = CSharpCompilation.Create("M", [tree], refs, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
Console.WriteLine("errors: " + comp.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error));
var model = comp.GetSemanticModel(tree);
foreach (var decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
{
    var s = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
    Console.WriteLine($"{s.Name,-8} syntax={decl.Kind(),-22} typeKind={s.TypeKind,-8} abstract={s.IsAbstract} sealed={s.IsSealed} record={s.IsRecord} mods=[{decl.Modifiers}] base={s.BaseType?.Name} ifaces=[{string.Join(",", s.Interfaces.Select(i => i.Name))}] attrs=[{string.Join(",", s.GetAttributes().Select(a => a.AttributeClass?.Name))}]");
}
