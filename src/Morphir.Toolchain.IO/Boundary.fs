// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain.IO

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain

/// Whether this request may open a transport. Offline requests perform no I/O.
[<RequireQualifiedAccess>]
type AcquisitionMode =
    | Online
    | Offline

/// Per-operation input supplied by the caller. This is not repository metadata or a persisted state format.
type FetchRequest =
    { Source: Uri
      MaxBytes: uint64
      Deadline: DateTimeOffset
      Mode: AcquisitionMode }

/// Transport/integrity outcomes only. None constitutes a trust or readiness decision or a serialized contract code.
[<RequireQualifiedAccess>]
type AcquisitionFailure =
    | Offline
    | InvalidRequest
    | DeadlineExceeded
    | TooLarge
    | LengthMismatch
    | DigestMismatch
    | TransportUnavailable
    | StoreUnavailable

/// Owns the response and its unbuffered content. Disposing it must release both with bounded cleanup, including on refusal.
type IAcquisitionResponse =
    inherit IAsyncDisposable
    abstract Content: Stream
    abstract DeclaredLength: int64 option

/// Opens only the explicit source. Implementations must honor cancellation and return headers without draining content.
type IAcquisitionTransport =
    abstract OpenAsync: source: Uri * ct: CancellationToken -> Task<IAcquisitionResponse>

/// Private staging, never an installation receipt. Dispose discards uncommitted bytes.
/// Publish must seal/flush and atomically expose verified bytes without replacing a winner, and report its commit consistently.
/// Dispose must perform bounded cleanup. Expected cleanup failures after a successful commit cannot undo that commit.
type IPrivateObject =
    inherit IAsyncDisposable
    abstract WriteAsync: bytes: ReadOnlyMemory<byte> * ct: CancellationToken -> ValueTask
    abstract PublishAsync: ct: CancellationToken -> Task

/// The store owns staging cleanup, immutable publication and verification of any existing winner.
/// Beginning or writing private bytes does not publish them. Cancellation must be honored before the store's commit.
type IAcquisitionStore =
    abstract BeginAsync:
        target: TargetPath * expected: AuthenticatedTarget * ct: CancellationToken -> Task<IPrivateObject>
