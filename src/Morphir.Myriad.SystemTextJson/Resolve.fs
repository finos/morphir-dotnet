// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

/// A type that System.Text.Json writes as a JSON primitive.
[<RequireQualifiedAccess>]
type Primitive =
    | String
    | Bool
    | Byte
    | SByte
    | Int16
    | UInt16
    | Int32
    | UInt32
    | Int64
    | UInt64
    | Single
    | Double
    | Decimal
    | Char
    | Guid
    | DateTime
    | DateTimeOffset
    | TimeSpan
    | DateOnly
    | TimeOnly
    | JsonElement

    /// The F# spelling of the type.
    member this.FSharpName =
        match this with
        | String -> "string"
        | Bool -> "bool"
        | Byte -> "byte"
        | SByte -> "sbyte"
        | Int16 -> "int16"
        | UInt16 -> "uint16"
        | Int32 -> "int"
        | UInt32 -> "uint32"
        | Int64 -> "int64"
        | UInt64 -> "uint64"
        | Single -> "float32"
        | Double -> "float"
        | Decimal -> "decimal"
        | Char -> "char"
        | Guid -> "System.Guid"
        | DateTime -> "System.DateTime"
        | DateTimeOffset -> "System.DateTimeOffset"
        | TimeSpan -> "System.TimeSpan"
        | DateOnly -> "System.DateOnly"
        | TimeOnly -> "System.TimeOnly"
        | JsonElement -> "System.Text.Json.JsonElement"

    /// The BCL type name with a TryParse member, for numbers only.
    member this.NumberTypeName =
        match this with
        | Byte -> Some "System.Byte"
        | SByte -> Some "System.SByte"
        | Int16 -> Some "System.Int16"
        | UInt16 -> Some "System.UInt16"
        | Int32 -> Some "System.Int32"
        | UInt32 -> Some "System.UInt32"
        | Int64 -> Some "System.Int64"
        | UInt64 -> Some "System.UInt64"
        | Single -> Some "System.Single"
        | Double -> Some "System.Double"
        | Decimal -> Some "System.Decimal"
        | _ -> None

    /// True for integer types; they refuse a fraction in a string.
    member this.IsInteger =
        match this with
        | Byte | SByte | Int16 | UInt16 | Int32 | UInt32 | Int64 | UInt64 -> true
        | _ -> false

    /// A short name for generated member names.
    member this.Slug =
        match this with
        | String -> "String"
        | Bool -> "Bool"
        | Byte -> "Byte"
        | SByte -> "SByte"
        | Int16 -> "Int16"
        | UInt16 -> "UInt16"
        | Int32 -> "Int32"
        | UInt32 -> "UInt32"
        | Int64 -> "Int64"
        | UInt64 -> "UInt64"
        | Single -> "Single"
        | Double -> "Double"
        | Decimal -> "Decimal"
        | Char -> "Char"
        | Guid -> "Guid"
        | DateTime -> "DateTime"
        | DateTimeOffset -> "DateTimeOffset"
        | TimeSpan -> "TimeSpan"
        | DateOnly -> "DateOnly"
        | TimeOnly -> "TimeOnly"
        | JsonElement -> "JsonElement"

/// The kind of sequence a JSON array is read into.
[<RequireQualifiedAccess>]
type SequenceKind =
    | List
    | Array
    | Set
    | ResizeArray
    | ReadOnlyList
    | ReadOnlyCollection

/// The kind of map a JSON object or array of pairs is read into.
[<RequireQualifiedAccess>]
type MapKind =
    | Map
    | ReadOnlyDictionary

/// A field type after resolution.
[<RequireQualifiedAccess>]
type TypeRef =
    | Primitive of Primitive
    | Option of TypeRef
    | ValueOption of TypeRef
    | Sequence of SequenceKind * TypeRef
    | Map of MapKind * key: TypeRef * value: TypeRef
    | Tuple of TypeRef list
    /// A record, union or enum declared in the input file.
    | Local of TypeDeclaration
    /// A named type declared elsewhere; the options must resolve it at run time.
    | External of fsharpName: string

    /// The F# spelling of the type, for generated code.
    member this.FSharpName: string =
        match this with
        | Primitive p -> p.FSharpName
        | Option inner -> $"({inner.FSharpName}) option"
        | ValueOption inner -> $"({inner.FSharpName}) voption"
        | Sequence(SequenceKind.List, inner) -> $"({inner.FSharpName}) list"
        | Sequence(SequenceKind.Array, inner) -> $"({inner.FSharpName})[]"
        | Sequence(SequenceKind.Set, inner) -> $"Set<{inner.FSharpName}>"
        | Sequence(SequenceKind.ResizeArray, inner) -> $"ResizeArray<{inner.FSharpName}>"
        | Sequence(SequenceKind.ReadOnlyList, inner) -> $"System.Collections.Generic.IReadOnlyList<{inner.FSharpName}>"
        | Sequence(SequenceKind.ReadOnlyCollection, inner) -> $"System.Collections.Generic.IReadOnlyCollection<{inner.FSharpName}>"
        | Map(MapKind.Map, key, value) -> $"Map<{key.FSharpName}, {value.FSharpName}>"
        | Map(MapKind.ReadOnlyDictionary, key, value) -> $"System.Collections.Generic.IReadOnlyDictionary<{key.FSharpName}, {value.FSharpName}>"
        | Tuple items -> "(" + (items |> List.map (fun item -> item.FSharpName) |> String.concat " * ") + ")"
        | Local declaration -> declaration.QualifiedName
        | External name -> name

    /// A name for generated members, unique for each distinct type.
    member this.Slug: string =
        match this with
        | Primitive p -> p.Slug
        | Option inner -> "Option_" + inner.Slug
        | ValueOption inner -> "ValueOption_" + inner.Slug
        | Sequence(kind, inner) -> $"{kind}_" + inner.Slug
        | Map(kind, key, value) -> $"{kind}_{key.Slug}_{value.Slug}"
        | Tuple items -> "Tuple_" + (items |> List.map (fun item -> item.Slug) |> String.concat "_") + "_End"
        | Local declaration -> String.concat "_" (declaration.Path @ [ declaration.Name ])
        | External name -> "External_" + (name |> String.map (fun c -> if System.Char.IsLetterOrDigit c then c else '_'))

/// Resolves field types against the declarations of the input file.
module Resolve =
    /// The .NET type name of a type, for UnionFieldNamesFromTypes (Int32, String, Point).
    let clrName (t: TypeRef) =
        match t with
        | TypeRef.Primitive p ->
            match p with
            | Primitive.String -> Some "String"
            | Primitive.Bool -> Some "Boolean"
            | other -> Some other.Slug
        | TypeRef.Local declaration -> Some declaration.Name
        | TypeRef.External name -> Some((name.Split('<')[0]).Split('.') |> Array.last)
        | _ -> None

    let private primitives =
        [
            [ "string"; "String"; "System.String" ], Primitive.String
            [ "bool"; "Boolean"; "System.Boolean" ], Primitive.Bool
            [ "byte"; "uint8"; "Byte"; "System.Byte" ], Primitive.Byte
            [ "sbyte"; "int8"; "SByte"; "System.SByte" ], Primitive.SByte
            [ "int16"; "Int16"; "System.Int16" ], Primitive.Int16
            [ "uint16"; "UInt16"; "System.UInt16" ], Primitive.UInt16
            [ "int"; "int32"; "Int32"; "System.Int32" ], Primitive.Int32
            [ "uint"; "uint32"; "UInt32"; "System.UInt32" ], Primitive.UInt32
            [ "int64"; "Int64"; "System.Int64" ], Primitive.Int64
            [ "uint64"; "UInt64"; "System.UInt64" ], Primitive.UInt64
            [ "float32"; "single"; "Single"; "System.Single" ], Primitive.Single
            [ "float"; "double"; "Double"; "System.Double" ], Primitive.Double
            [ "decimal"; "Decimal"; "System.Decimal" ], Primitive.Decimal
            [ "char"; "Char"; "System.Char" ], Primitive.Char
            [ "Guid"; "System.Guid" ], Primitive.Guid
            [ "DateTime"; "System.DateTime" ], Primitive.DateTime
            [ "DateTimeOffset"; "System.DateTimeOffset" ], Primitive.DateTimeOffset
            [ "TimeSpan"; "System.TimeSpan" ], Primitive.TimeSpan
            [ "DateOnly"; "System.DateOnly" ], Primitive.DateOnly
            [ "TimeOnly"; "System.TimeOnly" ], Primitive.TimeOnly
            [ "JsonElement"; "System.Text.Json.JsonElement" ], Primitive.JsonElement
        ]
        |> List.collect (fun (spellings, primitive) -> spellings |> List.map (fun spelling -> spelling, primitive))
        |> Map.ofList

    let private sequences =
        [
            [ "list"; "List" ], SequenceKind.List
            [ "array" ], SequenceKind.Array
            [ "Set" ], SequenceKind.Set
            [ "ResizeArray" ], SequenceKind.ResizeArray
            [ "IReadOnlyList"; "System.Collections.Generic.IReadOnlyList" ], SequenceKind.ReadOnlyList
            [ "IReadOnlyCollection"; "System.Collections.Generic.IReadOnlyCollection" ], SequenceKind.ReadOnlyCollection
        ]
        |> List.collect (fun (spellings, kind) -> spellings |> List.map (fun spelling -> spelling, kind))
        |> Map.ofList

    let private maps =
        [ "Map", MapKind.Map
          "IReadOnlyDictionary", MapKind.ReadOnlyDictionary
          "System.Collections.Generic.IReadOnlyDictionary", MapKind.ReadOnlyDictionary ]
        |> Map.ofList

    /// The names that the generator reads as built-in types.
    let reservedNames =
        Seq.concat [ Seq.ofArray (Array.ofSeq primitives.Keys); Seq.ofArray (Array.ofSeq sequences.Keys); Seq.ofArray (Array.ofSeq maps.Keys); seq { "option"; "Option"; "voption"; "ValueOption" } ]
        |> Seq.filter (fun name -> not (name.Contains '.'))
        |> Set.ofSeq

    let rec private text (syntax: TypeSyntax) =
        match syntax with
        | TypeSyntax.Named(name, []) -> String.concat "." name
        | TypeSyntax.Named(name, args) -> String.concat "." name + "<" + (args |> List.map text |> String.concat ", ") + ">"
        | TypeSyntax.Array element -> text element + "[]"
        | TypeSyntax.Tuple items -> "(" + (items |> List.map text |> String.concat " * ") + ")"
        | TypeSyntax.Variable name -> name
        | TypeSyntax.WithNull inner -> text inner + " | null"
        | TypeSyntax.Unsupported range -> range

    /// The text of a type as written, for error messages.
    let describe (syntax: TypeSyntax) = text syntax

    /// Resolves one type. The error text says why the type cannot be encoded.
    let rec resolve (locals: TypeDeclaration list) (seen: string list) (syntax: TypeSyntax) : Result<TypeRef, string> =
        let all (items: TypeSyntax list) =
            let results = items |> List.map (resolve locals seen)

            match results |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
            | Some error -> Error error
            | None -> Ok(results |> List.map (function Ok r -> r | Error _ -> failwith "unreachable"))

        let local (name: string list) =
            locals
            |> List.tryFind (fun declaration ->
                let full = declaration.Path @ [ declaration.Name ]
                full.Length >= name.Length && List.skip (full.Length - name.Length) full = name)

        match syntax with
        | TypeSyntax.Named(name, args) ->
            let dotted = String.concat "." name

            match local name, args with
            | Some declaration, _ when not declaration.Parameters.IsEmpty ->
                Error $"the generic type '{declaration.Name}' is not supported; declare a non-generic type or an abbreviation of a closed type."
            | Some declaration, [] ->
                match declaration.Shape with
                | TypeShape.Abbreviation target ->
                    if List.contains declaration.Name seen then
                        Error $"the type abbreviation '{declaration.Name}' refers to itself."
                    else
                        resolve locals (declaration.Name :: seen) target
                | TypeShape.Record _
                | TypeShape.Union _
                | TypeShape.Enum _ -> Ok(TypeRef.Local declaration)
                | TypeShape.Other -> Error $"the type '{declaration.Name}' is not a record, union, enum or abbreviation."
            | Some declaration, _ -> Error $"the type '{declaration.Name}' takes no type arguments."
            | None, [] when primitives.ContainsKey dotted -> Ok(TypeRef.Primitive primitives[dotted])
            | None, [ inner ] when dotted = "option" || dotted = "Option" ->
                resolve locals seen inner
                |> Result.bind (function
                    | TypeRef.Option _
                    | TypeRef.ValueOption _ -> Error "an option of an option has no JSON form that keeps both levels."
                    | inner -> Ok(TypeRef.Option inner))
            | None, [ inner ] when dotted = "voption" || dotted = "ValueOption" ->
                resolve locals seen inner
                |> Result.bind (function
                    | TypeRef.Option _
                    | TypeRef.ValueOption _ -> Error "an option of an option has no JSON form that keeps both levels."
                    | inner -> Ok(TypeRef.ValueOption inner))
            | None, [ inner ] when sequences.ContainsKey dotted -> resolve locals seen inner |> Result.map (fun inner -> TypeRef.Sequence(sequences[dotted], inner))
            | None, [ key; value ] when maps.ContainsKey dotted ->
                all [ key; value ] |> Result.map (fun parts -> TypeRef.Map(maps[dotted], parts[0], parts[1]))
            | None, _ when dotted = "seq" || dotted = "Seq" || dotted = "IEnumerable" ->
                Error "seq has no JSON form that can be read back; use list, array or ResizeArray."
            | None, _ -> Ok(TypeRef.External(text syntax))
        | TypeSyntax.Array element -> resolve locals seen element |> Result.map (fun inner -> TypeRef.Sequence(SequenceKind.Array, inner))
        | TypeSyntax.Tuple items -> all items |> Result.map TypeRef.Tuple
        | TypeSyntax.Variable name -> Error $"the generic parameter '{name}' is not supported."
        | TypeSyntax.WithNull inner -> Error $"'{text inner} | null' is not supported; use an option."
        | TypeSyntax.Unsupported range -> Error $"the type at {range} has a form that the generator does not read."
