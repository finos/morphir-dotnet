// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Morphir.Lang.CSharp.Analyzers;

/// <summary>The rule of the equality option: a type with IEEE equality from an attribute is <c>partial</c>.</summary>
public sealed partial class AdmissionAnalyzer
{
    /// <summary>The value of <c>FloatEquality.Ieee</c> in the attribute assembly.</summary>
    private const int Ieee = 1;

    /// <summary>The value of the <c>MorphirEquality</c> attribute of a symbol, or null when the symbol has none.</summary>
    private static int? EqualityOf(ISymbol symbol) =>
        symbol.GetAttributes()
            .Where(attribute => attribute.AttributeClass is { Name: "MorphirEqualityAttribute", ContainingNamespace: { Name: "Morphir", ContainingNamespace.IsGlobalNamespace: true } })
            .Select(attribute => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int value ? value : (int?)null)
            .FirstOrDefault();

    /// <summary>
    /// The equality of a type from the nearest attribute: the type, a base closed record, an enclosing type, the assembly.
    /// </summary>
    private static int? TypeEquality(INamedTypeSymbol type)
    {
        for (ISymbol? scope = type; scope is INamedTypeSymbol named; scope = named.BaseType is { IsClosed: true } closedBase ? closedBase : named.ContainingType)
        {
            if (EqualityOf(scope) is { } value)
            {
                return value;
            }
        }

        return EqualityOf(type.ContainingAssembly);
    }

    private static void ReportNonPartialEquality(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.IsRecord && !type.IsUnion)
        {
            return;
        }

        var memberIeee = type.GetMembers().OfType<IPropertySymbol>().Any(property => EqualityOf(property) == Ieee)
            || type.InstanceConstructors.SelectMany(constructor => constructor.Parameters).Any(parameter => EqualityOf(parameter) == Ieee);
        if (TypeEquality(type) != Ieee && !memberIeee)
        {
            return;
        }

        var declarations = type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(context.CancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .ToList();
        if (declarations.Count == 0 || declarations.Any(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rules.NonPartialEquality.Descriptor, declarations[0].Identifier.GetLocation(), type.Name));
    }
}
