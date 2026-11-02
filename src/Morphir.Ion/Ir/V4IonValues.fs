namespace Morphir.Ion

open System
open System.Globalization
open System.Numerics
open Morphir.IR
open Morphir.IR.V4
open Morphir.Ion.IonNodes
open Morphir.Ion.V4IonTypes

/// Decodes v4 literals, patterns, value expressions and let bindings.
module internal V4IonValues =
    let private empty = ValueAttributes.empty

    let private invalid path message = fail "invalid_node" path message

    let private finite path (number: float) lexeme =
        match FloatValue.create number lexeme with
        | Ok value -> FloatLiteral value
        | Error message -> fail "literal_out_of_range" path message

    /// A retained float lexeme. The grammar refuses what double parsing would take as a word, such as NaN.
    let private floatLexeme path (lexeme: string) =
        match DecimalLexeme.create lexeme with
        | Error message -> invalid path message
        | Ok _ ->
            finite
                path
                (Double.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture))
                (Some lexeme)

    let private integerDigits path (digits: string) =
        let unsigned = digits.TrimStart('-')

        if
            unsigned = ""
            || digits.Length
               - unsigned.Length > 1
            || not (Seq.forall Char.IsAsciiDigit unsigned)
        then
            invalid path $"'{digits}' is not an integer."

        IntegerLiteral(
            BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
        )

    let private isOneScalar (input: string) =
        (input.Length = 1
         && not (Char.IsSurrogate input[0]))
        || (input.Length = 2
            && Char.IsSurrogatePair(input[0], input[1]))

    /// Reads the operand of a literal head.
    let private literal head path (value: IonValue) =
        match head, value.Annotations, value.Data with
        | "int", [], IonData.Int number -> IntegerLiteral number
        | "int", [], IonData.String digits -> integerDigits path digits
        | "float", [], IonData.String lexeme -> floatLexeme path lexeme
        | "bool", [], IonData.Bool flag -> BoolLiteral flag
        | "string", [], IonData.String content -> StringLiteral content
        | "char", [], IonData.String content ->
            if isOneScalar content then
                CharLiteral content
            else
                invalid path "A character literal holds one Unicode scalar."
        | "decimal", [], IonData.String lexeme ->
            match DecimalLexeme.create lexeme with
            | Ok found -> DecimalLiteral found
            | Error message -> invalid path message
        | _ -> invalid path $"({head} ...) does not hold this operand."

    let private bareLiteral path (value: IonValue) =
        match value.Data with
        | IonData.Bool flag -> Some(BoolLiteral flag)
        | IonData.Int number -> Some(IntegerLiteral number)
        | IonData.Float number -> Some(finite path number.Value None)
        | _ -> None

    /// Reads <c>{ source, inferredType, extensions }</c>.
    let private valueAttributes path (value: IonValue) : ValueAttributes =
        let values = members path value
        closed path [ "source"; "inferredType"; "extensions" ] values

        {
            Source = optional path "source" sourceLocation values
            InferredType = optional path "inferredType" typ values
            Extensions = optional path "extensions" documentStruct values
        }

    /// The attribute struct right after a head, if any, and the operands left. A document's payload may be a
    /// struct, so a document has attributes only when two operands follow its head.
    let private takeAttributes head (args: Node list) =
        match head, args with
        | "document", [ _ ] -> empty, args
        | _, (path, first) :: rest when isPlainStruct first -> valueAttributes path first, rest
        | _ -> empty, args

    /// The head and operands of an S-expression, or nothing for <c>()</c>.
    let private sexpOf path (value: IonValue) =
        match value.Data with
        | IonData.Sexp [] -> None
        | IonData.Sexp(head :: rest) ->
            match head.Annotations, head.Data with
            | [], IonData.Symbol symbol ->
                Some(symbolText (itemAt path 0) symbol, operands path rest)
            | _ -> invalid (itemAt path 0) "An S-expression starts with a symbol."
        | _ -> invalid path "Expected an S-expression, a symbol or a literal."

    let private unannotated path (value: IonValue) what =
        if not value.Annotations.IsEmpty then
            invalid path $"A v4 {what} carries no Ion annotation."

    let private binding path name =
        AsPattern(empty, WildcardPattern empty, nameText path name)

    let rec pattern path (value: IonValue) : Pattern =
        enterNode ()
        unannotated path value "pattern"

        match value.Data with
        | IonData.Symbol symbol ->
            match symbolText path symbol with
            | "_" -> WildcardPattern empty
            | name -> binding path name
        | IonData.List [] -> EmptyListPattern empty
        | _ ->
            match bareLiteral path value with
            | Some found -> LiteralPattern(empty, found)
            | None ->
                match sexpOf path value with
                | Some(head, args) -> patternNode head path args
                | None -> UnitPattern empty

    and private patternNode head path args =
        let attributes, rest = takeAttributes head args
        let p (itemPath, item) = pattern itemPath item

        match head, rest with
        | "wildcard", [] -> WildcardPattern attributes
        | "unit", [] -> UnitPattern attributes
        | "emptyList", [] -> EmptyListPattern attributes
        | "as", [ inner; (namePath, name) ] ->
            AsPattern(attributes, p inner, nameOperand namePath name)
        | "tuple", items -> TuplePattern(attributes, List.map p items)
        | "constructor", (namePath, name) :: items ->
            ConstructorPattern(attributes, fqNameOperand namePath name, List.map p items)
        | "headTail", [ first; second ] -> HeadTailPattern(attributes, p first, p second)
        | ("int" | "float" | "bool" | "string" | "char" | "decimal"), [ (itemPath, item) ] ->
            LiteralPattern(attributes, literal head itemPath item)
        | "document", _ -> invalid path "A document cannot appear in a pattern."
        | ("wildcard" | "unit" | "emptyList" | "as" | "constructor" | "headTail" | "int" | "float" | "bool" | "string" | "char" | "decimal"),
          _ -> wrongOperands head path "other operands"
        | other, _ -> invalid path $"'{other}' is not a pattern."

    /// A lambda's pattern. A list is a list of parameters: one is that pattern, several a tuple of them.
    let private lambdaPattern path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.List(_ :: _ as items) ->
            let patterns =
                items
                |> List.mapi (fun i item -> pattern (itemAt path i) item)

            match patterns with
            | [ single ] -> single
            | _ -> TuplePattern(empty, patterns)
        | _ -> pattern path value

    let rec value path (element: IonValue) : Value =
        enterNode ()
        unannotated path element "value"

        match element.Data with
        | IonData.Symbol symbol -> Value.Variable(empty, nameText path (symbolText path symbol))
        | _ ->
            match bareLiteral path element with
            | Some found -> Value.Literal(empty, found)
            | None ->
                match sexpOf path element with
                | Some(head, args) -> valueNode head path args
                | None -> Value.Unit empty

    and private recordEntries path (args: Node list) =
        args
        |> List.map (fun (itemPath, item) ->
            let (namePath, name), (fieldPath, field) =
                pairOf itemPath "A record field is a name and a value." item

            nameOperand namePath name, value fieldPath field
        )
        |> uniqueNames path

    /// The draft's row spells the bindings as an S-expression and the reference writer as a list.
    and private letrecBindings path (element: IonValue) =
        let items =
            match element.Annotations, element.Data with
            | [], IonData.List items
            | [], IonData.Sexp items -> items
            | _ -> invalid path "letrec bindings are a list."

        let entries =
            items
            |> List.mapi (fun i item ->
                let (namePath, name), (definitionPath, definition) =
                    pairOf (itemAt path i) "A letrec binding is a name and a definition." item

                nameOperand namePath name, letBinding definitionPath definition
            )
            |> uniqueNames path

        match OrderedMap.ofList entries with
        | Ok map -> map
        | Error key ->
            fail
                "duplicate_name"
                path
                $"The name '{CanonicalNames.nameToString key}' appears more than once."

    and private valueNode head path args : Value =
        let attributes, rest = takeAttributes head args
        let v (itemPath, item) = value itemPath item

        match head, rest with
        | "unit", [] -> Value.Unit attributes
        | "variable", [ (itemPath, item) ] -> Value.Variable(attributes, nameOperand itemPath item)
        | "ref", [ (itemPath, item) ] -> Value.Reference(attributes, fqNameOperand itemPath item)
        | "constructor", [ (itemPath, item) ] ->
            Value.Constructor(attributes, fqNameOperand itemPath item)
        | "apply", [ f; x ] -> Value.Apply(attributes, v f, v x)
        | "lambda", [ (patternPath, p); body ] ->
            Value.Lambda(attributes, lambdaPattern patternPath p, v body)
        | "if", [ c; t; e ] -> Value.IfThenElse(attributes, v c, v t, v e)
        | "let", [ (namePath, name); (definitionPath, definition); body ] ->
            Value.LetDefinition(
                attributes,
                nameOperand namePath name,
                letBinding definitionPath definition,
                v body
            )
        | "letrec", [ (bindingsPath, bindings); body ] ->
            Value.LetRecursion(attributes, letrecBindings bindingsPath bindings, v body)
        | "match", scrutinee :: cases ->
            Value.PatternMatch(
                attributes,
                v scrutinee,
                cases
                |> List.map (fun (casePath, case) ->
                    let (patternPath, p), body =
                        pairOf casePath "A match case is a pattern and a body." case

                    pattern patternPath p, v body
                )
            )
        | "destructure", [ (patternPath, p); bound; body ] ->
            Value.Destructure(attributes, pattern patternPath p, v bound, v body)
        | "field", [ target; (namePath, name) ] ->
            Value.Field(attributes, v target, nameOperand namePath name)
        | "fieldFunction", [ (itemPath, item) ] ->
            Value.FieldFunction(attributes, nameOperand itemPath item)
        | "record", entries -> Value.Record(attributes, recordEntries path entries)
        | "update", target :: entries ->
            Value.UpdateRecord(attributes, v target, recordEntries path entries)
        | "tuple", items -> Value.Tuple(attributes, List.map v items)
        | "list", items -> Value.List(attributes, List.map v items)
        | "hole", [ (reasonPath, reason) ] ->
            Value.Hole(attributes, holeReason reasonPath reason, None)
        | "hole", [ (reasonPath, reason); (typePath, expected) ] ->
            Value.Hole(attributes, holeReason reasonPath reason, Some(typ typePath expected))
        | ("int" | "float" | "bool" | "string" | "char" | "decimal"), [ (itemPath, item) ] ->
            Value.Literal(attributes, literal head itemPath item)
        | "document", [ (itemPath, item) ] ->
            Value.Literal(attributes, DocumentLiteral(documentValue itemPath item))
        | ("unit" | "variable" | "ref" | "constructor" | "apply" | "lambda" | "if" | "let" | "letrec" | "match" | "destructure" | "field" | "fieldFunction" | "update" | "hole" | "int" | "float" | "bool" | "string" | "char" | "decimal" | "document"),
          _ -> wrongOperands head path "other operands"
        | other, _ -> invalid path $"'{other}' is not a value."

    /// A let binding: <c>{ inputTypes, outputType, body }</c>. It holds an expression body.
    and private letBinding path element : Documented<ValueDefinition> =
        let values = members path element
        closed path [ "inputTypes"; "outputType"; "body" ] values

        {
            Doc = None
            Value = {
                InputTypes = inputs path "inputTypes" values
                Body =
                    ExpressionBody(
                        required path "outputType" typ values,
                        required path "body" value values
                    )
            }
        }
