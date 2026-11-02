namespace Morphir.Ion

open System
open System.Globalization
open System.Numerics
open System.Text.Json
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Ion.IonNodes
open Morphir.Ion.ClassicIonTypes

/// Decodes v3 literals, patterns, value expressions and value definitions. A value or pattern attribute holds
/// the classic JSON of a type.
module internal ClassicIonValues =
    /// The attribute of a node with no attribute struct, which is what the pinned reference CLI gives one.
    let private absentAttribute: JsonElement = parseAttribute "[\"Unit\",{}]"

    let private outOfRange path message =
        fail "literal_out_of_range" path message

    let private wholeNumber path (value: BigInteger) =
        if fitsInt64 value then
            WholeNumberLiteral(int64 value)
        else
            outOfRange path "The whole number does not fit the classic literal."

    let private float path (value: IonFloat) =
        if Double.IsFinite value.Value then
            FloatLiteral value.Value
        else
            outOfRange path "The classic literal holds finite floats."

    let private decimalLiteral path (lexeme: string) =
        match Morphir.IR.V4.DecimalLexeme.create lexeme with
        | Error message -> fail "invalid_node" path message
        | Ok _ ->
            let mutable number = 0m

            let parsed =
                Decimal.TryParse(
                    lexeme,
                    NumberStyles.Number
                    ||| NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture,
                    &number
                )

            if
                parsed
                && Morphir.IR.V4.DecimalLexeme.holdsExactly lexeme number
            then
                DecimalLiteral number
            else
                outOfRange path $"The decimal {lexeme} does not fit the classic literal."

    let private charLiteral path (input: string) =
        if
            input.Length = 1
            && not (Char.IsSurrogate input[0])
        then
            CharLiteral input[0]
        elif
            input.Length = 2
            && Char.IsSurrogatePair(input[0], input[1])
        then
            outOfRange path "The classic literal holds one UTF-16 unit."
        else
            fail "invalid_node" path "A character literal holds one character."

    /// Reads the operand of a literal head.
    let private literal head path (value: IonValue) =
        match head, value.Annotations, value.Data with
        | "int", [], IonData.Int number -> wholeNumber path number
        | "float", [], IonData.Float number -> float path number
        | "bool", [], IonData.Bool flag -> BoolLiteral flag
        | "string", [], IonData.String content -> StringLiteral content
        | "char", [], IonData.String content -> charLiteral path content
        | "decimal", [], IonData.String content -> decimalLiteral path content
        | _ -> fail "invalid_node" path $"({head} ...) does not hold this operand."

    let private bareLiteral path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.Bool flag -> Some(BoolLiteral flag)
        | [], IonData.Int number -> Some(wholeNumber path number)
        | [], IonData.Float number -> Some(float path number)
        | _ -> None

    /// The attribute struct right after a head, or the absent attribute, and the operands left.
    let private takeAttribute (args: Node list) =
        match args with
        | (path, first) :: rest when isPlainStruct first ->
            let values = members path first
            closed path [ "inferredType" ] values
            typeAsAttribute (required path "inferredType" typ values), rest
        | _ -> absentAttribute, args

    let private sexpOf path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.Sexp [] -> None
        | [], IonData.Sexp(head :: rest) ->
            Some(symbolOrText (itemAt path 0) head, operands path rest)
        | _ -> fail "invalid_node" path "Expected an S-expression."

    let private bindingOf path name =
        AsPattern(absentAttribute, WildcardPattern absentAttribute, nameText path name)

    let rec pattern path (value: IonValue) : Pattern<JsonElement> =
        enterNode ()

        match value.Annotations, value.Data with
        | [], IonData.Symbol symbol ->
            match symbolText path symbol with
            | "_" -> WildcardPattern absentAttribute
            | name -> bindingOf path name
        | [], IonData.List [] -> EmptyListPattern absentAttribute
        | [], IonData.Sexp [] -> UnitPattern absentAttribute
        | _ ->
            match bareLiteral path value with
            | Some found -> LiteralPattern(absentAttribute, found)
            | None ->
                match sexpOf path value with
                | Some(head, args) -> patternNode head path args
                | None -> UnitPattern absentAttribute

    and private patternNode head path args =
        let attribute, rest = takeAttribute args
        let p (itemPath, item) = pattern itemPath item

        match head, rest with
        | "wildcard", [] -> WildcardPattern attribute
        | "unit", [] -> UnitPattern attribute
        | "emptyList", [] -> EmptyListPattern attribute
        | "as", [ inner; (namePath, name) ] ->
            AsPattern(attribute, p inner, nameOperand namePath name)
        | "tuple", items -> TuplePattern(attribute, List.map p items)
        | "constructor", (namePath, name) :: items ->
            ConstructorPattern(attribute, fqNameOperand namePath name, List.map p items)
        | "headTail", [ first; second ] -> HeadTailPattern(attribute, p first, p second)
        | ("int" | "float" | "bool" | "string" | "char" | "decimal"), [ (itemPath, item) ] ->
            LiteralPattern(attribute, literal head itemPath item)
        | "document", _ ->
            fail
                "unsupported_node"
                path
                "A document literal is a v4 node, and this is a v3 document."
        | ("wildcard" | "unit" | "emptyList" | "as" | "constructor" | "headTail" | "int" | "float" | "bool" | "string" | "char" | "decimal"),
          _ -> wrongOperands head path "other operands"
        | other, _ -> fail "invalid_node" path $"'{other}' is not a v3 pattern."

    /// A lambda's pattern. A list is a list of parameters: one is that pattern, several a tuple of them.
    let private lambdaPattern path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.List(_ :: _ as items) ->
            let patterns =
                items
                |> List.mapi (fun i item -> pattern (itemAt path i) item)

            match patterns with
            | [ single ] -> single
            | _ -> TuplePattern(absentAttribute, patterns)
        | _ -> pattern path value

    let rec value path (element: IonValue) : Value<JsonElement, JsonElement> =
        enterNode ()

        match element.Annotations, element.Data with
        | [], IonData.Symbol symbol ->
            Value.Variable(absentAttribute, nameText path (symbolText path symbol))
        | _ ->
            match bareLiteral path element with
            | Some found -> Value.Literal(absentAttribute, found)
            | None ->
                match sexpOf path element with
                | Some(head, args) -> valueNode head path args
                | None -> Value.Unit absentAttribute

    and private recordEntries (args: Node list) =
        let entries =
            args
            |> List.map (fun (itemPath, item) ->
                let (namePath, name), (fieldPath, field) =
                    pairOf itemPath "A record field is a name and a value." item

                nameOperand namePath name, value fieldPath field
            )

        match args with
        | [] -> Map.empty
        | (firstPath, _) :: _ ->
            entries
            |> uniqueNames firstPath
            |> Map.ofList

    and private letrecBindings path (element: IonValue) =
        let items =
            match element.Annotations, element.Data with
            | [], IonData.List items
            | [], IonData.Sexp items -> items
            | _ -> fail "invalid_node" path "letrec bindings are a list."

        items
        |> List.mapi (fun i item ->
            let (namePath, name), (definitionPath, definition) =
                pairOf (itemAt path i) "A letrec binding is a name and a definition." item

            nameOperand namePath name, valueDefinition definitionPath definition
        )
        |> uniqueNames path
        |> Map.ofList

    and private valueNode head path args : Value<JsonElement, JsonElement> =
        let attribute, rest = takeAttribute args
        let v (itemPath, item) = value itemPath item

        match head, rest with
        | "unit", [] -> Value.Unit attribute
        | "variable", [ (itemPath, item) ] -> Value.Variable(attribute, nameOperand itemPath item)
        | "ref", [ (itemPath, item) ] -> Value.Reference(attribute, fqNameOperand itemPath item)
        | "constructor", [ (itemPath, item) ] ->
            Value.Constructor(attribute, fqNameOperand itemPath item)
        | "apply", [ f; x ] -> Value.Apply(attribute, v f, v x)
        | "lambda", [ (patternPath, p); body ] ->
            Value.Lambda(attribute, lambdaPattern patternPath p, v body)
        | "if", [ c; t; e ] -> Value.IfThenElse(attribute, v c, v t, v e)
        | "let", [ (namePath, name); (definitionPath, definition); body ] ->
            Value.LetDefinition(
                attribute,
                nameOperand namePath name,
                valueDefinition definitionPath definition,
                v body
            )
        | "letrec", [ (bindingsPath, bindings); body ] ->
            Value.LetRecursion(attribute, letrecBindings bindingsPath bindings, v body)
        | "match", scrutinee :: cases ->
            Value.PatternMatch(
                attribute,
                v scrutinee,
                cases
                |> List.map (fun (casePath, case) ->
                    let (patternPath, p), body =
                        pairOf casePath "A match case is a pattern and a body." case

                    pattern patternPath p, v body
                )
            )
        | "destructure", [ (patternPath, p); bound; body ] ->
            Value.Destructure(attribute, pattern patternPath p, v bound, v body)
        | "field", [ target; (namePath, name) ] ->
            Value.Field(attribute, v target, nameOperand namePath name)
        | "fieldFunction", [ (itemPath, item) ] ->
            Value.FieldFunction(attribute, nameOperand itemPath item)
        | "record", entries -> Value.Record(attribute, recordEntries entries)
        | "update", target :: entries ->
            Value.UpdateRecord(attribute, v target, recordEntries entries)
        | "tuple", items -> Value.Tuple(attribute, List.map v items)
        | "list", items -> Value.List(attribute, List.map v items)
        | ("int" | "float" | "bool" | "string" | "char" | "decimal"), [ (itemPath, item) ] ->
            Value.Literal(attribute, literal head itemPath item)
        | ("hole" | "document"), _ ->
            fail "unsupported_node" path $"({head} ...) is a v4 node, and this is a v3 document."
        | ("unit" | "variable" | "ref" | "constructor" | "apply" | "lambda" | "if" | "let" | "letrec" | "match" | "destructure" | "field" | "fieldFunction" | "update" | "int" | "float" | "bool" | "string" | "char" | "decimal"),
          _ -> wrongOperands head path "other operands"
        | other, _ -> fail "invalid_node" path $"'{other}' is not a v3 value."

    /// Reads a list of <c>{ name, type, inferredType }</c>. A missing inferred type is the stated type.
    and private inputTypes path (values: Members) =
        optionalItems path "inputTypes" values
        |> List.map (fun (itemPath, item) ->
            let input = members itemPath item
            closed itemPath [ "name"; "type"; "inferredType" ] input
            let declared = required itemPath "type" typ input

            let attribute =
                optional itemPath "inferredType" typ input
                |> Option.defaultValue declared
                |> typeAsAttribute

            required itemPath "name" nameString input, attribute, declared
        )

    and valueDefinitionMembers path (values: Members) : ValueDefinition<JsonElement, JsonElement> = {
        InputTypes = inputTypes path values
        OutputType = required path "outputType" typ values
        Body = required path "body" value values
    }

    /// A let binding: <c>{ inputTypes, outputType, body }</c>.
    and private valueDefinition path element =
        let values = members path element
        closed path [ "inputTypes"; "outputType"; "body" ] values
        valueDefinitionMembers path values
