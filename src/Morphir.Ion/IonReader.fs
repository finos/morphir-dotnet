namespace Morphir.Ion

open System
open System.Globalization
open System.IO
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Eleven19.IonDotnet
open Eleven19.IonDotnet.Builders

/// <summary>Remaining allowance for one read, or for every file of a document tree.</summary>
type internal IonReadBudget(limits: IonReadLimits) =
    let mutable bytes = 0L
    let mutable nodes = 0L

    member _.Limits = limits

    /// Whether the bytes fit, counting them against the budget.
    member _.TakeBytes(count: int) =
        bytes <-
            bytes
            + int64 count

        bytes
        <= int64 limits.MaxDecodedBytes

    /// Whether another node fits, counting it against the budget.
    member _.TakeNode() =
        nodes <- nodes + 1L

        nodes
        <= int64 limits.MaxNodes

/// A limit was reached part way through reading.
exception private LimitReached of IonReadError

/// <summary>Reads Ion text and binary into immutable values.</summary>
[<RequireQualifiedAccess>]
module IonReader =
    let private binaryMarker = [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |]

    let private symbol (token: SymbolToken) =
        match token.Text with
        | null -> IonSymbol.Zero
        | text -> IonSymbol.Text text

    /// The canonical Ion text of a timestamp, keeping its declared precision and fraction digits.
    let private timestampText (value: Timestamp) =
        let culture = CultureInfo.InvariantCulture

        let local =
            match value.DateTimeValue.Kind with
            | DateTimeKind.Local -> value.AsDateTimeOffset().DateTime
            | _ -> value.DateTimeValue

        let offset () =
            match value.DateTimeValue.Kind with
            | DateTimeKind.Unspecified -> "-00:00"
            | DateTimeKind.Utc -> "Z"
            | _ ->
                let minutes = abs value.LocalOffset
                let sign = if value.LocalOffset < 0 then "-" else "+"

                sign
                + (minutes
                   / 60)
                    .ToString("D2", culture)
                + ":"
                + (minutes % 60).ToString("D2", culture)

        match value.TimestampPrecision with
        | Timestamp.Precision.Year -> local.ToString("yyyy'T'", culture)
        | Timestamp.Precision.Month -> local.ToString("yyyy-MM'T'", culture)
        | Timestamp.Precision.Day -> local.ToString("yyyy-MM-dd", culture)
        | Timestamp.Precision.Minute ->
            local.ToString("yyyy-MM-dd'T'HH:mm", culture)
            + offset ()
        | _ ->
            // The fraction's decimal keeps its scale, so "0.000" and "0" stay different.
            let fraction = value.FractionalSecond.ToString(culture).TrimStart('0')

            local.ToString("yyyy-MM-dd'T'HH:mm:ss", culture)
            + fraction
            + offset ()

    let private nullType ionType =
        if ionType = IonType.Bool then IonNullType.Bool
        elif ionType = IonType.Int then IonNullType.Int
        elif ionType = IonType.Float then IonNullType.Float
        elif ionType = IonType.Decimal then IonNullType.Decimal
        elif ionType = IonType.Timestamp then IonNullType.Timestamp
        elif ionType = IonType.Symbol then IonNullType.Symbol
        elif ionType = IonType.String then IonNullType.String
        elif ionType = IonType.Clob then IonNullType.Clob
        elif ionType = IonType.Blob then IonNullType.Blob
        elif ionType = IonType.List then IonNullType.List
        elif ionType = IonType.Sexp then IonNullType.Sexp
        elif ionType = IonType.Struct then IonNullType.Struct
        else IonNullType.Null

    /// Reads the values at the reader's current level. Recursion is bounded by the depth limit.
    let rec private readLevel
        (budget: IonReadBudget)
        (depth: int)
        (reader: IIonReader)
        : (SymbolToken * IonValue) list =
        // The depth limit may be raised past what the caller's stack holds. A short stack is a depth failure.
        RuntimeHelpers.EnsureSufficientExecutionStack()

        let values = ResizeArray()
        let mutable ionType = reader.MoveNext()

        while ionType
              <> IonType.None do
            if depth > budget.Limits.MaxDepth then
                raise (LimitReached(IonReadError.DepthLimitExceeded budget.Limits.MaxDepth))

            if not (budget.TakeNode()) then
                raise (LimitReached(IonReadError.NodeLimitExceeded budget.Limits.MaxNodes))

            let name =
                if reader.IsInStruct then
                    reader.GetFieldNameSymbol()
                else
                    SymbolToken.None

            let annotations =
                reader.GetTypeAnnotationSymbols()
                |> Seq.map symbol
                |> Seq.toList

            let children () =
                reader.StepIn()
                let items = readLevel budget (depth + 1) reader
                reader.StepOut()
                items

            let data =
                if reader.CurrentIsNull then
                    IonData.Null(nullType ionType)
                elif ionType = IonType.Bool then
                    IonData.Bool(reader.BoolValue())
                elif ionType = IonType.Int then
                    IonData.Int(reader.BigIntegerValue())
                elif ionType = IonType.Float then
                    IonData.Float { Value = reader.DoubleValue() }
                elif ionType = IonType.Decimal then
                    let value = reader.DecimalValue()

                    IonData.Decimal
                        {
                            Coefficient = value.Coefficient
                            Scale = value.Scale
                            IsNegativeZero = value.IsNegativeZero
                        }
                elif ionType = IonType.Timestamp then
                    IonData.Timestamp
                        {
                            Text = timestampText (reader.TimestampValue())
                        }
                elif ionType = IonType.Symbol then
                    IonData.Symbol(symbol (reader.SymbolValue()))
                elif ionType = IonType.String then
                    IonData.String(reader.StringValue())
                elif ionType = IonType.Clob then
                    IonData.Clob(reader.NewByteArray())
                elif ionType = IonType.Blob then
                    IonData.Blob(reader.NewByteArray())
                elif ionType = IonType.List then
                    IonData.List(
                        children ()
                        |> List.map snd
                    )
                elif ionType = IonType.Sexp then
                    IonData.Sexp(
                        children ()
                        |> List.map snd
                    )
                elif ionType = IonType.Struct then
                    IonData.Struct(
                        children ()
                        |> List.map (fun (field, value) -> symbol field, value)
                    )
                else
                    raise (
                        IonException(
                            "Unexpected Ion type "
                            + string ionType
                            + "."
                        )
                    )

            values.Add(
                (name,
                 {
                     Annotations = annotations
                     Data = data
                 })
            )

            ionType <- reader.MoveNext()

        List.ofSeq values

    /// Reads bytes that have already been counted against the budget.
    /// When <paramref name="propagateStack"/> is set, a short stack escapes as <see cref="InsufficientExecutionStackException"/>
    /// so that the caller can read again where there is room. Otherwise it is a depth failure.
    let private parse (propagateStack: bool) (budget: IonReadBudget) (bytes: ReadOnlyMemory<byte>) =
        let isBinary =
            bytes.Length
            >= 4
            && bytes.Span.Slice(0, 4).SequenceEqual(ReadOnlySpan<byte> binaryMarker)

        if
            not isBinary
            && bytes.Length > 0
            && bytes.Span[0] = 0xE0uy
        then
            // Ion text cannot start with this byte, so it is a binary marker for another version.
            Result.Error(
                IonReadError.UnsupportedIonVersion
                    "The input starts with a binary marker for an Ion version other than 1.0."
            )
        else
            try
                // The reader copies nothing it does not need, so a stream over the caller's memory is enough.
                let stream =
                    match MemoryMarshal.TryGetArray bytes with
                    | true, segment ->
                        new MemoryStream(segment.Array, segment.Offset, segment.Count, false)
                    | _ -> new MemoryStream(bytes.ToArray(), false)

                use stream = stream
                use reader = IonReaderBuilder.Build(stream)

                Result.Ok(
                    readLevel budget 1 reader
                    |> List.map snd
                )
            with
            | LimitReached error -> Result.Error error
            | :? InsufficientExecutionStackException when propagateStack -> reraise ()
            | :? InsufficientExecutionStackException ->
                Result.Error(IonReadError.DepthLimitExceeded budget.Limits.MaxDepth)
            | :? UnsupportedIonVersionException as error ->
                Result.Error(IonReadError.UnsupportedIonVersion error.Message)
            | :? OutOfMemoryException -> reraise ()
            // The package signals malformed input with several exception types, not all of them its own.
            | error -> Result.Error(IonReadError.MalformedIon error.Message)

    /// Reads bytes against a budget shared with other reads.
    let private readBudgeted
        (propagateStack: bool)
        (budget: IonReadBudget)
        (bytes: ReadOnlyMemory<byte>)
        =
        if budget.TakeBytes bytes.Length then
            parse propagateStack budget bytes
        else
            Result.Error(IonReadError.DecodedLimitExceeded budget.Limits.MaxDecodedBytes)

    let internal readWithBudget (budget: IonReadBudget) (bytes: ReadOnlyMemory<byte>) =
        readBudgeted false budget bytes

    /// Reads against a budget shared with other reads, except that a short stack raises
    /// <see cref="InsufficientExecutionStackException"/>, so that the caller can start again where there is room.
    let internal readWithBudgetOrThrowOnShortStack
        (budget: IonReadBudget)
        (bytes: ReadOnlyMemory<byte>)
        =
        readBudgeted true budget bytes

    /// Reads like <see cref="read"/>, except that a short stack raises <see cref="InsufficientExecutionStackException"/>.
    let internal readOrThrowOnShortStack (limits: IonReadLimits) (bytes: ReadOnlyMemory<byte>) =
        readBudgeted true (IonReadBudget limits) bytes

    /// <summary>Reads every top-level value in the bytes under the limits.</summary>
    let read
        (limits: IonReadLimits)
        (bytes: ReadOnlyMemory<byte>)
        : Result<IonValue list, IonReadError> =
        readWithBudget (IonReadBudget limits) bytes

    /// <summary>Reads every top-level value in a stream under the limits. The caller owns the stream.</summary>
    /// <remarks>Reading stops one byte past the limit, so an oversized stream is never read in full.</remarks>
    let readStream (limits: IonReadLimits) (stream: Stream) : Result<IonValue list, IonReadError> =
        let maximum =
            int64 limits.MaxDecodedBytes
            + 1L

        use memory = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 81920
        let mutable reading = true

        while reading do
            let wanted =
                min
                    (int64 buffer.Length)
                    (maximum
                     - memory.Length)

            let count =
                if wanted > 0L then
                    stream.Read(buffer, 0, int wanted)
                else
                    0

            if count = 0 then
                reading <- false
            else
                memory.Write(buffer, 0, count)

        read limits (ReadOnlyMemory<byte>(memory.GetBuffer(), 0, int memory.Length))
