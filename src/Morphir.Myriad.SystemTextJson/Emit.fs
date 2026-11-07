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

/// A union case after resolution.
type internal PlannedCase =
    {
        /// The F# case name.
        Name: string
        /// The JSON tag that is written.
        Tag: string
        /// Every JSON tag that is read.
        ReadTags: string list
        /// The fields, with their JSON member names.
        Fields: PlannedField list
    }

/// The JSON token kind that selects a case with SelectByTokenKind.
[<RequireQualifiedAccess>]
type internal TokenKind =
    | String
    | Number
    | Boolean
    | Array
    | Object

/// A type of the context with its resolved parts.
[<RequireQualifiedAccess>]
type internal PlannedShape =
    /// The fields, and true when a reader refuses unknown members.
    | Record of PlannedField list * refuseUnknown: bool
    /// The cases, the encoding rules, and the token kind of each case for SelectByTokenKind.
    | Union of PlannedCase list * UnionRules * TokenKind list
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
        | PlannedShape.Union _
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
        /// Unions of the input, by slug.
        Unions: Map<string, TypeDeclaration * PlannedCase list * UnionRules * TokenKind list>
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

    /// The direct parts of a shape.
    let private children (plan: Plan) (t: TypeRef) : TypeRef list =
        match t with
        | TypeRef.Primitive _
        | TypeRef.External _
        | TypeRef.Parsed _
        | TypeRef.Custom _ -> []
        | TypeRef.Option inner
        | TypeRef.ValueOption inner
        | TypeRef.Sequence(_, inner) -> [ inner ]
        | TypeRef.Map(_, key, value) -> [ key; value ]
        | TypeRef.Tuple items -> items
        | TypeRef.Local _ ->
            match plan.Records.TryFind t.Slug, plan.Unions.TryFind t.Slug with
            | Some(_, fields, _), _ -> fields |> List.map (fun field -> field.Type)
            | _, Some(_, cases, _, _) -> cases |> List.collect (fun case -> case.Fields |> List.map (fun field -> field.Type))
            | None, None -> []

    /// Every distinct shape that a codec method serves, children first. Recursive types are visited once.
    let private allShapes (plan: Plan) : TypeRef list =
        let seen = System.Collections.Generic.HashSet<string>()
        let result = ResizeArray<TypeRef>()

        let rec visit (t: TypeRef) =
            if seen.Add t.Slug then
                for child in children plan t do
                    visit child

                result.Add t

        for m in plan.Members do
            visit m.Target

        List.ofSeq result

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

        // The System.Text.Json formats, parsed exactly, with no JsonSerializerOptions (which is not AOT-safe without a context).
        let exactFormat (format: string) (parse: string) (label: string) =
            w.Line $"static member Read{p.Slug}(reader: byref<Utf8JsonReader>) : {name} ="
            w.Indent(fun () ->
                w.Line "match (if reader.TokenType = JsonTokenType.String then reader.GetString() else null) with"
                w.Line $"""| null -> raise (JsonException({literal (label + " was expected.")}))"""
                w.Line "| text ->"
                w.Indent(fun () ->
                    w.Line "let mutable value = Unchecked.defaultof<_>"
                    w.Line $"if {parse} then value"
                    w.Line $"""else raise (JsonException({literal (label + " was expected.")}))"""))

            w.Line $"static member Write{p.Slug}(writer: Utf8JsonWriter, value: {name}) ="
            w.Indent(fun () -> w.Line $"writer.WriteStringValue(value.ToString({literal format}, System.Globalization.CultureInfo.InvariantCulture))")

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
        | Primitive.TimeSpan ->
            exactFormat "c" "System.TimeSpan.TryParseExact(text, \"c\", System.Globalization.CultureInfo.InvariantCulture, &value)" "A JSON string with a time span"
        | Primitive.DateOnly ->
            exactFormat
                "yyyy-MM-dd"
                "System.DateOnly.TryParseExact(text, \"yyyy-MM-dd\", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, &value)"
                "A JSON string with a date"
        | Primitive.TimeOnly ->
            exactFormat
                "HH:mm:ss.FFFFFFF"
                "System.TimeOnly.TryParseExact(text, \"HH:mm:ss.FFFFFFF\", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, &value)"
                "A JSON string with a time"
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

    /// Writes one property for each field. valueOf gives the F# expression of a field's value.
    let private writeMembers (w: CodeWriter) (codec: string) (fields: PlannedField list) (valueOf: int -> PlannedField -> string) =
        fields
        |> List.iteri (fun index field ->
            let value = valueOf index field

            match field.Type with
            | TypeRef.Option inner when field.Skippable ->
                w.Line $"match {value} with"
                w.Line "| Some item ->"
                w.Indent(fun () ->
                    w.Line $"writer.WritePropertyName({literal field.JsonName})"
                    w.Line(writeStmt codec inner "item"))
                w.Line "| None -> ()"
            | TypeRef.ValueOption inner when field.Skippable ->
                w.Line $"match {value} with"
                w.Line "| ValueSome item ->"
                w.Indent(fun () ->
                    w.Line $"writer.WritePropertyName({literal field.JsonName})"
                    w.Line(writeStmt codec inner "item"))
                w.Line "| ValueNone -> ()"
            | _ ->
                w.Line $"writer.WritePropertyName({literal field.JsonName})"
                w.Line(writeStmt codec field.Type value))

    /// Reads the members of the JSON object at the reader into one local for each field (field0, field1, ...).
    /// The variable ignoreMember names a member to skip (the tag of an internally tagged union), or is empty.
    /// finish writes the expression that builds the value from the locals.
    let private readMembers
        (w: CodeWriter)
        (codec: string)
        (lenient: bool)
        (typeName: string)
        (fields: PlannedField list)
        (refuseUnknown: bool)
        (finish: string list -> unit)
        =
        let local index = $"field{index}"
        let seen index = $"seen{index}"

        fields
        |> List.iteri (fun index field ->
            w.Line $"let mutable {local index} = Unchecked.defaultof<{field.Type.FSharpName}>"
            w.Line $"let mutable {seen index} = false")

        objectLoop w typeName (fun () ->
            w.Line "match memberName with"
            w.Line "| name when ignoreMember.Length > 0 && String.Equals(name, ignoreMember, StringComparison.Ordinal) ->"
            w.Indent(fun () ->
                w.Line "if not (reader.TrySkip()) then"
                w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete."))

            fields
            |> List.iteri (fun index field ->
                let names = field.ReadNames |> List.map literal |> String.concat " | "
                w.Line $"| {names} ->"

                w.Indent(fun () ->
                    w.Line $"if {seen index} then"
                    w.Indent(fun () -> fail w $"The member '{field.JsonName}' of {typeName} appears more than once.")
                    w.Line $"{seen index} <- true"

                    // A failure inside the value names this member, so the message holds the path within the value.
                    let read () =
                        w.Line "try"
                        w.Indent(fun () -> w.Line $"{local index} <- {readExpr codec (lenient || field.NumbersFromStrings) field.Type}")
                        w.Line "with :? JsonException as error ->"
                        w.Indent(fun () ->
                            w.Line $"""raise (JsonException({literal $"In the member '{field.JsonName}' of {typeName}: "} + error.Message, error))""")

                    match field.Type with
                    | TypeRef.Option _
                    | TypeRef.ValueOption _ -> read ()
                    | _ when field.AllowNull ->
                        w.Line "if reader.TokenType = JsonTokenType.Null then"
                        w.Indent(fun () -> w.Line $"{local index} <- Unchecked.defaultof<{field.Type.FSharpName}>")
                        w.Line "else"
                        w.Indent read
                    | _ ->
                        w.Line "if reader.TokenType = JsonTokenType.Null then"
                        w.Indent(fun () -> fail w $"The member '{field.JsonName}' of {typeName} is null.")
                        read ()))

            w.Line "| _ ->"
            w.Indent(fun () ->
                if refuseUnknown then
                    w.Line $"""raise (JsonException($"The member '{{memberName}}' is not a member of {typeName}."))"""
                else
                    w.Line "if not (reader.TrySkip()) then"
                    w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")))

        fields
        |> List.iteri (fun index field ->
            match field.Type with
            | TypeRef.Option _ -> w.Line $"if not {seen index} then {local index} <- None"
            | TypeRef.ValueOption _ -> w.Line $"if not {seen index} then {local index} <- ValueNone"
            | _ ->
                w.Line $"if not {seen index} then"
                w.Indent(fun () -> fail w $"The member '{field.JsonName}' of {typeName} is required."))

        finish (fields |> List.mapi (fun index _ -> local index))

    let private recordMethods (w: CodeWriter) (codec: string) (lenient: bool) (t: TypeRef) (declaration: TypeDeclaration) (fields: PlannedField list) (refuseUnknown: bool) =
        let typeName = declaration.Name

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "writer.WriteStartObject()"
            w.Line $"{codec}.WriteMembers_{t.Slug}(writer, value, options)"
            w.Line "writer.WriteEndObject()")

        w.Line $"static member WriteMembers_{t.Slug}(writer: Utf8JsonWriter, value: {t.FSharpName}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            writeMembers w codec fields (fun _ field -> $"value.{field.Name}"))

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {t.FSharpName} ="
        w.Indent(fun () -> w.Line $"{codec}.ReadObject_{t.Slug}(&reader, options, \"\")")

        w.Line $"static member ReadObject_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions, ignoreMember: string) : {t.FSharpName} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "if reader.TokenType <> JsonTokenType.StartObject then"
            w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")

            readMembers w codec lenient typeName fields refuseUnknown (fun locals ->
                let assignments =
                    List.zip fields locals |> List.map (fun (field, local) -> $"{field.Name} = {local}") |> String.concat "; "

                w.Line $"({{ {assignments} }}: {t.FSharpName})"))

    let private unionMethods
        (w: CodeWriter)
        (codec: string)
        (lenient: bool)
        (plan: Plan)
        (t: TypeRef)
        (declaration: TypeDeclaration)
        (cases: PlannedCase list)
        (rules: UnionRules)
        (kinds: TokenKind list)
        (casePrefix: string)
        (nullCase: string option)
        =
        let e = rules.Encoding
        let typeName = declaration.Name
        let union = t.FSharpName
        let slug = t.Slug
        let indexed = List.indexed cases

        let construct (case: PlannedCase) (values: string list) =
            match values with
            | [] -> $"{casePrefix}{case.Name}"
            | _ -> $"""{casePrefix}{case.Name}({String.concat ", " values})"""

        let pattern (case: PlannedCase) =
            construct case (case.Fields |> List.mapi (fun index _ -> $"f{index}"))

        let singleCaseUnwrap = e.UnwrapSingleCaseUnions && cases.Length = 1 && cases.Head.Fields.Length = 1
        let unwrapSingle (case: PlannedCase) = e.UnwrapSingleFieldCases && (e.AdjacentTag || e.ExternalTag) && case.Fields.Length = 1

        /// The record type of a case with UnwrapRecordCases: one field of a record of this context.
        let recordOf (case: PlannedCase) =
            match case.Fields with
            | [ field ] when e.UnwrapRecordCases && plan.Records.ContainsKey field.Type.Slug -> Some field.Type
            | _ -> None

        let readField (field: PlannedField) =
            readExpr codec (lenient || field.NumbersFromStrings) field.Type

        /// Reads one positional value into a local, refusing null for a field that is not an option.
        let readPositional (field: PlannedField) (local: string) =
            match field.Type with
            | TypeRef.Option _
            | TypeRef.ValueOption _ -> ()
            | _ ->
                w.Line "if reader.TokenType = JsonTokenType.Null then"
                w.Indent(fun () -> fail w $"A field of {typeName} is null.")

            w.Line $"let {local} = {readField field}"

        let dispatch (call: int -> PlannedCase -> string) =
            w.Line "match caseIndex with"

            for index, case in indexed do
                w.Line $"| {index} -> {call index case}"

            w.Line $"""| _ -> raise (JsonException({literal $"The case of {typeName} is not known."}))"""

        // ---- write ----
        let writePayload (case: PlannedCase) =
            match recordOf case with
            | _ when unwrapSingle case -> w.Line(writeStmt codec case.Fields.Head.Type "f0")
            | Some record -> w.Line $"{codec}.Write_{record.Slug}(writer, f0, options)"
            | None when e.NamedFields ->
                w.Line "writer.WriteStartObject()"
                writeMembers w codec case.Fields (fun index _ -> $"f{index}")
                w.Line "writer.WriteEndObject()"
            | None ->
                w.Line "writer.WriteStartArray()"
                case.Fields |> List.iteri (fun index field -> w.Line(writeStmt codec field.Type $"f{index}"))
                w.Line "writer.WriteEndArray()"

        let writeInline (case: PlannedCase) =
            match recordOf case with
            | Some record -> w.Line $"{codec}.WriteMembers_{record.Slug}(writer, f0, options)"
            | None -> writeMembers w codec case.Fields (fun index _ -> $"f{index}")

        let writeCase (case: PlannedCase) =
            if singleCaseUnwrap || e.SelectByTokenKind then
                w.Line(writeStmt codec case.Fields.Head.Type "f0")
            elif case.Fields.IsEmpty && e.UnwrapFieldlessTags then
                w.Line $"writer.WriteStringValue({literal case.Tag})"
            elif e.ExternalTag then
                w.Line "writer.WriteStartObject()"
                w.Line $"writer.WritePropertyName({literal case.Tag})"

                if case.Fields.IsEmpty then
                    if e.NamedFields then
                        w.Line "writer.WriteStartObject()"
                        w.Line "writer.WriteEndObject()"
                    else
                        w.Line "writer.WriteStartArray()"
                        w.Line "writer.WriteEndArray()"
                else
                    writePayload case

                w.Line "writer.WriteEndObject()"
            elif e.InternalTag && not e.NamedFields then
                w.Line "writer.WriteStartArray()"
                w.Line $"writer.WriteStringValue({literal case.Tag})"
                case.Fields |> List.iteri (fun index field -> w.Line(writeStmt codec field.Type $"f{index}"))
                w.Line "writer.WriteEndArray()"
            elif e.InternalTag then
                w.Line "writer.WriteStartObject()"
                w.Line $"writer.WriteString({literal rules.TagName}, {literal case.Tag})"
                writeInline case
                w.Line "writer.WriteEndObject()"
            elif e.Untagged then
                w.Line "writer.WriteStartObject()"
                writeInline case
                w.Line "writer.WriteEndObject()"
            else
                w.Line "writer.WriteStartObject()"
                w.Line $"writer.WriteString({literal rules.TagName}, {literal case.Tag})"

                if not case.Fields.IsEmpty then
                    w.Line $"writer.WritePropertyName({literal rules.FieldsName})"
                    writePayload case

                w.Line "writer.WriteEndObject()"

        w.Line $"static member Write_{slug}(writer: Utf8JsonWriter, value: {union}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "match value with"

            for case in cases do
                w.Line $"| {pattern case} ->"

                if Some case.Name = nullCase then
                    w.Indent(fun () -> w.Line "writer.WriteNullValue()")
                else
                    w.Indent(fun () -> writeCase case))

        // ---- case lookup ----
        w.Line $"static member CaseOf_{slug}(tag: string) : int ="
        w.Indent(fun () ->
            if rules.TagCaseInsensitive then
                for index, case in indexed do
                    let test =
                        case.ReadTags
                        |> List.map (fun tag -> $"String.Equals(tag, {literal tag}, StringComparison.OrdinalIgnoreCase)")
                        |> String.concat " || "

                    w.Line $"""{(if index = 0 then "if" else "elif")} {test} then {index}"""

                w.Line "else -1"
            else
                w.Line "match tag with"

                for index, case in indexed do
                    w.Line $"""| {case.ReadTags |> List.map literal |> String.concat " | "} -> {index}"""

                w.Line "| _ -> -1")

        // ---- per-case readers ----
        for index, case in indexed do
            // The payload of AdjacentTag (the Fields member) and of ExternalTag (the one member's value).
            w.Line $"static member Args_{slug}_{index}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {union} ="
            w.Indent(fun () ->
                w.Line "ignore options"
                w.Line "let ignoreMember = \"\""
                w.Line "ignore ignoreMember"

                match recordOf case with
                | _ when case.Fields.IsEmpty ->
                    w.Line "if not (reader.TrySkip()) then"
                    w.Indent(fun () -> fail w $"The JSON value for {typeName} is not complete.")
                    w.Line(construct case [])
                | _ when unwrapSingle case ->
                    readPositional case.Fields.Head "value0"
                    w.Line(construct case [ "value0" ])
                | Some record -> w.Line(construct case [ $"{codec}.Read_{record.Slug}(&reader, options)" ])
                | None when e.NamedFields ->
                    w.Line "if reader.TokenType <> JsonTokenType.StartObject then"
                    w.Indent(fun () -> fail w $"A JSON object with the fields of {typeName}.{case.Name} was expected.")
                    readMembers w codec lenient $"{typeName}.{case.Name}" case.Fields rules.RefuseUnknownMembers (fun locals -> w.Line(construct case locals))
                | None ->
                    w.Line "if reader.TokenType <> JsonTokenType.StartArray then"
                    w.Indent(fun () -> fail w $"A JSON array with the fields of {typeName}.{case.Name} was expected.")

                    case.Fields
                    |> List.iteri (fun position field ->
                        w.Line "if not (reader.Read()) || reader.TokenType = JsonTokenType.EndArray then"
                        w.Indent(fun () -> fail w $"{typeName}.{case.Name} has {case.Fields.Length} fields.")
                        readPositional field $"value{position}")

                    w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndArray then"
                    w.Indent(fun () -> fail w $"{typeName}.{case.Name} has {case.Fields.Length} fields.")
                    w.Line(construct case (case.Fields |> List.mapi (fun position _ -> $"value{position}"))))

            // The fields after the tag in the array of InternalTag. The reader is on the tag.
            w.Line $"static member Items_{slug}_{index}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {union} ="
            w.Indent(fun () ->
                w.Line "ignore options"

                case.Fields
                |> List.iteri (fun position field ->
                    w.Line "if not (reader.Read()) || reader.TokenType = JsonTokenType.EndArray then"
                    w.Indent(fun () -> fail w $"{typeName}.{case.Name} has {case.Fields.Length} fields.")
                    readPositional field $"value{position}")

                w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndArray then"
                w.Indent(fun () -> fail w $"{typeName}.{case.Name} has {case.Fields.Length} fields.")
                w.Line(construct case (case.Fields |> List.mapi (fun position _ -> $"value{position}"))))

            // The fields as members of the object at the reader, which can also hold the tag (InternalTag, Untagged).
            w.Line $"static member Inline_{slug}_{index}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions, ignoreMember: string) : {union} ="
            w.Indent(fun () ->
                w.Line "ignore options"

                match recordOf case with
                | Some record -> w.Line(construct case [ $"{codec}.ReadObject_{record.Slug}(&reader, options, ignoreMember)" ])
                | None ->
                    w.Line "if reader.TokenType <> JsonTokenType.StartObject then"
                    w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")
                    readMembers w codec lenient $"{typeName}.{case.Name}" case.Fields rules.RefuseUnknownMembers (fun locals -> w.Line(construct case locals)))

        // ---- read ----
        let scanTag () =
            w.Line "let mutable scan = reader"
            w.Line "let mutable caseIndex = -1"
            w.Line "let mutable searching = true"
            w.Line "let mutable first = true"
            w.Line "while searching do"
            w.Indent(fun () ->
                w.Line "if not (scan.Read()) then"
                w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")
                w.Line "match scan.TokenType with"
                w.Line "| JsonTokenType.EndObject -> searching <- false"
                w.Line "| JsonTokenType.PropertyName ->"
                w.Indent(fun () ->
                    w.Line $"let isTag = scan.ValueTextEquals({literal rules.TagName})"

                    if not e.AllowUnorderedTag then
                        w.Line "if first && not isTag then"
                        w.Indent(fun () -> fail w $"The member '{rules.TagName}' of {typeName} must come first.")

                    w.Line "first <- false"
                    w.Line "if not (scan.Read()) then"
                    w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")
                    w.Line "if isTag then"
                    w.Indent(fun () ->
                        w.Line "match (if scan.TokenType = JsonTokenType.String then scan.GetString() else null) with"
                        w.Line $"""| null -> raise (JsonException({literal $"The member '{rules.TagName}' of {typeName} must be a string."}))"""
                        w.Line "| tag ->"
                        w.Indent(fun () ->
                            w.Line $"caseIndex <- {codec}.CaseOf_{slug}(tag)"
                            w.Line "if caseIndex < 0 then"
                            w.Indent(fun () -> w.Line $"""raise (JsonException($"'{{tag}}' is not a case of {typeName}."))"""))
                        w.Line "searching <- false")
                    w.Line "elif not (scan.TrySkip()) then"
                    w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete."))
                w.Line $"""| _ -> raise (JsonException({literal $"A member name of {typeName} was expected."}))""")
            w.Line "if caseIndex < 0 then"
            w.Indent(fun () -> fail w $"The member '{rules.TagName}' of {typeName} is required.")

        let fieldlessFromString () =
            w.Line "match reader.GetString() with"
            w.Line $"""| null -> raise (JsonException({literal $"A case name of {typeName} was expected."}))"""
            w.Line "| tag ->"
            w.Indent(fun () ->
                w.Line $"match {codec}.CaseOf_{slug}(tag) with"

                for index, case in indexed do
                    if case.Fields.IsEmpty then
                        w.Line $"| {index} -> {construct case []}"

                w.Line $"""| -1 -> raise (JsonException($"'{{tag}}' is not a case of {typeName}."))"""
                w.Line $"""| _ -> raise (JsonException($"The case '{{tag}}' of {typeName} has fields, so it cannot be a string."))""")

        w.Line $"static member Read_{slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {union} ="
        w.Indent(fun () ->
            w.Line "ignore options"

            if singleCaseUnwrap then
                let case = cases.Head
                readPositional case.Fields.Head "value0"
                w.Line(construct case [ "value0" ])
            elif e.SelectByTokenKind then
                w.Line "match reader.TokenType with"

                for (case, kind) in List.zip cases kinds do
                    let tokens =
                        match kind with
                        | TokenKind.String -> "JsonTokenType.String"
                        | TokenKind.Number -> "JsonTokenType.Number"
                        | TokenKind.Boolean -> "JsonTokenType.True | JsonTokenType.False"
                        | TokenKind.Array -> "JsonTokenType.StartArray"
                        | TokenKind.Object -> "JsonTokenType.StartObject"

                    w.Line $"| {tokens} -> {construct case [ readField case.Fields.Head ]}"

                w.Line $"""| _ -> raise (JsonException({literal $"No case of {typeName} has a value of this JSON kind."}))"""
            else
                match nullCase with
                | Some name ->
                    w.Line $"if reader.TokenType = JsonTokenType.Null then {casePrefix}{name}"
                    w.Line "elif reader.TokenType = JsonTokenType.String then"
                | None -> w.Line "if reader.TokenType = JsonTokenType.String then"

                w.Indent(fun () ->
                    if e.UnwrapFieldlessTags then
                        fieldlessFromString ()
                    else
                        fail w $"A JSON object for {typeName} was expected.")

                if e.ExternalTag then
                    w.Line "elif reader.TokenType <> JsonTokenType.StartObject then"
                    w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")
                    w.Line "else"
                    w.Indent(fun () ->
                        w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.PropertyName then"
                        w.Indent(fun () -> fail w $"A JSON object with one member, a case name of {typeName}, was expected.")
                        w.Line $"let caseIndex = match reader.GetString() with | null -> -1 | tag -> {codec}.CaseOf_{slug}(tag)"
                        w.Line "if caseIndex < 0 then"
                        w.Indent(fun () -> fail w $"The member is not a case of {typeName}.")
                        w.Line "reader.Read() |> ignore"
                        w.Line "let result ="
                        w.Indent(fun () -> dispatch (fun index _ -> $"{codec}.Args_{slug}_{index}(&reader, options)"))
                        w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.EndObject then"
                        w.Indent(fun () -> fail w $"A JSON object with one member, a case name of {typeName}, was expected.")
                        w.Line "result")
                elif e.InternalTag && not e.NamedFields then
                    w.Line "elif reader.TokenType <> JsonTokenType.StartArray then"
                    w.Indent(fun () -> fail w $"A JSON array for {typeName} was expected.")
                    w.Line "else"
                    w.Indent(fun () ->
                        w.Line "if not (reader.Read()) || reader.TokenType <> JsonTokenType.String then"
                        w.Indent(fun () -> fail w $"A JSON array that starts with a case name of {typeName} was expected.")
                        w.Line $"let caseIndex = match reader.GetString() with | null -> -1 | tag -> {codec}.CaseOf_{slug}(tag)"
                        dispatch (fun index _ -> $"{codec}.Items_{slug}_{index}(&reader, options)"))
                elif e.InternalTag then
                    w.Line "elif reader.TokenType <> JsonTokenType.StartObject then"
                    w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")
                    w.Line "else"
                    w.Indent(fun () ->
                        scanTag ()
                        dispatch (fun index _ -> $"{codec}.Inline_{slug}_{index}(&reader, options, {literal rules.TagName})"))
                elif e.Untagged then
                    w.Line "elif reader.TokenType <> JsonTokenType.StartObject then"
                    w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")
                    w.Line "else"
                    w.Indent(fun () ->
                        let fieldless = indexed |> List.filter (fun (_, case) -> case.Fields.IsEmpty)
                        w.Line "let mutable scan = reader"
                        w.Line "if not (scan.Read()) then"
                        w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")
                        w.Line "let caseIndex ="
                        w.Indent(fun () ->
                            w.Line "match scan.TokenType with"

                            match fieldless with
                            | [ (index, _) ] -> w.Line $"| JsonTokenType.EndObject -> {index}"
                            | _ -> ()

                            w.Line "| JsonTokenType.PropertyName ->"
                            w.Indent(fun () ->
                                w.Line "match scan.GetString() with"

                                for index, case in indexed do
                                    let names =
                                        match recordOf case with
                                        | Some record ->
                                            match plan.Records.TryFind record.Slug with
                                            | Some(_, fields, _) -> fields |> List.collect (fun f -> f.ReadNames)
                                            | None -> []
                                        | None -> case.Fields |> List.collect (fun f -> f.ReadNames)

                                    if not names.IsEmpty then
                                        w.Line $"""| {names |> List.map literal |> String.concat " | "} -> {index}"""

                                w.Line "| _ -> -1")
                            w.Line "| _ -> -1")
                        w.Line "if caseIndex < 0 then"
                        w.Indent(fun () -> fail w $"No case of {typeName} matches the members of the JSON object.")
                        dispatch (fun index _ -> $"{codec}.Inline_{slug}_{index}(&reader, options, \"\")"))
                else
                    w.Line "elif reader.TokenType <> JsonTokenType.StartObject then"
                    w.Indent(fun () -> fail w $"A JSON object for {typeName} was expected.")
                    w.Line "else"
                    w.Indent(fun () ->
                        scanTag ()
                        w.Line $"let mutable result = Unchecked.defaultof<{union}>"
                        w.Line "let mutable hasResult = false"

                        objectLoop w typeName (fun () ->
                            w.Line "match memberName with"
                            w.Line $"| {literal rules.TagName} ->"
                            w.Indent(fun () ->
                                w.Line "if not (reader.TrySkip()) then"
                                w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete."))
                            w.Line $"| {literal rules.FieldsName} ->"
                            w.Indent(fun () ->
                                w.Line "if hasResult then"
                                w.Indent(fun () -> fail w $"The member '{rules.FieldsName}' of {typeName} appears more than once.")
                                w.Line "result <-"
                                w.Indent(fun () -> dispatch (fun index _ -> $"{codec}.Args_{slug}_{index}(&reader, options)"))
                                w.Line "hasResult <- true")
                            w.Line "| _ ->"
                            w.Indent(fun () ->
                                if rules.RefuseUnknownMembers then
                                    w.Line $"""raise (JsonException($"The member '{{memberName}}' is not a member of {typeName}."))"""
                                else
                                    w.Line "if not (reader.TrySkip()) then"
                                    w.Indent(fun () -> fail w $"The JSON object for {typeName} is not complete.")))

                        w.Line "if not hasResult then"
                        w.Indent(fun () ->
                            w.Line "result <-"
                            w.Indent(fun () ->
                                w.Line "match caseIndex with"

                                for index, case in indexed do
                                    if case.Fields.IsEmpty then
                                        w.Line $"| {index} -> {construct case []}"

                                w.Line $"""| _ -> raise (JsonException({literal $"The member '{rules.FieldsName}' of {typeName} is required."}))"""))
                        w.Line "result"))

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
            | TypeRef.Primitive Primitive.String
            | TypeRef.Parsed _ -> true
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
                    match key with
                    | TypeRef.Parsed _ -> w.Line $"writer.WritePropertyName({codec}.Text_{key.Slug}(pair.Key))"
                    | _ -> w.Line "writer.WritePropertyName(pair.Key)"

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

                        match key with
                        | TypeRef.Parsed _ ->
                            w.Line "let key ="
                            w.Indent(fun () ->
                                w.Line $"try {codec}.Parse_{key.Slug}(name)"
                                w.Line "with :? JsonException as error -> raise (JsonException($\"The key '{name}': {error.Message}\", error))")
                            w.Line $"pairs.Add((key, {readExpr codec lenient value}))"
                        | _ -> w.Line $"pairs.Add((name, {readExpr codec lenient value}))"))
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

    let private parsedMethods (w: CodeWriter) (plan: Plan) (codec: string) (t: TypeRef) (name: string) (spec: ParsedSpec) =
        // Static members are reached through an abbreviation, so a case with the name of its type does not hide the type.
        let alias = $"{plan.Settings.Name.Value}Types.T_{t.Slug}"

        w.Line $"static member Parse_{t.Slug}(text: string) : {name} ="
        w.Indent(fun () ->
            if spec.UsesTryParse then
                w.Line $"let mutable value = Unchecked.defaultof<{name}>"
                w.Line $"if {alias}.{spec.ParseMember}(text, &value) then value"
                w.Line $"""else raise (JsonException($"'{{text}}' is not a valid {name}."))"""
            else
                w.Line $"match {alias}.{spec.ParseMember} text with"
                w.Line "| Ok value -> value"
                w.Line "| Error failure -> raise (JsonException(string failure))")

        w.Line $"static member Text_{t.Slug}(value: {name}) : string ="
        w.Indent(fun () ->
            if spec.TextMember = "ToString" then
                w.Line "string value"
            else
                w.Line $"value.{spec.TextMember}")

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {name}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line $"writer.WriteStringValue({codec}.Text_{t.Slug}(value))")

        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {name} ="
        w.Indent(fun () ->
            w.Line "ignore options"
            w.Line "match (if reader.TokenType = JsonTokenType.String then reader.GetString() else null) with"
            w.Line $"""| null -> raise (JsonException({literal $"A JSON string for {name} was expected."}))"""
            w.Line $"| text -> {codec}.Parse_{t.Slug}(text)")

    let private customMethods (w: CodeWriter) (plan: Plan) (t: TypeRef) (name: string) =
        let converter = $"{plan.Settings.Name.Value}Converters.c_{t.Slug}"

        w.Line $"static member Write_{t.Slug}(writer: Utf8JsonWriter, value: {name}, options: JsonSerializerOptions) : unit ="
        w.Indent(fun () -> w.Line $"{converter}.Write(writer, value, options)")
        w.Line $"static member Read_{t.Slug}(reader: byref<Utf8JsonReader>, options: JsonSerializerOptions) : {name} ="
        w.Indent(fun () ->
            w.Line $"match box ({converter}.Read(&reader, typeof<{name}>, options)) with"
            w.Line $"""| null -> raise (JsonException({literal $"The converter returned null for {name}."}))"""
            w.Line $"| value -> unbox<{name}> value")

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

        let customs =
            allShapes plan
            |> List.choose (function
                | TypeRef.Custom(name, converter) as t -> Some(t, name, converter)
                | _ -> None)

        if not customs.IsEmpty then
            w.Line "/// <summary>One instance of each user converter, created with no reflection.</summary>"
            w.Line $"module internal {plan.Settings.Name.Value}Converters ="
            w.Indent(fun () ->
                for t, name, converter in customs do
                    w.Line $"let c_{t.Slug}: JsonConverter<{name}> = {converter}()")
            w.Blank()

        let parsed =
            allShapes plan
            |> List.choose (function
                | TypeRef.Parsed(name, _) as t -> Some(t.Slug, name)
                | _ -> None)

        if not plan.Unions.IsEmpty || not parsed.IsEmpty then
            w.Line "/// <summary>Abbreviations of the unions and parsed values, to reach their cases and static members.</summary>"
            w.Line $"module internal {plan.Settings.Name.Value}Types ="
            w.Indent(fun () ->
                for KeyValue(slug, (declaration, _, _, _)) in plan.Unions do
                    w.Line $"type U_{slug} = {declaration.QualifiedName}"

                for slug, name in parsed do
                    w.Line $"type T_{slug} = {name}")
            w.Blank()

        let all = allShapes plan

        w.Line "/// <summary>Read and write methods for every shape of the context.</summary>"
        w.Line "[<AbstractClass; Sealed>]"
        w.Line $"type internal {codec} ="

        w.Indent(fun () ->
            for t in all do
                match t with
                | TypeRef.Primitive p -> primitiveMethods w codec p
                | TypeRef.Option inner when plan.Settings.Unions.Encoding.UnwrapOption -> optionMethods w codec lenient t inner "Some" "None"
                | TypeRef.ValueOption inner when plan.Settings.Unions.Encoding.UnwrapOption -> optionMethods w codec lenient t inner "ValueSome" "ValueNone"
                | TypeRef.Option inner
                | TypeRef.ValueOption inner ->
                    // Without UnwrapOption, an option is a union in the context encoding (None is still null).
                    let isOption = (match t with TypeRef.Option _ -> true | _ -> false)
                    let some, none = if isOption then "Some", "None" else "ValueSome", "ValueNone"
                    let rules = plan.Settings.Unions

                    // FSharp.Core: Some of Value: 'T is named; ValueSome of 'T is not.
                    let naming = defaultArg rules.FieldNaming plan.Settings.Records.Naming

                    let fieldName =
                        if isOption then naming.Apply "Value"
                        elif rules.Encoding.FieldNamesFromTypes then naming.Apply(defaultArg (Resolve.clrName inner) "Item")
                        else naming.Apply "Item"

                    let item =
                        {
                            Name = "Value"
                            JsonName = fieldName
                            ReadNames = [ fieldName ]
                            Type = inner
                            Skippable = false
                            AllowNull = false
                            NumbersFromStrings = false
                        }

                    let cases =
                        [ { Name = none; Tag = rules.TagNaming.Apply none; ReadTags = [ rules.TagNaming.Apply none ]; Fields = [] }
                          { Name = some; Tag = rules.TagNaming.Apply some; ReadTags = [ rules.TagNaming.Apply some ]; Fields = [ item ] } ]

                    let declaration =
                        { Name = (if isOption then "option" else "voption"); Path = []; Parameters = []; Shape = TypeShape.Other; Attributes = []; IsPrivate = false; Location = { Line = 0; Column = 0 } }

                    unionMethods w codec lenient plan t declaration cases rules [] "" (if isOption then Some none else None)
                | TypeRef.Sequence(kind, inner) -> sequenceMethods w codec lenient t kind inner
                | TypeRef.Map(kind, key, value) -> mapMethods w codec lenient plan.Settings.MapFormat t kind key value
                | TypeRef.Tuple items -> tupleMethods w codec lenient t items
                | TypeRef.External _ -> externalMethods w t
                | TypeRef.Parsed(name, spec) -> parsedMethods w plan codec t name spec
                | TypeRef.Custom(name, _) -> customMethods w plan t name
                | TypeRef.Local declaration ->
                    match plan.Records.TryFind t.Slug, plan.Enums.TryFind t.Slug, plan.Unions.TryFind t.Slug with
                    | Some(_, fields, refuseUnknown), _, _ -> recordMethods w codec lenient t declaration fields refuseUnknown
                    | _, Some _, _ -> enumMethods w t declaration
                    | _, _, Some(_, cases, rules, kinds) ->
                        // Cases are reached through an abbreviation, so a case with the name of its type does not hide the type.
                        unionMethods w codec lenient plan t declaration cases rules kinds $"{plan.Settings.Name.Value}Types.U_{t.Slug}." None
                    | None, None, None -> ()

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
            allShapes plan
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
