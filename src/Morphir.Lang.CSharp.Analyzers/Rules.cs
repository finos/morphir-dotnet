// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Morphir.Lang.CSharp.Analyzers;

/// <summary>One admission rule: a refusal of the C# frontend that the compiler and the IDE also report.</summary>
public sealed class AdmissionRule
{
    internal AdmissionRule(string id, string slug, string title, string message, string example)
    {
        Id = id;
        Slug = slug;
        Title = title;
        Message = message;
        Example = example;
        Descriptor = new DiagnosticDescriptor(
            id,
            title,
            message,
            AnalyzerIds.Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            helpLinkUri: Iri);
    }

    /// <summary>The short Roslyn id, for example <c>MORPHIR1001</c>.</summary>
    public string Id { get; }

    /// <summary>The slug of the frontend code: the code is <c>csharp.unsupported_</c> and this slug.</summary>
    public string Slug { get; }

    /// <summary>The frontend diagnostic code of the rule.</summary>
    public string Code => "csharp.unsupported_" + Slug;

    /// <summary>The absolute IRI of the rule. It is the help link of the diagnostic.</summary>
    public string Iri => "https://morphir.finos.org/rules/csharp/unsupported_" + Slug;

    /// <summary>A short title.</summary>
    public string Title { get; }

    /// <summary>The message format. <c>{0}</c> is the construct that the rule found.</summary>
    public string Message { get; }

    /// <summary>A C# example that the rule refuses, as one compilation unit body for the catalog test.</summary>
    public string Example { get; }

    /// <summary>The Roslyn descriptor.</summary>
    public DiagnosticDescriptor Descriptor { get; }
}

/// <summary>The catalog of admission rules. The analyzers, the frontend and the generated coverage page read it.</summary>
public static class Rules
{
    /// <summary>A loop. Group 6 of the change lowers the loops that the recognizer accepts.</summary>
    public static readonly AdmissionRule Loop = new(
        "MORPHIR1001", "loop",
        "A loop is not in the Morphir subset",
        "A '{0}' loop is not in the Morphir subset",
        "static class M\n{\n    static int F(int[] xs)\n    {\n        foreach (var x in xs) { }\n        return 0;\n    }\n}");

    /// <summary>A change of a variable, a field or an element.</summary>
    public static readonly AdmissionRule Mutation = new(
        "MORPHIR1002", "mutation",
        "A change of a value is not in the Morphir subset",
        "A change of '{0}' is not in the Morphir subset",
        "static class M\n{\n    static int F(int a)\n    {\n        a = 1;\n        return a;\n    }\n}");

    /// <summary>The type <c>dynamic</c>.</summary>
    public static readonly AdmissionRule Dynamic = new(
        "MORPHIR1003", "dynamic",
        "dynamic is not in the Morphir subset",
        "'dynamic' is not in the Morphir subset",
        "static class M\n{\n    static int F(dynamic d) => 1;\n}");

    /// <summary>A property with a setter.</summary>
    public static readonly AdmissionRule MutableProperty = new(
        "MORPHIR1004", "mutable_property",
        "A property with a set accessor is not in the Morphir subset",
        "Property '{0}' has a 'set' accessor",
        "sealed record R\n{\n    public int X { get; set; }\n}");

    /// <summary>A symbol from an assembly that the fixed reference set does not have.</summary>
    public static readonly AdmissionRule OutsideReference = new(
        "MORPHIR1005", "outside_reference",
        "A symbol outside the fixed reference set is not in the Morphir subset",
        "'{0}' comes from the assembly '{1}', which is outside the fixed reference set",
        "static class M\n{\n    static int F() => 0;\n}");

    /// <summary>A class, struct, interface or delegate that is not a record, a union or a static class.</summary>
    public static readonly AdmissionRule TypeKind = new(
        "MORPHIR1006", "type_kind",
        "This kind of type is not in the Morphir subset",
        "The {0} '{1}' is not in the Morphir subset. Use a record, a closed record, a union, an enum or a static class.",
        "class C { }");

    /// <summary>A field that can change.</summary>
    public static readonly AdmissionRule MutableField = new(
        "MORPHIR1007", "mutable_field",
        "A field that can change is not in the Morphir subset",
        "Field '{0}' can change",
        "static class M\n{\n    public static int X;\n}");

    /// <summary>The rules, in id order.</summary>
    public static IReadOnlyList<AdmissionRule> All { get; } = ImmutableArray.Create(
        Loop, Mutation, Dynamic, MutableProperty, OutsideReference, TypeKind, MutableField);

    /// <summary>Finds a rule by its Roslyn id.</summary>
    public static bool TryGetById(string id, out AdmissionRule rule)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                rule = candidate;
                return true;
            }
        }

        rule = null!;
        return false;
    }
}
