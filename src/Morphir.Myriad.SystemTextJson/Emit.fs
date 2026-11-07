// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Generator

open System
open System.Reflection
open System.Text

/// Writes F# source with four-space indentation.
type internal CodeWriter() =
    let builder = StringBuilder()
    let mutable depth = 0

    /// Writes one line at the current indentation. An empty line has no spaces.
    member _.Line(text: string) =
        if text.Length > 0 then
            builder.Append(' ', depth * 4).Append(text) |> ignore

        builder.Append('\n') |> ignore

    /// Writes an empty line.
    member this.Blank() = this.Line ""

    /// Writes the lines that the action writes, one level deeper.
    member _.Indent(action: unit -> unit) =
        depth <- depth + 1
        action ()
        depth <- depth - 1

    override _.ToString() = builder.ToString()

/// A record field after resolution.
type internal PlannedField =
    {
        /// The F# field name.
        Name: string
        /// The JSON member name that is written.
        JsonName: string
        /// Every JSON member name that is read.
        ReadNames: string list
        Type: TypeRef
        /// True when an absent option is left out of the JSON.
        Skippable: bool
        /// True when a reader accepts null for this field although it is not an option.
        AllowNull: bool
        /// True when a reader also accepts the number of this field in a JSON string (JsonNumberHandling).
        NumbersFromStrings: bool
    }

/// A type of the context with its resolved parts.
[<RequireQualifiedAccess>]
type internal PlannedShape =
    /// The fields, and true when a reader refuses unknown members.
    | Record of PlannedField list * refuseUnknown: bool
    | Enum of (string * int64) list
    | Abbreviation of TypeRef

/// A context member: a declared type and how to encode it.
type internal PlannedMember =
    {
        Declaration: TypeDeclaration
        Shape: PlannedShape
    }

    /// The F# type of the member.
    member this.FSharpName = this.Declaration.QualifiedName

    /// The shape that the converter of this member reads and writes.
    member this.Target =
        match this.Shape with
        | PlannedShape.Abbreviation target -> target
        | PlannedShape.Record _
        | PlannedShape.Enum _ -> TypeRef.Local this.Declaration

/// Everything the emitter needs for one context.
type internal Plan =
    {
        Settings: ContextSettings
        Source: SourceFile
        Members: PlannedMember list
        /// Records of the input, by the slug of their TypeRef, for the codec methods.
        Records: Map<string, TypeDeclaration * PlannedField list * bool>
        /// Enums of the input, by slug.
        Enums: Map<string, TypeDeclaration * (string * int64) list>
    }

/// Emits the generated file.
module internal Emit =
    let private version =
        let assembly = typeof<CodeWriter>.Assembly

        match assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> "0.0.0"
        | attribute -> attribute.InformationalVersion.Split('+')[0]

    /// An F# string literal.
    let literal (text: string) =
        let escaped =
            text
            |> Seq.map (fun c ->
                match c with
                | '"' -> "\\\""
                | '\\' -> "\\\\"
                | '\n' -> "\\n"
                | '\r' -> "\\r"
                | '\t' -> "\\t"
                | c when Char.IsControl c -> $"\\u{int c:x4}"
                | c -> string c)
            |> String.concat ""

        "\"" + escaped + "\""

    /// Every distinct shape that a codec method serves, children first.
    let rec private shapes (plan: Plan) (t: TypeRef) : TypeRef list =
        let children =
            match t with
            | TypeRef.Primitive _
            | TypeRef.External _ -> []
            | TypeRef.Option inner
            | TypeRef.ValueOption inner
            | TypeRef.Sequence(_, inner) -> [ inner ]
            | TypeRef.Map(_, key, value) -> [ key; value ]
            | TypeRef.Tuple items -> items
            | TypeRef.Local _ ->
                match plan.Records.TryFind t.Slug with
                | Some(_, fields, _) -> fields |> List.map (fun field -> field.Type)
                | None -> []

        (children |> List.collect (shapes plan)) @ [ t ]

    let private codecName (plan: Plan) = plan.Settings.Name.Value + "Codec"

    /// The expression that reads a value of the type. With lenient, numbers are also read from strings.
    let private readExpr (codec: string) (lenient: bool) (t: TypeRef) =
        let isNumber (p: Primitive) = p.NumberTypeName.IsSome

        match t with
        | TypeRef.Primitive p when lenient && isNumber p -> $"{codec}.Read{p.Slug}FromString(&reader)"
        | TypeRef.Primitive p -> $"{codec}.Read{p.Slug}(&reader)"
        | TypeRef.Option(TypeRef.Primitive p) when lenient && isNumber p ->
            $"(if reader.TokenType = JsonTokenType.Null then None else Some({codec}.Read{p.Slug}FromString(&reader)))"
        | TypeRef.ValueOption(TypeRef.Primitive p) when lenient && isNumber p ->
            $"(if reader.TokenType = JsonTokenType.Null then ValueNone else ValueSome({codec}.Read{p.Slug}FromString(&reader)))"
        | _ -> $"{codec}.Read_{t.Slug}(&reader, options)"

    let private writeStmt (codec: string) (t: TypeRef) (value: string) =
        match t with
        | TypeRef.Primitive p -> $"{codec}.Write{p.Slug}(writer, {value})"
        | _ -> $"{codec}.Write_{t.Slug}(writer, {value}, options)"

    let private fail (w: CodeWriter) (message: string) = w.Line $"raise (JsonException({literal message}))"

    let private primitiveMethods (w: CodeWriter) (codecPlaceholder: string) (p: Primitive) =
        let name = p.FSharpName

        let tryGet (method: string) (label: string) =
            w.Line $"static member Read{p.Slug}(reader: byref<Utf8JsonReader>) : {name} ="
            w.Indent(fun () ->
                w.Line "let mutable value = Unchecked.defaultof<_>"
                w.Line $"if reader.TokenType = JsonTokenType.Number && reader.{method}(&value) then value"
                w.Line $"""else raise (JsonException({literal (label + " was expected.")}))""")

        let tryGetString (method: string) (label: string) =
            w.Line $"static member Read{p.Slug}(reader: byref<Utf8JsonReader>) : {name} ="
            w.Indent(fun () ->
                w.Line "let mutable value = Unchecked.defaultof<_>"
                w.Line $"if reader.TokenType = JsonTokenType.String && reader.{method}(&value) then value"
                w.Line $"""else raise (JsonException({literal (label + " was expected.")}))""")

        let viaBuiltIn (converter: string) (label: string) =
            w.Line $"static member Read{p.Slug}(reader: byref<Utf8JsonReader>) : {name} ="
            w.Indent(fun () ->
                w.Line "if reader.TokenType <> JsonTokenType.String then"
                w.Indent(fun () -> fail w (label + " was expected."))
                w.Line $"JsonMetadataServices.{converter}.Read(&reader, typeof<{name}>, JsonSerializerOptions.Default)")

            w.Line $"static member Write{p.Slug}(writer: Utf8JsonWriter, value: {name}) ="
            w.Indent(fun () -> w.Line $"JsonMetadataServices.{converter}.Write(writer, value, JsonSerializerOptions.Default)")

        let number (method: string) (label: string) (write: string) =
            tryGet method label
            w.Line $"static member Write{p.Slug}(writer: Utf8JsonWriter, value: {name}) = writer.WriteNumberValue({write})"

            // The same number in a JSON string, for JsonNumberHandling.AllowReadingFromString.
            let bcl = defaultArg p.NumberTypeName name

            let styles =
                if p.IsInteger then
                    "System.Globalization.NumberStyles.AllowLeadingSign"
                else
                    "System.Globalization.NumberStyles.AllowLeadingSign ||| System.Globalization.NumberStyles.AllowDecimalPoint ||| System.Globalization.NumberStyles.AllowExponent"

            w.Line $"static member Read{p.Slug}FromString(reader: byref<Utf8JsonReader>) : {name} ="
            w.Indent(fun () ->
                w.Line "if reader.TokenType <> JsonTokenType.String then"
                w.Indent(fun () -> w.Line $"{codecPlaceholder}.Read{p.Slug}(&reader)")
                w.Line "else"
                w.Indent(fun () ->
                    w.Line "match reader.GetString() with"
                    w.Line $"""| null -> raise (JsonException({literal (label + " was expected.")}))"""
                    w.Line "| text ->"
                    w.Indent(fun () ->
                        w.Line "let mutable value = Unchecked.defaultof<_>"
                        w.Line $"if {bcl}.TryParse(text, {styles}, System.Globalization.CultureInfo.InvariantCulture, &value) then value"
                        w.Line $"""else raise (JsonException({literal (label + " was expected.")}))""")))

        match p with
        | Primitive.String ->
            w.Line "static member ReadString(reader: byref<Utf8JsonReader>) : string ="
            w.Indent(fun () ->
                w.Line "if reader.TokenType <> JsonTokenType.String then"
                w.Indent(fun () -> fail w "A JSON string was expected.")
                w.Line "match reader.GetString() with"
                w.Line "| null -> raise (JsonException(\"A JSON string was expected.\"))"
                w.Line "| text -> text")

            w.Line "static member WriteString(writer: Utf8JsonWriter, value: string) = writer.WriteStringValue(value)"
        | Primitive.Bool ->
            w.Line "static member ReadBool(reader: byref<Utf8JsonReader>) : bool ="
            w.Indent(fun () ->
                w.Line "match reader.TokenType with"
                w.Line "| JsonTokenType.True -> true"
                w.Line "| JsonTokenType.False -> false"
                w.Line "| _ -> raise (JsonException(\"A JSON boolean was expected.\"))")

            w.Line "static member WriteBool(writer: Utf8JsonWriter, value: bool) = writer.WriteBooleanValue(value)"
        | Primitive.Byte -> number "TryGetByte" "A JSON number in the Byte range" "int value"
        | Primitive.SByte -> number "TryGetSByte" "A JSON number in the SByte range" "int value"
        | Primitive.Int16 -> number "TryGetInt16" "A JSON number in the Int16 range" "int value"
        | Primitive.UInt16 -> number "TryGetUInt16" "A JSON number in the UInt16 range" "int value"
        | Primitive.Int32 -> number "TryGetInt32" "A JSON integer in the Int32 range" "value"
        | Primitive.UInt32 -> number "TryGetUInt32" "A JSON integer in the UInt32 range" "value"
        | Primitive.Int64 -> number "TryGetInt64" "A JSON integer in the Int64 range" "value"
        | Primitive.UInt64 -> number "TryGetUInt64" "A JSON integer in the UInt64 range" "value"
        | Primitive.Single -> number "TryGetSingle" "A JSON number in the Single range" "value"
        | Primitive.Double -> number "TryGetDouble" "A JSON number in the Double range" "value"
        | Primitive.Decimal -> number "TryGetDecimal" "A JSON number in the Decimal range" "value"
        | Primitive.Char ->
            w.Line "static member ReadChar(reader: byref<Utf8JsonReader>) : char ="
            w.Indent(fun () ->
                w.Line "if reader.TokenType <> JsonTokenType.String then"
                w.Indent(fun () -> fail w "A JSON string of one character was expected.")
                w.Line "match reader.GetString() with"
                w.Line "| null -> raise (JsonException(\"A JSON string of one character was expected.\"))"
                w.Line "| text when text.Length = 1 -> text[0]"
                w.Line "| _ -> raise (JsonException(\"A JSON string of one character was expected.\"))")

            w.Line "static member WriteChar(writer: Utf8JsonWriter, value: char) = writer.WriteStringValue(string value)"
        | Primitive.Guid ->
            tryGetString "TryGetGuid" "A JSON string with a GUID"
            w.Line "static member WriteGuid(writer: Utf8JsonWriter, value: System.Guid) = writer.WriteStringValue(value)"
        | Primitive.DateTime ->
            tryGetString "TryGetDateTime" "A JSON string with an ISO 8601 date and time"
            w.Line "static member WriteDateTime(writer: Utf8JsonWriter, value: System.DateTime) = writer.WriteStringValue(value)"
        | Primitive.DateTimeOffset ->
            tryGetString "TryGetDateTimeOffset" "A JSON string with an ISO 8601 date, time and offset"
            w.Line "static member WriteDateTimeOffset(writer: Utf8JsonWriter, value: System.DateTimeOffset) = writer.WriteStringValue(value)"
        | Primitive.TimeSpan -> viaBuiltIn "TimeSpanConverter" "A JSON string with a time span"
        | Primitive.DateOnly -> viaBuiltIn "DateOnlyConverter" "A JSON string with a date"
        | Primitive.TimeOnly -> viaBuiltIn "TimeOnlyConverter" "A JSON string with a time"
        | Primitive.JsonElement ->
            w.Line "static member ReadJsonElement(reader: byref<Utf8JsonReader>) : JsonElement = JsonElement.ParseValue(&reader)"
            w.Line "static member WriteJsonElement(writer: Utf8JsonWriter, value: JsonElement) = value.WriteTo(writer)"

    /// The loop over the members of a JSON object. The body handles one member name in the variable memberName.
    let private objectLoop (w: CodeWriter) (typeName: string) (body: unit -> unit) =
        w.Line "let mutable finished = false"
        w.Line "while not finished do"
        w.Indent(fun () ->
            w.Line "if not (reader.Read()) then"
            w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")
            w.Line "match reader.TokenType with"
            w.Line "| JsonTokenType.EndObject -> finished <- true"
            w.Line "| JsonTokenType.PropertyName ->"
            w.Indent(fun () ->
                w.Line "let memberName = reader.GetString()"
                w.Line "reader.Read() |> ignore"
                body ())
            w.Line $"""| _ -> raise (JsonException({literal $"A member name of {typeName} was expected."}))""")

    let private recordMethods (w: CodeWriter) (codec: string) (lenient: bool) (t: TypeRef) (declaration: TypeDeclaration) (fields: PlannedField list) (refuseUnknown: bool) =
        let typeName = declaration.Name
        let local (field: PlannedField) = "field_" + field.Name
        let seen (field: PlannedField) = "seen_" + field.Name

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "writer.WriteStartObject()"

            for field in fields do
                match field.Type with
                | TypeRef.Option inner when field.Skippable ->
                    w.Line $"match value.{field.Name} with"
                    w.Line "| Some item ->"
                    w.Indent(fun () ->
                        w.Line $"writer.WritePropertyName({literal field.JsonName})"
                        w.Line(writeStmt codec inner "item"))
                    w.Line "| None -> ()"
                | TypeRef.ValueOption inner when field.Skippable ->
                    w.Line $"match value.{field.Name} with"
                    w.Line "| ValueSome item ->"
                    w.Indent(fun () ->
                        w.Line $"writer.WritePropertyName({literal field.JsonName})"
                        w.Line(writeStmt codec inner "item"))
                    w.Line "| ValueNone -> ()"
                | _ ->
                    w.Line $"writer.WritePropertyName({literal field.JsonName})"
                    w.Line(writeStmt codec field.Type $"value.{field.Name}")

            w.Line "writer.WriteEndObject()")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "if reader.TokenType <> JsonTokenType.StartObject then"
            w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")

            for field in fields do
                w.Line $"let mutable {local field} = Unchecked.defaultof<{field.Type.FSharpName}>"
                w.Line $"let mutable {seen field} = false"

            objectLoop w typeName (fun () ->
                w.Line "match memberName with"

                for field in fields do
                    let names = field.ReadNames |> List.map literal |> String.concat " | "
                    w.Line $"| {names} ->"

                    w.Indent(fun () ->
                        w.Line $"if {seen field} then"
                        w.Indent(fun () -> fail w $"The member '{field.JsonName}' of {typeName} appears more than once.")
                        w.Line $"{seen field} <- true"

                        // A failure inside the value names this member, so the message holds the path within the value.
                        let read () =
                            w.Line "try"
                            w.Indent(fun () -> w.Line $"{local field} <- {readExpr codec (lenient || field.NumbersFromStrings) field.Type}")
                            w.Line "with :? JsonException as error ->"
                            w.Indent(fun () ->
                                w.Line $"""raise (JsonException({literal $"In the member '{field.JsonName}' of {typeName}: "} + error.Message, error))""")

                        match field.Type with
                        | TypeRef.Option _
                        | TypeRef.ValueOption _ -> read ()
                        | _ when field.AllowNull ->
                            w.Line "if reader.TokenType = JsonTokenType.Null then"
                            w.Indent(fun () -> w.Line $"{local field} <- Unchecked.defaultof<{field.Type.FSharpName}>")
                            w.Line "else"
                            w.Indent read
                        | _ ->
                            w.Line "if reader.TokenType = JsonTokenType.Null then"
                            w.Indent(fun () -> fail w $"The member '{field.JsonName}' of {typeName} is null.")
                            read ())

                w.Line "| _ ->"
                w.Indent(fun () ->
                    if refuseUnknown then
                        w.Line $"""raise (JsonException($"The member '{{memberName}}' is not a member of {typeName}."))"""
                    else
                        w.Line "if not (reader.TrySkip()) then"
                        w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")))

            for field in fields do
                match field.Type with
                | TypeRef.Option _ -> w.Line $"if not {seen field} then {local field} <- None"
                | TypeRef.ValueOption _ -> w.Line $"if not {seen field} then {local field} <- ValueNone"
                | _ ->
                    w.Line $"if not {seen field} then"
                    w.Indent(fun () -> fail w $"The member '{field.JsonName}' of {typeName} is required.")

            let assignments = fields |> List.map (fun field -> $"{field.Name} = {local field}") |> String.concat "; "
            w.Line $"({{ {assignments} }}: {t.FSharpName})")

    let private enumMethods (w: CodeWriter) (t: TypeRef) (declaration: TypeDeclaration) =
        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "writer.WriteNumberValue(int value)")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "let mutable number = 0"
            w.Line "if reader.TokenType = JsonTokenType.Number && reader.TryGetInt32(&number) then"
            w.Indent(fun () -> w.Line $"enum<{t.FSharpName}> number")
            w.Line "else"
            w.Indent(fun () -> fail w $"A JSON integer for {declaration.Name} was expected."))

    let private optionMethods (w: CodeWriter) (codec: string) (lenient: bool) (t: TypeRef) (inner: TypeRef) (some: string) (none: string) =
        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "match value with"
            w.Line $"""| {some} item -> {writeStmt codec inner "item"}"""
            w.Line $"| {none} -> writer.WriteNullValue()")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line $"if reader.TokenType = JsonTokenType.Null then {none} else {some}({readExpr codec lenient inner})")

    let private sequenceMethods (w: CodeWriter) (codec: string) (lenient: bool) (t: TypeRef) (kind: SequenceKind) (inner: TypeRef) =
        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "writer.WriteStartArray()"
            w.Line "for item in value do"
            w.Indent(fun () -> w.Line(writeStmt codec inner "item"))
            w.Line "writer.WriteEndArray()")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "if reader.TokenType <> JsonTokenType.StartArray then"
            w.Indent(fun () -> fail w "A JSON array was expected.")
            w.Line $"let items = ResizeArray<{inner.FSharpName}>()"
            w.Line "let mutable finished = false"
            w.Line "while not finished do"
            w.Indent(fun () ->
                w.Line "if not (reader.Read()) then"
                w.Indent(fun () -> fail w "The JSON array is not complete.")
                w.Line "if reader.TokenType = JsonTokenType.EndArray then finished <- true"
                w.Line $"else items.Add({readExpr codec lenient inner})")

            match kind with
            | SequenceKind.List -> w.Line "List.ofSeq items"
            | SequenceKind.Array -> w.Line "items.ToArray()"
            | SequenceKind.Set -> w.Line "Set.ofSeq items"
            | SequenceKind.ResizeArray -> w.Line "items"
            | SequenceKind.ReadOnlyList
            | SequenceKind.ReadOnlyCollection -> w.Line $"items.ToArray() :> {t.FSharpName}")

    let private tupleMethods (w: CodeWriter) (codec: string) (lenient: bool) (t: TypeRef) (items: TypeRef list) =
        let names = items |> List.mapi (fun index _ -> $"item{index}")

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line $"""let ({String.concat ", " names}) = value"""
            w.Line "writer.WriteStartArray()"

            for name, item in List.zip names items do
                w.Line(writeStmt codec item name)

            w.Line "writer.WriteEndArray()")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "if reader.TokenType <> JsonTokenType.StartArray then"
            w.Indent(fun () -> fail w $"A JSON array of {items.Length} items was expected.")

            for name, item in List.zip names items do
                w.Line "if not (reader.Read()) || reader.TokenType = JsonTokenType.EndArray then"
                w.Indent(fun () -> fail w $"A JSON array of {items.Length} items was expected.")
                w.Line $"let {name} = {readExpr codec lenient item}"

            w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndArray then"
            w.Indent(fun () -> fail w $"A JSON array of {items.Length} items was expected.")
            w.Line $"""({String.concat ", " names})""")

    /// True when a map with this key is written as a JSON object in the given format.
    let mapAsObject (format: MapFormat) (key: TypeRef) =
        let stringKey =
            match key with
            | TypeRef.Primitive Primitive.String -> true
            | _ -> false

        match format with
        | MapFormat.ArrayOfPairs -> false
        | MapFormat.Object
        | MapFormat.ObjectOrArrayOfPairs -> stringKey

    let private mapMethods (w: CodeWriter) (codec: string) (lenient: bool) (format: MapFormat) (t: TypeRef) (kind: MapKind) (key: TypeRef) (value: TypeRef) =
        let asObject = mapAsObject format key

        let build =
            match kind with
            | MapKind.Map -> "Map.ofSeq pairs"
            | MapKind.ReadOnlyDictionary -> $"System.Collections.Generic.Dictionary<{key.FSharpName}, {value.FSharpName}>(pairs |> Seq.map System.Collections.Generic.KeyValuePair) :> {t.FSharpName}"

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"

            if asObject then
                w.Line "writer.WriteStartObject()"
                w.Line "for pair in value do"
                w.Indent(fun () ->
                    w.Line "writer.WritePropertyName(pair.Key)"
                    w.Line(writeStmt codec value "pair.Value"))

                w.Line "writer.WriteEndObject()"
            else
                w.Line "writer.WriteStartArray()"
                w.Line "for pair in value do"
                w.Indent(fun () ->
                    w.Line "writer.WriteStartArray()"
                    w.Line(writeStmt codec key "pair.Key")
                    w.Line(writeStmt codec value "pair.Value")
                    w.Line "writer.WriteEndArray()")

                w.Line "writer.WriteEndArray()")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line $"let pairs = ResizeArray<{key.FSharpName} * {value.FSharpName}>()"

            if asObject then
                w.Line "if reader.TokenType <> JsonTokenType.StartObject then"
                w.Indent(fun () -> fail w "A JSON object was expected.")
                w.Line "let keys = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)"

                objectLoop w "the map" (fun () ->
                    w.Line "match memberName with"
                    w.Line "| null -> raise (JsonException(\"A member name was expected.\"))"
                    w.Line "| name ->"

                    w.Indent(fun () ->
                        w.Line "if not (keys.Add name) then"
                        w.Indent(fun () -> w.Line "raise (JsonException($\"The key '{name}' appears more than once.\"))")
                        w.Line $"pairs.Add((name, {readExpr codec lenient value}))"))
            else
                w.Line "if reader.TokenType <> JsonTokenType.StartArray then"
                w.Indent(fun () -> fail w "A JSON array of [key, value] pairs was expected.")
                w.Line "let mutable finished = false"
                w.Line "while not finished do"

                w.Indent(fun () ->
                    w.Line "if not (reader.Read()) then"
                    w.Indent(fun () -> fail w "The JSON array is not complete.")
                    w.Line "if reader.TokenType = JsonTokenType.EndArray then finished <- true"
                    w.Line "else"

                    w.Indent(fun () ->
                        w.Line "if reader.TokenType <> JsonTokenType.StartArray || not (reader.Read()) then"
                        w.Indent(fun () -> fail w "A [key, value] pair was expected.")
                        w.Line $"let key = {readExpr codec lenient key}"
                        w.Line "if not (reader.Read()) then"
                        w.Indent(fun () -> fail w "A [key, value] pair was expected.")
                        w.Line $"let item = {readExpr codec lenient value}"
                        w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndArray then"
                        w.Indent(fun () -> fail w "A [key, value] pair was expected.")
                        w.Line "pairs.Add((key, item))"))

            w.Line build)

    let private externalMethods (w: CodeWriter) (t: TypeRef) =
        let name = t.FSharpName

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {name}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () -> w.Line $"JsonSerializer.Serialize(writer, value, options.GetTypeInfo(typeof<{name}>) :?> JsonTypeInfo<{name}>)")
        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {name} ="
        w.Indent(fun () ->
            w.Line $"match box (JsonSerializer.Deserialize(&reader, options.GetTypeInfo(typeof<{name}>) :?> JsonTypeInfo<{name}>)) with"
            w.Line $"""| null -> raise (JsonException({literal $"The JSON value for {name} is null."}))"""
            w.Line $"| value -> unbox<{name}> value")

    let private codec (w: CodeWriter) (plan: Plan) =
        let codec = codecName plan
        let lenient = plan.Settings.NumbersFromStrings

        let all =
            plan.Members
            |> List.collect (fun m -> shapes plan m.Target)
            |> List.distinctBy (fun t -> t.Slug)

        w.Line "/// <summary>Read and write methods for every shape of the context.</summary>"
        w.Line "[<AbstractClass; Sealed>]"
        w.Line $"type internal {codec} ="

        w.Indent(fun () ->
            for t in all do
                match t with
                | TypeRef.Primitive p -> primitiveMethods w codec p
                | TypeRef.Option inner -> optionMethods w codec lenient t inner "Some" "None"
                | TypeRef.ValueOption inner -> optionMethods w codec lenient t inner "ValueSome" "ValueNone"
                | TypeRef.Sequence(kind, inner) -> sequenceMethods w codec lenient t kind inner
                | TypeRef.Map(kind, key, value) -> mapMethods w codec lenient plan.Settings.MapFormat t kind key value
                | TypeRef.Tuple items -> tupleMethods w codec lenient t items
                | TypeRef.External _ -> externalMethods w t
                | TypeRef.Local declaration ->
                    match plan.Records.TryFind t.Slug, plan.Enums.TryFind t.Slug with
                    | Some(_, fields, refuseUnknown), _ -> recordMethods w codec lenient t declaration fields refuseUnknown
                    | _, Some _ -> enumMethods w t declaration
                    | None, None -> ()

            // An empty class still needs a member.
            w.Line "static member internal Shapes = 0")

    let private converterName (plan: Plan) (m: PlannedMember) = $"{plan.Settings.Name.Value}_{m.Declaration.Name}_Converter"

    let private converters (w: CodeWriter) (plan: Plan) =
        let codec = codecName plan

        for m in plan.Members do
            let target = m.Target
            w.Line $"/// <summary>The converter for {m.Declaration.Name}.</summary>"
            w.Line $"type internal {converterName plan m}() ="
            w.Indent(fun () ->
                w.Line $"inherit JsonConverter<{m.FSharpName}>()"
                w.Line $"override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, options: JsonSerializerOptions) : {m.FSharpName} ="

                w.Indent(fun () ->
                    match target with
                    | TypeRef.Primitive p -> w.Line $"ignore options; {codec}.Read{p.Slug}(&reader)"
                    | _ -> w.Line $"{codec}.Read_{target.Slug}(&reader, options)")

                w.Line $"override _.Write(writer: Utf8JsonWriter, value: {m.FSharpName}, options: JsonSerializerOptions) ="

                w.Indent(fun () ->
                    match target with
                    | TypeRef.Primitive p -> w.Line $"ignore options; {codec}.Write{p.Slug}(writer, value)"
                    | _ -> w.Line $"{codec}.Write_{target.Slug}(writer, value, options)"))

            w.Blank()

    let private header (w: CodeWriter) (source: SourceFile) =
        w.Line "//------------------------------------------------------------------------------"
        w.Line "// <auto-generated>"
        w.Line $"//     Generated by Morphir.Myriad.SystemTextJson {version} from {IO.Path.GetFileName source.File}."
        w.Line "//     Changes to this file are lost when the project builds again."
        w.Line "// </auto-generated>"
        w.Line "//------------------------------------------------------------------------------"

    /// The context type, with its default options, its resolver and its type info members.
    let private context (w: CodeWriter) (plan: Plan) =
        let name = plan.Settings.Name.Value

        let externals =
            plan.Members
            |> List.collect (fun m -> shapes plan m.Target)
            |> List.choose (function
                | TypeRef.External n -> Some n
                | _ -> None)
            |> List.distinct

        w.Line $"/// <summary>The System.Text.Json context generated from {IO.Path.GetFileName plan.Source.File}.</summary>"
        w.Line $"[<System.CodeDom.Compiler.GeneratedCode(\"Morphir.Myriad.SystemTextJson\", \"{version}\")>]"
        w.Line $"type {name}(options: JsonSerializerOptions) ="
        w.Indent(fun () ->
            w.Line "inherit JsonSerializerContext(options)"
            w.Blank()
            w.Line "static let generatedOptions = JsonSerializerOptions()"
            w.Line $"static let defaultContext = lazy ({name}(JsonSerializerOptions(generatedOptions)))"

            for m in plan.Members do
                w.Line $"let mutable cache_{m.Declaration.Name} = Unchecked.defaultof<JsonTypeInfo<{m.FSharpName}>>"

            w.Blank()
            w.Line "/// <summary>Creates a context that is not bound to options.</summary>"
            w.Line $"new() = {name}(Unchecked.defaultof<JsonSerializerOptions>)"
            w.Blank()
            w.Line "/// <summary>The context with the generated default options.</summary>"
            w.Line $"static member Default: {name} = defaultContext.Force()"
            w.Blank()

            for m in plan.Members do
                w.Line $"/// <summary>The type info for {m.Declaration.Name}.</summary>"
                w.Line $"member this.{m.Declaration.Name}: JsonTypeInfo<{m.FSharpName}> ="
                w.Indent(fun () ->
                    w.Line $"if obj.ReferenceEquals(cache_{m.Declaration.Name}, null) then"
                    w.Indent(fun () -> w.Line $"cache_{m.Declaration.Name} <- (this.Options.GetTypeInfo(typeof<{m.FSharpName}>) :?> JsonTypeInfo<{m.FSharpName}>)")
                    w.Line $"cache_{m.Declaration.Name}")

                w.Blank()

            w.Line "/// <summary>The options that the attributes and the project settings give.</summary>"
            w.Line "override _.GeneratedSerializerOptions = generatedOptions"
            w.Blank()
            w.Line "/// <summary>The type info for a type, from the options of this context.</summary>"
            w.Line "override this.GetTypeInfo(typ: Type) ="
            w.Indent(fun () ->
                w.Line "match this.Options.TryGetTypeInfo typ with"
                w.Line "| true, info -> info"
                w.Line "| _ -> Unchecked.defaultof<JsonTypeInfo>")
            w.Blank()
            w.Line "/// <summary>"
            w.Line "/// Checks that the options can resolve each type that the converters of this context ask the options for."
            w.Line "/// Throws InvalidOperationException that names the first type they cannot resolve."
            w.Line "/// </summary>"
            w.Line "member _.Verify(options: JsonSerializerOptions) : unit ="
            w.Indent(fun () ->
                w.Line "ignore options"

                for external in externals do
                    w.Line "try"
                    w.Indent(fun () -> w.Line $"options.GetTypeInfo(typeof<{external}>) |> ignore")
                    w.Line "with :? NotSupportedException as error ->"
                    w.Indent(fun () ->
                        w.Line $"""raise (InvalidOperationException({literal $"The options cannot resolve {external}, which {name} uses. Add a context that covers it to the options."}, error))"""))
            w.Blank()
            w.Line "interface IJsonTypeInfoResolver with"
            w.Indent(fun () ->
                w.Line "member _.GetTypeInfo(typ: Type, options: JsonSerializerOptions) ="
                w.Indent(fun () ->
                    match plan.Members with
                    | [] ->
                        w.Line "ignore typ"
                        w.Line "ignore options"
                        w.Line "Unchecked.defaultof<JsonTypeInfo>"
                    | members ->
                        members
                        |> List.iteri (fun index m ->
                            let keyword = if index = 0 then "if" else "elif"
                            w.Line $"{keyword} typ = typeof<{m.FSharpName}> then"
                            w.Indent(fun () -> w.Line $"JsonMetadataServices.CreateValueInfo<{m.FSharpName}>(options, {converterName plan m}()) :> JsonTypeInfo"))

                        w.Line "else"
                        w.Indent(fun () -> w.Line "Unchecked.defaultof<JsonTypeInfo>"))))

    /// The complete generated file.
    let file (plan: Plan) : string =
        let w = CodeWriter()
        header w plan.Source
        w.Line $"namespace {plan.Settings.Namespace.Value}"
        w.Blank()

        let fixedOpens = [ "System"; "System.Text.Json"; "System.Text.Json.Serialization"; "System.Text.Json.Serialization.Metadata" ]

        for scope in fixedOpens @ plan.Source.Scopes @ plan.Source.Opens |> List.distinct do
            w.Line $"open {scope}"

        w.Blank()
        codec w plan
        w.Blank()
        converters w plan
        context w plan
        w.ToString()
