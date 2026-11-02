namespace Morphir.Json.V4

open System
open System.Globalization
open System.Numerics
open System.Text.Json
open Morphir.IR.V4
open Morphir.Json.V4.V4JsonDecoding

/// Hand-written decoding of v4 type expressions, patterns, literals, value expressions and value bodies.
module internal V4JsonValues =
    let rec documentValue location (element: JsonElement) : DocumentValue =
        enterNode ()

        match element.ValueKind with
        | JsonValueKind.Null -> DocumentNull
        | JsonValueKind.True -> DocumentBool true
        | JsonValueKind.False -> DocumentBool false
        | JsonValueKind.Number -> DocumentNumber(element.GetRawText())
        | JsonValueKind.String -> DocumentString(text location element)
        | JsonValueKind.Array -> DocumentArray(listOf documentValue location element)
        | JsonValueKind.Object ->
            members location element
            |> List.map (fun (key, item) -> key, documentValue (memberAt location key) item)
            |> orderedMap location id
            |> DocumentObject
        | _ -> fail "invalid_type" location "Expected a JSON value."

    /// Constraints and extensions are objects.
    let private documentObject location (element: JsonElement) =
        if
            element.ValueKind
            <> JsonValueKind.Object
        then
            fail "invalid_type" location "Expected an object."

        documentValue location element

    let private lineOrColumn location (element: JsonElement) =
        let mutable number = 0

        if
            element.ValueKind
            <> JsonValueKind.Number
            || not (element.TryGetInt32(&number))
        then
            fail "invalid_type" location "Expected an integer."

        number

    let private sourceLocation location element =
        let values = members location element
        closed location [ "startLine"; "startColumn"; "endLine"; "endColumn" ] values

        {
            StartLine = required location [ "startLine" ] lineOrColumn values
            StartColumn = required location [ "startColumn" ] lineOrColumn values
            EndLine = required location [ "endLine" ] lineOrColumn values
            EndColumn = required location [ "endColumn" ] lineOrColumn values
        }

    let private attributeNames = [ "attributes"; "attrs" ]

    let private typeAttributes location values =
        match tryPick location attributeNames values with
        | None -> TypeAttributes.empty
        | Some(key, element) ->
            let here = memberAt location key
            let attributes = members here element
            closed here [ "source"; "constraints"; "extensions" ] attributes

            {
                Source = optional here [ "source" ] sourceLocation attributes
                Constraints = optional here [ "constraints" ] documentObject attributes
                Extensions = optional here [ "extensions" ] documentObject attributes
            }

    /// Reads a payload that is either its compact form, or an object holding attributes beside one member.
    let private compactOrExpanded
        attributesOf
        (emptyAttributes: 'a)
        memberName
        decode
        location
        (payload: JsonElement)
        =
        if payload.ValueKind = JsonValueKind.Object then
            let values = members location payload

            closed
                location
                (memberName
                 :: attributeNames)
                values

            attributesOf location values, required location [ memberName ] decode values
        else
            emptyAttributes, decode location payload

    /// Reads a record payload: fields under a member, or the legacy map directly under the node.
    /// A legacy map can hold a field named "fields", whose value is a type or value rather than
    /// a field map, so the member is only the wrapper when it holds an object that is not a node.
    let private recordPayload attributesOf (emptyAttributes: 'a) tags decode location payload =
        let values = members location payload

        let holdsFieldMap =
            values
            |> List.exists (fun (key, element: JsonElement) ->
                key = "fields"
                && element.ValueKind = JsonValueKind.Object
                && members (memberAt location key) element
                   |> List.forall (fun (fieldName, _) -> not (List.contains fieldName tags))
            )

        if holdsFieldMap then
            closed
                location
                ("fields"
                 :: attributeNames)
                values

            attributesOf location values, required location [ "fields" ] (nameMap decode) values
        else
            emptyAttributes, nameMap decode location payload

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

    let private typeTags = [
        "Variable"
        "Reference"
        "Tuple"
        "Record"
        "ExtensibleRecord"
        "Function"
        "Unit"
    ]

    let rec typ location (element: JsonElement) : Type =
        enterNode ()

        match element.ValueKind with
        | JsonValueKind.String ->
            let input = nameString location element

            if looksQualified input then
                Type.Reference(TypeAttributes.empty, fqNameText location input, [])
            elif isBareTypeVariable input then
                Type.Variable(TypeAttributes.empty, nameText location input)
            else
                fail "invalid_name" location $"'{input}' is not a type variable or an FQName."
        | JsonValueKind.Array -> Type.Tuple(TypeAttributes.empty, listOf typ location element)
        | JsonValueKind.Object ->
            let tag, payload = nodeTag location typeTags (members location element)
            typeNode tag (memberAt location tag) payload
        | _ -> fail "invalid_type" location "Expected a type."

    and private typeNode tag location payload =
        match tag with
        | "Variable" ->
            let attributes, variable =
                compactOrExpanded typeAttributes TypeAttributes.empty "name" name location payload

            Type.Variable(attributes, variable)
        | "Reference" -> typeReference location payload
        | "Tuple" ->
            let attributes, elements =
                compactOrExpanded
                    typeAttributes
                    TypeAttributes.empty
                    "elements"
                    (listOf typ)
                    location
                    payload

            Type.Tuple(attributes, elements)
        | "Record" ->
            let attributes, fields =
                recordPayload typeAttributes TypeAttributes.empty typeTags typ location payload

            Type.Record(attributes, fields)
        | "ExtensibleRecord" ->
            let values = members location payload

            closed
                location
                ("variable"
                 :: "fields"
                 :: attributeNames)
                values

            Type.ExtensibleRecord(
                typeAttributes location values,
                required location [ "variable" ] name values,
                required location [ "fields" ] (nameMap typ) values
            )
        | "Function" ->
            let values = members location payload
            let parameterNames = [ "parameterType"; "argumentType"; "arg" ]
            let returnNames = [ "returnType"; "result" ]

            closed
                location
                (parameterNames
                 @ returnNames
                 @ attributeNames)
                values

            Type.Function(
                typeAttributes location values,
                required location parameterNames typ values,
                required location returnNames typ values
            )
        | "Unit" ->
            let values = members location payload
            closed location attributeNames values
            Type.Unit(typeAttributes location values)
        | other -> fail "unknown_node" location $"'{other}' is not a type."

    and private typeReference location (payload: JsonElement) =
        match payload.ValueKind with
        | JsonValueKind.Array ->
            match items location payload with
            | referenced :: (_ :: _ as arguments) ->
                Type.Reference(
                    TypeAttributes.empty,
                    fqName (itemAt location 0) referenced,
                    arguments
                    |> List.mapi (fun i item -> typ (itemAt location (i + 1)) item)
                )
            | _ ->
                fail
                    "invalid_type"
                    location
                    "A reference array holds an FQName and at least one argument."
        | JsonValueKind.Object ->
            let values = members location payload

            closed
                location
                ("fqname"
                 :: "args"
                 :: attributeNames)
                values

            Type.Reference(
                typeAttributes location values,
                required location [ "fqname" ] fqName values,
                optional location [ "args" ] (listOf typ) values
                |> Option.defaultValue []
            )
        | _ -> Type.Reference(TypeAttributes.empty, fqName location payload, [])

    let private valueAttributes location values =
        match tryPick location attributeNames values with
        | None -> ValueAttributes.empty
        | Some(key, element) ->
            let here = memberAt location key
            let attributes = members here element
            closed here [ "source"; "inferredType"; "extensions" ] attributes

            {
                Source = optional here [ "source" ] sourceLocation attributes
                InferredType = optional here [ "inferredType" ] typ attributes
                Extensions = optional here [ "extensions" ] documentObject attributes
            }

    let private invalidLiteral location message = fail "invalid_literal" location message

    let private floatLiteral location (lexeme: string) =
        match
            FloatValue.create
                (Double.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture))
                (Some lexeme)
        with
        | Ok value -> FloatLiteral value
        | Error message -> invalidLiteral location message

    let private isWholeLexeme (lexeme: string) =
        lexeme
        |> Seq.forall (fun c ->
            c <> '.'
            && c <> 'e'
            && c <> 'E'
        )

    let private integerLiteral (lexeme: string) =
        IntegerLiteral(
            BigInteger.Parse(lexeme, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
        )

    /// A number with a point or an exponent is a float, and any other is an integer (decision 0009).
    let private numberLiteral location (element: JsonElement) =
        let lexeme = element.GetRawText()

        if isWholeLexeme lexeme then
            integerLiteral lexeme
        else
            floatLiteral location lexeme

    /// Reads a literal's scalar: compact, or under a value member where other members are ignored.
    let private scalarPayload location (payload: JsonElement) =
        if payload.ValueKind = JsonValueKind.Object then
            match
                members location payload
                |> List.tryFind (fun (key, _) -> key = "value")
            with
            | Some(_, scalar) -> scalar
            | None -> invalidLiteral location "An expanded literal holds its scalar under 'value'."
        else
            payload

    let private literalText location element =
        stringOf "invalid_literal" location element

    let private isOneScalar (input: string) =
        (input.Length = 1
         && not (Char.IsSurrogate input[0]))
        || (input.Length = 2
            && Char.IsSurrogatePair(input[0], input[1]))

    let private literalTags = [
        "BoolLiteral"
        "CharLiteral"
        "StringLiteral"
        "IntegerLiteral"
        "WholeNumberLiteral"
        "FloatLiteral"
        "DecimalLiteral"
        "DocumentLiteral"
    ]

    let private typedLiteral tag location (payload: JsonElement) =
        match tag with
        | "DocumentLiteral" -> DocumentLiteral(documentValue location payload)
        | "BoolLiteral" ->
            match (scalarPayload location payload).ValueKind with
            | JsonValueKind.True -> BoolLiteral true
            | JsonValueKind.False -> BoolLiteral false
            | _ -> invalidLiteral location "Expected a boolean."
        | "CharLiteral" ->
            let input = literalText location (scalarPayload location payload)

            if not (isOneScalar input) then
                invalidLiteral location "A character is one Unicode scalar."

            CharLiteral input
        | "StringLiteral" -> StringLiteral(literalText location (scalarPayload location payload))
        | "IntegerLiteral"
        | "WholeNumberLiteral" ->
            let number = scalarPayload location payload

            if
                number.ValueKind
                <> JsonValueKind.Number
                || not (isWholeLexeme (number.GetRawText()))
            then
                invalidLiteral location "An integer's lexeme is a whole number."

            integerLiteral (number.GetRawText())
        | "FloatLiteral" ->
            let number = scalarPayload location payload

            if
                number.ValueKind
                <> JsonValueKind.Number
            then
                invalidLiteral location "Expected a number."

            floatLiteral location (number.GetRawText())
        | "DecimalLiteral" ->
            match DecimalLexeme.create (literalText location (scalarPayload location payload)) with
            | Ok lexeme -> DecimalLiteral lexeme
            | Error message -> invalidLiteral location message
        | other -> fail "unknown_node" location $"'{other}' is not a literal."

    /// Reads a direct scalar or a typed wrapper, the two shapes a literal takes under a node.
    let private directOrTyped location (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.True -> BoolLiteral true
        | JsonValueKind.False -> BoolLiteral false
        | JsonValueKind.Number -> numberLiteral location element
        | JsonValueKind.String -> StringLiteral(literalText location element)
        | JsonValueKind.Object ->
            let tag, payload = nodeTag location literalTags (members location element)
            typedLiteral tag (memberAt location tag) payload
        | _ -> invalidLiteral location "Expected a literal."

    /// Reads a literal payload, including the expanded form that puts attributes beside a literal member.
    let private literalPayload location (element: JsonElement) =
        let isExpanded =
            element.ValueKind = JsonValueKind.Object
            && members location element
               |> List.exists (fun (key, _) ->
                   key = "literal"
                   || List.contains key attributeNames
               )

        if isExpanded then
            let values = members location element

            closed
                location
                ("literal"
                 :: attributeNames)
                values

            valueAttributes location values, required location [ "literal" ] directOrTyped values
        else
            ValueAttributes.empty, directOrTyped location element

    let private patternTags = [
        "WildcardPattern"
        "AsPattern"
        "TuplePattern"
        "ConstructorPattern"
        "EmptyListPattern"
        "HeadTailPattern"
        "LiteralPattern"
        "UnitPattern"
    ]

    let private attributesOnly location payload =
        let values = members location payload
        closed location attributeNames values
        valueAttributes location values

    let rec pattern location (element: JsonElement) : Pattern =
        enterNode ()

        match element.ValueKind with
        | JsonValueKind.Array ->
            TuplePattern(ValueAttributes.empty, listOf pattern location element)
        | JsonValueKind.Object ->
            let tag, payload = nodeTag location patternTags (members location element)
            patternNode tag (memberAt location tag) payload
        | _ -> fail "invalid_type" location "Expected a pattern."

    and private patternNode tag location payload =
        match tag with
        | "WildcardPattern" -> WildcardPattern(attributesOnly location payload)
        | "EmptyListPattern" -> EmptyListPattern(attributesOnly location payload)
        | "UnitPattern" -> UnitPattern(attributesOnly location payload)
        | "AsPattern" ->
            let values = members location payload

            closed
                location
                ("pattern"
                 :: "name"
                 :: attributeNames)
                values

            AsPattern(
                valueAttributes location values,
                required location [ "pattern" ] pattern values,
                required location [ "name" ] name values
            )
        | "TuplePattern" ->
            let attributes, patterns =
                compactOrExpanded
                    valueAttributes
                    ValueAttributes.empty
                    "patterns"
                    (listOf pattern)
                    location
                    payload

            TuplePattern(attributes, patterns)
        | "ConstructorPattern" ->
            let values = members location payload

            closed
                location
                ("fqname"
                 :: "patterns"
                 :: attributeNames)
                values

            ConstructorPattern(
                valueAttributes location values,
                required location [ "fqname" ] fqName values,
                required location [ "patterns" ] (listOf pattern) values
            )
        | "HeadTailPattern" ->
            let values = members location payload

            closed
                location
                ("head"
                 :: "tail"
                 :: attributeNames)
                values

            HeadTailPattern(
                valueAttributes location values,
                required location [ "head" ] pattern values,
                required location [ "tail" ] pattern values
            )
        | "LiteralPattern" ->
            match literalPayload location payload with
            | _, DocumentLiteral _ ->
                invalidLiteral location "A document cannot be pattern matched."
            | attributes, literal -> LiteralPattern(attributes, literal)
        | other -> fail "unknown_node" location $"'{other}' is not a pattern."

    let private holeReason location element =
        let tag, payload =
            nodeTag
                location
                [ "UnresolvedReference"; "DeletedDuringRefactor"; "TypeMismatch" ]
                (members location element)

        let here = memberAt location tag
        let values = members here payload

        match tag with
        | "UnresolvedReference" ->
            closed here [ "target" ] values
            UnresolvedReference(required here [ "target" ] fqName values)
        | "DeletedDuringRefactor" ->
            closed here [ "tx-id" ] values
            DeletedDuringRefactor(required here [ "tx-id" ] text values)
        | "TypeMismatch" ->
            closed here [ "expected"; "found" ] values

            TypeMismatch(
                required here [ "expected" ] text values,
                required here [ "found" ] text values
            )
        | other -> fail "unknown_node" location $"'{other}' is not a hole reason."

    let incompleteness location element =
        let tag, payload = nodeTag location [ "Hole"; "Draft" ] (members location element)
        let here = memberAt location tag

        match tag with
        | "Draft" ->
            emptyPayload here payload
            Incompleteness.Draft
        | "Hole" ->
            let values = members here payload
            closed here [ "reason"; "partialBody" ] values

            Incompleteness.Hole(
                required here [ "reason" ] holeReason values,
                optional here [ "partialBody" ] typ values
            )
        | other -> fail "unknown_node" location $"'{other}' is not an incompleteness."

    let private nativeHint location element =
        let tag, payload =
            nodeTag
                location
                [ "Arithmetic"; "Comparison"; "StringOp"; "CollectionOp"; "PlatformSpecific" ]
                (members location element)

        let here = memberAt location tag

        let nullary hint =
            emptyPayload here payload
            hint

        match tag with
        | "Arithmetic" -> nullary Arithmetic
        | "Comparison" -> nullary Comparison
        | "StringOp" -> nullary StringOp
        | "CollectionOp" -> nullary CollectionOp
        | "PlatformSpecific" ->
            let values = members here payload
            closed here [ "platform" ] values
            PlatformSpecific(required here [ "platform" ] text values)
        | other -> fail "unknown_node" location $"'{other}' is not a native hint."

    let private nativeInfo location element =
        let values = members location element
        closed location [ "hint"; "description" ] values

        {
            Hint = required location [ "hint" ] nativeHint values
            Description = optional location [ "description" ] text values
        }

    let private externalBinding location element =
        let values = members location element
        closed location [ "targetPlatform"; "externalName" ] values

        {
            TargetPlatform = required location [ "targetPlatform" ] text values
            ExternalName = required location [ "externalName" ] text values
        }

    /// Decision 0008: at least one binding, and a platform appears once.
    let private externalBindings location element =
        let bindings = listOf externalBinding location element

        if List.isEmpty bindings then
            fail "invalid_type" location "An external body needs at least one binding."

        let platforms =
            bindings
            |> List.map (fun binding -> binding.TargetPlatform)

        if
            (List.distinct platforms).Length
            <> platforms.Length
        then
            fail "duplicate_member" location "A target platform appears more than once."

        bindings

    let private valueTags = [
        "Literal"
        "Constructor"
        "Tuple"
        "List"
        "Record"
        "Variable"
        "Reference"
        "Field"
        "FieldFunction"
        "Apply"
        "Lambda"
        "LetDefinition"
        "LetRecursion"
        "Destructure"
        "IfThenElse"
        "PatternMatch"
        "UpdateRecord"
        "Unit"
        "Hole"
    ]

    let private bodyTags = [ "ExpressionBody"; "NativeBody"; "ExternalBody"; "IncompleteBody" ]

    let rec value location (element: JsonElement) : Value =
        enterNode ()

        match element.ValueKind with
        | JsonValueKind.True -> Value.Literal(ValueAttributes.empty, BoolLiteral true)
        | JsonValueKind.False -> Value.Literal(ValueAttributes.empty, BoolLiteral false)
        | JsonValueKind.Number ->
            Value.Literal(ValueAttributes.empty, numberLiteral location element)
        | JsonValueKind.String ->
            let input = nameString location element

            if looksQualified input then
                Value.Reference(ValueAttributes.empty, fqNameText location input)
            else
                Value.Variable(ValueAttributes.empty, nameText location input)
        | JsonValueKind.Array -> Value.List(ValueAttributes.empty, listOf value location element)
        | JsonValueKind.Object ->
            let tag, payload = nodeTag location valueTags (members location element)
            valueNode tag (memberAt location tag) payload
        | _ -> fail "invalid_type" location "Expected a value."

    and private valueNode tag location payload =
        /// Reads a node whose payload is a compact form or an object of attributes and one member.
        let compact memberName decode =
            compactOrExpanded
                valueAttributes
                ValueAttributes.empty
                memberName
                decode
                location
                payload

        /// Reads a node whose payload is an object of attributes and the given member names.
        let expanded (allowed: string list) =
            let values = members location payload

            closed
                location
                (allowed
                 @ attributeNames)
                values

            valueAttributes location values, values

        match tag with
        | "Literal" -> Value.Literal(literalPayload location payload)
        | "Constructor" -> Value.Constructor(compact "fqname" fqName)
        | "Tuple" -> Value.Tuple(compact "elements" (listOf value))
        | "List" -> Value.List(compact "items" (listOf value))
        | "Record" ->
            Value.Record(
                recordPayload valueAttributes ValueAttributes.empty valueTags value location payload
            )
        | "Variable" -> Value.Variable(compact "name" name)
        | "Reference" -> Value.Reference(compact "fqname" fqName)
        | "FieldFunction" -> Value.FieldFunction(compact "name" name)
        | "Field" ->
            let targetNames = [ "target"; "subject" ]
            let nameNames = [ "name"; "fieldName" ]

            let attributes, values =
                expanded (
                    targetNames
                    @ nameNames
                )

            Value.Field(
                attributes,
                required location targetNames value values,
                required location nameNames name values
            )
        | "Apply" ->
            let attributes, values = expanded [ "function"; "argument" ]

            Value.Apply(
                attributes,
                required location [ "function" ] value values,
                required location [ "argument" ] value values
            )
        | "Lambda" ->
            let attributes, values = expanded [ "pattern"; "body" ]

            Value.Lambda(
                attributes,
                required location [ "pattern" ] pattern values,
                required location [ "body" ] value values
            )
        | "LetDefinition" ->
            let nameNames = [ "name"; "valueName" ]
            let definitionNames = [ "definition"; "valueDefinition" ]
            let inNames = [ "in"; "inValue" ]

            let attributes, values =
                expanded (
                    nameNames
                    @ definitionNames
                    @ inNames
                )

            Value.LetDefinition(
                attributes,
                required location nameNames name values,
                required location definitionNames valueDefinition values,
                required location inNames value values
            )
        | "LetRecursion" ->
            let attributes, values = expanded [ "definitions"; "in" ]

            Value.LetRecursion(
                attributes,
                required location [ "definitions" ] (orderedNameMap valueDefinition) values,
                required location [ "in" ] value values
            )
        | "Destructure" ->
            let attributes, values = expanded [ "pattern"; "value"; "in" ]

            Value.Destructure(
                attributes,
                required location [ "pattern" ] pattern values,
                required location [ "value" ] value values,
                required location [ "in" ] value values
            )
        | "IfThenElse" ->
            let thenNames = [ "then"; "thenBranch" ]
            let elseNames = [ "else"; "elseBranch" ]

            let attributes, values =
                expanded (
                    "condition"
                    :: thenNames
                    @ elseNames
                )

            Value.IfThenElse(
                attributes,
                required location [ "condition" ] value values,
                required location thenNames value values,
                required location elseNames value values
            )
        | "PatternMatch" ->
            let attributes, values = expanded [ "value"; "cases" ]

            Value.PatternMatch(
                attributes,
                required location [ "value" ] value values,
                required location [ "cases" ] (listOf patternCase) values
            )
        | "UpdateRecord" ->
            let attributes, values = expanded [ "target"; "fields" ]

            Value.UpdateRecord(
                attributes,
                required location [ "target" ] value values,
                required location [ "fields" ] (nameMap value) values
            )
        | "Unit" -> Value.Unit(attributesOnly location payload)
        | "Hole" ->
            let attributes, values = expanded [ "reason"; "expectedType" ]

            Value.Hole(
                attributes,
                required location [ "reason" ] holeReason values,
                optional location [ "expectedType" ] typ values
            )
        | other -> fail "unknown_node" location $"'{other}' is not a value."

    and private patternCase location element =
        let values = members location element
        closed location [ "pattern"; "body" ] values
        required location [ "pattern" ] pattern values, required location [ "body" ] value values

    /// Reads a value definition's flattened body variant.
    and valueDefinitionMembers location (values: Members) : ValueDefinition =
        let tag, payload = nodeTag location bodyTags values
        let here = memberAt location tag
        let body = members here payload

        let inputs () =
            required here [ "inputTypes" ] (nameMap typ) body

        let output () = required here [ "outputType" ] typ body

        match tag with
        | "ExpressionBody" ->
            closed here [ "inputTypes"; "outputType"; "body" ] body

            {
                InputTypes = inputs ()
                Body = ExpressionBody(output (), required here [ "body" ] value body)
            }
        | "NativeBody" ->
            closed here [ "inputTypes"; "outputType"; "nativeInfo" ] body

            {
                InputTypes = inputs ()
                Body = NativeBody(output (), required here [ "nativeInfo" ] nativeInfo body)
            }
        | "ExternalBody" ->
            let isLegacy = not (hasMember "externals" body)

            let bindingNames =
                if isLegacy then
                    [ "targetPlatform"; "externalName" ]
                else
                    [ "externals" ]

            closed
                here
                ([ "inputTypes"; "outputType"; "body" ]
                 @ bindingNames)
                body

            let bindings =
                if isLegacy then
                    [
                        {
                            TargetPlatform = required here [ "targetPlatform" ] text body
                            ExternalName = required here [ "externalName" ] text body
                        }
                    ]
                else
                    required here [ "externals" ] externalBindings body

            {
                InputTypes = inputs ()
                Body =
                    ExternalBody(
                        output (),
                        {
                            Externals = bindings
                            Fallback = optional here [ "body" ] value body
                        }
                    )
            }
        | "IncompleteBody" ->
            closed here [ "inputTypes"; "outputType"; "incompleteness"; "partialBody" ] body

            {
                InputTypes = inputs ()
                Body =
                    IncompleteBody(
                        optional here [ "outputType" ] typ body,
                        required here [ "incompleteness" ] incompleteness body,
                        optional here [ "partialBody" ] value body
                    )
            }
        | other -> fail "unknown_node" location $"'{other}' is not a value definition body."

    and valueDefinition location element : Documented<ValueDefinition> =
        documented valueDefinitionMembers notAnObject location element
