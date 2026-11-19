// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Globalization
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks

/// A stream failure. The connection must stop rather than guess the next frame boundary.
[<RequireQualifiedAccess>]
type FramingFailure =
    | HeaderTooLarge
    | BodyTooLarge
    | NegotiatedBodyTooLarge of Maximum: int
    | MissingLength
    | InvalidLength
    | NotFramed
    | TruncatedHeader
    | TruncatedBody

    /// Stable diagnostic text for the stream boundary.
    override this.ToString() =
        match this with
        | HeaderTooLarge -> "The Content-Length header block exceeds 8192 bytes."
        | BodyTooLarge -> "The Content-Length body exceeds 64 MiB."
        | NegotiatedBodyTooLarge maximum -> $"The JSON-RPC frame body exceeds maxFrameBytes ({maximum})."
        | MissingLength -> "The frame has no Content-Length header."
        | InvalidLength -> "The frame has an invalid or repeated Content-Length header."
        | NotFramed -> "The stream is not framed with ASCII Content-Length headers."
        | TruncatedHeader -> "The stream ended inside a frame header."
        | TruncatedBody -> "The stream ended inside a frame body."

/// One complete frame, a clean end between frames, or a terminal framing failure.
[<RequireQualifiedAccess>]
type FrameRead =
    | Message of body: byte array
    | EndOfStream
    | Refused of failure: FramingFailure

    /// Handles every outcome for C# callers.
    member this.Match(message: Func<byte array, 'T>, endOfStream: Func<'T>, refused: Func<FramingFailure, 'T>) =
        match this with
        | Message body -> message.Invoke body
        | EndOfStream -> endOfStream.Invoke()
        | Refused failure -> refused.Invoke failure

    /// Stable diagnostic text, without reflecting on the payload.
    override this.ToString() =
        match this with
        | Message body -> $"Frame ({body.Length} bytes)"
        | EndOfStream -> "End of stream"
        | Refused failure -> failure.ToString()

/// The transport starts at its protocol ceiling and can narrow after initialization.
type internal FrameBodyBudget() =
    let mutable maximum = 64 * 1024 * 1024

    static member ProtocolMaximum = 64 * 1024 * 1024

    member _.Restrict(limit: int) =
        Volatile.Write(&maximum, min FrameBodyBudget.ProtocolMaximum limit)

    member _.Refusal(length: int64) =
        let limit = Volatile.Read(&maximum)
        if length > int64 FrameBodyBudget.ProtocolMaximum then Some FramingFailure.BodyTooLarge
        elif length > int64 limit then Some(FramingFailure.NegotiatedBodyTooLarge limit)
        else None

    member budget.CheckWrite(length: int) =
        match budget.Refusal(int64 length) with
        | Some FramingFailure.BodyTooLarge -> invalidArg "body" (FramingFailure.BodyTooLarge.ToString())
        | Some failure -> raise (IOException(failure.ToString()))
        | None -> ()

/// The MEP stream framing shared by hosts and guests. Payload sizes count UTF-8 bytes.
[<AbstractClass; Sealed>]
type ContentLengthFraming private () =
    /// Maximum complete header block, including the final empty line.
    static member MaxHeaderBytes = 8192

    /// Maximum body allocation admitted by a frame.
    static member MaxBodyBytes = FrameBodyBudget.ProtocolMaximum

    /// Reads one frame, preserving bytes. Cancellation and I/O failures propagate to the caller.
    static member ReadAsync(stream: Stream, ct: CancellationToken) : Task<FrameRead> =
        ContentLengthFraming.ReadAsync(stream, FrameBodyBudget(), ct)

    /// Checks the current budget after reading the header and before allocating or reading the body.
    static member internal ReadAsync(stream: Stream, budget: FrameBodyBudget, ct: CancellationToken) : Task<FrameRead> =
        task {
            ct.ThrowIfCancellationRequested()
            let header = ResizeArray<byte>()
            let single = Array.zeroCreate<byte> 1
            let mutable complete = false
            let mutable failure = None
            let mutable ended = false

            while not complete && failure.IsNone && not ended do
                let! count = stream.ReadAsync(single.AsMemory(), ct)

                if count = 0 then
                    ended <- true
                elif single[0] > 127uy then
                    failure <- Some FramingFailure.NotFramed
                else
                    header.Add single[0]
                    let n = header.Count

                    if n > ContentLengthFraming.MaxHeaderBytes then
                        failure <- Some FramingFailure.HeaderTooLarge
                    elif single[0] = 10uy && (n < 2 || header[n - 2] <> 13uy) then
                        failure <- Some FramingFailure.NotFramed
                    elif
                        n >= 4
                        && header[n - 4] = 13uy
                        && header[n - 3] = 10uy
                        && header[n - 2] = 13uy
                        && header[n - 1] = 10uy
                    then
                        complete <- true
                    elif n >= 2 && header[n - 2] = 13uy && header[n - 1] = 10uy then
                        let line = Encoding.ASCII.GetString(header.ToArray()).TrimEnd('\r', '\n')

                        if not (line.Contains ':') then
                            failure <- Some FramingFailure.NotFramed

            match failure with
            | Some error -> return FrameRead.Refused error
            | None when ended && header.Count = 0 -> return FrameRead.EndOfStream
            | None when ended -> return FrameRead.Refused FramingFailure.TruncatedHeader
            | None ->
                let lines =
                    Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)

                let mutable length = None
                let mutable invalid = false

                for line in lines do
                    let colon = line.IndexOf ':'

                    if colon <= 0 then
                        invalid <- true
                    elif line.Substring(0, colon).Equals("Content-Length", StringComparison.OrdinalIgnoreCase) then
                        let value = line.Substring(colon + 1).Trim()

                        match Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture) with
                        | true, parsed when length.IsNone -> length <- Some parsed
                        | _ -> invalid <- true

                match length, length |> Option.bind budget.Refusal with
                | _ when invalid -> return FrameRead.Refused FramingFailure.InvalidLength
                | None, _ -> return FrameRead.Refused FramingFailure.MissingLength
                | Some _, Some refusal -> return FrameRead.Refused refusal
                | Some value, None ->
                    let body = Array.zeroCreate<byte> (int value)
                    let mutable offset = 0
                    let mutable truncated = false

                    while offset < body.Length && not truncated do
                        let! count = stream.ReadAsync(body.AsMemory(offset), ct)

                        if count = 0 then
                            truncated <- true
                        else
                            offset <- offset + count

                    return
                        if truncated then
                            FrameRead.Refused FramingFailure.TruncatedBody
                        else
                            FrameRead.Message body
        }

    /// Writes and flushes one frame. The caller serializes concurrent writes to the same stream.
    static member WriteAsync(stream: Stream, body: ReadOnlyMemory<byte>, ct: CancellationToken) : Task =
        task {
            ct.ThrowIfCancellationRequested()

            if body.Length > ContentLengthFraming.MaxBodyBytes then
                invalidArg (nameof body) (FramingFailure.BodyTooLarge.ToString())

            let length = body.Length.ToString(CultureInfo.InvariantCulture)
            let header = Encoding.ASCII.GetBytes $"Content-Length: {length}\r\n\r\n"
            do! stream.WriteAsync(header.AsMemory(), ct)
            do! stream.WriteAsync(body, ct)
            do! stream.FlushAsync(ct)
        }
