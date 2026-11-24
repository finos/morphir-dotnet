namespace Morphir.Ion

open System
open System.Threading

/// <summary>Reads a single-file Morphir distribution written in the Ion IR format.</summary>
[<RequireQualifiedAccess>]
module IonIr =
    /// The stack for a document that does not fit the caller's. A Debug build takes about 2 KB for each level
    /// of a value, so the default depth limit needs about 1 MB, which is most of a thread's stack. The memory
    /// is committed only as it is used.
    let private deepStackBytes =
        256
        * 1024
        * 1024

    let private decodeHere (values: IonValue list) =
        let document = NormalizedDocument.normalize values
        let version = document.Header.FormatVersion

        match document.Header.Family with
        | ClassicFamily -> ClassicIr(version, ClassicIonDistribution.decode document)
        | V4Family -> V4Ir(version, V4IonDistribution.decode document)

    let private tooDeep: IonIrError = {
        Code = "depth_limit_exceeded"
        Message = "The document nests too deeply to read."
        Path = None
        Location = None
    }

    let private unexpected (error: exn) : IonIrError = {
        Code = "ion_ir_decode_failed"
        Message =
            "The reader failed unexpectedly: "
            + error.Message
        Path = None
        Location = None
    }

    /// Runs the work. A short stack is raised as <see cref="InsufficientExecutionStackException"/>, for
    /// <see cref="withRoom"/> to meet, and running out of memory is raised as it is. Any other exception
    /// comes back as an error, since the readers return errors for bad input and an exception is a defect.
    let private attempt (failed: IonIrError -> 'r) (work: unit -> 'r) : 'r =
        try
            work ()
        with
        | IonIrFailure error -> failed error
        | :? InsufficientExecutionStackException -> reraise ()
        | :? OutOfMemoryException -> reraise ()
        | error -> failed (unexpected error)

    /// Runs the work again on a thread with a large stack. Whatever it raises, except running out of memory,
    /// comes back as an error here, since an exception on this thread would end the process.
    let private onDeepStack (failed: IonIrError -> 'r) (work: unit -> 'r) : 'r =
        let result = ref (failed tooDeep)

        let run () =
            try
                result.Value <- attempt failed work
            with
            | :? InsufficientExecutionStackException -> result.Value <- failed tooDeep
            | :? OutOfMemoryException -> reraise ()
            | error -> result.Value <- failed (unexpected error)

        let thread = Thread(run, deepStackBytes)
        thread.IsBackground <- true

        let started =
            try
                thread.Start()
                true
            with _ ->
                // The stack could not be had, so the document is too deep for this process.
                false

        if started then
            thread.Join()
            result.Value
        else
            failed tooDeep

    /// Tries the work here, and where the caller's stack is short, again with room. The work must start from
    /// nothing each time it runs, so a budget or a source it uses is made inside it. A document tree reads its
    /// files in such work, and its sources are opened again.
    let internal withRoom (failed: IonIrError -> 'r) (work: unit -> 'r) : 'r =
        try
            attempt failed work
        with :? InsufficientExecutionStackException ->
            onDeepStack failed work

    /// Decodes top-level values that a caller already read.
    let internal decode (values: IonValue list) : Result<IrDocument, IonIrError> =
        withRoom Error (fun () -> Ok(decodeHere values))

    /// <summary>Reads a single-file distribution, datagram or record, text or binary.</summary>
    /// <remarks>
    /// A 3.x document gives <see cref="ClassicIr"/> and a 4.0.0 document gives <see cref="V4Ir"/>. A syntax or
    /// limit error keeps the code of <see cref="IonReadError"/>. Bad input gives an error, never an exception.
    /// </remarks>
    let read
        (limits: IonReadLimits)
        (bytes: ReadOnlyMemory<byte>)
        : Result<IrDocument, IonIrError> =
        // Reading and decoding both recurse, so both run under the same fallback.
        withRoom
            Error
            (fun () ->
                match IonReader.readOrThrowOnShortStack limits bytes with
                | Ok values -> Ok(decodeHere values)
                | Error error ->
                    Error
                        {
                            Code = IonReadError.code error
                            Message = IonReadError.describe error
                            Path = None
                            Location = None
                        }
            )

    let private lossyError (message: string) : Result<'a, string * string> =
        Error("data.lossy_conversion", message)

    let private foldResults (f: 'a -> Result<unit, string * string>) (items: 'a list) : Result<unit, string * string> =
        items
        |> List.fold (fun state item -> state |> Result.bind (fun () -> f item)) (Ok())

    let private tryAnnotationTexts (location: string) (annotations: IonSymbol list) : Result<string list, string * string> =
        annotations
        |> List.fold
            (fun state symbol ->
                state
                |> Result.bind (fun acc ->
                    match symbol with
                    | IonSymbol.Text text -> Ok(text :: acc)
                    | IonSymbol.Zero ->
                        lossyError
                            $"Ion value in {location} carries an unnamed symbol-zero annotation that JSON IR cannot preserve."
                )
            )
            (Ok [])
        |> Result.map List.rev

    let private requireUnannotated (location: string) (context: string) (value: IonValue) : Result<unit, string * string> =
        tryAnnotationTexts location value.Annotations
        |> Result.bind (function
            | [] -> Ok()
            | names ->
                let joined = String.concat "::" names
                lossyError $"Ion {context} in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let private structFieldsOf
        (location: string)
        (context: string)
        (value: IonValue)
        : Result<(string * IonValue) list, string * string> =
        match value.Data with
        | IonData.Struct fields ->
            fields
            |> List.fold
                (fun state (symbol, item) ->
                    state
                    |> Result.bind (fun acc ->
                        match symbol with
                        | IonSymbol.Zero ->
                            lossyError
                                $"Ion {context} in {location} carries an unnamed symbol-zero field that JSON IR cannot preserve."
                        | IonSymbol.Text name -> Ok((name, item) :: acc)
                    )
                )
                (Ok [])
            |> Result.map List.rev
        | _ -> lossyError $"Expected an Ion struct for {context} in {location}."

    let private plainStructFieldsOf
        (location: string)
        (context: string)
        (value: IonValue)
        : Result<(string * IonValue) list, string * string> =
        requireUnannotated location context value
        |> Result.bind (fun () -> structFieldsOf location context value)

    let private plainListOf
        (location: string)
        (context: string)
        (value: IonValue)
        : Result<IonValue list, string * string> =
        requireUnannotated location context value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.List items -> Ok items
            | _ -> lossyError $"Expected an Ion list for {context} in {location}."
        )

    let private verifyPlainScalar (location: string) (context: string) (value: IonValue) : Result<unit, string * string> =
        requireUnannotated location context value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.Struct _
            | IonData.List _
            | IonData.Sexp _ -> lossyError $"Expected a plain scalar for {context} in {location}."
            | _ -> Ok()
        )

    let rec private verifyDocumentValue (location: string) (value: IonValue) : Result<unit, string * string> =
        IonNodes.enterNode ()

        tryAnnotationTexts location value.Annotations
        |> Result.bind (fun names ->
            match names, value.Data with
            | [ "number" ], IonData.String _ -> Ok()
            | [], IonData.Struct _ ->
                structFieldsOf location "document object" value
                |> Result.bind (foldResults (fun (_, item) -> verifyDocumentValue location item))
            | [], IonData.List items -> foldResults (verifyDocumentValue location) items
            | [],
              (IonData.Null IonNullType.Null
              | IonData.Bool _
              | IonData.Int _
              | IonData.Decimal _
              | IonData.String _) -> Ok()
            | [], _ ->
                lossyError $"Ion document value in {location} has a non-JSON Ion type that JSON IR cannot preserve."
            | _, _ ->
                let joined = String.concat "::" names
                lossyError $"Ion document value in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let private verifyDocumentStruct (location: string) (context: string) (value: IonValue) : Result<unit, string * string> =
        plainStructFieldsOf location context value
        |> Result.bind (foldResults (fun (_, item) -> verifyDocumentValue location item))

    let private verifySourceLocation (location: string) (value: IonValue) : Result<unit, string * string> =
        plainStructFieldsOf location "source location" value
        |> Result.bind (
            foldResults (fun (name, item) ->
                match name with
                | "startLine"
                | "startColumn"
                | "endLine"
                | "endColumn" -> verifyPlainScalar location $"source.{name}" item
                | other ->
                    lossyError
                        $"Ion source location in {location} carries unknown member '{other}' that JSON IR cannot preserve."
            )
        )

    let rec private verifyTypeExpr (location: string) (value: IonValue) : Result<unit, string * string> =
        IonNodes.enterNode ()

        let checkTypeAttr (name, item) =
            match name with
            | "source" -> verifySourceLocation location item
            | "constraints" -> verifyDocumentStruct location "type constraints" item
            | "extensions" -> verifyDocumentStruct location "type extensions" item
            | other ->
                lossyError $"Ion type in {location} carries unknown member '{other}' that JSON IR cannot preserve."

        let verifyNamedTypeFields (fieldsValue: IonValue) =
            plainListOf location "fields" fieldsValue
            |> Result.bind (
                foldResults (fun item ->
                    plainStructFieldsOf location "field" item
                    |> Result.bind (
                        foldResults (fun (k, v) ->
                            match k with
                            | "name" -> verifyPlainScalar location "field.name" v
                            | "type" -> verifyTypeExpr location v
                            | other ->
                                lossyError
                                    $"Ion field in {location} carries unknown member '{other}' that JSON IR cannot preserve."
                        )
                    )
                )
            )

        tryAnnotationTexts location value.Annotations
        |> Result.bind (fun names ->
            match names, value.Data with
            | [], IonData.String _ -> Ok()
            | ([] | [ "tuple" ]), IonData.List items -> foldResults (verifyTypeExpr location) items
            | [ "tuple" ], IonData.Struct _ ->
                structFieldsOf location "tuple type" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        if k = "elements" then
                            plainListOf location "tuple elements" v
                            |> Result.bind (foldResults (verifyTypeExpr location))
                        else
                            checkTypeAttr (k, v)
                    )
                )
            | [ "variable" ], IonData.Struct _ ->
                structFieldsOf location "variable type" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        if k = "name" then verifyPlainScalar location "variable.name" v else checkTypeAttr (k, v)
                    )
                )
            | [ "reference" ], IonData.Struct _ ->
                structFieldsOf location "reference type" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "name" -> verifyPlainScalar location "reference.name" v
                        | "arguments" ->
                            plainListOf location "reference arguments" v
                            |> Result.bind (foldResults (verifyTypeExpr location))
                        | _ -> checkTypeAttr (k, v)
                    )
                )
            | [ "record" ], IonData.Struct _ ->
                structFieldsOf location "record type" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        if k = "fields" then verifyNamedTypeFields v else checkTypeAttr (k, v)
                    )
                )
            | [ "extensibleRecord" ], IonData.Struct _ ->
                structFieldsOf location "extensibleRecord type" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "variable" -> verifyPlainScalar location "extensibleRecord.variable" v
                        | "fields" -> verifyNamedTypeFields v
                        | _ -> checkTypeAttr (k, v)
                    )
                )
            | [ "function" ], IonData.Struct _ ->
                structFieldsOf location "function type" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "parameterType"
                        | "returnType" -> verifyTypeExpr location v
                        | _ -> checkTypeAttr (k, v)
                    )
                )
            | [ "unit" ], IonData.Struct _ ->
                structFieldsOf location "unit type" value
                |> Result.bind (foldResults checkTypeAttr)
            | _, _ ->
                let joined = String.concat "::" names
                lossyError $"Ion type in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let private verifyHoleReason (location: string) (value: IonValue) : Result<unit, string * string> =
        tryAnnotationTexts location value.Annotations
        |> Result.bind (fun names ->
            match names, value.Data with
            | [ "unresolvedReference" ], IonData.Struct _ ->
                structFieldsOf location "unresolvedReference" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        if k = "target" then
                            verifyPlainScalar location "unresolvedReference.target" v
                        else
                            lossyError $"Ion hole reason in {location} carries unknown member '{k}'."
                    )
                )
            | [ "deletedDuringRefactor" ], IonData.Struct _ ->
                structFieldsOf location "deletedDuringRefactor" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        if k = "txId" then
                            verifyPlainScalar location "deletedDuringRefactor.txId" v
                        else
                            lossyError $"Ion hole reason in {location} carries unknown member '{k}'."
                    )
                )
            | [ "typeMismatch" ], IonData.Struct _ ->
                structFieldsOf location "typeMismatch" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "expected"
                        | "found" -> verifyPlainScalar location $"typeMismatch.{k}" v
                        | _ -> lossyError $"Ion hole reason in {location} carries unknown member '{k}'."
                    )
                )
            | _, _ ->
                let joined = String.concat "::" names
                lossyError $"Ion hole reason in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let private verifyIncompleteness (location: string) (value: IonValue) : Result<unit, string * string> =
        tryAnnotationTexts location value.Annotations
        |> Result.bind (fun names ->
            match names, value.Data with
            | [], IonData.Symbol(IonSymbol.Text "draft") -> Ok()
            | [ "hole" ], IonData.Struct _ ->
                structFieldsOf location "incompleteness hole" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "reason" -> verifyHoleReason location v
                        | "partialBody" -> verifyTypeExpr location v
                        | _ -> lossyError $"Ion incompleteness hole in {location} carries unknown member '{k}'."
                    )
                )
            | _, _ ->
                let joined = String.concat "::" names
                lossyError $"Ion incompleteness in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let private verifyNativeHint (location: string) (value: IonValue) : Result<unit, string * string> =
        tryAnnotationTexts location value.Annotations
        |> Result.bind (fun names ->
            match names, value.Data with
            | [], IonData.Symbol(IonSymbol.Text _) -> Ok()
            | [ "platformSpecific" ], IonData.Struct _ ->
                structFieldsOf location "platformSpecific hint" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        if k = "platform" then
                            verifyPlainScalar location "platformSpecific.platform" v
                        else
                            lossyError $"Ion native hint in {location} carries unknown member '{k}'."
                    )
                )
            | _, _ ->
                let joined = String.concat "::" names
                lossyError $"Ion native hint in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let private verifyInputs (location: string) (context: string) (value: IonValue) : Result<unit, string * string> =
        requireUnannotated location context value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.Struct _ ->
                structFieldsOf location context value
                |> Result.bind (foldResults (fun (_, item) -> verifyTypeExpr location item))
            | IonData.List items ->
                items
                |> foldResults (fun item ->
                    plainStructFieldsOf location $"{context} entry" item
                    |> Result.bind (
                        foldResults (fun (k, v) ->
                            match k with
                            | "name" -> verifyPlainScalar location $"{context}.name" v
                            | "type"
                            | "inferredType" -> verifyTypeExpr location v
                            | other ->
                                lossyError
                                    $"Ion {context} entry in {location} carries unknown member '{other}' that JSON IR cannot preserve."
                        )
                    )
                )
            | _ -> lossyError $"Expected an Ion struct or list for {context} in {location}."
        )

    let private verifyValueAttributes (location: string) (value: IonValue) : Result<unit, string * string> =
        plainStructFieldsOf location "value attributes" value
        |> Result.bind (
            foldResults (fun (k, v) ->
                match k with
                | "source" -> verifySourceLocation location v
                | "inferredType" -> verifyTypeExpr location v
                | "extensions" -> verifyDocumentStruct location "value extensions" v
                | other ->
                    lossyError
                        $"Ion value attributes in {location} carry unknown member '{other}' that JSON IR cannot preserve."
            )
        )

    let private verifyPair
        (location: string)
        (context: string)
        (verifyFirst: IonValue -> Result<unit, string * string>)
        (verifySecond: IonValue -> Result<unit, string * string>)
        (value: IonValue)
        : Result<unit, string * string> =
        requireUnannotated location context value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.List [ first; second ]
            | IonData.Sexp [ first; second ] ->
                verifyFirst first
                |> Result.bind (fun () -> verifySecond second)
            | _ -> lossyError $"Expected a two-element pair for {context} in {location}."
        )

    let rec private verifyPattern (location: string) (value: IonValue) : Result<unit, string * string> =
        IonNodes.enterNode ()

        requireUnannotated location "pattern" value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.Symbol _
            | IonData.Bool _
            | IonData.Int _
            | IonData.Float _
            | IonData.List []
            | IonData.Sexp [] -> Ok()
            | IonData.Sexp(head :: args) ->
                verifyPlainScalar location "pattern head" head
                |> Result.bind (fun () ->
                    let headName =
                        match head.Data with
                        | IonData.Symbol(IonSymbol.Text name)
                        | IonData.String name -> Ok name
                        | _ -> lossyError $"Invalid pattern head in {location}."

                    headName
                    |> Result.bind (fun name ->
                        let attrCheck, rest =
                            match args with
                            | first :: tail when IonNodes.isPlainStruct first ->
                                verifyValueAttributes location first, tail
                            | _ -> Ok(), args

                        attrCheck
                        |> Result.bind (fun () ->
                            match name, rest with
                            | "as", [ inner; binding ] ->
                                verifyPattern location inner
                                |> Result.bind (fun () -> verifyPlainScalar location "as pattern name" binding)
                            | "constructor", ctorName :: items ->
                                verifyPlainScalar location "constructor pattern name" ctorName
                                |> Result.bind (fun () -> foldResults (verifyPattern location) items)
                            | ("int" | "float" | "bool" | "string" | "char" | "decimal"), [ lit ] ->
                                verifyPlainScalar location $"{name} literal" lit
                            | _, items -> foldResults (verifyPattern location) items
                        )
                    )
                )
            | _ -> Ok()
        )

    let rec private verifyValueExpr (location: string) (value: IonValue) : Result<unit, string * string> =
        IonNodes.enterNode ()

        requireUnannotated location "value expression" value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.Symbol _
            | IonData.Bool _
            | IonData.Int _
            | IonData.Float _
            | IonData.Sexp [] -> Ok()
            | IonData.Sexp(head :: args) ->
                verifyPlainScalar location "value head" head
                |> Result.bind (fun () ->
                    let headName =
                        match head.Data with
                        | IonData.Symbol(IonSymbol.Text name)
                        | IonData.String name -> Ok name
                        | _ -> lossyError $"Invalid value head in {location}."

                    headName
                    |> Result.bind (fun name ->
                        let attrCheck, rest =
                            match name, args with
                            | "document", [ _ ] -> Ok(), args
                            | _, first :: tail when IonNodes.isPlainStruct first ->
                                verifyValueAttributes location first, tail
                            | _ -> Ok(), args

                        attrCheck
                        |> Result.bind (fun () ->
                            match name, rest with
                            | "document", [ payload ] -> verifyDocumentValue location payload
                            | "hole", [ reason ] -> verifyHoleReason location reason
                            | "hole", [ reason; expected ] ->
                                verifyHoleReason location reason
                                |> Result.bind (fun () -> verifyTypeExpr location expected)
                            | "lambda", [ pat; body ] ->
                                let patCheck =
                                    requireUnannotated location "lambda pattern" pat
                                    |> Result.bind (fun () ->
                                        match pat.Data with
                                        | IonData.List(_ :: _ as items) -> foldResults (verifyPattern location) items
                                        | _ -> verifyPattern location pat
                                    )

                                patCheck
                                |> Result.bind (fun () -> verifyValueExpr location body)
                            | "let", [ bindingName; definition; body ] ->
                                verifyPlainScalar location "let name" bindingName
                                |> Result.bind (fun () -> verifyLetBinding location definition)
                                |> Result.bind (fun () -> verifyValueExpr location body)
                            | "letrec", [ bindings; body ] ->
                                requireUnannotated location "letrec bindings" bindings
                                |> Result.bind (fun () ->
                                    match bindings.Data with
                                    | IonData.List items
                                    | IonData.Sexp items ->
                                        foldResults
                                            (verifyPair
                                                location
                                                "letrec binding"
                                                (verifyPlainScalar location "letrec binding name")
                                                (verifyLetBinding location))
                                            items
                                    | _ -> lossyError $"Expected a list or s-expression for letrec bindings in {location}."
                                )
                                |> Result.bind (fun () -> verifyValueExpr location body)
                            | "match", scrutinee :: cases ->
                                verifyValueExpr location scrutinee
                                |> Result.bind (fun () ->
                                    foldResults
                                        (verifyPair
                                            location
                                            "match case"
                                            (verifyPattern location)
                                            (verifyValueExpr location))
                                        cases
                                )
                            | "destructure", [ pat; bound; body ] ->
                                verifyPattern location pat
                                |> Result.bind (fun () -> verifyValueExpr location bound)
                                |> Result.bind (fun () -> verifyValueExpr location body)
                            | "field", [ target; fieldName ] ->
                                verifyValueExpr location target
                                |> Result.bind (fun () -> verifyPlainScalar location "field name" fieldName)
                            | ("variable" | "ref" | "constructor" | "fieldFunction"), [ operand ] ->
                                verifyPlainScalar location $"{name} operand" operand
                            | ("int" | "float" | "bool" | "string" | "char" | "decimal"), [ lit ] ->
                                verifyPlainScalar location $"{name} literal" lit
                            | "record", entries ->
                                foldResults
                                    (verifyPair
                                        location
                                        "record field"
                                        (verifyPlainScalar location "record field name")
                                        (verifyValueExpr location))
                                    entries
                            | "update", target :: entries ->
                                verifyValueExpr location target
                                |> Result.bind (fun () ->
                                    foldResults
                                        (verifyPair
                                            location
                                            "record update field"
                                            (verifyPlainScalar location "record update field name")
                                            (verifyValueExpr location))
                                        entries
                                )
                            | _, items -> foldResults (verifyValueExpr location) items
                        )
                    )
                )
            | _ -> Ok()
        )

    and private verifyLetBinding (location: string) (value: IonValue) : Result<unit, string * string> =
        plainStructFieldsOf location "let binding" value
        |> Result.bind (
            foldResults (fun (k, v) ->
                match k with
                | "inputTypes" -> verifyInputs location "let.inputTypes" v
                | "outputType" -> verifyTypeExpr location v
                | "body" -> verifyValueExpr location v
                | other ->
                    lossyError $"Ion let binding in {location} carries unknown member '{other}' that JSON IR cannot preserve."
            )
        )

    let private verifyMorphirAnnotation (location: string) (value: IonValue) : Result<unit, string * string> =
        requireUnannotated location "annotation" value
        |> Result.bind (fun () ->
            match value.Data with
            | IonData.String _ -> Ok()
            | IonData.Struct _ ->
                structFieldsOf location "annotation" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "name" -> verifyPlainScalar location "annotation.name" v
                        | "arguments" ->
                            plainListOf location "annotation.arguments" v
                            |> Result.bind (
                                foldResults (fun arg ->
                                    if IonNodes.isPlainStruct arg then
                                        plainStructFieldsOf location "named annotation argument" arg
                                        |> Result.bind (
                                            foldResults (fun (ak, av) ->
                                                match ak with
                                                | "name" -> verifyPlainScalar location "annotation argument name" av
                                                | "value" -> verifyValueExpr location av
                                                | other ->
                                                    lossyError
                                                        $"Ion annotation argument in {location} carries unknown member '{other}'."
                                            )
                                        )
                                    else
                                        verifyValueExpr location arg
                                )
                            )
                        | other ->
                            lossyError $"Ion annotation in {location} carries unknown member '{other}'."
                    )
                )
            | _ -> lossyError $"Expected a string or plain struct for Morphir annotation in {location}."
        )

    let private verifyConstructors (location: string) (value: IonValue) : Result<unit, string * string> =
        plainListOf location "constructors" value
        |> Result.bind (
            foldResults (fun ctor ->
                plainStructFieldsOf location "constructor" ctor
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "name" -> verifyPlainScalar location "constructor.name" v
                        | "args" ->
                            plainListOf location "constructor.args" v
                            |> Result.bind (
                                foldResults (fun arg ->
                                    plainStructFieldsOf location "constructor arg" arg
                                    |> Result.bind (
                                        foldResults (fun (ak, av) ->
                                            match ak with
                                            | "name" -> verifyPlainScalar location "constructor arg name" av
                                            | "type" -> verifyTypeExpr location av
                                            | other ->
                                                lossyError
                                                    $"Ion constructor arg in {location} carries unknown member '{other}'."
                                        )
                                    )
                                )
                            )
                        | other -> lossyError $"Ion constructor in {location} carries unknown member '{other}'."
                    )
                )
            )
        )

    let private verifyExternals (location: string) (value: IonValue) : Result<unit, string * string> =
        plainListOf location "externals" value
        |> Result.bind (
            foldResults (fun item ->
                plainStructFieldsOf location "external binding" item
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "targetPlatform"
                        | "externalName" -> verifyPlainScalar location $"externals.{k}" v
                        | other -> lossyError $"Ion external binding in {location} carries unknown member '{other}'."
                    )
                )
            )
        )

    let private verifyEntryPoints (location: string) (value: IonValue) : Result<unit, string * string> =
        plainStructFieldsOf location "entryPoints" value
        |> Result.bind (
            foldResults (fun (_, entry) ->
                plainStructFieldsOf location "entryPoint" entry
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "target"
                        | "kind"
                        | "doc" -> verifyPlainScalar location $"entryPoint.{k}" v
                        | other -> lossyError $"Ion entryPoint in {location} carries unknown member '{other}'."
                    )
                )
            )
        )

    let rec internal verifyTopLevelElementLossless (location: string) (value: IonValue) : Result<unit, string * string> =
        IonNodes.enterNode ()

        let checkMemberField (k, v) =
            match k with
            | "package"
            | "module"
            | "name"
            | "doc"
            | "access"
            | "description"
            | "fromBaseType"
            | "toBaseType" -> verifyPlainScalar location k v
            | "typeParams" ->
                plainListOf location "typeParams" v
                |> Result.bind (foldResults (verifyPlainScalar location "typeParam"))
            | "annotations" ->
                plainListOf location "annotations" v
                |> Result.bind (foldResults (verifyMorphirAnnotation location))
            | "typeExp"
            | "partialTypeExp"
            | "baseType"
            | "outputType"
            | "output" -> verifyTypeExpr location v
            | "inputTypes"
            | "inputs" -> verifyInputs location k v
            | "constructors" -> verifyConstructors location v
            | "body" -> verifyValueExpr location v
            | "hint" -> verifyNativeHint location v
            | "externals" -> verifyExternals location v
            | "incompleteness" -> verifyIncompleteness location v
            | other ->
                lossyError $"Ion member in {location} carries unknown member '{other}' that JSON IR cannot preserve."

        tryAnnotationTexts location value.Annotations
        |> Result.bind (fun names ->
            match names, value.Data with
            | ([ "package"; "spec" ] | [ "package"; "def" ]), IonData.Struct _ ->
                structFieldsOf location "package element" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "name" -> verifyPlainScalar location "package.name" v
                        | "modules" ->
                            plainListOf location "package.modules" v
                            |> Result.bind (foldResults (verifyTopLevelElementLossless location))
                        | other ->
                            lossyError
                                $"Ion package element in {location} carries unknown member '{other}' that JSON IR cannot preserve."
                    )
                )
            | ([ "module"; "spec" ] | [ ("public" | "private"); "def"; "module" ]), IonData.Struct _ ->
                structFieldsOf location "module element" value
                |> Result.bind (
                    foldResults (fun (k, v) ->
                        match k with
                        | "package"
                        | "name"
                        | "doc" -> verifyPlainScalar location $"module.{k}" v
                        | "annotations" ->
                            plainListOf location "module.annotations" v
                            |> Result.bind (foldResults (verifyMorphirAnnotation location))
                        | "types"
                        | "values" ->
                            plainListOf location $"module.{k}" v
                            |> Result.bind (foldResults (verifyTopLevelElementLossless location))
                        | other ->
                            lossyError
                                $"Ion module element in {location} carries unknown member '{other}' that JSON IR cannot preserve."
                    )
                )
            | ([ ("public" | "private"); "def"; ("alias" | "custom" | "incomplete"); "type" ]
              | [ ("public" | "private"); "def"; "value" ]
              | [ ("public" | "private"); "def"; ("native" | "external" | "incomplete"); "value" ]
              | [ "public"; "spec"; ("opaque" | "alias" | "custom" | "derived"); "type" ]
              | [ "public"; "spec"; "value" ]),
              IonData.Struct _ ->
                structFieldsOf location "member element" value
                |> Result.bind (foldResults checkMemberField)
            | _, _ ->
                let joined = String.concat "::" names
                lossyError $"Ion element in {location} carries annotation '{joined}' that JSON IR cannot preserve."
        )

    let internal verifyHeaderLossless (allowPathBudget: bool) (location: string) (header: IonValue) : Result<unit, string * string> =
        match header.Annotations, header.Data with
        | [ IonSymbol.Text "morphir" ], IonData.Struct fields ->
            fields
            |> foldResults (fun (symbol, value) ->
                match symbol with
                | IonSymbol.Zero ->
                    lossyError
                        $"Ion header in {location} carries an unnamed symbol-zero member that JSON IR cannot preserve."
                | IonSymbol.Text "pathBudget" ->
                    if not allowPathBudget then
                        lossyError
                            $"Single-document Ion header in {location} carries 'pathBudget', which single-document JSON IR does not preserve."
                    else
                        match value.Annotations, value.Data with
                        | [], IonData.Int n when n >= 64I -> Ok()
                        | _ ->
                            lossyError
                                $"Ion tree manifest 'pathBudget' in {location} carries annotations or non-integer data that JSON IR cannot preserve."
                | IonSymbol.Text "critical" ->
                    match value.Annotations, value.Data with
                    | [], IonData.List [] -> Ok()
                    | _ ->
                        lossyError
                            $"Ion header in {location} carries a non-empty or annotated 'critical' list that JSON IR cannot preserve."
                | IonSymbol.Text("formatVersion" | "ionVersion" | "kind" | "packageName" as name) ->
                    verifyPlainScalar location $"header.{name}" value
                | IonSymbol.Text "entryPoints" -> verifyEntryPoints location value
                | IonSymbol.Text("dependencies" | "modules" as name) ->
                    plainListOf location $"header.{name}" value
                    |> Result.bind (foldResults (verifyTopLevelElementLossless location))
                | IonSymbol.Text unknown ->
                    lossyError
                        $"Ion header in {location} carries unknown noncritical member '{unknown}' that JSON IR cannot preserve."
            )
        | _, IonData.Struct _ ->
            lossyError $"Ion header in {location} carries extra annotations that JSON IR cannot preserve."
        | _ -> lossyError $"Ion header in {location} is not a morphir:: struct."

    let internal verifySingleDocumentLossless (values: IonValue list) : Result<unit, string * string> =
        match values with
        | [] -> lossyError "Retained Ion document has no top-level header value."
        | header :: rest ->
            verifyHeaderLossless false "input" header
            |> Result.bind (fun () ->
                match rest with
                | [] -> Ok()
                | _ ->
                    let last = List.last rest

                    match last.Annotations, last.Data with
                    | [ IonSymbol.Text "morphir_footer" ], IonData.Struct [] ->
                        rest
                        |> List.take (rest.Length - 1)
                        |> foldResults (verifyTopLevelElementLossless "input")
                    | _ ->
                        lossyError
                            "Ion datagram carries trailing values or non-empty footer metadata that JSON IR cannot preserve."
            )

    /// <summary>
    /// Verifies that a retained single-document Ion IR input contains no header members (including <c>pathBudget</c>),
    /// annotations not consumed by the decoder at their grammar position, symbol-zero fields, or trailing values that
    /// would be silently dropped when converting to single-document JSON IR. Resource/parse limit failures report their
    /// own error <paramref name="code"/> rather than <c>data.lossy_conversion</c>. For C# callers.
    /// </summary>
    [<CompiledName("TryVerifyLosslessJsonConversion")>]
    let tryVerifyLosslessJsonConversion
        (
            limits: IonReadLimits,
            bytes: ReadOnlyMemory<byte>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] code: byref<string>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] reason: byref<string>
        ) : bool =
        let check =
            withRoom
                (fun err -> Error(err.Code, err.Message))
                (fun () ->
                    match IonReader.readOrThrowOnShortStack limits bytes with
                    | Ok values -> verifySingleDocumentLossless values
                    | Error err -> Error(IonReadError.code err, IonReadError.describe err)
                )

        match check with
        | Ok() ->
            code <- Unchecked.defaultof<_>
            reason <- Unchecked.defaultof<_>
            true
        | Error(errorCode, message) ->
            code <- errorCode
            reason <- message
            false
