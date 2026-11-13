// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Collections.Generic

/// The declared source of a composed candidate. This is not execution authority.
[<RequireQualifiedAccess>]
type internal ComposedOrigin =
    | LocalDeveloper
    | ReleaseChecksum
    member this.WireValue =
        match this with
        | ComposedOrigin.LocalDeveloper -> "local-developer"
        | ComposedOrigin.ReleaseChecksum -> "release-checksum"

/// Exact local preview.2 host input and its independently selected process identities.
type LocalComposedCandidate internal (host: LocalHostCandidate, selection: Sha256, frontend: ExtensionId, backend: ExtensionId, origin: ComposedOrigin) =
    /// The host closure with the exact preview.2 input digest.
    member _.Host = host
    /// Digest of the separately supplied portable selection.
    member _.SelectionSha256 = selection
    /// Selected full frontend identity.
    member _.FrontendId = frontend
    /// Selected distinct full backend identity.
    member _.BackendId = backend
    member internal _.Origin = origin
    override _.ToString() = $"{frontend.Text}, {backend.Text}, candidate {host.ManifestSha256.Text}"

module LocalComposedDocument =
    open Reading
    let schema kind = $"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.2/{kind}.schema.json"
    let private readCandidateFor family (bytes: byte array) : Result<LocalComposedCandidate,ReaderRefusal> = result {
        let! host = LocalCandidateDocument.readCandidateFor family ["extensions"] bytes
        let! value = Syntax.parseJson bytes
        let! document = asObject (node value DocumentPath.root)
        let! origin = required "origin" (tag ["local-developer",ComposedOrigin.LocalDeveloper;"release-checksum",ComposedOrigin.ReleaseChecksum]) document
        let! extensions = objectMember "extensions" document
        match extensions.Members |> List.tryFind(fun (name,_) -> not (List.contains name ["selectionSha256";"frontendId";"backendId"])) with
        | Some(name,_) -> return! refuse ReaderCode.InvalidDocument (extensions.Path.Child name)
        | None -> ()
        let! selection = required "selectionSha256" sha256 extensions
        let! frontend = required "frontendId" (validated ExtensionId.tryParsePreview) extensions
        let! backend = required "backendId" (validated ExtensionId.tryParsePreview) extensions
        if frontend.Text = backend.Text then return! refuse ReaderCode.InconsistentDocument (extensions.Path.Child "backendId")
        return LocalComposedCandidate(host,selection,frontend,backend,origin)
    }

    let readCandidate bytes = readCandidateFor "0.1.0-preview.2" bytes

    /// Reads the isolated preview.4 family without converting earlier local families.
    let readReleaseCandidate (bytes: byte array) : Result<LocalComposedCandidate,ReaderRefusal> =
        readCandidateFor "0.1.0-preview.4" bytes

/// An exact release asset. Its digest is integrity input, not publisher authentication.
type internal ReleaseAsset = { Name: TargetPath; Url: Uri; Bytes: uint64; Sha256: Sha256; Role: string }
type internal ReleaseBundle = { Version: SemVer; SourceCommit: string; Platform: Platform; Assets: ReleaseAsset list }
type internal ComposedPin = { Id: ExtensionId; Version: SemVer; ArchiveSha256: Sha256 }
type internal ComposedManifest = { Identity: Sha256; CandidateSha256: Sha256; SelectionSha256: Sha256; CatalogSha256: Sha256; Locks: (ExtensionId * Sha256) list; Metadata: (ExtensionId * TargetPath * Sha256) list; Workflow: Value; Acquisition: Value option }

module internal ReleaseBundleDocument =
    open Reading
    let repository = "https://github.com/finos/morphir-dotnet"
    let family = "0.1.0-preview.4"
    let schema kind = $"https://morphir.finos.org/schemas/local-toolchains/{family}/{kind}.schema.json"
    let roles = ["host-candidate";"host-archive";"selection";"frontend-release";"frontend-manifest";"frontend-archive";"backend-release";"backend-manifest";"backend-archive"]
    let fileName platform = $"morphir-toolchain-{Platform.name platform}.json"
    let closed names (o: ObjectNode) =
        match o.Members |> List.tryFind(fun (name,_) -> not (List.contains name names)) with
        | Some(name,_) -> refuse ReaderCode.InvalidDocument (o.Path.Child name)
        | None -> Ok o
    let constant name expected o = result {
        let! actual = required name text o
        if actual <> expected then return! refuse ReaderCode.InconsistentDocument (o.Path.Child name)
    }
    let envelope family kind names bytes = result {
        let! value = Syntax.parseJson bytes
        let! o = asObject (node value DocumentPath.root)
        match o.Members with
        | ("$schema",_)::_ -> ()
        | _ -> return! refuse ReaderCode.InvalidDocument (o.Path.Child "$schema")
        do! constant "$schema" $"https://morphir.finos.org/schemas/local-toolchains/{family}/{kind}.schema.json" o
        return! closed ("$schema"::names) o
    }
    let read bytes : Result<ReleaseBundle,ReaderRefusal> = result {
        let! o = envelope family "bundle" ["version";"sourceCommit";"platform";"assets"] bytes
        let! version = required "version" version o
        let! platform = required "platform" (tag ["win-x64",Platform.WinX64;"win-arm64",Platform.WinArm64;"osx-arm64",Platform.OsxArm64]) o
        let! commit = required "sourceCommit" text o
        if commit.Length <> 40 || commit |> Seq.exists(fun c -> not (Char.IsAsciiDigit c || c >= 'a' && c <= 'f')) then
            return! refuse ReaderCode.InvalidValue (o.Path.Child "sourceCommit")
        let prefix = $"{repository}/releases/download/v{version.Text}/"
        let asset n = result {
            let! fields = asObject n |> Result.bind(closed ["name";"url";"bytes";"sha256";"role"])
            let! name = required "name" targetPath fields
            if name.Text.Contains '/' then return! refuse ReaderCode.InvalidValue (fields.Path.Child "name")
            let! url = required "url" text fields
            // The download location is deterministic, so escaped separators, another tag and another repository cannot substitute.
            if url <> prefix + name.Text then return! refuse ReaderCode.InconsistentDocument (fields.Path.Child "url")
            let! count = required "bytes" integer fields
            if count = 0UL || count > (1UL <<< 30) then return! refuse ReaderCode.InvalidValue (fields.Path.Child "bytes")
            let! digest = required "sha256" sha256 fields
            let! role = required "role" (tag (roles |> List.map(fun role -> role,role))) fields
            let platformName = Platform.name platform
            let matchesPlatform =
                match role with
                | "host-candidate" -> name.Text = $"morphir-host-candidate-{platformName}.json"
                | "selection" -> name.Text = $"morphir-extensions-{platformName}.json"
                | _ -> name.Text.Contains($"-{platformName}.",StringComparison.Ordinal)
            if not matchesPlatform then return! refuse ReaderCode.InconsistentDocument (fields.Path.Child "name")
            return {Name=name;Url=Uri url;Bytes=count;Sha256=digest;Role=role}
        }
        let! assets = required "assets" (array asset) o
        if assets.Length <> roles.Length || (assets |> List.map _.Role |> List.sort) <> List.sort roles
           || (assets |> List.map(fun asset -> asset.Name.Text.ToUpperInvariant()) |> Set.ofList).Count <> assets.Length then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "assets")
        return {Version=version;SourceCommit=commit;Platform=platform;Assets=assets}
    }
    let verifyChecksums name bundleBytes (checksums: byte array) (bundle: ReleaseBundle) : Result<unit,ReaderRefusal> = result {
        let! text =
            try Ok(System.Text.UTF8Encoding(false,true).GetString checksums)
            with :? System.Text.DecoderFallbackException -> refuse ReaderCode.InvalidDocument DocumentPath.root
        let mutable sums = Map.empty
        for line in text.Split('\n') |> Array.map(fun line -> line.TrimEnd '\r') |> Array.filter((<>) "") do
            if line.Length < 67 || line[64] <> ' ' || (line[65] <> ' ' && line[65] <> '*') then
                return! refuse ReaderCode.InvalidDocument DocumentPath.root
            let! digest = Sha256.tryParse(line.Substring(0,64)) |> Result.mapError(fun code -> {Code=code;Path=DocumentPath.root})
            let! path = TargetPath.tryParse(line.Substring 66) |> Result.mapError(fun code -> {Code=code;Path=DocumentPath.root})
            if path.Text.Contains '/' || sums.ContainsKey path.Text then return! refuse ReaderCode.InconsistentDocument DocumentPath.root
            sums <- sums.Add(path.Text,digest)
        for path,digest in (name,Sha256.ofBytes bundleBytes)::(bundle.Assets |> List.map(fun asset -> asset.Name.Text,asset.Sha256)) do
            if sums.TryFind path <> Some digest then return! refuse ReaderCode.InconsistentDocument DocumentPath.root
    }

module internal ComposedDocuments =
    open Reading
    open ReleaseBundleDocument
    let fixedCaseSha256 = "98bc857c1ab63bd5aae8bbe6a1851b30c86913bc33efd980d5cc4bf5cbb41df0"
    let fixedSourceSha256 = "f924e05d37b0517ac8777b495691ad86207f2b1dc7f5b6e038e6f6e370817d6c"
    let fixedGeneratedSha256 = "64e08313f3e57be2c18995a2e8b275db9d921341958da2c8cd960c601c066de0"
    let readPinsFor platform bytes : Result<ComposedPin list,ReaderRefusal> = result {
        let! value = Syntax.parseJson bytes
        let understood = set ["$schema";"critical";"extensions";"extensions.extensionId";"extensions.version";"extensions.runtime";"extensions.platforms";"extensions.platforms.*";"extensions.sha256"]
        let! o = previewEnvelopeAt PreviewDocument.Selection understood (node value DocumentPath.root)
        let forbidden (o: ObjectNode) =
            match ["schemaVersion";"path";"program";"home"] |> List.tryFind(fun key -> has key o) with
            | Some key -> refuse ReaderCode.InconsistentDocument (o.Path.Child key)
            | None -> Ok ()
        do! forbidden o
        let pin n = result {
            let! fields = asObject n |> Result.bind(memberRules (set ["extensionId";"version";"runtime";"platforms";"sha256"]) n)
            do! forbidden fields
            let! id = required "extensionId" (validated ExtensionId.tryParsePreview) fields
            let! version = required "version" version fields
            do! constant "runtime" "process" fields
            if has "sha256" fields then return! refuse ReaderCode.InconsistentDocument (fields.Path.Child "sha256")
            let! platforms = required "platforms" (platformMap (fun _ n -> sha256 n)) fields
            let! digest =
                platforms |> List.tryFind(fun (candidate,_) -> candidate = platform) |> Option.map(snd >> Ok)
                |> Option.defaultValue(refuse ReaderCode.InconsistentDocument (fields.Path.Child "platforms"))
            return {Id=id;Version=version;ArchiveSha256=digest}
        }
        let! pins = required "extensions" (array pin) o
        let ids = pins |> List.map(fun pin -> pin.Id.Text)
        if ids <> List.sort ids || ids.Length <> (Set.ofList ids).Count || ids.IsEmpty then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "extensions")
        return pins
    }
    let readPins bytes = readPinsFor Platform.WinX64 bytes
    let private provider (candidate: LocalComposedCandidate) n = result {
        let! o = asObject n |> Result.bind(closed ["role";"extensionId";"version";"mepVersion";"modelTuple";"checks"])
        let! role = required "role" (tag ["frontend","frontend";"backend","backend"]) o
        let! id = required "extensionId" (validated ExtensionId.tryParsePreview) o
        let expected = if role = "frontend" then candidate.FrontendId else candidate.BackendId
        if id <> expected then return! refuse ReaderCode.InconsistentDocument (o.Path.Child "extensionId")
        let! _ = required "version" version o
        do! constant "mepVersion" "0.2.0-draft.1" o
        let! tuple = objectMember "modelTuple" o |> Result.bind(closed ["operation";"profile";"codec";"envelopeVersion";"irVersion"])
        let! parsedTuple = required "modelTuple" modelTuple o
        let operation = if role = "frontend" then ModelOperation.CompileModel else ModelOperation.GenerateModel
        if parsedTuple.Operation <> operation then return! refuse ReaderCode.InconsistentDocument (tuple.Path.Child "operation")
        do! constant "profile" "morphir-ir" tuple
        do! constant "codec" "morphir-json" tuple
        do! constant "envelopeVersion" "0.1.0-draft.1" tuple
        do! constant "irVersion" "4.0.0" tuple
        let! checks = required "checks" ReceiptDocument.componentChecks o
        if checks.Integrity <> CheckStatus.Passed || checks.Capability <> CheckStatus.Passed then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "checks")
        return role
    }
    let readManifest family (candidate: LocalComposedCandidate) bytes : Result<ComposedManifest,ReaderRefusal> = result {
        let! o = envelope family "generation" (["candidateSha256";"selectionSha256";"catalogSha256";"locks";"metadata";"workflow"] @ if family="0.1.0-preview.4" then ["acquisition"] else []) bytes
        let! candidateHash = required "candidateSha256" sha256 o
        let! selection = required "selectionSha256" sha256 o
        let! catalog = required "catalogSha256" sha256 o
        if candidateHash <> candidate.Host.ManifestSha256 || selection <> candidate.SelectionSha256 then
            return! refuse ReaderCode.InconsistentDocument o.Path
        let binding n = result {
            let! fields = asObject n |> Result.bind(closed ["extensionId";"sha256"])
            let! id = required "extensionId" (validated ExtensionId.tryParsePreview) fields
            let! hash = required "sha256" sha256 fields
            return id,hash
        }
        let! locks = required "locks" (array binding) o
        let ids = locks |> List.map(fun (id,_) -> id.Text)
        if ids <> List.sort ids || ids.Length <> (Set.ofList ids).Count || not(List.contains candidate.FrontendId.Text ids && List.contains candidate.BackendId.Text ids) then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "locks")
        let envelopeBinding n = result {
            let! fields = asObject n |> Result.bind(closed ["extensionId";"path";"sha256"])
            let! id = required "extensionId" (validated ExtensionId.tryParsePreview) fields
            let! path = required "path" targetPath fields
            let! hash = required "sha256" sha256 fields
            if path.Text <> $"extensions/metadata/{id.Text}/release.json" || not(List.contains id.Text ids) then
                return! refuse ReaderCode.UnsupportedCase fields.Path
            return id,path,hash
        }
        let! metadata = required "metadata" (array envelopeBinding) o
        let metadataNames = metadata |> List.map(fun (id,path,_) -> id.Text,path.Text)
        if metadataNames <> List.sort metadataNames || (Set.ofList metadataNames).Count <> metadataNames.Length then
            return! refuse ReaderCode.InconsistentDocument (o.Path.Child "metadata")
        let! workflowNode = required "workflow" (fun n -> Ok n) o
        let! workflow = asObject workflowNode |> Result.bind(closed ["case";"caseSha256";"sourceSha256";"ir";"generatedFiles";"providers";"checks"])
        do! constant "case" "fsharp-compile-generate" workflow
        do! constant "caseSha256" fixedCaseSha256 workflow
        do! constant "sourceSha256" fixedSourceSha256 workflow
        let! ir = objectMember "ir" workflow |> Result.bind(closed ["sha256";"semanticMatch"])
        let! _ = required "sha256" sha256 ir
        let! matches = required "semanticMatch" (function {Value=Value.Boolean value} -> Ok value | n -> refuse ReaderCode.InvalidDocument n.Path) ir
        if not matches then return! refuse ReaderCode.InconsistentDocument ir.Path
        let generated n = result {
            let! file = asObject n |> Result.bind(closed ["path";"bytes";"sha256"])
            do! constant "path" "Pricing.fs" file
            do! constant "sha256" fixedGeneratedSha256 file
            let! count = required "bytes" integer file
            if count <> 112UL then return! refuse ReaderCode.InconsistentDocument file.Path
        }
        let! files = required "generatedFiles" (array generated) workflow
        if files.Length <> 1 then return! refuse ReaderCode.InconsistentDocument (workflow.Path.Child "generatedFiles")
        let! roles = required "providers" (array(provider candidate)) workflow
        if List.sort roles <> ["backend";"frontend"] then return! refuse ReaderCode.InconsistentDocument (workflow.Path.Child "providers")
        let workflowCheck n = result {
            let! fields = asObject n |> Result.bind(closed ["check";"status";"workflow";"reason"])
            do! constant "check" "workflow" fields
            do! constant "status" "passed" fields
            do! constant "workflow" "fsharp-compile-generate" fields
            let! _ = optional "reason" (validated NonBlankText.tryParse) fields
            return ()
        }
        let! checks = required "checks" (array workflowCheck) workflow
        if checks.Length <> 1 then return! refuse ReaderCode.InconsistentDocument (workflow.Path.Child "checks")
        let acquisitionReader n = result {
            let! fields = asObject n |> Result.bind(closed ["repository";"tag";"bundle";"checksums"])
            do! constant "repository" repository fields
            let! tag = required "tag" text fields
            if not(tag.StartsWith("v",StringComparison.Ordinal)) then return! refuse ReaderCode.InvalidValue fields.Path
            let! _ = SemVer.tryParse(tag.Substring 1) |> Result.mapError(fun code -> {Code=code;Path=fields.Path.Child "tag"})
            for memberName,path in ["bundle","acquisition/bundle.json";"checksums","acquisition/SHA256SUMS"] do
                let! item = objectMember memberName fields |> Result.bind(closed ["path";"sha256"])
                do! constant "path" path item
                let! _ = required "sha256" sha256 item
                ()
            return n.Value
        }
        let! acquisition = optional "acquisition" acquisitionReader o
        if (candidate.Origin = ComposedOrigin.ReleaseChecksum) <> acquisition.IsSome then return! refuse ReaderCode.InconsistentDocument o.Path
        return {Identity=Sha256.ofBytes bytes;CandidateSha256=candidateHash;SelectionSha256=selection;CatalogSha256=catalog;Locks=locks;Metadata=metadata;Workflow=workflowNode.Value;Acquisition=acquisition}
    }

    let familyOf (bytes: byte array) = result {
        let! value = Syntax.parseJson bytes
        let! o = asObject (node value DocumentPath.root)
        let! url = required "$schema" text o
        match url with
        | "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.2/candidate.schema.json" -> return "0.1.0-preview.2"
        | "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.4/candidate.schema.json" -> return "0.1.0-preview.4"
        | _ -> return! refuse ReaderCode.UnsupportedContractVersion (o.Path.Child "$schema")
    }
    let candidateForFamily family bytes =
        if family="0.1.0-preview.4" then LocalComposedDocument.readReleaseCandidate bytes
        else LocalComposedDocument.readCandidate bytes
    let schemaFor family kind = $"https://morphir.finos.org/schemas/local-toolchains/{family}/{kind}.schema.json"
    let receipt family (candidate: LocalComposedCandidate) identity =
        let fields =
            match Syntax.parseJson(LocalCandidateDocument.writeReceipt candidate.Host) with
            | Ok(Value.Object fields) -> fields
            | _ -> invalidOp "The shared host receipt writer produced invalid JSON."
        Value.Object ((fields |> List.map(fun (name,value) ->
            name,match name with
                 | "$schema" -> Value.String(schemaFor family "receipt")
                 | "origin" -> Value.String candidate.Origin.WireValue
                 | "readiness" -> Value.String "workflow-verified"
                 | _ -> value)) @ ["generationSha256",Value.String ((identity: Sha256).Text)])
        |> JsonText.toBytes
    let receiptMatches family candidate identity bytes = result {
        let! actual = Syntax.parseJson bytes
        let! expected = Syntax.parseJson(receipt family candidate identity)
        if ParsedValue.ordered actual <> ParsedValue.ordered expected then return! refuse ReaderCode.InconsistentDocument DocumentPath.root
    }
    let pointer family (candidate: LocalComposedCandidate) identity receiptBytes =
        Value.Object [
            "$schema",Value.String(schemaFor family "selection")
            "origin",Value.String candidate.Origin.WireValue
            "generationSha256",Value.String ((identity: Sha256).Text)
            "receiptSha256",Value.String ((Sha256.ofBytes receiptBytes).Text)
            "receiptPath",Value.String(LocalCandidateDocument.receiptPath identity)] |> JsonText.toBytes
    let readPointer family bytes = result {
        let! o = envelope family "selection" ["origin";"generationSha256";"receiptSha256";"receiptPath"] bytes
        do! if family="0.1.0-preview.4" then Ok () else constant "origin" "local-developer" o
        let! origin = required "origin" (tag (if family="0.1.0-preview.4" then ["local-developer",ComposedOrigin.LocalDeveloper;"release-checksum",ComposedOrigin.ReleaseChecksum] else ["local-developer",ComposedOrigin.LocalDeveloper])) o
        let! id = required "generationSha256" sha256 o
        let! receipt = required "receiptSha256" sha256 o
        do! constant "receiptPath" (LocalCandidateDocument.receiptPath id) o
        return id,receipt,origin
    }
    let writeManifest family (candidate: LocalComposedCandidate) (catalog: byte array) (locks: (ExtensionId * byte array) list) (metadata: (ExtensionId * TargetPath * byte array) list) workflow acquisition =
        Value.Object ([
            "$schema",Value.String(schemaFor family "generation")
            "candidateSha256",Value.String candidate.Host.ManifestSha256.Text
            "selectionSha256",Value.String candidate.SelectionSha256.Text
            "catalogSha256",Value.String ((Sha256.ofBytes catalog).Text)
            "locks",Value.Array(locks |> List.sortBy(fun (id,_) -> id.Text) |> List.map(fun (id,bytes) -> Value.Object ["extensionId",Value.String id.Text;"sha256",Value.String ((Sha256.ofBytes bytes).Text)]))
            "metadata",Value.Array(metadata |> List.sortBy(fun (id,path,_) -> id.Text,path.Text) |> List.map(fun (id,path,bytes) -> Value.Object ["extensionId",Value.String id.Text;"path",Value.String path.Text;"sha256",Value.String ((Sha256.ofBytes bytes).Text)]))
            "workflow",workflow ] @ (acquisition |> Option.map(fun value -> ["acquisition",value]) |> Option.defaultValue [])) |> JsonText.toBytes
