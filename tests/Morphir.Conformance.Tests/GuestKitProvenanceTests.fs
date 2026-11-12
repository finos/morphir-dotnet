// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Task 3.5 of add-wasm-extension-host: the pinned generic guest kit keeps its upstream bytes, and its README and
/// requirements still state the three comparison rules the runner implements.
module Morphir.Conformance.Tests.GuestKitProvenanceTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.RegularExpressions
open Expecto
open Morphir.Conformance.Tests.GuestKitFakeGuest

/// The finos/morphir commit of the kit pin.
[<Literal>]
let KitCommit = "8b3c4c35430dfb313cf439e4df1a9c0bb7e1834d"

let private provenance () =
    File.ReadAllText(Path.Combine(kitDirectory, "PROVENANCE.md"))

let private rows (manifest: string) =
    Regex.Matches(manifest, @"(?m)^\| `([^`]+)` \| `[^`]+` \| `([0-9a-f]{64})` \|\r?$")
    |> Seq.map (fun row -> row.Groups[1].Value, row.Groups[2].Value)
    |> Map.ofSeq

let private sha256 (path: string) =
    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes path))

let private relative (path: string) =
    Path
        .GetRelativePath(kitDirectory, path)
        .Replace(Path.DirectorySeparatorChar, '/')

/// Text with every run of white space read as one space, so a rule wrapped at another column still matches.
let private flat (text: string) = Regex.Replace(text, @"\s+", " ")

/// The README's statement of the comparison rules, as the runner implements them.
[<Literal>]
let ReadmeRules =
    "Outputs are compared by meaning. A compiled IR must equal the expected IR after three rules only: JSON object member order is ignored; in IR 3 the order of the `modules`, `types` and `values` lists is ignored; in IR 4, at an `outputType` or a member of `inputTypes`, a `Reference` with an `fqname` and no or empty `args` equals the FQName string."

/// The requirement "Semantic IR comparison" of mep/generic-guest-profile.
[<Literal>]
let SpecRules =
    "A compiled IR SHALL equal the expected IR after these rules only: JSON object member order is ignored; in IR `3` the order of the `modules`, `types` and `values` name-keyed lists is ignored; in IR `4`, at an `outputType` or a member of `inputTypes`, a `Reference` with an `fqname` and no or empty `args` equals its FQName string."

[<Tests>]
let tests =
    // PlatformSpecific: these tests use the real file system, the checkout or worker processes, so the platform
    // scope of the Test target runs them on every operating system (docs/contributing/ci-os-matrix.md).
    testLabel "PlatformSpecific"
    <| testList "Generic guest kit pin (task 3.5)" [
        test "Every recorded file keeps its recorded SHA-256" {
            let entries = rows (provenance ())
            Expect.isGreaterThan entries.Count 0 "rows"

            let mismatches = [
                for KeyValue(file, expected) in entries do
                    let path =
                        Path.Combine(kitDirectory, file.Replace('/', Path.DirectorySeparatorChar))

                    if not (File.Exists path) then
                        $"{file}: missing"
                    elif
                        sha256 path
                        <> expected
                    then
                        $"{file}: recorded {expected}, found {sha256 path}"
            ]

            Expect.isEmpty mismatches "mismatches"
        }

        test "Every pinned file has a provenance row" {
            let entries = rows (provenance ())

            let unrecorded =
                Directory.GetFiles(kitDirectory, "*", SearchOption.AllDirectories)
                |> Array.map relative
                |> Array.filter (fun name ->
                    name
                    <> "PROVENANCE.md"
                    && not (entries.ContainsKey name)
                )

            Expect.isEmpty unrecorded "unrecorded files"
        }

        test "Provenance names the pinned commit" {
            Expect.stringContains (provenance ()) $"`{KitCommit}`" "commit"
        }

        test "Provenance rows parse the same with either line ending" {
            let manifest = provenance().Replace("\r\n", "\n")
            Expect.equal (rows (manifest.Replace("\n", "\r\n"))) (rows manifest) "rows"
        }

        test "The pinned README states the three comparison rules the runner implements" {
            let readme = File.ReadAllText(Path.Combine(kitDirectory, "README.md"))
            Expect.stringContains (flat readme) ReadmeRules "the README's rules"
        }

        test "The pinned requirements state the three comparison rules the runner implements" {
            let spec =
                File.ReadAllText(Path.Combine(kitDirectory, "generic-guest-profile", "spec.md"))

            Expect.stringContains (flat spec) SpecRules "the requirement's rules"
        }

        test "The pinned kit is read by the runner's reader" {
            Expect.equal kit.Value.Profile "generic-json-library-v1" "profile"
        }
    ]
