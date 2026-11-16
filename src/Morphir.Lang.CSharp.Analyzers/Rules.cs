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
            helpLinkUri: Iri,
            // A rule says that a construct cannot become a model, so no pragma, .editorconfig entry or NoWarn turns it
            // off. A [SuppressMessage] attribute is refused by its own rule.
            customTags: WellKnownDiagnosticTags.NotConfigurable);
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
    /// <summary>
    /// A loop that the loop recognizer refuses. The frontend lowers the other loops to a list function or a recursion.
    /// <c>{0}</c> is the keyword of the loop and <c>{1}</c> is the reason.
    /// </summary>
    public static readonly AdmissionRule Loop = new(
        "MORPHIR1001", "loop",
        "A loop with no Morphir form is not in the Morphir subset",
        "The '{0}' loop is not in the Morphir subset: it {1}",
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

    /// <summary>
    /// A <c>SuppressMessage</c> attribute that names a Morphir rule. Roslyn lets this attribute hide a rule that is not
    /// configurable, so the attribute itself is refused. <c>morphir compile</c> refuses the hidden construct all the same.
    /// </summary>
    public static readonly AdmissionRule Suppression = new(
        "MORPHIR1008", "suppression",
        "A suppression of a Morphir rule is not in the Morphir subset",
        "The attribute suppresses the Morphir rule '{0}'. A construct that a Morphir rule refuses cannot become a model; change the construct.",
        "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Morphir\", \"MORPHIR1001\")]\nstatic class M\n{\n    public static int F() => 1;\n}");

    /// <summary>
    /// An older encoding of a sum type: an <c>abstract record</c> (with a private constructor and <c>sealed record</c>
    /// cases) that is not <c>closed</c>. The preferred form is a <c>closed record</c>, and the code fix of
    /// <c>Morphir.Lang.CSharp.CodeFixes</c> changes the older form to it. A <c>closed record</c> and a union are not
    /// reported.
    /// </summary>
    public static readonly AdmissionRule OlderSumType = new(
        "MORPHIR1009", "abstract_record",
        "An older encoding of a sum type is not in the Morphir subset",
        "The abstract record '{0}' is an older encoding of a sum type. Write 'closed record {0}' with 'sealed record' cases.",
        "public abstract record Shape\n{\n    private Shape() { }\n    public sealed record Circle(double Radius) : Shape;\n    public sealed record Rect(double W, double H) : Shape;\n}");

    /// <summary>
    /// A record or a union with IEEE equality from a <c>MorphirEquality</c> attribute that is not <c>partial</c>. An
    /// attribute cannot change how compiled C# compares two records, so the equality members are added to the type, and
    /// the type must be <c>partial</c>.
    /// </summary>
    public static readonly AdmissionRule NonPartialEquality = new(
        "MORPHIR1010", "non_partial_equality",
        "A type with IEEE equality must be partial",
        "The type '{0}' has IEEE equality from a MorphirEquality attribute and is not partial. Add 'partial' to the type, because the equality members are added to it.",
        "[Morphir.MorphirEquality(Morphir.FloatEquality.Ieee)]\npublic sealed record Quote(double Value);");

    /// <summary>The rules, in id order.</summary>
    public static IReadOnlyList<AdmissionRule> All { get; } = ImmutableArray.Create(
        Loop, Mutation, Dynamic, MutableProperty, OutsideReference, TypeKind, MutableField, Suppression, OlderSumType,
        NonPartialEquality);

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
