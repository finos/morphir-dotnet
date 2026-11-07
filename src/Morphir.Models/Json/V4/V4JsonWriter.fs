namespace Morphir.Json.V4

open System
open System.Buffers
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Text.Encodings.Web
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4

/// <summary>Why an IR v4 document or node was not written.</summary>
type V4WriteError = {
    /// <summary>A stable rule identifier, such as <c>invalid_name</c>.</summary>
    Rule: string
    /// <summary>Where the problem is, as a JSON path into the output.</summary>
    Location: string
    /// <summary>A readable description.</summary>
    Message: string
}

exception private V4WriteFailure of V4WriteError

/// <summary>
/// Writes the IR v4 model as canonical JSON (IRW-1). A node with no attributes takes its compact spelling, and a
/// node with attributes takes its expanded spelling with <c>attributes</c> as the first member. Members are
/// written in a fixed order, names use the <c>CanonicalNames</c> writers, and every number lexeme comes from the
/// model, never from a double. <c>V4Json</c> reads the output back to an equal model. Content that a reader
/// refuses, such as a name with no canonical spelling, is refused with an error and no output.
/// </summary>
[<RequireQualifiedAccess>]
module V4JsonWriter =
    let private fail rule location message : 'a =
        raise (
            V4WriteFailure
                {
                    Rule = rule
                    Location = location
                    Message = message
                }
        )

    let private at location name = V4JsonDecoding.memberAt location name
    let private item location index = V4JsonDecoding.itemAt location index

    /// Returns text that a JSON writer can encode, refusing null and a lone surrogate.
    let private checkedText location (text: string) =
        if isNull (box text) then
            fail "invalid_text" location "The text is null."

        let mutable index = 0

        while index < text.Length do
            if
                Char.IsHighSurrogate text[index]
                && index + 1 < text.Length
                && Char.IsLowSurrogate text[index + 1]
            then
                index <- index + 2
            elif Char.IsSurrogate text[index] then
                fail "invalid_text" location "The text holds a lone surrogate."
            else
                index <- index + 1

        text

    let private stringValue (json: Utf8JsonWriter) location (text: string) =
        json.WriteStringValue(checkedText location text)

    let private propertyName (json: Utf8JsonWriter) location (name: string) =
        json.WritePropertyName(checkedText location name)

    /// Spells a name, path or FQName, and refuses one whose spelling reads back as another value.
    let private spelled location parse toText (value: 't) =
        let text = toText value

        match parse text with
        | Ok parsed when parsed = value -> text
        | _ ->
            fail
                "invalid_name"
                location
                $"The name spelled '{text}' has no canonical spelling that reads back as the same name."

    let private nameText location (name: Name) =
        spelled location CanonicalNames.parseName CanonicalNames.nameToString name

    let private pathText location (path: Path) =
        spelled location CanonicalNames.parsePath CanonicalNames.pathToString path

    let private fqNameText location (fqName: FQName) =
        spelled location CanonicalNames.parseFQName CanonicalNames.fqNameToString fqName

    /// Whether text is a JSON number: -? (0 | [1-9][0-9]*) (. [0-9]+)? ([eE] [+-]? [0-9]+)?
    let private isJsonNumber (text: string) =
        let length = if isNull (box text) then 0 else text.Length

        let isDigitAt position =
            position < length
            && Char.IsAsciiDigit text[position]

        let rec digitsEnd position =
            if isDigitAt position then
                digitsEnd (
                    position
                    + 1
                )
            else
                position

        let afterSign =
            if
                length > 0
                && text[0] = '-'
            then
                1
            else
                0

        let afterInteger =
            if
                isDigitAt afterSign
                && text[afterSign] = '0'
            then
                afterSign
                + 1
            elif isDigitAt afterSign then
                digitsEnd afterSign
            else
                -1

        let afterFraction =
            if afterInteger < 0 then
                -1
            elif
                afterInteger < length
                && text[afterInteger] = '.'
            then
                let fractionEnd =
                    digitsEnd (
                        afterInteger
                        + 1
                    )

                if
                    fractionEnd > afterInteger
                                  + 1
                then
                    fractionEnd
                else
                    -1
            else
                afterInteger

        let afterExponent =
            if afterFraction < 0 then
                -1
            elif
                afterFraction < length
                && (text[afterFraction] = 'e'
                    || text[afterFraction] = 'E')
            then
                let signEnd =
                    if
                        afterFraction
                        + 1 < length
                        && (text[afterFraction
                                 + 1] = '+'
                            || text[afterFraction
                                    + 1] = '-')
                    then
                        afterFraction
                        + 2
                    else
                        afterFraction
                        + 1

                let exponentEnd = digitsEnd signEnd

                if exponentEnd > signEnd then exponentEnd else -1
            else
                afterFraction

        length > 0
        && afterExponent = length

    /// Writes a number lexeme exactly as the model holds it.
    let private rawNumber (json: Utf8JsonWriter) location (lexeme: string) =
        if not (isJsonNumber lexeme) then
            fail "invalid_number" location $"'{lexeme}' is not a JSON number."

        json.WriteRawValue(lexeme, false)

    let private objectOf (json: Utf8JsonWriter) (writeMembers: unit -> unit) =
        json.WriteStartObject()
        writeMembers ()
        json.WriteEndObject()

    /// Writes { "tag": payload }.
    let private tagged (json: Utf8JsonWriter) (tag: string) (writePayload: unit -> unit) =
        json.WriteStartObject()
        json.WritePropertyName tag
        writePayload ()
        json.WriteEndObject()

    /// Writes one member, giving the writer the member's location.
    let private writeMember
        (json: Utf8JsonWriter)
        location
        (memberName: string)
        (write: string -> unit)
        =
        json.WritePropertyName memberName
        write (at location memberName)

    let private arrayOf
        (json: Utf8JsonWriter)
        location
        (write: string -> 'a -> unit)
        (items: 'a list)
        =
        json.WriteStartArray()

        items
        |> List.iteri (fun index element -> write (item location index) element)

        json.WriteEndArray()

    /// Writes an object keyed by canonical names in entry order, refusing a name that appears twice.
    let private nameMap
        (json: Utf8JsonWriter)
        location
        (write: string -> 'v -> unit)
        (entries: (Name * 'v) list)
        =
        let seen = HashSet<string>(StringComparer.Ordinal)

        objectOf
            json
            (fun () ->
                for key, element in entries do
                    let text = nameText location key

                    if not (seen.Add text) then
                        fail
                            "duplicate_member"
                            location
                            $"The name '{text}' appears more than once."

                    json.WritePropertyName text
                    write (at location text) element
            )

    /// Writes an object keyed by canonical paths in entry order.
    let private pathMap
        (json: Utf8JsonWriter)
        location
        (pathOf: 'k -> Path)
        (write: string -> 'v -> unit)
        entries
        =
        objectOf
            json
            (fun () ->
                for key, element in entries do
                    let text = pathText location (pathOf key)
                    json.WritePropertyName text
                    write (at location text) element
            )

    let private doc (json: Utf8JsonWriter) location (text: string option) =
        text
        |> Option.iter (fun value ->
            writeMember json location "doc" (fun here -> stringValue json here value)
        )

    let rec private documentValue (json: Utf8JsonWriter) location (value: DocumentValue) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        match value with
        | DocumentNull -> json.WriteNullValue()
        | DocumentBool flag -> json.WriteBooleanValue flag
        | DocumentNumber lexeme -> rawNumber json location lexeme
        | DocumentString text -> stringValue json location text
        | DocumentArray items -> arrayOf json location (documentValue json) items
        | DocumentObject members ->
            objectOf
                json
                (fun () ->
                    for key, element in OrderedMap.toList members do
                        propertyName json location key
                        documentValue json (at location key) element
                )

    /// Constraints and extensions are document objects.
    let private documentObject (json: Utf8JsonWriter) location (value: DocumentValue) =
        match value with
        | DocumentObject _ -> documentValue json location value
        | _ -> fail "invalid_type" location "Constraints and extensions are document objects."

    let private sourceLocation (json: Utf8JsonWriter) (source: SourceLocation) =
        objectOf
            json
            (fun () ->
                json.WriteNumber("startLine", source.StartLine)
                json.WriteNumber("startColumn", source.StartColumn)
                json.WriteNumber("endLine", source.EndLine)
                json.WriteNumber("endColumn", source.EndColumn)
            )

    let private optionalMember
        (json: Utf8JsonWriter)
        location
        memberName
        (write: string -> 'a -> unit)
        value
        =
        value
        |> Option.iter (fun present ->
            writeMember json location memberName (fun here -> write here present)
        )

    let private typeAttributes (json: Utf8JsonWriter) location (attributes: TypeAttributes) =
        if
            attributes
            <> TypeAttributes.empty
        then
            writeMember
                json
                location
                "attributes"
                (fun here ->
                    objectOf
                        json
                        (fun () ->
                            optionalMember
                                json
                                here
                                "source"
                                (fun _ -> sourceLocation json)
                                attributes.Source

                            optionalMember
                                json
                                here
                                "constraints"
                                (documentObject json)
                                attributes.Constraints

                            optionalMember
                                json
                                here
                                "extensions"
                                (documentObject json)
                                attributes.Extensions
                        )
                )

    /// The schema's pattern for a bare type variable, which is narrower than a name.
    let private isBareTypeVariable (input: string) =
        let isWordChar (c: char) =
            (c >= 'a'
             && c <= 'z')
            || (c >= '0'
                && c <= '9')

        input.Length > 0
        && input[0]
           >= 'a'
        && input[0]
           <= 'z'
        && input.Split('-')
           |> Array.forall (fun word ->
               word.Length > 0
               && word
                  |> Seq.forall isWordChar
           )

    let rec private typ (json: Utf8JsonWriter) location (node: Type) =
        RuntimeHelpers.EnsureSufficientExecutionStack()

        /// Writes the expanded payload: attributes first, then the given members.
        let expanded tag attributes (writeMembers: string -> unit) =
            tagged
                json
                tag
                (fun () ->
                    let here = at location tag

                    objectOf
                        json
                        (fun () ->
                            typeAttributes json here attributes
                            writeMembers here
                        )
                )

        let child here memberName (value: Type) =
            writeMember json here memberName (fun inner -> typ json inner value)

        match node with
        | Type.Variable(attributes, variable) ->
            let text = nameText location variable

            if attributes = TypeAttributes.empty then
                if isBareTypeVariable text then
                    json.WriteStringValue text
                else
                    tagged json "Variable" (fun () -> json.WriteStringValue text)
            else
                expanded "Variable" attributes (fun _ -> json.WriteString("name", text))
        | Type.Reference(attributes, fqName, arguments) ->
            let text = fqNameText location fqName

            match attributes = TypeAttributes.empty, arguments with
            | true, [] -> json.WriteStringValue text
            | true, _ ->
                tagged
                    json
                    "Reference"
                    (fun () ->
                        let here = at location "Reference"
                        json.WriteStartArray()
                        json.WriteStringValue text

                        arguments
                        |> List.iteri (fun index argument ->
                            typ json (item here (index + 1)) argument
                        )

                        json.WriteEndArray()
                    )
            | false, _ ->
                expanded
                    "Reference"
                    attributes
                    (fun here ->
                        json.WriteString("fqname", text)

                        if not arguments.IsEmpty then
                            writeMember
                                json
                                here
                                "args"
                                (fun inner -> arrayOf json inner (typ json) arguments)
                    )
        | Type.Tuple(attributes, elements) ->
            if attributes = TypeAttributes.empty then
                tagged
                    json
                    "Tuple"
                    (fun () -> arrayOf json (at location "Tuple") (typ json) elements)
            else
                expanded
                    "Tuple"
                    attributes
                    (fun here ->
                        writeMember
                            json
                            here
                            "elements"
                            (fun inner -> arrayOf json inner (typ json) elements)
                    )
        | Type.Record(attributes, fields) ->
            expanded
                "Record"
                attributes
                (fun here ->
                    writeMember
                        json
                        here
                        "fields"
                        (fun inner -> nameMap json inner (typ json) fields)
                )
        | Type.ExtensibleRecord(attributes, variable, fields) ->
            expanded
                "ExtensibleRecord"
                attributes
                (fun here ->
                    json.WriteString("variable", nameText (at here "variable") variable)

                    writeMember
                        json
                        here
                        "fields"
                        (fun inner -> nameMap json inner (typ json) fields)
                )
        | Type.Function(attributes, parameterType, returnType) ->
            expanded
                "Function"
                attributes
                (fun here ->
                    child here "parameterType" parameterType
                    child here "returnType" returnType
                )
        | Type.Unit attributes -> expanded "Unit" attributes ignore

    let private isOneScalar (input: string) =
        (input.Length = 1
         && not (Char.IsSurrogate input[0]))
        || (input.Length = 2
            && Char.IsSurrogatePair(input[0], input[1]))

    /// The float's lexeme when the model keeps one, and otherwise the shortest text that reads back as the value.
    let private floatLexeme location (value: FloatValue) =
        match value.Lexeme with
        | Some lexeme ->
            if not (isJsonNumber lexeme) then
                fail "invalid_literal" location $"'{lexeme}' is not a JSON number."

            if
                Double.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture)
                <> value.Value
            then
                fail
                    "invalid_literal"
                    location
                    $"The lexeme '{lexeme}' does not spell the float's value."

            lexeme
        | None ->
            let text = value.Value.ToString("R", CultureInfo.InvariantCulture)

            if
                text.IndexOfAny [| '.'; 'e'; 'E' |]
                >= 0
            then
                text
            else
                text
                + ".0"

    /// Writes a literal in its typed wrapper, such as { "IntegerLiteral": 42 }.
    let private literal (json: Utf8JsonWriter) location (value: Literal) =
        match value with
        | BoolLiteral flag -> tagged json "BoolLiteral" (fun () -> json.WriteBooleanValue flag)
        | CharLiteral text ->
            if
                isNull (box text)
                || not (isOneScalar text)
            then
                fail
                    "invalid_literal"
                    (at location "CharLiteral")
                    "A character is one Unicode scalar."

            tagged json "CharLiteral" (fun () -> json.WriteStringValue text)
        | StringLiteral text ->
            tagged
                json
                "StringLiteral"
                (fun () -> stringValue json (at location "StringLiteral") text)
        | IntegerLiteral number ->
            tagged
                json
                "IntegerLiteral"
                (fun () -> json.WriteRawValue(number.ToString(CultureInfo.InvariantCulture), false))
        | FloatLiteral number ->
            let lexeme = floatLexeme (at location "FloatLiteral") number
            tagged json "FloatLiteral" (fun () -> json.WriteRawValue(lexeme, false))
        | DecimalLiteral lexeme ->
            tagged
                json
                "DecimalLiteral"
                (fun () -> json.WriteStringValue(DecimalLexeme.value lexeme))
        | DocumentLiteral document ->
            tagged
                json
                "DocumentLiteral"
                (fun () -> documentValue json (at location "DocumentLiteral") document)

    let private valueAttributes (json: Utf8JsonWriter) location (attributes: ValueAttributes) =
        if
            attributes
            <> ValueAttributes.empty
        then
            writeMember
                json
                location
                "attributes"
                (fun here ->
                    objectOf
                        json
                        (fun () ->
                            optionalMember
                                json
                                here
                                "source"
                                (fun _ -> sourceLocation json)
                                attributes.Source

                            optionalMember
                                json
                                here
                                "inferredType"
                                (typ json)
                                attributes.InferredType

                            optionalMember
                                json
                                here
                                "extensions"
                                (documentObject json)
                                attributes.Extensions
                        )
                )

    /// Writes the expanded payload of a value or pattern node: attributes first, then the given members.
    let private expandedValueNode
        (json: Utf8JsonWriter)
        location
        tag
        (attributes: ValueAttributes)
        (writeMembers: string -> unit)
        =
        tagged
            json
            tag
            (fun () ->
                let here = at location tag

                objectOf
                    json
                    (fun () ->
                        valueAttributes json here attributes
                        writeMembers here
                    )
            )

    let private nameValue (json: Utf8JsonWriter) location (name: Name) =
        json.WriteStringValue(nameText location name)

    let private fqNameValue (json: Utf8JsonWriter) location (fqName: FQName) =
        json.WriteStringValue(fqNameText location fqName)

    let rec private pattern (json: Utf8JsonWriter) location (node: Pattern) =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let expanded = expandedValueNode json location

        let child here memberName (value: Pattern) =
            writeMember json here memberName (fun inner -> pattern json inner value)

        match node with
        | WildcardPattern attributes -> expanded "WildcardPattern" attributes ignore
        | AsPattern(attributes, inner, variable) ->
            expanded
                "AsPattern"
                attributes
                (fun here ->
                    child here "pattern" inner
                    writeMember json here "name" (fun l -> nameValue json l variable)
                )
        | TuplePattern(attributes, patterns) ->
            if attributes = ValueAttributes.empty then
                tagged
                    json
                    "TuplePattern"
                    (fun () -> arrayOf json (at location "TuplePattern") (pattern json) patterns)
            else
                expanded
                    "TuplePattern"
                    attributes
                    (fun here ->
                        writeMember
                            json
                            here
                            "patterns"
                            (fun inner -> arrayOf json inner (pattern json) patterns)
                    )
        | ConstructorPattern(attributes, fqName, patterns) ->
            expanded
                "ConstructorPattern"
                attributes
                (fun here ->
                    writeMember json here "fqname" (fun l -> fqNameValue json l fqName)

                    writeMember
                        json
                        here
                        "patterns"
                        (fun inner -> arrayOf json inner (pattern json) patterns)
                )
        | EmptyListPattern attributes -> expanded "EmptyListPattern" attributes ignore
        | HeadTailPattern(attributes, head, tail) ->
            expanded
                "HeadTailPattern"
                attributes
                (fun here ->
                    child here "head" head
                    child here "tail" tail
                )
        | LiteralPattern(_, DocumentLiteral _) ->
            fail
                "invalid_literal"
                (at location "LiteralPattern")
                "A document cannot be pattern matched."
        | LiteralPattern(attributes, value) ->
            if attributes = ValueAttributes.empty then
                tagged
                    json
                    "LiteralPattern"
                    (fun () -> literal json (at location "LiteralPattern") value)
            else
                expanded
                    "LiteralPattern"
                    attributes
                    (fun here ->
                        writeMember json here "literal" (fun inner -> literal json inner value)
                    )
        | UnitPattern attributes -> expanded "UnitPattern" attributes ignore

    let private holeReason (json: Utf8JsonWriter) location (reason: HoleReason) =
        match reason with
        | UnresolvedReference target ->
            tagged
                json
                "UnresolvedReference"
                (fun () ->
                    let here = at location "UnresolvedReference"

                    objectOf
                        json
                        (fun () ->
                            writeMember json here "target" (fun l -> fqNameValue json l target)
                        )
                )
        | DeletedDuringRefactor txId ->
            tagged
                json
                "DeletedDuringRefactor"
                (fun () ->
                    let here = at location "DeletedDuringRefactor"

                    objectOf
                        json
                        (fun () ->
                            writeMember json here "tx-id" (fun l -> stringValue json l txId)
                        )
                )
        | TypeMismatch(expected, found) ->
            tagged
                json
                "TypeMismatch"
                (fun () ->
                    let here = at location "TypeMismatch"

                    objectOf
                        json
                        (fun () ->
                            writeMember
                                json
                                here
                                "expected"
                                (fun l -> stringValue json l expected)

                            writeMember json here "found" (fun l -> stringValue json l found)
                        )
                )

    let private incompleteness (json: Utf8JsonWriter) location (value: Incompleteness) =
        match value with
        | Incompleteness.Draft -> tagged json "Draft" (fun () -> objectOf json ignore)
        | Incompleteness.Hole(reason, partialBody) ->
            tagged
                json
                "Hole"
                (fun () ->
                    let here = at location "Hole"

                    objectOf
                        json
                        (fun () ->
                            writeMember json here "reason" (fun l -> holeReason json l reason)
                            optionalMember json here "partialBody" (typ json) partialBody
                        )
                )

    let private nativeInfo (json: Utf8JsonWriter) location (info: NativeInfo) =
        let nullary tag =
            tagged json tag (fun () -> objectOf json ignore)

        objectOf
            json
            (fun () ->
                writeMember
                    json
                    location
                    "hint"
                    (fun here ->
                        match info.Hint with
                        | Arithmetic -> nullary "Arithmetic"
                        | Comparison -> nullary "Comparison"
                        | StringOp -> nullary "StringOp"
                        | CollectionOp -> nullary "CollectionOp"
                        | PlatformSpecific platform ->
                            tagged
                                json
                                "PlatformSpecific"
                                (fun () ->
                                    let inner = at here "PlatformSpecific"

                                    objectOf
                                        json
                                        (fun () ->
                                            writeMember
                                                json
                                                inner
                                                "platform"
                                                (fun l -> stringValue json l platform)
                                        )
                                )
                    )

                optionalMember json location "description" (stringValue json) info.Description
            )

    /// Decision 0008: at least one binding, and a platform appears once.
    let private externalBindings (json: Utf8JsonWriter) location (bindings: ExternalBinding list) =
        if bindings.IsEmpty then
            fail "invalid_type" location "An external body needs at least one binding."

        let platforms =
            bindings
            |> List.map (fun binding -> binding.TargetPlatform)

        if
            (List.distinct platforms).Length
            <> platforms.Length
        then
            fail "duplicate_member" location "A target platform appears more than once."

        arrayOf
            json
            location
            (fun here (binding: ExternalBinding) ->
                objectOf
                    json
                    (fun () ->
                        writeMember
                            json
                            here
                            "targetPlatform"
                            (fun l -> stringValue json l binding.TargetPlatform)

                        writeMember
                            json
                            here
                            "externalName"
                            (fun l -> stringValue json l binding.ExternalName)
                    )
            )
            bindings

    let rec private value (json: Utf8JsonWriter) location (node: Value) =
        RuntimeHelpers.EnsureSufficientExecutionStack()
        let expanded = expandedValueNode json location

        /// Writes the compact payload, or the expanded payload that holds it under one member.
        let compact tag attributes (memberName: string) (write: string -> unit) =
            if attributes = ValueAttributes.empty then
                tagged json tag (fun () -> write (at location tag))
            else
                expanded tag attributes (fun here -> writeMember json here memberName write)

        let child here memberName (inner: Value) =
            writeMember json here memberName (fun l -> value json l inner)

        let childPattern here memberName (inner: Pattern) =
            writeMember json here memberName (fun l -> pattern json l inner)

        match node with
        | Value.Literal(attributes, literalValue) ->
            compact "Literal" attributes "literal" (fun here -> literal json here literalValue)
        | Value.Constructor(attributes, fqName) ->
            compact "Constructor" attributes "fqname" (fun here -> fqNameValue json here fqName)
        | Value.Tuple(attributes, elements) ->
            compact
                "Tuple"
                attributes
                "elements"
                (fun here -> arrayOf json here (value json) elements)
        | Value.List(attributes, items) ->
            compact "List" attributes "items" (fun here -> arrayOf json here (value json) items)
        | Value.Record(attributes, fields) ->
            expanded
                "Record"
                attributes
                (fun here ->
                    writeMember
                        json
                        here
                        "fields"
                        (fun inner -> nameMap json inner (value json) fields)
                )
        | Value.Variable(attributes, variable) ->
            compact "Variable" attributes "name" (fun here -> nameValue json here variable)
        | Value.Reference(attributes, fqName) ->
            compact "Reference" attributes "fqname" (fun here -> fqNameValue json here fqName)
        | Value.Field(attributes, target, fieldName) ->
            expanded
                "Field"
                attributes
                (fun here ->
                    child here "target" target
                    writeMember json here "name" (fun l -> nameValue json l fieldName)
                )
        | Value.FieldFunction(attributes, fieldName) ->
            compact "FieldFunction" attributes "name" (fun here -> nameValue json here fieldName)
        | Value.Apply(attributes, func, argument) ->
            expanded
                "Apply"
                attributes
                (fun here ->
                    child here "function" func
                    child here "argument" argument
                )
        | Value.Lambda(attributes, argumentPattern, body) ->
            expanded
                "Lambda"
                attributes
                (fun here ->
                    childPattern here "pattern" argumentPattern
                    child here "body" body
                )
        | Value.LetDefinition(attributes, valueName, definition, inValue) ->
            expanded
                "LetDefinition"
                attributes
                (fun here ->
                    writeMember json here "name" (fun l -> nameValue json l valueName)

                    writeMember
                        json
                        here
                        "definition"
                        (fun l -> documentedValueDefinition json l definition)

                    child here "in" inValue
                )
        | Value.LetRecursion(attributes, definitions, inValue) ->
            expanded
                "LetRecursion"
                attributes
                (fun here ->
                    writeMember
                        json
                        here
                        "definitions"
                        (fun inner ->
                            nameMap
                                json
                                inner
                                (documentedValueDefinition json)
                                (OrderedMap.toList definitions)
                        )

                    child here "in" inValue
                )
        | Value.Destructure(attributes, destructurePattern, destructured, inValue) ->
            expanded
                "Destructure"
                attributes
                (fun here ->
                    childPattern here "pattern" destructurePattern
                    child here "value" destructured
                    child here "in" inValue
                )
        | Value.IfThenElse(attributes, condition, thenBranch, elseBranch) ->
            expanded
                "IfThenElse"
                attributes
                (fun here ->
                    child here "condition" condition
                    child here "then" thenBranch
                    child here "else" elseBranch
                )
        | Value.PatternMatch(attributes, matched, cases) ->
            expanded
                "PatternMatch"
                attributes
                (fun here ->
                    child here "value" matched

                    writeMember
                        json
                        here
                        "cases"
                        (fun inner ->
                            arrayOf
                                json
                                inner
                                (fun caseLocation (casePattern, body) ->
                                    objectOf
                                        json
                                        (fun () ->
                                            childPattern caseLocation "pattern" casePattern
                                            child caseLocation "body" body
                                        )
                                )
                                cases
                        )
                )
        | Value.UpdateRecord(attributes, target, fields) ->
            expanded
                "UpdateRecord"
                attributes
                (fun here ->
                    child here "target" target

                    writeMember
                        json
                        here
                        "fields"
                        (fun inner -> nameMap json inner (value json) fields)
                )
        | Value.Unit attributes -> expanded "Unit" attributes ignore
        | Value.Hole(attributes, reason, expectedType) ->
            expanded
                "Hole"
                attributes
                (fun here ->
                    writeMember json here "reason" (fun l -> holeReason json l reason)
                    optionalMember json here "expectedType" (typ json) expectedType
                )

    /// Writes a value definition's body variant as a member of an object that is already open.
    and private valueDefinitionBody (json: Utf8JsonWriter) location (definition: ValueDefinition) =
        let body tag (writeMembers: string -> unit) =
            writeMember
                json
                location
                tag
                (fun here ->
                    objectOf
                        json
                        (fun () ->
                            writeMember
                                json
                                here
                                "inputTypes"
                                (fun inner -> nameMap json inner (typ json) definition.InputTypes)

                            writeMembers here
                        )
                )

        let outputType here (output: Type) =
            writeMember json here "outputType" (fun inner -> typ json inner output)

        match definition.Body with
        | ExpressionBody(output, expression) ->
            body
                "ExpressionBody"
                (fun here ->
                    outputType here output
                    writeMember json here "body" (fun inner -> value json inner expression)
                )
        | NativeBody(output, info) ->
            body
                "NativeBody"
                (fun here ->
                    outputType here output
                    writeMember json here "nativeInfo" (fun inner -> nativeInfo json inner info)
                )
        | ExternalBody(output, info) ->
            body
                "ExternalBody"
                (fun here ->
                    outputType here output

                    writeMember
                        json
                        here
                        "externals"
                        (fun inner -> externalBindings json inner info.Externals)

                    optionalMember json here "body" (value json) info.Fallback
                )
        | IncompleteBody(output, reason, partialBody) ->
            body
                "IncompleteBody"
                (fun here ->
                    output
                    |> Option.iter (outputType here)

                    writeMember
                        json
                        here
                        "incompleteness"
                        (fun inner -> incompleteness json inner reason)

                    optionalMember json here "partialBody" (value json) partialBody
                )

    /// Writes a documented value definition flattened: doc first, then the body variant.
    and private documentedValueDefinition
        (json: Utf8JsonWriter)
        location
        (definition: Documented<ValueDefinition>)
        =
        objectOf
            json
            (fun () ->
                doc json location definition.Doc
                valueDefinitionBody json location definition.Value
            )

    let private accessTag (access: Access) =
        match access with
        | Public -> "Public"
        | Private -> "Private"

    /// Writes the access level as the key: { "Public": value }.
    let private accessControlled
        (json: Utf8JsonWriter)
        (write: string -> 't -> unit)
        location
        (controlled: AccessControlled<'t>)
        =
        objectOf
            json
            (fun () ->
                writeMember
                    json
                    location
                    (accessTag controlled.Access)
                    (fun here -> write here controlled.Value)
            )

    let private typeParams (json: Utf8JsonWriter) location (parameters: Name list) =
        arrayOf json location (nameValue json) parameters

    let private constructors (json: Utf8JsonWriter) location (declared: Constructors) =
        let parameter here (parameterName: Name, parameterType: Type) =
            json.WriteStartArray()
            nameValue json (item here 0) parameterName
            typ json (item here 1) parameterType
            json.WriteEndArray()

        nameMap
            json
            location
            (fun here (parameters: (Name * Type) list) -> arrayOf json here parameter parameters)
            (OrderedMap.toList declared)

    /// Writes a tag member whose payload is an object of the given members.
    let private taggedMember (json: Utf8JsonWriter) location tag (writeMembers: string -> unit) =
        writeMember json location tag (fun here -> objectOf json (fun () -> writeMembers here))

    let private documentedTypeDefinition
        (json: Utf8JsonWriter)
        location
        (definition: Documented<TypeDefinition>)
        =
        objectOf
            json
            (fun () ->
                doc json location definition.Doc

                match definition.Value with
                | TypeAliasDefinition(parameters, typeExp) ->
                    taggedMember
                        json
                        location
                        "TypeAliasDefinition"
                        (fun here ->
                            writeMember
                                json
                                here
                                "typeParams"
                                (fun l -> typeParams json l parameters)

                            writeMember json here "typeExp" (fun l -> typ json l typeExp)
                        )
                | CustomTypeDefinition(parameters, access, declared) ->
                    taggedMember
                        json
                        location
                        "CustomTypeDefinition"
                        (fun here ->
                            writeMember
                                json
                                here
                                "typeParams"
                                (fun l -> typeParams json l parameters)

                            json.WriteString("access", accessTag access)

                            writeMember
                                json
                                here
                                "constructors"
                                (fun l -> constructors json l declared)
                        )
                | IncompleteTypeDefinition(parameters, reason, partialTypeExp) ->
                    taggedMember
                        json
                        location
                        "IncompleteTypeDefinition"
                        (fun here ->
                            writeMember
                                json
                                here
                                "typeParams"
                                (fun l -> typeParams json l parameters)

                            writeMember
                                json
                                here
                                "incompleteness"
                                (fun l -> incompleteness json l reason)

                            optionalMember json here "partialTypeExp" (typ json) partialTypeExp
                        )
            )

    /// Writes an annotation: the compact string when it has no arguments or one plain text argument, and the
    /// structured object otherwise.
    let private annotation (json: Utf8JsonWriter) location (declared: Annotation) =
        let name = fqNameText location declared.Name

        match declared.Arguments with
        | [] -> json.WriteStringValue name
        | [ PositionalArgument(Value.Literal(attributes, StringLiteral text)) ] when
            attributes = ValueAttributes.empty
            && not (String.IsNullOrEmpty text)
            ->
            stringValue json location $"{name}:{text}"
        | arguments ->
            objectOf
                json
                (fun () ->
                    json.WriteString("name", name)

                    writeMember
                        json
                        location
                        "arguments"
                        (fun here ->
                            arrayOf
                                json
                                here
                                (fun argumentLocation argument ->
                                    match argument with
                                    | PositionalArgument positional ->
                                        value json argumentLocation positional
                                    | NamedArgument(argumentName, named) ->
                                        objectOf
                                            json
                                            (fun () ->
                                                writeMember
                                                    json
                                                    argumentLocation
                                                    "name"
                                                    (fun l -> nameValue json l argumentName)

                                                writeMember
                                                    json
                                                    argumentLocation
                                                    "value"
                                                    (fun l -> value json l named)
                                            )
                                )
                                arguments
                        )
                )

    /// Writes annotations first, and only when there are some.
    let private annotations (json: Utf8JsonWriter) location (values: Annotation list) =
        if not values.IsEmpty then
            writeMember
                json
                location
                "annotations"
                (fun here -> arrayOf json here (annotation json) values)

    let private documentedTypeSpecification
        (json: Utf8JsonWriter)
        location
        (specification: Documented<TypeSpecification>)
        =
        let parametersOf here parameters =
            writeMember json here "typeParams" (fun l -> typeParams json l parameters)

        objectOf
            json
            (fun () ->
                doc json location specification.Doc

                match specification.Value with
                | TypeAliasSpecification(declared, parameters, typeExp) ->
                    taggedMember
                        json
                        location
                        "TypeAliasSpecification"
                        (fun here ->
                            annotations json here declared
                            parametersOf here parameters
                            writeMember json here "typeExp" (fun l -> typ json l typeExp)
                        )
                | OpaqueTypeSpecification(declared, parameters) ->
                    taggedMember
                        json
                        location
                        "OpaqueTypeSpecification"
                        (fun here ->
                            annotations json here declared

                            if not parameters.IsEmpty then
                                parametersOf here parameters
                        )
                | CustomTypeSpecification(declared, parameters, declaredConstructors) ->
                    taggedMember
                        json
                        location
                        "CustomTypeSpecification"
                        (fun here ->
                            annotations json here declared
                            parametersOf here parameters

                            writeMember
                                json
                                here
                                "constructors"
                                (fun l -> constructors json l declaredConstructors)
                        )
                | DerivedTypeSpecification(declared, parameters, baseType, fromBaseType, toBaseType) ->
                    taggedMember
                        json
                        location
                        "DerivedTypeSpecification"
                        (fun here ->
                            annotations json here declared
                            parametersOf here parameters
                            writeMember json here "baseType" (fun l -> typ json l baseType)

                            writeMember
                                json
                                here
                                "fromBaseType"
                                (fun l -> fqNameValue json l fromBaseType)

                            writeMember
                                json
                                here
                                "toBaseType"
                                (fun l -> fqNameValue json l toBaseType)
                        )
            )

    let private documentedValueSpecification
        (json: Utf8JsonWriter)
        location
        (specification: Documented<ValueSpecification>)
        =
        objectOf
            json
            (fun () ->
                doc json location specification.Doc
                annotations json location specification.Value.Annotations

                if not specification.Value.Inputs.IsEmpty then
                    writeMember
                        json
                        location
                        "inputs"
                        (fun here -> nameMap json here (typ json) specification.Value.Inputs)

                writeMember
                    json
                    location
                    "output"
                    (fun here -> typ json here specification.Value.Output)
            )

    let private moduleDefinition (json: Utf8JsonWriter) location (definition: ModuleDefinition) =
        objectOf
            json
            (fun () ->
                writeMember
                    json
                    location
                    "types"
                    (fun here ->
                        nameMap
                            json
                            here
                            (accessControlled json (documentedTypeDefinition json))
                            (OrderedMap.toList definition.Types)
                    )

                writeMember
                    json
                    location
                    "values"
                    (fun here ->
                        nameMap
                            json
                            here
                            (accessControlled json (documentedValueDefinition json))
                            (OrderedMap.toList definition.Values)
                    )

                doc json location definition.Doc
            )

    let private moduleSpecification
        (json: Utf8JsonWriter)
        location
        (specification: ModuleSpecification)
        =
        objectOf
            json
            (fun () ->
                annotations json location specification.Annotations

                writeMember
                    json
                    location
                    "types"
                    (fun here ->
                        nameMap
                            json
                            here
                            (documentedTypeSpecification json)
                            (OrderedMap.toList specification.Types)
                    )

                writeMember
                    json
                    location
                    "values"
                    (fun here ->
                        nameMap
                            json
                            here
                            (documentedValueSpecification json)
                            (OrderedMap.toList specification.Values)
                    )

                doc json location specification.Doc
            )

    let private modulePathOf (ModulePath path) = path
    let private packagePathOf (PackageName path) = path

    let private packageDefinition (json: Utf8JsonWriter) location (definition: PackageDefinition) =
        objectOf
            json
            (fun () ->
                writeMember
                    json
                    location
                    "modules"
                    (fun here ->
                        pathMap
                            json
                            here
                            modulePathOf
                            (accessControlled json (moduleDefinition json))
                            (OrderedMap.toList definition.Modules)
                    )
            )

    let private packageSpecification
        (json: Utf8JsonWriter)
        location
        (specification: PackageSpecification)
        =
        objectOf
            json
            (fun () ->
                writeMember
                    json
                    location
                    "modules"
                    (fun here ->
                        pathMap
                            json
                            here
                            modulePathOf
                            (moduleSpecification json)
                            (OrderedMap.toList specification.Modules)
                    )
            )

    let private entryPointKind (kind: EntryPointKind) =
        match kind with
        | Main -> "main"
        | Command -> "command"
        | Handler -> "handler"
        | Job -> "job"
        | Policy -> "policy"

    /// Writes entry points in order, refusing a name that appears twice.
    let private entryPoints (json: Utf8JsonWriter) location (entries: (string * EntryPoint) list) =
        let seen = HashSet<string>(StringComparer.Ordinal)

        objectOf
            json
            (fun () ->
                for key, entry in entries do
                    propertyName json location key

                    if not (seen.Add key) then
                        fail
                            "duplicate_member"
                            location
                            $"The entry point '{key}' appears more than once."

                    let here = at location key

                    objectOf
                        json
                        (fun () ->
                            writeMember
                                json
                                here
                                "target"
                                (fun l -> fqNameValue json l entry.Target)

                            json.WriteString("kind", entryPointKind entry.Kind)
                            doc json here entry.Doc
                        )
            )

    let private distributionNode (json: Utf8JsonWriter) location (distribution: Distribution) =
        let packageName here (PackageName path) =
            writeMember json here "packageName" (fun l -> json.WriteStringValue(pathText l path))

        let dependencies here write entries =
            writeMember
                json
                here
                "dependencies"
                (fun l -> pathMap json l packagePathOf write entries)

        objectOf
            json
            (fun () ->
                match distribution with
                | Library(name, libraryDependencies, definition) ->
                    taggedMember
                        json
                        location
                        "Library"
                        (fun here ->
                            packageName here name

                            dependencies
                                here
                                (packageSpecification json)
                                (OrderedMap.toList libraryDependencies)

                            writeMember
                                json
                                here
                                "def"
                                (fun l -> packageDefinition json l definition)
                        )
                | Specs(name, specsDependencies, specification) ->
                    taggedMember
                        json
                        location
                        "Specs"
                        (fun here ->
                            packageName here name

                            dependencies
                                here
                                (packageSpecification json)
                                (OrderedMap.toList specsDependencies)

                            writeMember
                                json
                                here
                                "spec"
                                (fun l -> packageSpecification json l specification)
                        )
                | Application(name, applicationDependencies, definition, declaredEntryPoints) ->
                    taggedMember
                        json
                        location
                        "Application"
                        (fun here ->
                            packageName here name

                            dependencies
                                here
                                (packageDefinition json)
                                (OrderedMap.toList applicationDependencies)

                            writeMember
                                json
                                here
                                "def"
                                (fun l -> packageDefinition json l definition)

                            writeMember
                                json
                                here
                                "entryPoints"
                                (fun l -> entryPoints json l declaredEntryPoints)
                        )
            )

    /// Writes the integer 4 for <c>Classic 4</c> and the <c>4.0.0</c> release, and the exact SemVer string
    /// for any other version of major 4.
    let private formatVersionMember (json: Utf8JsonWriter) (version: Versioning.FormatVersion) =
        match version with
        | Versioning.Classic 4 -> json.WriteNumber("formatVersion", 4)
        | Versioning.SemVer semantic when semantic.Major = 4 ->
            if
                semantic.Minor = 0
                && semantic.Patch = 0
                && Option.isNone semantic.PreRelease
            then
                json.WriteNumber("formatVersion", 4)
            else
                json.WriteString("formatVersion", Versioning.SemanticVersion.toString semantic)
        | other ->
            fail
                "unsupported_format_version"
                (at "$" "formatVersion")
                $"The v4 writer does not write format version {Versioning.version other}."

    let private run
        (indented: bool)
        (write: Utf8JsonWriter -> unit)
        : Result<byte array, V4WriteError> =
        let buffer = ArrayBufferWriter<byte>()

        try
            use json =
                new Utf8JsonWriter(
                    buffer,
                    JsonWriterOptions(
                        Indented = indented,
                        IndentSize = 2,
                        NewLine = "\n",
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    )
                )

            write json
            json.Flush()
            Ok(buffer.WrittenSpan.ToArray())
        with
        | V4WriteFailure error -> Error error
        | :? InsufficientExecutionStackException ->
            Error
                {
                    Rule = "depth_limit_exceeded"
                    Location = "$"
                    Message = "The model is nested too deeply to write."
                }
        | :? InvalidOperationException as failure ->
            Error
                {
                    Rule = "depth_limit_exceeded"
                    Location = "$"
                    Message = failure.Message
                }

    /// <summary>
    /// Writes a complete document for a format version of major 4. <c>formatVersion</c> is the integer
    /// <c>4</c> for <c>Classic 4</c> and <c>4.0.0</c>, and the exact SemVer string for any other version, such
    /// as <c>"4.1.0-draft.1"</c>. Indented output uses two spaces.
    /// </summary>
    let writeDocument
        (indented: bool)
        (formatVersion: Versioning.FormatVersion)
        (distribution: Distribution)
        : Result<byte array, V4WriteError> =
        run
            indented
            (fun json ->
                objectOf
                    json
                    (fun () ->
                        formatVersionMember json formatVersion

                        writeMember
                            json
                            "$"
                            "distribution"
                            (fun here -> distributionNode json here distribution)
                    )
            )

    /// <summary>Writes a complete IR v4 document with <c>"formatVersion": 4</c>.</summary>
    let writeDistribution
        (indented: bool)
        (distribution: Distribution)
        : Result<byte array, V4WriteError> =
        writeDocument indented (Versioning.Classic 4) distribution

    /// <summary>Writes a type expression.</summary>
    let writeType (indented: bool) (node: Type) : Result<byte array, V4WriteError> =
        run indented (fun json -> typ json "$" node)

    /// <summary>Writes a value expression.</summary>
    let writeValue (indented: bool) (node: Value) : Result<byte array, V4WriteError> =
        run indented (fun json -> value json "$" node)

    /// <summary>Writes a pattern.</summary>
    let writePattern (indented: bool) (node: Pattern) : Result<byte array, V4WriteError> =
        run indented (fun json -> pattern json "$" node)
