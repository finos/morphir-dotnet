// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.IdentifiersTests

open System
open Expecto
open Morphir.Extensions
open Morphir.Extensions.Installation

let private nullableText (text: string | null) = text

let private get =
    function
    | Ok value -> value
    | Error error -> failtest (error.ToString())

let private label (text: string | null) =
    match text with
    | null -> "<null>"
    | "" -> "<empty>"
    | value -> value

let private code expected =
    function
    | Error(error: InstallationFailure) -> Expect.equal error.Code expected "stable diagnostic"
    | Ok _ -> failtest "expected refusal"

[<Tests>]
let tests =
    testList "Identifiers" [
        for text in
            [
                nullableText null
                ""
                "con"
                "con.tools"
                "com1"
                "a-"
                "A"
                "a..b"
                String('a', 239)
            ] do
            testCase $"refuses nonportable external id {label text}"
            <| fun () ->
                DistributionIdentifiers.ParseExtensionId text
                |> code "extension.id_not_portable"
        for text in
            [ "morphir-elm"; "org.finos.morphir.fsharp-frontend"; "tools.con"; String('a', 238) ] do
            testCase $"accepts external id {text}"
            <| fun () ->
                Expect.equal
                    (DistributionIdentifiers.ParseExtensionId text
                     |> get)
                        .Value
                    text
                    "existing ExtensionId"
        test "built-ins remain valid; external installs cannot reserve their prefix" {
            Expect.equal
                (ExtensionId.Create "morphir.example").Value
                "morphir.example"
                "domain id stays valid"

            DistributionIdentifiers.ParseExtensionId "morphir.example"
            |> code "extension.reserved_id"
        }
        for text in
            [ nullableText null; ""; "a/b.exe"; "../x"; "CON.exe"; "aux"; "file."; "a\\b"; "a:b" ] do
            testCase $"refuses artifact filename {label text}"
            <| fun () ->
                DistributionIdentifiers.ParseArtifactFileName text
                |> code "extension.path_not_portable"
        test "artifact filename is a parsed target path with exactly one segment" {
            Expect.equal
                (DistributionIdentifiers.ParseArtifactFileName "backend.exe"
                 |> get)
                    .Text
                "backend.exe"
                "filename"
        }
        for text in
            [
                nullableText null
                ""
                "../x"
                "a/../x"
                "/x"
                "C:/x"
                "a\\b"
                "a/CON.exe"
                "a//b"
                "a/"
                "a/e\u0301"
                "a/"
                + String(char 0xD800, 1)
            ] do
            testCase $"refuses declared path {label text}"
            <| fun () ->
                DistributionIdentifiers.ParseDeclaredPath text
                |> code "extension.path_not_portable"
        test "declared paths use both shared length bounds" {
            DistributionIdentifiers.ParseDeclaredPath(String('a', 241))
            |> code "extension.path_not_portable"

            DistributionIdentifiers.ParseDeclaredPath(
                String('\u754c', 80)
                + "/"
                + String('\u754c', 80)
                + "/"
                + String('\u754c', 10)
                + "a"
            )
            |> code "extension.path_not_portable"

            Expect.isOk
                (DistributionIdentifiers.ParseDeclaredPath(String('a', 240)))
                "240 UTF-16 units"

            Expect.isOk
                (DistributionIdentifiers.ParseDeclaredPath(
                    String('\u754c', 80)
                    + "/"
                    + String('\u754c', 80)
                    + "/"
                    + String('\u754c', 10)
                ))
                "512 UTF-8 bytes"
        }
        test "digest uses the existing MEP SHA-256 value" {
            let digest = String('a', 64)

            Expect.equal
                (DistributionIdentifiers.ParseDigest digest
                 |> get)
                    .Value
                digest
                "canonical digest"

            DistributionIdentifiers.ParseDigest(String('A', 64))
            |> code "extension.invalid_digest"

            DistributionIdentifiers.ParseDigest null
            |> code "extension.invalid_digest"
        }
        test "selection defaults to stable and refuses both options" {
            Expect.equal
                (DistributionSelection.Parse(null, null)
                 |> get)
                    .Text
                "stable"
                "default"

            DistributionSelection.Parse("stable", "1.0.0")
            |> code "extension.invalid_selection"
        }
        for text in [ "stable"; "preview"; "insiders"; "preview/team-2" ] do
            testCase $"accepts channel {text}"
            <| fun () ->
                Expect.equal
                    (DistributionSelection.Parse(text, null)
                     |> get)
                        .Text
                    text
                    "canonical channel"
        for text in [ ""; "Stable"; "preview/"; "preview/a-"; "preview/a/b"; "nightly" ] do
            testCase $"refuses channel {text}"
            <| fun () ->
                DistributionSelection.Parse(text, null)
                |> code "extension.invalid_selection"
        test "exact version retains metadata and strict SemVer spelling" {
            Expect.equal
                (DistributionSelection.Parse(null, "1.0.0+build.2")
                 |> get)
                    .Text
                "1.0.0+build.2"
                "identity"

            for text in [ "1.0"; "v1.0.0"; "01.0.0"; "1.0.0-01"; String('9', 300) ] do
                DistributionSelection.Parse(null, text)
                |> code "extension.invalid_selection"
        }
        test "TryParse outputs identify the success and failure branches" {
            let ok, value, failure = DistributionSelection.TryParse(null, "1.0.0")
            Expect.isTrue ok "parsed"
            Expect.equal value.Text "1.0.0" "value"
            Expect.isTrue (obj.ReferenceEquals(failure, null)) "no failure"
            let ok, value, failure = DistributionSelection.TryParse("", null)
            Expect.isFalse ok "refused"
            Expect.isTrue (obj.ReferenceEquals(value, null)) "no value"
            Expect.equal failure.Code "extension.invalid_selection" "failure"
        }
        test "source literals are checked and compare by ordinal identity" {
            Expect.equal
                (DistributionSelection.CreateChannel "stable")
                (DistributionSelection.Parse(null, null)
                 |> get)
                "same channel"

            Expect.equal
                (DistributionSelection.CreateVersion "1.0.0")
                (DistributionSelection.Parse(null, "1.0.0")
                 |> get)
                "same version"

            Expect.notEqual
                (DistributionSelection.CreateVersion "1.0.0+a")
                (DistributionSelection.CreateVersion "1.0.0+b")
                "metadata is identity"

            Expect.throws
                (fun () ->
                    DistributionSelection.CreateVersion "1.0"
                    |> ignore
                )
                "bad source literal"
        }
    ]
