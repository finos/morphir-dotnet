// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open Morphir.Toolchain
open Morphir.Mep

type internal ReleaseClaims = {
    Json: Value
    Projection: Value
    Extension: ExtensionInfo
    ProtocolVersions: ProtocolVersion list
}

type internal ReleaseArtifact = {
    Runtime: ArtifactRuntime
    Platform: string option
    Filename: TargetPath
    Sha256: Sha256Digest
    Claims: ReleaseClaims
    Package: Value option
}

type internal ReleaseDescriptor = {
    ExtensionId: Morphir.Extensions.ExtensionId
    Version: SemVer
    Artifacts: ReleaseArtifact list
    Json: Value
}

module internal Descriptors =
    open SharedJson

    let private releaseSchema =
        "https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/release.schema.json"

    let private version path value = parsed path SemVer.tryParse value

    let private knownClaims =
        set [
            "protocolVersions"
            "extension"
            "extension.id"
            "extension.name"
            "extension.version"
            "extension.types"
            "extension.description"
            "extension.author"
            "extension.homepage"
            "extension.license"
            "extension.min_sdk_version"
            "capabilities"
            "capabilities.frontend"
            "capabilities.frontend.languages"
            "capabilities.frontend.languages.id"
            "capabilities.frontend.languages.fileExtensions"
            "capabilities.frontend.irVersions"
            "capabilities.frontend.compile"
            "capabilities.frontend.incremental"
            "capabilities.frontend.fragments"
            "capabilities.frontend.multiDocument"
            "capabilities.backend"
            "capabilities.backend.targets"
            "capabilities.backend.irVersions"
            "capabilities.backend.generate"
            "critical"
        ]

    let private critical path known items =
        match optional "critical" items with
        | None -> []
        | Some value ->
            let paths =
                strings $"{path}.critical" value
                |> distinct $"{path}.critical"

            for memberPath in paths do
                if not (Set.contains memberPath known) then
                    fail
                        "extension.unsupported_critical_member"
                        path
                        $"Unknown critical member '{memberPath}'."

            paths

    let private select names items =
        items
        |> List.filter (fun (name, _) -> List.contains name names)
        |> Value.Object

    let private claims path value =
        let items = members path value

        for forbidden in [ "$schema"; "claimsVersion"; "statementVersion" ] do
            if
                optional forbidden items
                |> Option.isSome
            then
                fail
                    "extension.unsupported_contract_version"
                    $"{path}.{forbidden}"
                    "Embedded claims have no schema or version member."

        let protocolVersions =
            required path "protocolVersions" items
            |> array $"{path}.protocolVersions"
            |> List.mapi (fun index value ->
                // MEP's parsed type also names legacy protocols; the current family admits canonical SemVer only.
                let location = $"{path}.protocolVersions[{index + 1}]"
                let exact = version location value
                ProtocolVersion.Create exact.Text
            )

        if List.isEmpty protocolVersions then
            invalid $"{path}.protocolVersions" "Expected at least one protocol version."

        let identityPath = $"{path}.extension"

        let identity =
            required path "extension" items
            |> members identityPath

        let id =
            required identityPath "id" identity
            |> parsed $"{identityPath}.id" Morphir.Extensions.ExtensionId.Parse

        let name =
            required identityPath "name" identity
            |> nonempty $"{identityPath}.name"

        let extensionVersion =
            required identityPath "version" identity
            |> version $"{identityPath}.version"

        let types =
            required identityPath "types" identity
            |> strings $"{identityPath}.types"
            |> distinct $"{identityPath}.types"

        if List.isEmpty types then
            invalid $"{identityPath}.types" "Expected at least one capability kind."

        let kinds =
            types
            |> List.map (
                function
                | "frontend" -> ExtensionType.Frontend
                | "backend" -> ExtensionType.Backend
                | "transform" -> ExtensionType.Transform
                | "validator" -> ExtensionType.Validator
                | "workspace" -> ExtensionType.Workspace
                | other -> invalid $"{identityPath}.types" $"Unknown capability kind '{other}'."
            )

        let capabilitiesPath = $"{path}.capabilities"

        let capabilities =
            required path "capabilities" items
            |> members capabilitiesPath

        let projected = ResizeArray<string * Value>()

        for kind in types do
            let location = $"{capabilitiesPath}.{kind}"

            let metadata =
                required capabilitiesPath kind capabilities
                |> members location

            let fields =
                match kind with
                | "frontend" ->
                    let languages =
                        required location "languages" metadata
                        |> array $"{location}.languages"

                    for language in languages do
                        let languageItems = members $"{location}.languages" language

                        required $"{location}.languages" "id" languageItems
                        |> nonempty $"{location}.languages.id"
                        |> ignore

                        required $"{location}.languages" "fileExtensions" languageItems
                        |> strings $"{location}.languages.fileExtensions"
                        |> ignore

                    required location "irVersions" metadata
                    |> strings $"{location}.irVersions"
                    |> ignore

                    required location "compile" metadata
                    |> boolean $"{location}.compile"
                    |> ignore

                    [
                        "languages"
                        "irVersions"
                        "compile"
                        "incremental"
                        "fragments"
                        "multiDocument"
                    ]
                | "backend" ->
                    required location "targets" metadata
                    |> strings $"{location}.targets"
                    |> ignore

                    required location "irVersions" metadata
                    |> strings $"{location}.irVersions"
                    |> ignore

                    required location "generate" metadata
                    |> boolean $"{location}.generate"
                    |> ignore

                    [ "targets"; "irVersions"; "generate" ]
                | "transform" -> []
                | _ -> invalid location "Capability reader is not implemented yet."

            projected.Add(kind, select fields metadata)

        let understood = critical path knownClaims items

        let optionalText name =
            optional name identity
            |> Option.map (text $"{identityPath}.{name}")

        let extension = {
            Id = id.Value
            Name = name
            Version = extensionVersion.Text
            Description = optionalText "description"
            Types = List.toArray kinds
            Author = optionalText "author"
            Homepage = optionalText "homepage"
            License = optionalText "license"
            MinSdkVersion = optionalText "min_sdk_version"
        }

        let projection =
            Value.Object [
                "protocolVersions",
                Value.Array(
                    protocolVersions
                    |> List.map (fun p -> Value.String p.Value)
                )
                "extension",
                select
                    [
                        "id"
                        "name"
                        "version"
                        "types"
                        "description"
                        "author"
                        "homepage"
                        "license"
                        "min_sdk_version"
                    ]
                    identity
                "capabilities", Value.Object(List.ofSeq projected)
                "critical", Value.Array(List.map Value.String understood)
            ]

        {
            Json = value
            Projection = projection
            Extension = extension
            ProtocolVersions = protocolVersions
        }

    let private artifact path value =
        let items = members path value

        let runtime =
            match
                required path "runtime" items
                |> text $"{path}.runtime"
            with
            | "process" -> ArtifactRuntime.Process
            | "wasm" -> ArtifactRuntime.Wasm
            | _ -> invalid $"{path}.runtime" "Expected process or wasm."

        let platform =
            optional "platform" items
            |> Option.map (nonempty $"{path}.platform")

        match runtime, platform with
        | ArtifactRuntime.Process, None ->
            invalid $"{path}.platform" "Process requires a target triple."
        | ArtifactRuntime.Wasm, Some _ -> invalid $"{path}.platform" "WASM has no platform member."
        | _ -> ()

        let filename =
            required path "filename" items
            |> parsed $"{path}.filename" DistributionIdentifiers.ParseArtifactFileName

        let digest =
            required path "sha256" items
            |> parsed $"{path}.sha256" Sha256Digest.Parse

        let claimSet =
            required path "claims" items
            |> claims $"{path}.claims"

        let package = optional "package" items

        {
            Runtime = runtime
            Platform = platform
            Filename = filename
            Sha256 = digest
            Claims = claimSet
            Package = package
        }

    let read (bytes: byte array) : Result<ReleaseDescriptor, InstallationFailure> =
        match SharedJson.read "release.json" bytes with
        | Error error -> Error error
        | Ok document ->
            protect (fun () ->
                let path = "release.json"
                let items = members path document
                schema path releaseSchema items

                let id =
                    required path "extensionId" items
                    |> parsed $"{path}.extensionId" Morphir.Extensions.ExtensionId.Parse

                let releaseVersion =
                    required path "version" items
                    |> version $"{path}.version"

                let shortId =
                    required path "shortId" items
                    |> nonempty $"{path}.shortId"

                let shortIdValid =
                    Char.IsAsciiLetterLower shortId[0]
                    && shortId
                       |> Seq.forall (fun c ->
                           Char.IsAsciiLetterLower c
                           || Char.IsAsciiDigit c
                           || c = '-'
                       )
                    && shortId[shortId.Length
                               - 1]
                       <> '-'

                if not shortIdValid then
                    invalid $"{path}.shortId" "Expected a lowercase portable release token."

                let differences =
                    required path "platformDifferences" items
                    |> text $"{path}.platformDifferences"

                if
                    differences
                    <> "none"
                    && differences
                       <> "declared"
                then
                    invalid $"{path}.platformDifferences" "Expected none or declared."

                let artifacts =
                    required path "artifacts" items
                    |> array $"{path}.artifacts"
                    |> List.mapi (fun i value -> artifact $"{path}.artifacts[{i + 1}]" value)

                if List.isEmpty artifacts then
                    invalid $"{path}.artifacts" "Expected at least one artifact."

                let known =
                    set [
                        "$schema"
                        "extensionId"
                        "shortId"
                        "version"
                        "gitCommit"
                        "platformDifferences"
                        "artifacts"
                        "critical"
                        "artifacts.runtime"
                        "artifacts.platform"
                        "artifacts.filename"
                        "artifacts.sha256"
                        "artifacts.claims"
                        "artifacts.package"
                        "artifacts.package.format"
                        "artifacts.package.root"
                        "artifacts.package.entryPoint"
                        "artifacts.package.executables"
                        "artifacts.package.treeSha256"
                    ]
                    |> Set.union (
                        knownClaims
                        |> Set.map (fun value -> $"artifacts.claims.{value}")
                    )

                critical path known items
                |> ignore

                let first = artifacts.Head

                for item in artifacts do
                    if
                        item.Runtime
                        <> first.Runtime
                    then
                        fail
                            "extension.invalid_descriptor"
                            path
                            "A descriptor cannot mix runtime kinds."

                    if
                        item.Claims.Extension.Id
                        <> id.Value
                    then
                        fail
                            "extension.claims_mismatch"
                            path
                            "claims.extension.id differs from extensionId."

                    if
                        item.Claims.Extension.Version
                        <> releaseVersion.Text
                    then
                        fail
                            "extension.claims_mismatch"
                            path
                            "claims.extension.version differs from version."

                    if differences = "none" then
                        match
                            difference "claims" first.Claims.Projection item.Claims.Projection
                        with
                        | Some memberPath ->
                            fail
                                "extension.claims_mismatch"
                                path
                                $"Artifact claims differ at {memberPath}."
                        | None -> ()

                {
                    ExtensionId = id
                    Version = releaseVersion
                    Artifacts = artifacts
                    Json = document
                }
            )
