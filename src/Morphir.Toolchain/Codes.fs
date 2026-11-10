// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

/// The eight codes a document reader reports. Each has the result diagnostic kind <c>unsupported-contract</c>.
[<RequireQualifiedAccess>]
type ReaderCode =
    /// Not the document's syntax, a required member is absent, or a member has the wrong JSON type.
    | InvalidDocument
    /// <c>schemaVersion</c> is not a listed version.
    | UnsupportedContractVersion
    /// <c>critical</c> names a path the reader does not understand.
    | UnsupportedCriticalMember
    /// A tagged member has an unknown case.
    | UnsupportedCase
    /// A scalar fails its validated type.
    | InvalidValue
    /// A target path breaks the target path rules.
    | NonPortablePath
    /// A credential member or a URL carrying credentials.
    | CredentialInDocument
    /// Valid members that contradict each other.
    | InconsistentDocument

/// The codes that check one document against another. Each has the result diagnostic kind <c>lock-binding</c>.
[<RequireQualifiedAccess>]
type BindingCode =
    | RequestChanged
    | PlatformNotLocked
    | LockChanged
    | ReceiptLockMismatch
    | LockRecipeMismatch

/// The codes of the <c>unsupported-artifact</c> diagnostic kind.
[<RequireQualifiedAccess>]
type ArtifactCode =
    | UnsupportedArtifact
    | AbiUnavailable
    | NoCompatibleRelease

/// The codes of the <c>trust-failure</c> diagnostic kind.
[<RequireQualifiedAccess>]
type TrustCode =
    | TargetNotAuthenticated
    | TargetLengthMismatch
    | TargetDigestMismatch
    | ReleaseDescriptorMismatch
    | ArtifactPlatformMismatch
    /// A document names a repository identity other than the one the check runs against.
    | RepositoryMismatch
    | ReleaseRevoked
    | MetadataSignatureInvalid
    | MetadataExpired
    | MetadataRollback
    | MetadataLinkMismatch
    | MetadataRootRotationInvalid
    | OfflineMetadataUnavailable
    | RepositoryUnavailable

/// The result diagnostic kinds. Every stable code has exactly one.
[<RequireQualifiedAccess>]
type DiagnosticKind =
    | UnsupportedArtifact
    | MissingPrerequisite
    | TrustFailure
    | OfflineContentUnavailable
    | OwnershipConflict
    | ReadinessFailed
    | UnsupportedContract
    | SelectionUnavailable
    | LockBinding

/// A stable code. The case structure fixes each code's diagnostic kind.
[<RequireQualifiedAccess>]
type Code =
    | Reader of reader: ReaderCode
    | Binding of binding: BindingCode
    | Artifact of artifact: ArtifactCode
    | Trust of trust: TrustCode
    | MissingPrerequisite
    | OfflineContentUnavailable
    | OwnershipConflict
    | ReadinessFailed
    | GenerationNotInstalled

/// A refusal from a document reader: one code and the member path it concerns.
type ReaderRefusal = { Code: ReaderCode; Path: DocumentPath }

/// Spellings and the code-to-kind table of the overview page.
module Code =

    let private readers =
        [ ReaderCode.InvalidDocument, "invalid_document"
          ReaderCode.UnsupportedContractVersion, "unsupported_contract_version"
          ReaderCode.UnsupportedCriticalMember, "unsupported_critical_member"
          ReaderCode.UnsupportedCase, "unsupported_case"
          ReaderCode.InvalidValue, "invalid_value"
          ReaderCode.NonPortablePath, "non_portable_path"
          ReaderCode.CredentialInDocument, "credential_in_document"
          ReaderCode.InconsistentDocument, "inconsistent_document" ]

    let private bindings =
        [ BindingCode.RequestChanged, "request_changed"
          BindingCode.PlatformNotLocked, "platform_not_locked"
          BindingCode.LockChanged, "lock_changed"
          BindingCode.ReceiptLockMismatch, "receipt_lock_mismatch"
          BindingCode.LockRecipeMismatch, "lock_recipe_mismatch" ]

    let private artifacts =
        [ ArtifactCode.UnsupportedArtifact, "unsupported_artifact"
          ArtifactCode.AbiUnavailable, "abi_unavailable"
          ArtifactCode.NoCompatibleRelease, "no_compatible_release" ]

    let private trusts =
        [ TrustCode.TargetNotAuthenticated, "target_not_authenticated"
          TrustCode.TargetLengthMismatch, "target_length_mismatch"
          TrustCode.TargetDigestMismatch, "target_digest_mismatch"
          TrustCode.ReleaseDescriptorMismatch, "release_descriptor_mismatch"
          TrustCode.ArtifactPlatformMismatch, "artifact_platform_mismatch"
          TrustCode.RepositoryMismatch, "repository_mismatch"
          TrustCode.ReleaseRevoked, "release_revoked"
          TrustCode.MetadataSignatureInvalid, "metadata_signature_invalid"
          TrustCode.MetadataExpired, "metadata_expired"
          TrustCode.MetadataRollback, "metadata_rollback"
          TrustCode.MetadataLinkMismatch, "metadata_link_mismatch"
          TrustCode.MetadataRootRotationInvalid, "metadata_root_rotation_invalid"
          TrustCode.OfflineMetadataUnavailable, "offline_metadata_unavailable"
          TrustCode.RepositoryUnavailable, "repository_unavailable" ]

    /// Every stable code with its spelling, in the order of the overview page's table.
    let all: (Code * string) list =
        [ yield! readers |> List.map (fun (code, name) -> Code.Reader code, name)
          yield! bindings |> List.map (fun (code, name) -> Code.Binding code, name)
          yield! artifacts |> List.map (fun (code, name) -> Code.Artifact code, name)
          Code.MissingPrerequisite, "missing_prerequisite"
          Code.OfflineContentUnavailable, "offline_content_unavailable"
          Code.OwnershipConflict, "ownership_conflict"
          Code.ReadinessFailed, "readiness_failed"
          Code.GenerationNotInstalled, "generation_not_installed"
          yield! trusts |> List.map (fun (code, name) -> Code.Trust code, name) ]

    /// The stable spelling, such as <c>invalid_document</c>.
    let name (code: Code) =
        all |> List.find (fun (candidate, _) -> candidate = code) |> snd

    /// The code with this spelling, if it is a stable code.
    let tryParse (text: string) =
        all |> List.tryFind (fun (_, name) -> name = text) |> Option.map fst

    let readerName (code: ReaderCode) = name (Code.Reader code)
    let bindingName (code: BindingCode) = name (Code.Binding code)

    /// The one result diagnostic kind of a code.
    let kind (code: Code) =
        match code with
        | Code.Reader _ -> DiagnosticKind.UnsupportedContract
        | Code.Binding _ -> DiagnosticKind.LockBinding
        | Code.Artifact _ -> DiagnosticKind.UnsupportedArtifact
        | Code.Trust _ -> DiagnosticKind.TrustFailure
        | Code.MissingPrerequisite -> DiagnosticKind.MissingPrerequisite
        | Code.OfflineContentUnavailable -> DiagnosticKind.OfflineContentUnavailable
        | Code.OwnershipConflict -> DiagnosticKind.OwnershipConflict
        | Code.ReadinessFailed -> DiagnosticKind.ReadinessFailed
        | Code.GenerationNotInstalled -> DiagnosticKind.SelectionUnavailable

/// Spellings of the diagnostic kinds.
module DiagnosticKind =
    let all =
        [ DiagnosticKind.UnsupportedArtifact, "unsupported-artifact"
          DiagnosticKind.MissingPrerequisite, "missing-prerequisite"
          DiagnosticKind.TrustFailure, "trust-failure"
          DiagnosticKind.OfflineContentUnavailable, "offline-content-unavailable"
          DiagnosticKind.OwnershipConflict, "ownership-conflict"
          DiagnosticKind.ReadinessFailed, "readiness-failed"
          DiagnosticKind.UnsupportedContract, "unsupported-contract"
          DiagnosticKind.SelectionUnavailable, "selection-unavailable"
          DiagnosticKind.LockBinding, "lock-binding" ]

    let name (kind: DiagnosticKind) =
        all |> List.find (fun (candidate, _) -> candidate = kind) |> snd

    let tryParse (text: string) =
        all |> List.tryFind (fun (_, name) -> name = text) |> Option.map fst
