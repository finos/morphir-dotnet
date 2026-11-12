// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Collections.Generic

/// Exact local preview.2 host input and its independently selected process identities.
type LocalComposedCandidate internal (host: LocalHostCandidate, selection: Sha256, frontend: ExtensionId, backend: ExtensionId) =
    /// The host closure with the exact preview.2 input digest.
    member _.Host = host
    /// Digest of the separately supplied portable selection.
    member _.SelectionSha256 = selection
    /// Selected full frontend identity.
    member _.FrontendId = frontend
    /// Selected distinct full backend identity.
    member _.BackendId = backend
    override _.ToString() = $"{frontend.Text}, {backend.Text}, candidate {host.ManifestSha256.Text}"

module LocalComposedDocument =
    open Reading
    let schema kind = $"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.2/{kind}.schema.json"
    let private readCandidateFor family (bytes: byte array) : Result<LocalComposedCandidate,ReaderRefusal> = result {
        let! host = LocalCandidateDocument.readCandidateFor family ["extensions"] bytes
        let! value = Syntax.parseJson bytes
        let! document = asObject (node value DocumentPath.root)
        let! extensions = objectMember "extensions" document
        match extensions.Members |> List.tryFind(fun (name,_) -> not (List.contains name ["selectionSha256";"frontendId";"backendId"])) with
        | Some(name,_) -> return! refuse ReaderCode.InvalidDocument (extensions.Path.Child name)
        | None -> ()
        let! selection = required "selectionSha256" sha256 extensions
        let! frontend = required "frontendId" (validated ExtensionId.tryParsePreview) extensions
        let! backend = required "backendId" (validated ExtensionId.tryParsePreview) extensions
        if frontend.Text = backend.Text then return! refuse ReaderCode.InconsistentDocument (extensions.Path.Child "backendId")
        return LocalComposedCandidate(host,selection,frontend,backend)
    }

    let readCandidate bytes = readCandidateFor "0.1.0-preview.2" bytes

    /// Reads release-checksum candidates without converting earlier local families.
    let readReleaseCandidate (bytes: byte array) : Result<LocalComposedCandidate,ReaderRefusal> =
        readCandidateFor "0.1.0-preview.3" bytes

/// An exact release asset. Its digest is integrity input, not publisher authentication.
type internal ReleaseAsset = { Name: TargetPath; Url: Uri; Bytes: uint64; Sha256: Sha256; Role: string }
type internal ReleaseBundle = { Version: SemVer; SourceCommit: string; Assets: ReleaseAsset list }
type internal ComposedPin = { Id: ExtensionId; Version: SemVer; ArchiveSha256: Sha256 }
type internal ComposedManifest = { Identity: Sha256; CandidateSha256: Sha256; SelectionSha256: Sha256; CatalogSha256: Sha256; Locks: (ExtensionId * Sha256) list; Workflow: Value; Acquisition: Value option }

module internal ReleaseBundleDocument =
    let read (_bytes: byte array) : Result<ReleaseBundle,ReaderRefusal> =
        Error { Code = ReaderCode.UnsupportedCase; Path = DocumentPath.root }
    let verifyChecksums (_name: string) (_bundleBytes: byte array) (_checksums: byte array) (_bundle: ReleaseBundle) : Result<unit,ReaderRefusal> =
        Error { Code = ReaderCode.UnsupportedCase; Path = DocumentPath.root }

module internal ComposedDocuments =
    let readPins (_bytes: byte array) : Result<ComposedPin list,ReaderRefusal> =
        Error { Code = ReaderCode.UnsupportedCase; Path = DocumentPath.root }
    let readManifest (_family: string) (_candidate: LocalComposedCandidate) (_bytes: byte array) : Result<ComposedManifest,ReaderRefusal> =
        Error { Code = ReaderCode.UnsupportedCase; Path = DocumentPath.root }
