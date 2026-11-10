// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// Result diagnostics for refusals. Messages name members and identities, never a member's value, so a credential
/// that a reader refused is never repeated.
module Diagnostics =

    let private message (text: string) =
        match NonBlankText.tryParse text with
        | Ok message -> message
        | Error _ -> failwith "unreachable: every message here is non-blank"

    let private error detail text instruction =
        { Detail = detail
          Severity = Severity.Error
          Message = message text
          Instruction = instruction }

    /// An <c>unsupported-contract</c> diagnostic for a reader refusal.
    let ofReaderRefusal (document: DocumentKind) (refusal: ReaderRefusal) =
        let at =
            match DocumentPath.render refusal.Path with
            | "" -> "the document"
            | path -> path

        let text =
            match refusal.Code with
            | ReaderCode.InvalidDocument -> $"The {DocumentKind.name document} is not a valid document at {at}."
            | ReaderCode.UnsupportedContractVersion ->
                $"This manager reads toolchain {DocumentKind.name document} documents {ContractVersion.current} only."
            | ReaderCode.UnsupportedCriticalMember -> $"The {DocumentKind.name document} names {at} as critical, which this manager does not understand."
            | ReaderCode.UnsupportedCase -> $"The {DocumentKind.name document} has an unknown case at {at}."
            | ReaderCode.InvalidValue -> $"The {DocumentKind.name document} has an invalid value at {at}."
            | ReaderCode.NonPortablePath -> $"The {DocumentKind.name document} has a target path that is not portable at {at}."
            | ReaderCode.CredentialInDocument -> $"The {DocumentKind.name document} carries a credential at {at}; no toolchain document may."
            | ReaderCode.InconsistentDocument -> $"The {DocumentKind.name document} contradicts itself at {at}."

        error (DiagnosticDetail.UnsupportedContract(refusal.Code, document, refusal.Path)) text None

    /// A <c>lock-binding</c> diagnostic for a binding refusal.
    let ofBindingRefusal (refusal: BindingRefusal) =
        let text, instruction =
            match refusal.Problem with
            | BindingProblem.RequestChanged ->
                "The request bytes no longer hash to the lock's requestSha256. Run an explicit setup or update.",
                Some [ "morphirup"; "setup"; "--yes" ]
            | BindingProblem.PlatformNotLocked platform ->
                $"The lock records no {Platform.name platform} closure. Extend the lock with an explicit setup.",
                Some [ "morphirup"; "setup"; "--platform"; Platform.name platform ]
            | BindingProblem.LockChanged -> "The receipt was installed from other lock bytes.", None
            | BindingProblem.ReceiptLockMismatch -> $"The receipt does not repeat the lock at {DocumentPath.render refusal.Path}.", None
            | BindingProblem.LockRecipeMismatch ->
                $"The lock is not a closure its recipe allows at {DocumentPath.render refusal.Path}.", None

        error (DiagnosticDetail.LockBinding(refusal.Problem, refusal.Path)) text instruction

    /// An <c>unsupported-artifact</c> diagnostic for a resolution failure.
    let ofResolutionFailure (failure: ResolutionFailure) =
        let platform = Platform.name failure.Platform
        let path = DocumentPath.render failure.Path

        let problem, text =
            match failure.Code, failure.Abi with
            | ArtifactCode.AbiUnavailable, Some abi ->
                ArtifactProblem.AbiUnavailable abi,
                $"The selected provider at {path} needs the {abi.Text} WASM ABI, which the host does not support. No other ABI is substituted."
            | ArtifactCode.NoCompatibleRelease, _ -> ArtifactProblem.NoCompatibleRelease, $"No release matches {path}."
            | _ ->
                ArtifactProblem.Unsupported,
                $"No artifact matches the request at {path} for {platform}. No other implementation, deployment, release, platform or ABI is substituted."

        let alternatives =
            if List.isEmpty failure.Alternatives then None else Some failure.Alternatives

        error (DiagnosticDetail.UnsupportedArtifact(problem, failure.Component, failure.Platform, alternatives)) text None
