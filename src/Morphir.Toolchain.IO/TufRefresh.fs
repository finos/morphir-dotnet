// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain

// Internal control flow only: no transport, persistence format or publisher authority is activated.
[<RequireQualifiedAccess>]
type internal CheckpointRefusal =
    | StorageUnavailable

type internal TufCheckpointProgress =
    { Accepted: TufTrustedSet
      Acknowledged: TufTrustedSet
      Pending: TufAcceptedCheckpoint option }

[<RequireQualifiedAccess>]
type internal TufCheckpointResult =
    | Completed of result: RefreshResult * progress: TufCheckpointProgress
    | PersistenceRefused of refusal: CheckpointRefusal * progress: TufCheckpointProgress
    | Cancelled of cancellation: CancellationToken * progress: TufCheckpointProgress
    | VerificationRefused of failure: TufFailure * progress: TufCheckpointProgress

module internal TufCheckpointRefresh =
    // Callback completion owns the operation lifetime: never detach an outstanding read/write on cancellation.
    // The caller supplies cooperative callbacks and may pass a token linked to its operation deadline.
    let private readAsync fetch (request: TufMetadataRequest) ct = task {
        try return! fetch request ct
        with
        | :? OperationCanceledException as cancelled -> return raise cancelled
        | _ -> return Error(TufFailure.Unavailable request.Role)
    }

    let private acknowledgeAsync persist checkpoint ct = task {
        try return! persist checkpoint ct
        with
        | :? OperationCanceledException as cancelled -> return raise cancelled
        | _ -> return Error CheckpointRefusal.StorageUnavailable
    }

    /// Drives only the shared pure core. Accepted evidence is visible while durability is pending; it is never
    /// advertised as acknowledged until the injected callback returns Ok. No target operation is exposed here.
    let runAsync
        (start: DateTimeOffset)
        (trusted: TufTrustedSet)
        (fetch: TufMetadataRequest -> CancellationToken -> Task<Result<byte array option, TufFailure>>)
        (persist: TufAcceptedCheckpoint -> CancellationToken -> Task<Result<unit, CheckpointRefusal>>)
        (ct: CancellationToken)
        : Task<TufCheckpointResult> = task {
        let mutable progress =
            { Accepted = TufTrustedSnapshot.copy trusted
              Acknowledged = TufTrustedSnapshot.copy trusted
              Pending = None }
        let mutable step = TufAcceptance.start start trusted
        let mutable outcome = None
        try
            while outcome.IsNone do
                match step with
                | TufAcceptanceStep.Request(request, supply) ->
                    ct.ThrowIfCancellationRequested()
                    let! response = readAsync fetch request ct
                    ct.ThrowIfCancellationRequested()
                    step <- supply response
                | TufAcceptanceStep.Accepted(checkpoint, acknowledge) ->
                    // Record acceptance before checking cancellation: verification may have just accepted a key change.
                    progress <- { progress with Accepted = checkpoint.Accepted; Pending = Some checkpoint }
                    ct.ThrowIfCancellationRequested()
                    let! acknowledgement = acknowledgeAsync persist checkpoint ct
                    match acknowledgement with
                    | Error refusal ->
                        ct.ThrowIfCancellationRequested()
                        outcome <- Some(TufCheckpointResult.PersistenceRefused(refusal, progress))
                    | Ok () ->
                        // A successful acknowledgement remains true even if cancellation arrives immediately after it.
                        progress <- { progress with Acknowledged = TufTrustedSnapshot.copy progress.Accepted; Pending = None }
                        ct.ThrowIfCancellationRequested()
                        step <- acknowledge ()
                | TufAcceptanceStep.Complete result ->
                    ct.ThrowIfCancellationRequested()
                    outcome <- Some(TufCheckpointResult.Completed(result, progress))
                | TufAcceptanceStep.Refused(failure, accepted) ->
                    progress <- { progress with Accepted = TufTrustedSnapshot.copy accepted }
                    ct.ThrowIfCancellationRequested()
                    outcome <- Some(TufCheckpointResult.VerificationRefused(failure, progress))
            return Option.get outcome
        with :? OperationCanceledException as cancelled ->
            let token = if ct.IsCancellationRequested then ct else cancelled.CancellationToken
            return TufCheckpointResult.Cancelled(token, progress)
    }
