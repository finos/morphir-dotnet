[<RequireQualifiedAccess>]
module rec Morphir.IR.Codecs.Type

open Fable.Core.JS
open Json
open Morphir.IR
open Morphir.IR.Type
open Morphir.SDK

/// Encode a type into JSON.
let rec encoder (encodeAttributes: 'a -> Value) (tpe: Type<'a>) : Encode.Value =
    match tpe with
    | Type.Unit attr -> Encode.list id [ Encode.string "Unit"; encodeAttributes attr ]
    | Type.Variable (attr, name) ->
        Encode.list id [ Encode.string "Variable"; encodeAttributes attr; Name.encoder name ]
    | Type.Reference (attributes, typeName, typeParameters) ->
        Encode.list id [
            Encode.string "Reference"
            encodeAttributes attributes
            FQName.encoder typeName
            Encode.list (encoder encodeAttributes) typeParameters
        ]
    | Type.Tuple (attr, elements) ->
        Encode.list id [
            Encode.string "Tuple"
            encodeAttributes attr
            Encode.list (encoder encodeAttributes) elements
        ]
    | Type.Record (attr, fieldTypes) ->
        Encode.list id [
            Encode.string "Record"
            encodeAttributes attr
            Encode.list (encodeField encodeAttributes) fieldTypes
        ]
    | Type.ExtensibleRecord (attributes, variableName, fieldTypes) ->
        Encode.list id [
            Encode.string "ExtensibleRecord"
            encodeAttributes attributes
            Name.encoder variableName
            Encode.list (encodeField encodeAttributes) fieldTypes
        ]
    | Type.Function (attr, argumentType, returnType) ->
        Encode.list id [
            Encode.string "Function"
            encodeAttributes attr
            encoder encodeAttributes argumentType
            encoder encodeAttributes returnType
        ]

/// Decode a type from JSON.
let rec decoder (decodeAttributes: Decode.Decoder<'a>) : Decode.Decoder<Type<'a>> =
    Decode.index 0 Decode.string
    |> Decode.andThen (
        function
        | "Variable" ->
            Decode.map2
                Type.variable
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 Name.decoder)
        | "Reference" ->
            Decode.map3
                Type.reference
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 FQName.decoder)
                (Decode.index 3 (Decode.list (decoder decodeAttributes)))
        | "Tuple" ->
            Decode.map2
                Type.tuple
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 (Decode.list (decoder decodeAttributes)))
        | "Record" ->
            Decode.map2
                Type.record
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 (Decode.list (decodeField decodeAttributes)))
        | "ExtensibleRecord" ->
            Decode.map3
                Type.extensibleRecord
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 Name.decoder)
                (Decode.index 3 (Decode.list (decodeField decodeAttributes)))
        | "Function" ->
            Decode.map3
                Type.``function``
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 (decoder decodeAttributes))
                (Decode.index 3 (decoder decodeAttributes))
        | "Unit" -> Decode.map Type.unit (Decode.index 1 decodeAttributes)
        | kind -> Decode.fail $"Unknown kind: {kind}"
    )

let encodeField encodeAttributes field : Value =
    Encode.object [ "name", Name.encoder field.Name; "tpe", encoder encodeAttributes field.Type ]

let decodeField decodeAttributes : Decode.Decoder<Field<'a>> =
    Decode.map2
        field
        (Decode.field "name" Name.Codec.decodeName)
        (Decode.field "tpe" (decoder decodeAttributes))

let encodeConstructors encodeAttributes (constructors: Constructors<'a>) : Value =
    constructors
    |> Dict.toList
    |> Encode.list (fun (ctorName, ctorArgs) ->
        Encode.list id [
            Name.encoder ctorName
            ctorArgs
            |> Encode.list (fun (ctorArgName, ctorArgType) ->
                Encode.list id [ Name.encoder ctorArgName; encoder encodeAttributes ctorArgType ]
            )
        ]
    )

let decodeConstructors decodeAttributes : Decode.Decoder<Constructors<'a>> =
    Decode.list (
        Decode.map2
            Tuple.pair
            (Decode.index 0 Name.decoder)
            (Decode.index
                1
                (Decode.list (
                    Decode.map2
                        Tuple.pair
                        (Decode.index 0 Name.decoder)
                        (Decode.index 1 (decoder decodeAttributes))
                )))
    )
    |> Decode.map Dict.fromList

let encodeSpecification encodeAttributes spec : Value =
    match spec with
    | TypeAliasSpecification (typeParams, exp) ->
        Encode.list id [
            Encode.string "TypeAliasSpecification"
            Encode.list Name.encoder typeParams
            encoder encodeAttributes exp
        ]
    | OpaqueTypeSpecification typeParams ->
        Encode.list id [
            Encode.string "OpaqueTypeSpecification"
            Encode.list Name.encoder typeParams
        ]
    | CustomTypeSpecification (typeParams, constructors) ->
        Encode.list id [
            Encode.string "CustomTypeSpecification"
            Encode.list Name.encoder typeParams
            encodeConstructors encodeAttributes constructors
        ]
    | DerivedTypeSpecification (typeParams, config) ->
        Encode.list id [
            Encode.string "DerivedTypeSpecification"
            Encode.list Name.encoder typeParams
            Encode.object [
                "baseType", encoder encodeAttributes config.BaseType
                "fromBaseType", FQName.encoder config.FromBaseType
                "toBaseType", FQName.encoder config.ToBaseType
            ]
        ]

let decodeSpecification decodeAttributes : Decode.Decoder<Specification<'a>> =
    let decodeDerivedTypeConfig =
        Decode.map3
            (fun baseType fromBaseType toBaseType -> {
                BaseType = baseType
                FromBaseType = fromBaseType
                ToBaseType = toBaseType
            }
            )
            (Decode.field "baseType" (decoder decodeAttributes))
            (Decode.field "fromBaseType" FQName.decoder)
            (Decode.field "toBaseType" FQName.decoder)

    Decode.index 0 Decode.string
    |> Decode.andThen (
        function
        | "TypeAliasSpecification" ->
            Decode.map2
                typeAliasSpecification
                (Decode.index 1 (Decode.list Name.decoder))
                (Decode.index 2 (decoder decodeAttributes))
        | "OpaqueTypeSpecification" ->
            Decode.map opaqueTypeSpecification (Decode.index 1 (Decode.list Name.decoder))
        | "CustomTypeSpecification" ->
            Decode.map2
                customTypeSpecification
                (Decode.index 1 (Decode.list Name.decoder))
                (Decode.index 2 (decodeConstructors decodeAttributes))
        | "DerivedTypeSpecification" ->
            Decode.map2
                derivedTypeSpecification
                (Decode.index 1 (Decode.list Name.decoder))
                (Decode.index 2 decodeDerivedTypeConfig)
        | kind -> Decode.fail $"Unknown kind: {kind}"
    )

let encodeDefinition encodeAttributes (definition: Definition<'a>) : Value =
    match definition with
    | TypeAliasDefinition (typeParams, exp) ->
        Encode.list id [
            Encode.string "TypeAliasDefinition"
            Encode.list Name.encoder typeParams
            encoder encodeAttributes exp
        ]
    | CustomTypeDefinition (typeParams, constructors) ->
        Encode.list id [
            Encode.string "CustomTypeDefinition"
            Encode.list Name.encoder typeParams
            AccessControlled.encoder (encodeConstructors encodeAttributes) constructors
        ]

let decodeDefinition (decodeAttributes: Decode.Decoder<'a>) : Decode.Decoder<Definition<'a>> =
    Decode.index 0 Decode.string
    |> Decode.andThen (
        function
        | "TypeAliasDefinition" ->
            Decode.map2
                typeAliasDefinition
                (Decode.index 1 (Decode.list Name.decoder))
                (Decode.index 2 (decoder decodeAttributes))
        | "CustomTypeDefinition" ->
            Decode.map2
                customTypeDefinition
                (Decode.index 1 (Decode.list Name.decoder))
                (Decode.index 2 (AccessControlled.decoder (decodeConstructors decodeAttributes)))
        | kind -> Decode.fail $"Unknown kind: {kind}"
    )
