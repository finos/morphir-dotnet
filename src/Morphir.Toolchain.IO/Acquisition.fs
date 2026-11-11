// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain

exception private Refused of AcquisitionFailure

module private BoundedTransfer =
    let refuse failure = raise (Refused failure)

    let active (clock: TimeProvider) (request: FetchRequest) (ct: CancellationToken) =
        ct.ThrowIfCancellationRequested()
        if clock.GetUtcNow() >= request.Deadline then refuse AcquisitionFailure.DeadlineExceeded

    let transport (work: unit -> Task<'T>) = task {
        try
            return! work ()
        with
        | :? IOException
        | :? HttpRequestException -> return refuse AcquisitionFailure.TransportUnavailable
    }

    let store (work: unit -> Task<'T>) = task {
        try
            return! work ()
        with
        | :? IOException
        | :? UnauthorizedAccessException -> return refuse AcquisitionFailure.StoreUnavailable
    }

    type ResponseLease(response: IAcquisitionResponse) =
        let mutable opened = true
        member _.CloseAsync() =
            if opened then
                opened <- false
                transport (fun () -> task { do! response.DisposeAsync() })
            else Task.FromResult(())
        interface IAsyncDisposable with
            member this.DisposeAsync() = ValueTask(this.CloseAsync())

    // A header constrains the transfer, but never replaces an EOF, length or digest check.
    let lengths (request: FetchRequest) (expectedLength: uint64 option) (declared: int64 option) =
        match expectedLength with
        | Some length when length > request.MaxBytes -> refuse AcquisitionFailure.TooLarge
        | _ -> ()

        let declaredLength =
            match declared with
            | Some length when length < 0L -> refuse AcquisitionFailure.LengthMismatch
            | Some length when uint64 length > request.MaxBytes -> refuse AcquisitionFailure.TooLarge
            | Some length -> Some(uint64 length)
            | None -> None

        match expectedLength, declaredLength with
        | Some expected, Some header when expected <> header -> refuse AcquisitionFailure.LengthMismatch
        | _ -> ()

        let exact = Option.orElse declaredLength expectedLength
        Option.defaultValue request.MaxBytes exact, exact

    let copy
        (clock: TimeProvider)
        (request: FetchRequest)
        (input: Stream)
        (limit: uint64)
        (exact: uint64 option)
        (expectedDigest: Sha256 option)
        (write: ReadOnlyMemory<byte> -> CancellationToken -> Task)
        (ct: CancellationToken)
        = task {
        // Avoid limit + 1 overflow, and never size working memory from an untrusted length.
        let bufferSize = if limit >= 65536UL then 65536 else int limit + 1
        let buffer = Array.zeroCreate<byte> bufferSize
        let digest = Digest.Incremental()
        let mutable total = 0UL
        let mutable reading = true

        while reading do
            active clock request ct
            let remaining = limit - total
            let size = if remaining >= uint64 buffer.Length then buffer.Length else int remaining + 1
            let! count = transport (fun () -> input.ReadAsync(buffer.AsMemory(0, size), ct).AsTask())
            active clock request ct

            if count < 0 || count > size then
                refuse AcquisitionFailure.TransportUnavailable
            elif count = 0 then
                reading <- false
            elif uint64 count > remaining then
                refuse (if exact.IsSome then AcquisitionFailure.LengthMismatch else AcquisitionFailure.TooLarge)
            else
                digest.Append(buffer, 0, count)
                do! write (ReadOnlyMemory(buffer, 0, count)) ct
                total <- total + uint64 count
                active clock request ct

        if exact |> Option.exists ((<>) total) then refuse AcquisitionFailure.LengthMismatch
        let actual = digest.Finish()
        if expectedDigest |> Option.exists (fun expected -> expected.Text <> actual) then
            refuse AcquisitionFailure.DigestMismatch
    }

/// Bounded byte acquisition. Expectations are caller input, never publisher authentication or readiness evidence.
[<Sealed>]
type Acquisition(transport: IAcquisitionTransport, clock: TimeProvider) =
    do
        if obj.ReferenceEquals(transport, null) then nullArg (nameof transport)
        if obj.ReferenceEquals(clock, null) then nullArg (nameof clock)

    let run (request: FetchRequest) (ct: CancellationToken) (work: CancellationToken -> Task<'T>) = task {
        ct.ThrowIfCancellationRequested()
        if request.Mode = AcquisitionMode.Offline then
            return Error AcquisitionFailure.Offline
        elif obj.ReferenceEquals(request.Source, null) || not request.Source.IsAbsoluteUri then
            return Error AcquisitionFailure.InvalidRequest
        else
            let remaining = request.Deadline - clock.GetUtcNow()
            if remaining <= TimeSpan.Zero then
                return Error AcquisitionFailure.DeadlineExceeded
            else
                // The platform timer has a finite supported due time. Refuse an unsupported request before I/O.
                let timer =
                    try Ok(new CancellationTokenSource(remaining, clock))
                    with :? ArgumentOutOfRangeException -> Error AcquisitionFailure.InvalidRequest
                match timer with
                | Error failure -> return Error failure
                | Ok deadline ->
                    use deadline = deadline
                    use linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token)
                    try
                        BoundedTransfer.active clock request linked.Token
                        let! value = work linked.Token
                        return Ok value
                    with
                    | _ when ct.IsCancellationRequested ->
                        return raise (OperationCanceledException(ct))
                    | _ when deadline.IsCancellationRequested ->
                        return Error AcquisitionFailure.DeadlineExceeded
                    | Refused failure -> return Error failure
    }

    /// Bounded bytes for a verifier, without publisher authentication.
    member _.ReadMetadataAsync
        (request: FetchRequest, expected: TufMetaFile option, ct: CancellationToken)
        : Task<Result<byte array, AcquisitionFailure>> =
        run request ct (fun token -> task {
            if request.MaxBytes > uint64 Array.MaxLength then BoundedTransfer.refuse AcquisitionFailure.InvalidRequest
            let expectedLength = expected |> Option.map _.Length
            BoundedTransfer.lengths request expectedLength None |> ignore
            let! response = BoundedTransfer.transport (fun () -> transport.OpenAsync(request.Source, token))
            use responseScope = new BoundedTransfer.ResponseLease(response)
            BoundedTransfer.active clock request token
            let limit, exact = BoundedTransfer.lengths request expectedLength response.DeclaredLength
            // Capacity is checked before allocation and cannot grow: the copy loop refuses the first excess byte.
            use output = new MemoryStream(int limit)
            do! BoundedTransfer.copy clock request response.Content limit exact (expected |> Option.map _.Sha256)
                    (fun bytes cancellation -> output.WriteAsync(bytes, cancellation).AsTask()) token
            do! responseScope.CloseAsync()
            BoundedTransfer.active clock request token
            return output.ToArray()
        })

    /// Exact byte integrity and private publication, without trust, installation receipts or readiness.
    member _.AcquireTargetAsync
        (request: FetchRequest, target: TargetPath, expected: AuthenticatedTarget, store: IAcquisitionStore, ct: CancellationToken)
        : Task<Result<unit, AcquisitionFailure>> =
        run request ct (fun token -> task {
            BoundedTransfer.lengths request (Some expected.Length) None |> ignore
            let! response = BoundedTransfer.transport (fun () -> transport.OpenAsync(request.Source, token))
            use responseScope = new BoundedTransfer.ResponseLease(response)
            BoundedTransfer.active clock request token
            let limit, exact = BoundedTransfer.lengths request (Some expected.Length) response.DeclaredLength
            let! staged = BoundedTransfer.store (fun () -> store.BeginAsync(target, expected, token))
            let mutable committed = false
            use stageScope = {
                new IAsyncDisposable with
                    member _.DisposeAsync() = ValueTask(task {
                        try
                            do! staged.DisposeAsync()
                        with
                        | :? IOException when committed -> ()
                        | :? UnauthorizedAccessException when committed -> ()
                        | :? OperationCanceledException when committed -> ()
                        | :? IOException
                        | :? UnauthorizedAccessException -> BoundedTransfer.refuse AcquisitionFailure.StoreUnavailable
                    })
            }
            BoundedTransfer.active clock request token
            do! BoundedTransfer.copy clock request response.Content limit exact (Some expected.Sha256)
                    (fun bytes cancellation -> BoundedTransfer.store (fun () -> task { do! staged.WriteAsync(bytes, cancellation) })) token
            // Dispose the response before the only operation that can make bytes visible.
            do! responseScope.CloseAsync()
            BoundedTransfer.active clock request token
            do! BoundedTransfer.store (fun () -> task { do! staged.PublishAsync token })
            committed <- true
            // The store owns its commit boundary. Cancellation arriving after a successful commit cannot undo it.
        })
