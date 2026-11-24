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

    let rec internal verifyValueNoSymbolZero (location: string) (value: IonValue) : Result<unit, string> =
        match value.Data with
        | IonData.Struct fields ->
            fields
            |> List.fold
                (fun state (symbol, item) ->
                    state
                    |> Result.bind (fun () ->
                        match symbol with
                        | IonSymbol.Zero ->
                            Error $"Ion value in {location} carries an unnamed symbol-zero field that JSON IR cannot preserve."
                        | IonSymbol.Text _ -> verifyValueNoSymbolZero location item
                    )
                )
                (Ok())
        | IonData.List items
        | IonData.Sexp items ->
            items
            |> List.fold
                (fun state item -> state |> Result.bind (fun () -> verifyValueNoSymbolZero location item))
                (Ok())
        | _ -> Ok()

    let internal verifyHeaderLossless (allowPathBudget: bool) (location: string) (header: IonValue) : Result<unit, string> =
        match header.Annotations, header.Data with
        | [ IonSymbol.Text "morphir" ], IonData.Struct fields ->
            fields
            |> List.fold
                (fun state (symbol, value) ->
                    state
                    |> Result.bind (fun () ->
                        match symbol with
                        | IonSymbol.Zero ->
                            Error $"Ion header in {location} carries an unnamed symbol-zero member that JSON IR cannot preserve."
                        | IonSymbol.Text "pathBudget" ->
                            if not allowPathBudget then
                                Error $"Single-document Ion header in {location} carries 'pathBudget', which single-document JSON IR does not preserve."
                            else
                                match value.Annotations, value.Data with
                                | [], IonData.Int n when n >= 64I -> Ok()
                                | _ ->
                                    Error $"Ion tree manifest 'pathBudget' in {location} carries annotations or non-integer data that JSON IR cannot preserve."
                        | IonSymbol.Text "critical" ->
                            match value.Annotations, value.Data with
                            | [], IonData.List [] -> Ok()
                            | _ ->
                                Error $"Ion header in {location} carries a non-empty or annotated 'critical' list that JSON IR cannot preserve."
                        | IonSymbol.Text("formatVersion"
                        | "ionVersion"
                        | "kind"
                        | "packageName"
                        | "entryPoints"
                        | "dependencies"
                        | "modules" as name) ->
                            if not value.Annotations.IsEmpty then
                                Error $"Ion header member '{name}' in {location} carries annotations that JSON IR cannot preserve."
                            else
                                verifyValueNoSymbolZero location value
                        | IonSymbol.Text unknown ->
                            Error $"Ion header in {location} carries unknown noncritical member '{unknown}' that JSON IR cannot preserve."
                    )
                )
                (Ok())
        | _, IonData.Struct _ ->
            Error $"Ion header in {location} carries extra annotations that JSON IR cannot preserve."
        | _ -> Error $"Ion header in {location} is not a morphir:: struct."

    let internal verifySingleDocumentLossless (values: IonValue list) : Result<unit, string> =
        match values with
        | [] -> Error "Retained Ion document has no top-level header value."
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
                        |> List.fold
                            (fun state item -> state |> Result.bind (fun () -> verifyValueNoSymbolZero "input" item))
                            (Ok())
                    | _ ->
                        Error "Ion datagram carries trailing values or non-empty footer metadata that JSON IR cannot preserve."
            )

    /// <summary>
    /// Verifies that a retained single-document Ion IR input contains no header members (including <c>pathBudget</c>),
    /// annotations, symbol-zero fields, or trailing values that would be silently dropped when converting to
    /// single-document JSON IR. For C# callers: returns <c>true</c> when lossless, or <c>false</c> with
    /// <paramref name="reason"/>.
    /// </summary>
    [<CompiledName("TryVerifyLosslessJsonConversion")>]
    let tryVerifyLosslessJsonConversion
        (
            limits: IonReadLimits,
            bytes: ReadOnlyMemory<byte>,
            [<System.Runtime.InteropServices.Out; System.Diagnostics.CodeAnalysis.MaybeNullWhen(true)>] reason: byref<string>
        ) : bool =
        let check =
            withRoom
                (fun err -> Error err.Message)
                (fun () ->
                    match IonReader.readOrThrowOnShortStack limits bytes with
                    | Ok values -> verifySingleDocumentLossless values
                    | Error err -> Error(IonReadError.describe err)
                )

        match check with
        | Ok() ->
            reason <- Unchecked.defaultof<_>
            true
        | Error message ->
            reason <- message
            false
