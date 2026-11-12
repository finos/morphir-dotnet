// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.PlatformsTests

open Expecto
open Morphir.Extensions.Installation

let private nullableText (text: string | null) = text

let private get =
    function
    | Ok value -> value
    | Error error -> failtest (error.ToString())

[<Tests>]
let tests =
    testList "Platforms" [
        for rid, triple, os, arch in
            [
                "win-x64", "x86_64-pc-windows-msvc", "windows", "x86_64"
                "win-arm64", "aarch64-pc-windows-msvc", "windows", "aarch64"
                "osx-x64", "x86_64-apple-darwin", "macos", "x86_64"
                "osx-arm64", "aarch64-apple-darwin", "macos", "aarch64"
                "linux-x64", "x86_64-unknown-linux-gnu", "linux", "x86_64"
                "linux-arm64", "aarch64-unknown-linux-gnu", "linux", "aarch64"
            ] do
            testCase $"maps {rid} both ways"
            <| fun () ->
                let row =
                    InstallationPlatform.Parse rid
                    |> get

                Expect.equal
                    (InstallationPlatform.Create rid).RuntimeIdentifier
                    rid
                    "checked source literal"

                Expect.equal row.DescriptorTriple triple "descriptor spelling"

                let index =
                    row.GetIndexPlatform()
                    |> get

                Expect.equal (index.Os, index.Arch) (os, arch) "index spelling"

                Expect.equal
                    (InstallationPlatform.FromDescriptorTriple triple
                     |> get)
                        .RuntimeIdentifier
                    rid
                    "triple inverse"

                Expect.equal
                    (InstallationPlatform.FromIndexPlatform(os, arch)
                     |> get)
                        .RuntimeIdentifier
                    rid
                    "index inverse"
        for rid, triple in
            [
                "linux-musl-x64", "x86_64-unknown-linux-musl"
                "linux-musl-arm64", "aarch64-unknown-linux-musl"
            ] do
            testCase $"keeps {rid} distinct from glibc"
            <| fun () ->
                let row =
                    InstallationPlatform.Parse rid
                    |> get

                Expect.equal row.DescriptorTriple triple "triple can name musl"

                Expect.equal
                    (InstallationPlatform.FromDescriptorTriple triple
                     |> get)
                        .RuntimeIdentifier
                    rid
                    "inverse"

                let ok, _, failure = row.TryGetIndexPlatform()
                Expect.isFalse ok "v1 index cannot name musl"
                Expect.equal failure.Code "extension.no_matching_artifact" "diagnostic"
                Expect.stringContains failure.Message rid "names the requested platform"
        test "rejects unknown and noncanonical spellings" {
            for text in [ nullableText null; "amiga"; "WIN-X64"; "windows-x64" ] do
                Expect.isError (InstallationPlatform.Parse text) "unknown RID"

            Expect.isError
                (InstallationPlatform.FromIndexPlatform("linux", "arm64"))
                "index spells it aarch64"

            Expect.isError
                (InstallationPlatform.FromDescriptorTriple "win-x64")
                "RID is not a triple"
        }
        test "table covers precisely the existing toolchain platforms" {
            let actual =
                InstallationPlatform.All
                |> Seq.map (fun row -> row.RuntimeIdentifier)
                |> Seq.toList

            Expect.equal
                actual
                (Morphir.Toolchain.Platform.all
                 |> List.map snd)
                "reuse existing order and identity"
        }
    ]
