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

    /// <summary>A <c>lock-binding</c> diagnostic for a binding refusal, or a <c>trust-failure</c> diagnostic for
    /// <c>repository_mismatch</c>, whose <c>repositoryId</c> is the repository the check ran against. Instructions name
    /// only commands this build runs, so a refusal that needs setup or update says so in its message instead.</summary>
    let ofBindingRefusal (refusal: BindingRefusal) =
        let path = DocumentPath.render refusal.Path

        match refusal.Failure with
        | BindingFailure.RepositoryMismatch repository ->
            error
                (DiagnosticDetail.TrustFailure(TrustCode.RepositoryMismatch, repository, None, None, None))
                $"The document names another repository at {path} than {repository.Text}, the repository this check runs against."
                None
        | BindingFailure.Binding problem ->
            let text =
                match problem with
                | BindingProblem.RequestChanged ->
                    "The request bytes no longer hash to the lock's requestSha256. An explicit setup or update writes a new lock; this build does not implement them yet."
                | BindingProblem.PlatformNotLocked platform ->
                    $"The lock records no {Platform.name platform} closure. An explicit setup extends the lock; this build does not implement it yet (morphirup setup --plan --platform {Platform.name platform} shows the plan)."
                | BindingProblem.LockChanged -> "The receipt was installed from other lock bytes."
                | BindingProblem.ReceiptLockMismatch -> $"The receipt does not repeat the lock at {path}."
                | BindingProblem.LockRecipeMismatch -> $"The lock is not a closure its recipe allows at {path}."

            error (DiagnosticDetail.LockBinding(problem, refusal.Path)) text None
    /// <summary>A diagnostic for a recipe target refusal: <c>unsupported-contract</c> for the reader codes (document
    /// <c>recipe</c>), <c>trust-failure</c> naming <paramref name="repository"/> for the trust codes, and
    /// <c>unsupported-artifact</c> for a channel that selects nothing.</summary>
    let ofTargetRefusal (repository: Sha256) (platform: Platform) (on: Component) (refusal: TargetRefusal) =
        let path = DocumentPath.render refusal.Path

        match refusal.Code with
        | Code.Reader code -> ofReaderRefusal DocumentKind.Recipe { Code = code; Path = refusal.Path }
        | Code.Trust code ->
            let at = if path = "" then "" else " at " + path

            error
                (DiagnosticDetail.TrustFailure(code, repository, None, None, None))
                $"The recipe target was refused with {Code.name refusal.Code}{at}."
                None
        | _ ->
            error
                (DiagnosticDetail.UnsupportedArtifact(ArtifactProblem.Unsupported, on, platform, None))
                $"No recipe target matches the request at {path}."
                None

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
