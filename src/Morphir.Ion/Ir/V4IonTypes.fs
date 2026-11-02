namespace Morphir.Ion

open System.Globalization
open System.Numerics
open System.Text.RegularExpressions
open Morphir.IR
open Morphir.IR.V4
open Morphir.Ion.IonNodes

/// Decodes the JSON-compatible payloads, source locations, type expressions and incompleteness of IR v4.
module internal V4IonTypes =
    let private invalidPayload path message =
        fail "invalid_document_payload" path message

    let private jsonNumber =
        Regex(@"\A-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant)

    /// A decimal payload's lexeme: the Ion text of the decimal with <c>d</c> written as <c>e</c>. Ion leaves a
    /// decimal's text open, so this follows the text the pinned reference CLI gives, which the vendored vectors
    /// record. No form is padded with more than six zeros, so nothing here grows with the scale.
    let decimalLexeme (value: IonDecimal) =
        let digits =
            BigInteger.Abs(value.Coefficient).ToString(CultureInfo.InvariantCulture)

        let sign =
            if
                value.Coefficient.Sign < 0
                || (value.Coefficient.IsZero
                    && value.IsNegativeZero)
            then
                "-"
            else
                ""

        let exponent = -(int64 value.Scale)
        let length = int64 digits.Length

        let pointAt =
            length
            + exponent

        let scientific () =
            let adjusted =
                pointAt
                - 1L

            let mark = if adjusted < 0L then "" else "+"
            let power = adjusted.ToString(CultureInfo.InvariantCulture)

            if digits.Length = 1 then
                digits
                + "e"
                + mark
                + power
            else
                digits.Substring(0, 1)
                + "."
                + digits.Substring 1
                + "e"
                + mark
                + power

        let body =
            if exponent = 0L then
                if digits.Length > 6 then scientific () else digits
            elif exponent > 0L then
                digits
                + "e+"
                + exponent.ToString(CultureInfo.InvariantCulture)
            elif pointAt > 0L then
                digits.Substring(0, int pointAt)
                + "."
                + digits.Substring(int pointAt)
            elif pointAt > -6L then
                "0."
                + System.String('0', int -pointAt)
                + digits
            else
                scientific ()

        sign
        + body

    /// Reads JSON-compatible Ion, spelled as a document payload is.
    let rec documentValue path (value: IonValue) : DocumentValue =
        enterNode ()

        match value.Annotations, value.Data with
        | [ IonSymbol.Text "number" ], IonData.String lexeme ->
            if jsonNumber.IsMatch lexeme then
                DocumentNumber lexeme
            else
                invalidPayload path $"'{lexeme}' is not a JSON number."
        | [], IonData.Null IonNullType.Null -> DocumentNull
        | [], IonData.Bool flag -> DocumentBool flag
        | [], IonData.Int number -> DocumentNumber(number.ToString(CultureInfo.InvariantCulture))
        | [], IonData.Decimal number -> DocumentNumber(decimalLexeme number)
        | [], IonData.String content -> DocumentString content
        | [], IonData.List items ->
            DocumentArray(
                items
                |> List.mapi (fun i item -> documentValue (itemAt path i) item)
            )
        | [], IonData.Struct fields -> documentObject path fields
        | [], IonData.Null _ -> invalidPayload path "A typed null has no JSON form."
        | [], IonData.Float _ ->
            invalidPayload
                path
                "An Ion float has no place in a document; a number is an int or a decimal."
        | [], IonData.Timestamp _ -> invalidPayload path "An Ion timestamp has no JSON form."
        | [], IonData.Symbol _ -> invalidPayload path "An Ion symbol has no JSON form."
        | [], IonData.Blob _ -> invalidPayload path "An Ion blob has no JSON form."
        | [], IonData.Clob _ -> invalidPayload path "An Ion clob has no JSON form."
        | [], IonData.Sexp _ -> invalidPayload path "An S-expression has no JSON form."
        | _ ->
            invalidPayload path "JSON-compatible Ion holds no annotation but number:: on a string."

    and private documentObject path fields =
        let entries =
            fields
            |> List.map (fun (field, item) ->
                match field with
                | IonSymbol.Text name -> name, documentValue (memberAt path name) item
                | IonSymbol.Zero -> invalidPayload path "A member with no text has no JSON form."
            )

        match OrderedMap.ofList entries with
        | Ok map -> DocumentObject map
        | Error name ->
            invalidPayload (memberAt path name) $"The member '{name}' appears more than once."

    /// Constraints and extensions are JSON objects.
    let documentStruct path (value: IonValue) =
        match value.Data with
        | IonData.Struct _ -> documentValue path value
        | _ -> fail "invalid_type" path "Expected a struct."

    let private lineOrColumn path (value: IonValue) =
        match value.Annotations, value.Data with
        | [], IonData.Int number when
            number
            >= BigInteger System.Int32.MinValue
            && number
               <= BigInteger System.Int32.MaxValue
            ->
            int number
        | _ -> fail "invalid_type" path "Expected an integer."

    let sourceLocation path (value: IonValue) : SourceLocation =
        let values = members path value
        closed path [ "startLine"; "startColumn"; "endLine"; "endColumn" ] values

        {
            StartLine = required path "startLine" lineOrColumn values
            StartColumn = required path "startColumn" lineOrColumn values
            EndLine = required path "endLine" lineOrColumn values
            EndColumn = required path "endColumn" lineOrColumn values
        }

    let private typeAttributes path (values: Members) : TypeAttributes = {
        Source = optional path "source" sourceLocation values
        Constraints = optional path "constraints" documentStruct values
        Extensions = optional path "extensions" documentStruct values
    }

    let private attributeMembers = [ "source"; "constraints"; "extensions" ]

    let rec typ path (value: IonValue) : Type =
        enterNode ()

        let expanded (allowed: string list) =
            let values = members path value

            closed
                path
                (allowed
                 @ attributeMembers)
                values

            typeAttributes path values, values

        match annotationNames path value, value.Data with
        | [], IonData.String input ->
            if looksQualified input then
                Type.Reference(TypeAttributes.empty, fqNameText path input, [])
            else
                Type.Variable(TypeAttributes.empty, nameText path input)
        | ([] | [ "tuple" ]), IonData.List _ ->
            Type.Tuple(TypeAttributes.empty, types path { value with Annotations = [] })
        | [ "tuple" ], IonData.Struct _ ->
            let attributes, values = expanded [ "elements" ]
            Type.Tuple(attributes, required path "elements" types values)
        | [ "variable" ], IonData.Struct _ ->
            let attributes, values = expanded [ "name" ]
            Type.Variable(attributes, required path "name" nameString values)
        | [ "reference" ], IonData.Struct _ ->
            let attributes, values = expanded [ "name"; "arguments" ]

            Type.Reference(
                attributes,
                required path "name" fqNameString values,
                optional path "arguments" types values
                |> Option.defaultValue []
            )
        | [ "record" ], IonData.Struct _ ->
            let attributes, values = expanded [ "fields" ]
            Type.Record(attributes, fields path values)
        | [ "extensibleRecord" ], IonData.Struct _ ->
            let attributes, values = expanded [ "variable"; "fields" ]

            Type.ExtensibleRecord(
                attributes,
                required path "variable" nameString values,
                fields path values
            )
        | [ "function" ], IonData.Struct _ ->
            let attributes, values = expanded [ "parameterType"; "returnType" ]

            Type.Function(
                attributes,
                required path "parameterType" typ values,
                required path "returnType" typ values
            )
        | [ "unit" ], IonData.Struct _ ->
            let attributes, _ = expanded []
            Type.Unit attributes
        | [], _ -> fail "invalid_type" path "Expected a type."
        | names, _ -> fail "invalid_node" path $"{spell names} is not a type."

    and private types path value =
        listItems path value
        |> List.map (fun (itemPath, item) -> typ itemPath item)

    and private fields path (values: Members) = namedTypes path "fields" values

    /// Reads a list of <c>{ name, type }</c>, refusing a repeated name.
    and namedTypes path name (values: Members) : (Name * Type) list =
        let here = memberAt path name

        optionalItems path name values
        |> List.map (fun (itemPath, item) ->
            let entry = members itemPath item
            closed itemPath [ "name"; "type" ] entry

            required itemPath "name" nameString entry, required itemPath "type" typ entry
        )
        |> uniqueNames here

    /// Reads a struct keyed by name, in order: parameters. A missing struct is empty.
    let inputs path name (values: Members) : (Name * Type) list =
        match tryMember name values with
        | None -> []
        | Some value ->
            let here = memberAt path name

            keyedMembers here value
            |> List.map (fun (key, item) ->
                let itemPath = memberAt here key
                nameText itemPath key, typ itemPath item
            )
            |> uniqueNames here

    let holeReason path (value: IonValue) =
        match annotationNames path value, value.Data with
        | [ "unresolvedReference" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "target" ] values
            UnresolvedReference(required path "target" fqNameString values)
        | [ "deletedDuringRefactor" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "txId" ] values
            DeletedDuringRefactor(required path "txId" text values)
        | [ "typeMismatch" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "expected"; "found" ] values
            TypeMismatch(required path "expected" text values, required path "found" text values)
        | names, _ -> fail "invalid_node" path $"{spell names} is not a hole reason."

    /// Reads <c>draft</c> or <c>hole::{ reason, partialBody }</c>.
    let incompleteness path (value: IonValue) =
        match annotationNames path value, value.Data with
        | [], IonData.Symbol(IonSymbol.Text "draft") -> Incompleteness.Draft
        | [ "hole" ], IonData.Struct _ ->
            let values = members path value
            closed path [ "reason"; "partialBody" ] values

            Incompleteness.Hole(
                required path "reason" holeReason values,
                optional path "partialBody" typ values
            )
        | _ -> fail "invalid_node" path "incompleteness is draft or hole::{ reason }."
