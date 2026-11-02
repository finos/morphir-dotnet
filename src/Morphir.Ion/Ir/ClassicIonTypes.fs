namespace Morphir.Ion

open System.Text
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Ion.IonNodes

/// Decodes v3 type expressions, and builds the classic JSON form of a type for the attributes that hold one.
module internal ClassicIonTypes =
    /// The deepest JSON an attribute can hold: a type nested to any read limit a caller is likely to set.
    let private attributeOptions = JsonDocumentOptions(MaxDepth = 1_000_000)

    let parseAttribute (json: string) : JsonElement =
        use document = JsonDocument.Parse(json, attributeOptions)
        document.RootElement.Clone()

    /// A classic type's attribute. Ion has no spelling for one, and the JSON form is always empty.
    let typeAttribute: JsonElement = parseAttribute "{}"

    let private appendWords (builder: StringBuilder) (Name words) =
        builder.Append '['
        |> ignore

        words
        |> List.iteri (fun i word ->
            if i > 0 then
                builder.Append ','
                |> ignore

            // A word holds only lowercase letters and digits, so it needs no escaping.
            builder.Append('"').Append(word).Append('"')
            |> ignore
        )

        builder.Append ']'
        |> ignore

    let private appendPath (builder: StringBuilder) (Path names) =
        builder.Append '['
        |> ignore

        names
        |> List.iteri (fun i name ->
            if i > 0 then
                builder.Append ','
                |> ignore

            appendWords builder name
        )

        builder.Append ']'
        |> ignore

    let private appendFqName (builder: StringBuilder) (name: FQName) =
        let (PackageName package) = name.PackagePath
        let (ModulePath modulePath) = name.ModulePath

        builder.Append '['
        |> ignore

        appendPath builder package

        builder.Append ','
        |> ignore

        appendPath builder modulePath

        builder.Append ','
        |> ignore

        appendWords builder name.LocalName

        builder.Append ']'
        |> ignore

    /// Writes a type as the classic JSON writes it.
    let rec private appendType (builder: StringBuilder) (value: Type<JsonElement>) =
        enterNode ()

        let start (tag: string) =
            builder.Append("[\"").Append(tag).Append("\",{}")
            |> ignore

        let separator () =
            builder.Append ','
            |> ignore

        let finish () =
            builder.Append ']'
            |> ignore

        let appendTypes (items: Type<JsonElement> list) =
            builder.Append '['
            |> ignore

            items
            |> List.iteri (fun i item ->
                if i > 0 then
                    separator ()

                appendType builder item
            )

            finish ()

        let appendFields (fields: Field<JsonElement> list) =
            builder.Append '['
            |> ignore

            fields
            |> List.iteri (fun i field ->
                if i > 0 then
                    separator ()

                builder.Append "{\"name\":"
                |> ignore

                appendWords builder field.Name

                builder.Append ",\"tpe\":"
                |> ignore

                appendType builder field.Type

                builder.Append '}'
                |> ignore
            )

            finish ()

        match value with
        | Type.Variable(_, name) ->
            start "Variable"
            separator ()
            appendWords builder name
            finish ()
        | Type.Reference(_, name, arguments) ->
            start "Reference"
            separator ()
            appendFqName builder name
            separator ()
            appendTypes arguments
            finish ()
        | Type.Tuple(_, items) ->
            start "Tuple"
            separator ()
            appendTypes items
            finish ()
        | Type.Record(_, fields) ->
            start "Record"
            separator ()
            appendFields fields
            finish ()
        | Type.ExtensibleRecord(_, variable, fields) ->
            start "ExtensibleRecord"
            separator ()
            appendWords builder variable
            separator ()
            appendFields fields
            finish ()
        | Type.Function(_, parameter, result) ->
            start "Function"
            separator ()
            appendType builder parameter
            separator ()
            appendType builder result
            finish ()
        | Type.Unit _ ->
            start "Unit"
            finish ()

    /// A type in the classic JSON form, the form a value or pattern attribute takes.
    let typeAsAttribute (value: Type<JsonElement>) : JsonElement =
        let builder = StringBuilder()
        appendType builder value
        parseAttribute (builder.ToString())

    let private unit: Type<JsonElement> = Type.Unit typeAttribute

    let rec typ path (value: IonValue) : Type<JsonElement> =
        enterNode ()

        match annotationNames path value, value.Data with
        | [], IonData.String input ->
            if looksQualified input then
                Type.Reference(typeAttribute, fqNameText path input, [])
            else
                Type.Variable(typeAttribute, nameText path input)
        | ([] | [ "tuple" ]), IonData.List _ ->
            Type.Tuple(typeAttribute, tupleItems path { value with Annotations = [] })
        | [ "tuple" ], IonData.Struct _ ->
            fail
                "unsupported_node"
                path
                "A tuple with attributes is a v4 node, and this is a v3 document."
        | [ "variable" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "name" ] values
            Type.Variable(typeAttribute, required path "name" nameString values)
        | [ "reference" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "name"; "arguments" ] values

            Type.Reference(
                typeAttribute,
                required path "name" fqNameString values,
                optionalItems path "arguments" values
                |> List.map (fun (itemPath, item) -> typ itemPath item)
            )
        | [ "record" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "fields" ] values
            Type.Record(typeAttribute, fields path values)
        | [ "extensibleRecord" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "variable"; "fields" ] values

            Type.ExtensibleRecord(
                typeAttribute,
                required path "variable" nameString values,
                fields path values
            )
        | [ "function" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "parameterType"; "returnType" ] values

            Type.Function(
                typeAttribute,
                required path "parameterType" typ values,
                required path "returnType" typ values
            )
        | [ "unit" ], IonData.Struct _ ->
            closed path [] (members path value)
            unit
        | [], _ -> fail "invalid_type" path "Expected a type."
        | names, _ -> fail "invalid_node" path $"{spell names} is not a v3 type."

    and private tupleItems path value =
        listItems path value
        |> List.map (fun (itemPath, item) -> typ itemPath item)

    and private fields path (values: Members) : Field<JsonElement> list =
        optionalItems path "fields" values
        |> List.map (fun (itemPath, item) ->
            let field = members itemPath item
            closed itemPath [ "name"; "type" ] field

            {
                Name = required itemPath "name" nameString field
                Type = required itemPath "type" typ field
            }
        )

    /// Reads <c>[ { name, args: [ { name, type } ] } ]</c>.
    let constructors path (values: Members) : Type.Constructors<JsonElement> =
        optionalItems path "constructors" values
        |> List.map (fun (itemPath, item) ->
            let constructor = members itemPath item
            closed itemPath [ "name"; "args" ] constructor

            let args =
                optionalItems itemPath "args" constructor
                |> List.map (fun (argPath, arg) ->
                    let argument = members argPath arg
                    closed argPath [ "name"; "type" ] argument

                    required argPath "name" nameString argument,
                    required argPath "type" typ argument
                )

            required itemPath "name" nameString constructor, args
        )
        |> uniqueNames path
        |> Map.ofList
