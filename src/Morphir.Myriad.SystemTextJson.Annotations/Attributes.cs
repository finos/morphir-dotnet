// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;

namespace Morphir.Myriad.SystemTextJson;

// The generator reads these attributes from the F# syntax tree at build time; nothing reads them at run time.
// Attribute arguments must be literals, so the generator can read them without type checking.

/// <summary>Sets the JSON encoding of one union. Unset members keep the context defaults.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class JsonUnionAttribute : Attribute
{
    /// <summary>The base encoding with its flags, or a preset.</summary>
    public JsonUnionEncoding BaseEncoding { get; set; }

    /// <summary>The name of the case member. The default is <c>Case</c>.</summary>
    public string? TagName { get; set; }

    /// <summary>The name of the fields member for <see cref="JsonUnionEncoding.AdjacentTag"/>. The default is <c>Fields</c>.</summary>
    public string? FieldsName { get; set; }

    /// <summary>How case names are made from F# case names.</summary>
    public JsonNaming TagNaming { get; set; }

    /// <summary>How field names are made from F# field names.</summary>
    public JsonNaming FieldNaming { get; set; }

    /// <summary>What a reader does with an unknown member of a case object.</summary>
    public JsonUnknownMembers UnknownMembers { get; set; }

    /// <summary>When true, a reader matches case names with no regard to letter case.</summary>
    public bool TagCaseInsensitive { get; set; }
}

/// <summary>Sets the member rules of one record. Unset members keep the context defaults.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class JsonRecordAttribute : Attribute
{
    /// <summary>How member names are made from F# field names.</summary>
    public JsonNaming Naming { get; set; }

    /// <summary>When an absent option is left out.</summary>
    public JsonSkippableOptionFields SkippableOptionFields { get; set; }

    /// <summary>What a reader does with an unknown member.</summary>
    public JsonUnknownMembers UnknownMembers { get; set; }

    /// <summary>When true, a reader accepts <c>null</c> for a field that is not an option.</summary>
    public bool AllowNullFields { get; set; }
}

/// <summary>
/// Sets the JSON name of a union case or a record field. The first name is written; every name is accepted
/// when reading. On a case, <see cref="Field"/> names one of the case's fields instead of the case.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class JsonNameAttribute : Attribute
{
    /// <summary>Creates the attribute with one or more names.</summary>
    public JsonNameAttribute(string name, params string[] otherNames)
    {
        Name = name;
        OtherNames = otherNames;
    }

    /// <summary>The name that is written.</summary>
    public string Name { get; }

    /// <summary>More names that a reader accepts.</summary>
    public string[] OtherNames { get; }

    /// <summary>On a union case: the F# name of the case field that this attribute names.</summary>
    public string? Field { get; set; }
}

/// <summary>
/// Marks a type whose values come only from a parser, such as a single-case union with a private case.
/// A writer writes the text member; a reader calls the parse member and never builds an invalid value.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class JsonParsedValueAttribute : Attribute
{
    /// <summary>
    /// The static parse member. Its type is <c>string -&gt; Result&lt;T, 'E&gt;</c>, or C#-style
    /// <c>TryParse(string, out T)</c> when <see cref="UsesTryParse"/> is true. The default is <c>Parse</c>.
    /// </summary>
    public string ParseMember { get; set; } = "Parse";

    /// <summary>True when <see cref="ParseMember"/> is a <c>TryParse</c> member with an out value.</summary>
    public bool UsesTryParse { get; set; }

    /// <summary>The instance member that gives the text. The default is <c>ToString</c>.</summary>
    public string TextMember { get; set; } = "ToString";
}

/// <summary>Leaves a type out of the generated context, also when a context member refers to it.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, AllowMultiple = false)]
public sealed class JsonIgnoreTypeAttribute : Attribute
{
}
