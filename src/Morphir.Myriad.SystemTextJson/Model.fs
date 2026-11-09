// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

/// A place in the input file, for error messages. Lines start at 1, columns at 1.
type Location = { Line: int; Column: int }

/// A literal value of an attribute argument.
[<RequireQualifiedAccess>]
type AttributeValue =
    | Text of string
    | Bool of bool
    | Int of int
    /// An integer or finite floating-point literal, encoded as an invariant JSON number.
    | Number of string
    /// A dotted name, such as <c>JsonNaming.CamelCase</c>, or names joined by <c>|||</c>.
    | Names of string list
    /// The type in <c>typeof&lt;T&gt;</c>, as written.
    | TypeOf of string
    /// An expression that the generator does not read.
    | Other

/// One attribute as written: its short name (no <c>Attribute</c> suffix), positional and named arguments.
type AttributeUse =
    {
        Name: string
        Positional: AttributeValue list
        Named: (string * AttributeValue) list
        Location: Location
    }

/// A type expression as written in the input, before resolution.
[<RequireQualifiedAccess>]
type TypeSyntax =
    /// A dotted name with optional type arguments, such as <c>int</c>, <c>System.Guid</c>, <c>Map&lt;string, int&gt;</c>.
    | Named of name: string list * arguments: TypeSyntax list
    | Array of element: TypeSyntax
    | Tuple of elements: TypeSyntax list
    /// A generic parameter such as <c>'T</c>.
    | Variable of string
    /// <c>T | null</c>.
    | WithNull of TypeSyntax
    | Unsupported of text: string

/// A field of a record or of a union case.
type FieldSyntax =
    {
        /// The F# name; None for an unnamed union case field.
        Name: string option
        Type: TypeSyntax
        Attributes: AttributeUse list
        Location: Location
    }

/// A union case.
type CaseSyntax =
    {
        Name: string
        Fields: FieldSyntax list
        Attributes: AttributeUse list
        Location: Location
    }

/// The representation of a declared type.
[<RequireQualifiedAccess>]
type TypeShape =
    /// A record. <c>IsPrivate</c> is true for <c>private { ... }</c>.
    | Record of fields: FieldSyntax list * isPrivate: bool
    /// A union. <c>IsPrivate</c> is true for <c>private | ...</c>.
    | Union of cases: CaseSyntax list * isPrivate: bool
    /// An enum with its case names and values.
    | Enum of cases: (string * int64) list
    /// <c>type X = T</c>.
    | Abbreviation of TypeSyntax
    /// A class, interface or other type the generator does not encode.
    | Other

/// A type declared in the input file.
type TypeDeclaration =
    {
        /// The type name.
        Name: string
        /// The namespace and module path that holds the type.
        Path: string list
        /// The generic parameters, empty for a non-generic type.
        Parameters: string list
        Shape: TypeShape
        Attributes: AttributeUse list
        /// True when the type itself is private or internal to its file.
        IsPrivate: bool
        Location: Location
    }

    /// The fully qualified F# name, for generated code.
    member this.QualifiedName = "global." + String.concat "." (this.Path @ [ this.Name ])

/// What the generator reads from one input file.
type SourceFile =
    {
        /// The file name, for error messages.
        File: string
        /// The namespaces and top-level modules that the input declares, opened by the generated file.
        Scopes: string list
        /// The open declarations of the input, as written (for example "System" or "type System.Math").
        Opens: string list
        /// Every type declared in the file, in order.
        Types: TypeDeclaration list
    }
