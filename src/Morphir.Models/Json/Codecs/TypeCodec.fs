namespace Morphir.Json.Codecs

open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Versioning
open Morphir.Json

/// <summary>
/// TypeCodec provides generic encoding and decoding for Type<'attributes>.
/// This uses the Thoth-inspired composable encoder/decoder pattern with version-aware tag naming.
/// The codec is parameterized by attribute encoders/decoders, making it fully generic.
/// </summary>
module TypeCodec =

    /// <summary>
    /// Tag names for v2+ format (PascalCase).
    /// </summary>
    module Tags =
        let variable = "Variable"
        let reference = "Reference"
        let tuple = "Tuple"
        let record = "Record"
        let extensibleRecord = "ExtensibleRecord"
        let func = "Function"
        let unit = "Unit"

    /// <summary>
    /// Gets the tag for a type based on format version.
    /// </summary>
    let private getTag (version: FormatVersion) (baseName: string) : string =
        TagNaming.typeTag version baseName

    /// <summary>
    /// Normalizes a tag for comparison (handles both v1 and v2+ formats).
    /// </summary>
    let private normalizeTag (tag: string) : string =
        tag.ToLowerInvariant()

    /// <summary>
    /// Generic encoder for Type<'attributes> using JsonElement-based composable approach.
    /// Takes an encoder for the attribute type and returns an encoder for Type<'attributes>.
    /// </summary>
    let rec encodeWithOptions<'attributes> (options: MorphirJsonOptions) (encodeAttrs: Encoder<'attributes>) (typ: Type<'attributes>) : JsonElement =
        let version = options.FormatVersion
        let encodeType = encodeWithOptions options encodeAttrs  // Recursive reference

        match typ with
        | Variable(attrs, name) ->
            Encode.arrayOfElements [
                Encode.string (getTag version Tags.variable)
                encodeAttrs attrs
                NameCodec.encodeWithOptions options name
            ]

        | Reference(attrs, fqName, typeArgs) ->
            Encode.arrayOfElements [
                Encode.string (getTag version Tags.reference)
                encodeAttrs attrs
                FQNameCodec.encodeWithOptions options fqName
                Encode.list encodeType typeArgs
            ]

        | Tuple(attrs, elementTypes) ->
            Encode.arrayOfElements [
                Encode.string (getTag version Tags.tuple)
                encodeAttrs attrs
                Encode.list encodeType elementTypes
            ]

        | Record(attrs, fields) ->
            let encodeField (field: Field<'attributes>) : JsonElement =
                Encode.arrayOfElements [
                    NameCodec.encodeWithOptions options field.Name
                    encodeType field.Type
                ]

            Encode.arrayOfElements [
                Encode.string (getTag version Tags.record)
                encodeAttrs attrs
                Encode.list encodeField fields
            ]

        | ExtensibleRecord(attrs, varName, fields) ->
            let encodeField (field: Field<'attributes>) : JsonElement =
                Encode.arrayOfElements [
                    NameCodec.encodeWithOptions options field.Name
                    encodeType field.Type
                ]

            Encode.arrayOfElements [
                Encode.string (getTag version Tags.extensibleRecord)
                encodeAttrs attrs
                NameCodec.encodeWithOptions options varName
                Encode.list encodeField fields
            ]

        | Function(attrs, argType, returnType) ->
            Encode.arrayOfElements [
                Encode.string (getTag version Tags.func)
                encodeAttrs attrs
                encodeType argType
                encodeType returnType
            ]

        | Unit attrs ->
            Encode.arrayOfElements [
                Encode.string (getTag version Tags.unit)
                encodeAttrs attrs
            ]

    /// <summary>
    /// Generic encoder for Type<'attributes> using default options (v3).
    /// Takes an encoder for the attribute type and returns an encoder for Type<'attributes>.
    /// </summary>
    let rec encode<'attributes> (encodeAttrs: Encoder<'attributes>) (typ: Type<'attributes>) : JsonElement =
        encodeWithOptions MorphirJsonOptions.defaultOptions encodeAttrs typ

    /// <summary>
    /// Generic decoder for Type<'attributes> using JsonElement-based composable approach.
    /// Takes a decoder for the attribute type and returns a decoder for Type<'attributes>.
    /// </summary>
    let rec decodeWithOptions<'attributes> (options: MorphirJsonOptions) (decodeAttrs: Decoder<'attributes>) (element: JsonElement) : Result<Type<'attributes>, string> =
        let decodeType = decodeWithOptions options decodeAttrs  // Recursive reference

        // Decode tag and attributes from array
        match Decode.index 0 Decode.string element with
        | Error msg -> Error msg
        | Ok tag ->
            match Decode.index 1 decodeAttrs element with
            | Error msg -> Error msg
            | Ok attrs ->
                let normalizedTag = normalizeTag tag
                match normalizedTag with
                | "variable" ->
                    match Decode.index 2 (NameCodec.decodeWithOptions options) element with
                    | Error msg -> Error msg
                    | Ok name -> Ok (Variable(attrs, name))

                | "reference" ->
                    match Decode.index 2 (FQNameCodec.decodeWithOptions options) element with
                    | Error msg -> Error msg
                    | Ok fqName ->
                        match Decode.index 3 (Decode.list decodeType) element with
                        | Error msg -> Error msg
                        | Ok typeArgs -> Ok (Reference(attrs, fqName, typeArgs))

                | "tuple" ->
                    match Decode.index 2 (Decode.list decodeType) element with
                    | Error msg -> Error msg
                    | Ok elementTypes -> Ok (Tuple(attrs, elementTypes))

                | "record" ->
                    let decodeField (fieldElement: JsonElement) : Result<Field<'attributes>, string> =
                        match Decode.index 0 (NameCodec.decodeWithOptions options) fieldElement with
                        | Error msg -> Error msg
                        | Ok name ->
                            match Decode.index 1 decodeType fieldElement with
                            | Error msg -> Error msg
                            | Ok typ -> Ok { Name = name; Type = typ }

                    match Decode.index 2 (Decode.list decodeField) element with
                    | Error msg -> Error msg
                    | Ok fields -> Ok (Record(attrs, fields))

                | "extensiblerecord" ->
                    let decodeField (fieldElement: JsonElement) : Result<Field<'attributes>, string> =
                        match Decode.index 0 (NameCodec.decodeWithOptions options) fieldElement with
                        | Error msg -> Error msg
                        | Ok name ->
                            match Decode.index 1 decodeType fieldElement with
                            | Error msg -> Error msg
                            | Ok typ -> Ok { Name = name; Type = typ }

                    match Decode.index 2 (NameCodec.decodeWithOptions options) element with
                    | Error msg -> Error msg
                    | Ok varName ->
                        match Decode.index 3 (Decode.list decodeField) element with
                        | Error msg -> Error msg
                        | Ok fields -> Ok (ExtensibleRecord(attrs, varName, fields))

                | "function" ->
                    match Decode.index 2 decodeType element with
                    | Error msg -> Error msg
                    | Ok argType ->
                        match Decode.index 3 decodeType element with
                        | Error msg -> Error msg
                        | Ok returnType -> Ok (Function(attrs, argType, returnType))

                | "unit" ->
                    Ok (Unit attrs)

                | _ ->
                    Error $"Unknown Type tag: {tag}"

    /// <summary>
    /// Generic decoder for Type<'attributes> using default options (v3).
    /// Takes a decoder for the attribute type and returns a decoder for Type<'attributes>.
    /// </summary>
    let rec decode<'attributes> (decodeAttrs: Decoder<'attributes>) (element: JsonElement) : Result<Type<'attributes>, string> =
        decodeWithOptions MorphirJsonOptions.defaultOptions decodeAttrs element

    /// <summary>
    /// Creates a specialized encoder for a specific attribute type.
    /// Example: let rawTypeEncoder = TypeCodec.encoder Encode.unit
    /// </summary>
    let encoder<'attributes> (encodeAttrs: Encoder<'attributes>) : Encoder<Type<'attributes>> =
        encode encodeAttrs

    /// <summary>
    /// Creates a specialized decoder for a specific attribute type.
    /// Example: let rawTypeDecoder = TypeCodec.decoder Decode.unit
    /// </summary>
    let decoder<'attributes> (decodeAttrs: Decoder<'attributes>) : Decoder<Type<'attributes>> =
        decode decodeAttrs
