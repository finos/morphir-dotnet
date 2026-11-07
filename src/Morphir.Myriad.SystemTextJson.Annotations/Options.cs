// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;

namespace Morphir.Myriad.SystemTextJson;

/// <summary>
/// How a union is written as JSON. Combine one base encoding with flags, or use a preset. The names, the values and
/// the meanings are those of FSharp.SystemTextJson's <c>JsonUnionEncoding</c>, so the same options give the same JSON.
/// </summary>
[Flags]
public enum JsonUnionEncoding
{
    /// <summary>Base encoding: <c>{"Case":"Name","Fields":[...]}</c>.</summary>
    AdjacentTag = 0x1,

    /// <summary>Base encoding: <c>{"Name":[...]}</c>.</summary>
    ExternalTag = 0x2,

    /// <summary>Base encoding: <c>["Name",...]</c>, or <c>{"Case":"Name",...}</c> with <see cref="NamedFields"/>.</summary>
    InternalTag = 0x4,

    /// <summary>Write the fields as an object with one member for each field, not as an array.</summary>
    NamedFields = 0x100,

    /// <summary>Base encoding: the fields only, as an object, with no case name. Includes <see cref="NamedFields"/>.</summary>
    Untagged = 0x8 | NamedFields,

    /// <summary>Write a case with no field as the string of its name.</summary>
    UnwrapFieldlessTags = 0x200,

    /// <summary>Write <c>None</c> as <c>null</c> and <c>Some x</c> as <c>x</c>.</summary>
    UnwrapOption = 0x400,

    /// <summary>Write a union with one case and one field as that field.</summary>
    UnwrapSingleCaseUnions = 0x800,

    /// <summary>With <see cref="AdjacentTag"/> or <see cref="ExternalTag"/>: write the single field of a case without the array or object around it.</summary>
    UnwrapSingleFieldCases = 0x1000,

    /// <summary>Write the members of a case's single record field in place of the field. Includes <see cref="NamedFields"/>.</summary>
    UnwrapRecordCases = 0x2000 | NamedFields,

    /// <summary>When reading, accept the case name after other members.</summary>
    AllowUnorderedTag = 0x4000,

    /// <summary>Name fields that have no name after their type, such as <c>Int32</c> or <c>Point</c>.</summary>
    UnionFieldNamesFromTypes = 0x8000,

    /// <summary>
    /// With <see cref="Untagged"/>: choose the case by the JSON token kind of the value, and write the value of the
    /// case's one field alone. No two cases may share a token kind. Not part of FSharp.SystemTextJson.
    /// </summary>
    SelectByTokenKind = 0x10000,

    /// <summary>Preset: <see cref="AdjacentTag"/>, <see cref="UnwrapOption"/>, <see cref="UnwrapSingleCaseUnions"/>, <see cref="AllowUnorderedTag"/>.</summary>
    Default = AdjacentTag | UnwrapOption | UnwrapSingleCaseUnions | AllowUnorderedTag,

    /// <summary>Preset like Newtonsoft.Json: <see cref="AdjacentTag"/>, <see cref="AllowUnorderedTag"/>.</summary>
    NewtonsoftLike = AdjacentTag | AllowUnorderedTag,

    /// <summary>Preset like Thoth.Json: <see cref="InternalTag"/>, <see cref="UnwrapFieldlessTags"/>, <see cref="AllowUnorderedTag"/>.</summary>
    ThothLike = InternalTag | UnwrapFieldlessTags | AllowUnorderedTag,

    /// <summary>
    /// Preset like FSharpLu.Json: <see cref="ExternalTag"/>, <see cref="UnwrapFieldlessTags"/>, <see cref="UnwrapOption"/>,
    /// <see cref="UnwrapSingleFieldCases"/>, <see cref="AllowUnorderedTag"/>.
    /// </summary>
    FSharpLuLike = ExternalTag | UnwrapFieldlessTags | UnwrapOption | UnwrapSingleFieldCases | AllowUnorderedTag,
}

/// <summary>How member names, case names and field names are made from F# names.</summary>
public enum JsonNaming
{
    /// <summary>The F# name as it is.</summary>
    AsIs = 0,

    /// <summary><c>camelCase</c>.</summary>
    CamelCase,

    /// <summary><c>PascalCase</c>.</summary>
    PascalCase,

    /// <summary><c>snake_case</c>.</summary>
    SnakeCaseLower,

    /// <summary><c>SNAKE_CASE</c>.</summary>
    SnakeCaseUpper,

    /// <summary><c>kebab-case</c>.</summary>
    KebabCaseLower,

    /// <summary><c>KEBAB-CASE</c>.</summary>
    KebabCaseUpper,
}

/// <summary>How an F# map or dictionary is written.</summary>
public enum JsonMapFormat
{
    /// <summary>An object when the key is a string or a parsed value, otherwise an array of pairs.</summary>
    ObjectOrArrayOfPairs = 0,

    /// <summary>Always an object. The key must be a string or a parsed value.</summary>
    Object,

    /// <summary>Always an array of <c>[key, value]</c> pairs.</summary>
    ArrayOfPairs,
}

/// <summary>What a reader does with an object member that the type does not declare.</summary>
public enum JsonUnknownMembers
{
    /// <summary>Skip the member.</summary>
    Ignore = 0,

    /// <summary>Fail with a <c>JsonException</c> that names the member.</summary>
    Refuse,
}

/// <summary>When an absent option is left out of the JSON.</summary>
public enum JsonSkippableOptionFields
{
    /// <summary>Write <c>null</c> for <c>None</c>. A missing member reads as <c>None</c>.</summary>
    Never = 0,

    /// <summary>Leave out a member whose value is <c>None</c> or <c>ValueNone</c>.</summary>
    Always,
}
