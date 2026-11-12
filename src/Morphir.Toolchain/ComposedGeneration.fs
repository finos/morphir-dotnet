// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

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
    let readCandidate (bytes: byte array) : Result<LocalComposedCandidate,ReaderRefusal> = result {
        let! host = LocalCandidateDocument.readCandidateFor "0.1.0-preview.2" ["extensions"] bytes
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
