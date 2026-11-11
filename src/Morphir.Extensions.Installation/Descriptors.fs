// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open Morphir.Toolchain
open Morphir.Mep

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

    let private knownClaims = Claims.knownPaths

    let private critical = Claims.readCritical

    let private claims = Claims.readEmbedded

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
