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
/// On an abbreviation of a type from another assembly (<c>type IdText = ExtensionId</c>), it makes that type
/// a parsed value in the context.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
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

/// <summary>Supplies a scalar value for an absent record member. Explicit null keeps the field's normal rules.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonDefaultValueAttribute : Attribute
{
    /// <summary>Creates a string default, without JSON quoting.</summary>
    public JsonDefaultValueAttribute(string value) => Value = value;
    /// <summary>Creates a Boolean default.</summary>
    public JsonDefaultValueAttribute(bool value) => Value = value;
    /// <summary>Creates an 8-bit unsigned integer default.</summary>
    public JsonDefaultValueAttribute(byte value) => Value = value;
    /// <summary>Creates an 8-bit signed integer default.</summary>
    public JsonDefaultValueAttribute(sbyte value) => Value = value;
    /// <summary>Creates a 16-bit signed integer default.</summary>
    public JsonDefaultValueAttribute(short value) => Value = value;
    /// <summary>Creates a 16-bit unsigned integer default.</summary>
    public JsonDefaultValueAttribute(ushort value) => Value = value;
    /// <summary>Creates a 32-bit signed integer default.</summary>
    public JsonDefaultValueAttribute(int value) => Value = value;
    /// <summary>Creates a 32-bit unsigned integer default.</summary>
    public JsonDefaultValueAttribute(uint value) => Value = value;
    /// <summary>Creates a 64-bit signed integer default.</summary>
    public JsonDefaultValueAttribute(long value) => Value = value;
    /// <summary>Creates a 64-bit unsigned integer default.</summary>
    public JsonDefaultValueAttribute(ulong value) => Value = value;
    /// <summary>Creates a single-precision floating-point default.</summary>
    public JsonDefaultValueAttribute(float value) => Value = value;
    /// <summary>Creates a double-precision floating-point default.</summary>
    public JsonDefaultValueAttribute(double value) => Value = value;

    /// <summary>The scalar value used when the member is absent.</summary>
    public object Value { get; }
}

/// <summary>Supplies an explicit JSON-literal default read through the generated field codec.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonDefaultJsonAttribute : Attribute
{
    /// <summary>Creates a missing-member default from one complete JSON value.</summary>
    public JsonDefaultJsonAttribute(string json) => Json = json;
    /// <summary>The JSON literal read when the member is absent.</summary>
    public string Json { get; }
}

/// <summary>Supplies an empty collection for an absent record member.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonDefaultEmptyAttribute : Attribute
{
}

/// <summary>Calls a static no-argument method when a record member is absent.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonDefaultValueProviderAttribute : Attribute
{
    /// <summary>Names the provider type and method. Use nameof for the method name.</summary>
    public JsonDefaultValueProviderAttribute(Type providerType, string methodName)
    {
        ProviderType = providerType;
        MethodName = methodName;
    }
    /// <summary>The type declaring the accessible static method.</summary>
    public Type ProviderType { get; }
    /// <summary>The method called directly by generated code.</summary>
    public string MethodName { get; }
}

/// <summary>Omits an empty record collection on writing. It does not change missing-member or null rules.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonOmitEmptyAttribute : Attribute
{
}

/// <summary>
/// Omits a Boolean record field on writing when it equals its <see cref="JsonDefaultValueAttribute"/>. The field
/// needs that default, so an absent member reads back as the same value.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonOmitDefaultAttribute : Attribute
{
}

/// <summary>
/// Reads one legacy spelling of a record's version member and normalizes it to the current version.
/// The current JSON member name comes from the field's normal naming rules.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonVersionAliasAttribute : Attribute
{
    /// <summary>Declares the legacy member name and exact legacy/current version values.</summary>
    public JsonVersionAliasAttribute(string legacyName, string legacyVersion, string currentVersion)
    {
        LegacyName = legacyName;
        LegacyVersion = legacyVersion;
        CurrentVersion = currentVersion;
    }

    /// <summary>The member name used only by the legacy version.</summary>
    public string LegacyName { get; }

    /// <summary>The exact legacy version value.</summary>
    public string LegacyVersion { get; }

    /// <summary>The canonical current version value.</summary>
    public string CurrentVersion { get; }

    /// <summary>The JSON name of a string collection whose exact version-member path is migrated.</summary>
    public string? CriticalMember { get; set; }
}

/// <summary>Requires every path in a record's string collection to match one of these exact paths.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class JsonKnownPathsAttribute : Attribute
{
    /// <summary>Declares the exact paths. A known parent does not allow unknown children.</summary>
    public JsonKnownPathsAttribute(params string[] paths) => Paths = paths;

    /// <summary>The paths accepted by the reader and writer.</summary>
    public string[] Paths { get; }
}
