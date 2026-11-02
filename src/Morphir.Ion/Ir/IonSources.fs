namespace Morphir.Ion

open System
open Morphir.IR.Pipeline

/// Reading the bytes of a file or a tree's file.
module internal IonSources =
    let private binaryMarker = [| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |]

    /// Whether bytes start with the Ion 1.0 binary marker.
    let hasBinaryMarker (bytes: ReadOnlyMemory<byte>) =
        bytes.Length
        >= 4
        && bytes.Span.Slice(0, 4).SequenceEqual(ReadOnlySpan<byte> binaryMarker)

    /// Reads a stream to its end, or until it holds one byte more than the limit allows, so that an oversized
    /// stream is never read in full.
    let private readCapped
        (limits: IonReadLimits)
        (stream: System.IO.Stream)
        : ReadOnlyMemory<byte> =
        let maximum =
            min
                (int64 limits.MaxDecodedBytes
                 + 1L)
                (int64 Int32.MaxValue)

        use memory = new System.IO.MemoryStream()
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

        ReadOnlyMemory<byte>(memory.ToArray())

    /// The bytes of a file's content, or the exception that stopped them. Absent content is empty.
    let readContent
        (limits: IonReadLimits)
        (content: VFileContent option)
        : Result<ReadOnlyMemory<byte>, exn> =
        match content with
        | None -> Ok ReadOnlyMemory<byte>.Empty
        | Some(VFileContent.Bytes bytes) -> Ok bytes
        | Some other ->
            match VFileContent.openRead other with
            | Result.Error(ContentError.ReadFailed(_, cause)) -> Result.Error cause
            | Result.Ok stream ->
                try
                    use stream = stream
                    Ok(readCapped limits stream)
                with
                | :? OutOfMemoryException -> reraise ()
                | error -> Result.Error error
