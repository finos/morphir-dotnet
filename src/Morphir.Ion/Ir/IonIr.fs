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
