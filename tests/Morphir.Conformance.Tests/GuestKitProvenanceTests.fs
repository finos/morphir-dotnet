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
    testList "Generic guest kit pin (task 3.5)" [
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
                    // Only adoption/ has a separate authority, exact inventory and digest checks below.
                    name
                    <> "PROVENANCE.md"
                    && not (name.StartsWith("adoption/", StringComparison.Ordinal))
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

/// The approved parent contract is a second authority; it does not repin the historical kit.
[<Literal>]
let private AdoptionCommit = "9e7d90e2fb5708580baef7cdd323cd994030c237"

// Fixed digests from the original pin, independent of the editable provenance table.
let private originalPinnedDigests =
    Map.ofList [
        "LICENSE", "b40930bbcf80744c86c46a12bc9da056641d722716c378f5659b9e555ef833e1"
        "NOTICE", "02a20174dd6ff4d866cad7d04ce8d61503306f3266f3c03261418d5011805889"
        "README.md", "7f37b522fbae4e10e914319aac3b0c61ffd23ac59773ae56aea6334ff32a3764"
        "generic-guest-profile/spec.md", "2b38bbcb827706e1ab4eb68a7f84bbdd984b0934285d30351dbf5224f598f750"
        "guest-generic-json.json", "f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae"
    ]

// Each source file's raw bytes were matched to its Git blob at AdoptionCommit before hashing.
// These expectations are not read from the later import or from an implementation's output.
let private adoptionInputs = [
    (
        "proposal.md",
        "openspec/changes/drop-generic-guest-legacy-tier/proposal.md",
        "2c1a961551068e983096508e495e31ce5901689c6e6198af5e26c8e985d4fa3b"
    )
    (
        "design.html",
        "openspec/changes/drop-generic-guest-legacy-tier/design.html",
        "adb0015ef33ae198e15584f173afb1a09ec1896a1975d6ff9a1aa9e41661ab20"
    )
    (
        "tasks.md",
        "openspec/changes/drop-generic-guest-legacy-tier/tasks.md",
        "61bae4278086285fc4375d10c285bcda2bc5415d3afb0f08227dae071d03bc3e"
    )
    (
        "specs/mep/generic-guest-profile/spec.md",
        "openspec/changes/drop-generic-guest-legacy-tier/specs/mep/generic-guest-profile/spec.md",
        "2d7f292a8af946a3a65cab20203a143f500be10b367869eb35983b4567d30bb5"
    )
]

let private adoptionDirectory = Path.Combine(kitDirectory, "adoption")

let private adoptionProvenance () =
    let path = Path.Combine(adoptionDirectory, "PROVENANCE.md")
    Expect.isTrue (File.Exists path) "the separately attributed adoption/PROVENANCE.md must exist"
    File.ReadAllText path

[<Tests>]
let adoptionTests =
    testList "Generic guest kit adoption provenance (task 2.1)" [
        test "The frozen kit retains its original authority and file digests" {
            let manifest = provenance ()
            Expect.stringContains manifest $"Commit: `{KitCommit}`" "the original kit authority"
            Expect.isFalse (manifest.Contains AdoptionCommit) "the old pin must not claim the later contract authority"
            Expect.equal (rows manifest) originalPinnedDigests "the original five provenance rows"

            for KeyValue(file, expected) in originalPinnedDigests do
                let path = Path.Combine(kitDirectory, file.Replace('/', Path.DirectorySeparatorChar))
                Expect.equal (sha256 path) expected $"unchanged original bytes of {file}"
        }

        test "The adoption manifest names its approved authority and inherited notices" {
            let manifest = adoptionProvenance ()
            Expect.stringContains manifest "Repository: https://github.com/finos/morphir" "the source repository"
            Expect.stringContains manifest $"Commit: `{AdoptionCommit}`" "the approved contract authority"
            Expect.stringContains manifest $"Baseline commit: `{KitCommit}`" "the distinct historical kit authority"
            Expect.stringContains manifest "../PROVENANCE.md" "the unchanged baseline provenance"
            Expect.stringContains manifest "../LICENSE" "the retained upstream licence"
            Expect.stringContains manifest "../NOTICE" "the retained upstream notices"
            Expect.stringContains manifest "Apache-2.0" "the import licence"
        }

        test "The adoption manifest maps exactly the four approved files and source paths" {
            let entries =
                Regex.Matches(
                    adoptionProvenance (),
                    @"(?m)^\| `([^`]+)` \| `([^`]+)` \| `([0-9a-f]{64})` \|\r?$"
                )
                |> Seq.map (fun row -> row.Groups[1].Value, row.Groups[2].Value, row.Groups[3].Value)
                |> Seq.sort
                |> Seq.toList

            Expect.equal entries (List.sort adoptionInputs) "exact source paths, fixed digests and no duplicate rows"
        }

        test "The adoption directory contains only the four inputs and their provenance" {
            Expect.isTrue (Directory.Exists adoptionDirectory) "the separately attributed adoption directory must exist"

            let actual =
                Directory.GetFiles(adoptionDirectory, "*", SearchOption.AllDirectories)
                |> Array.map (fun path -> Path.GetRelativePath(adoptionDirectory, path).Replace('\\', '/'))
                |> Array.sort
                |> Array.toList

            let expected =
                "PROVENANCE.md" :: (adoptionInputs |> List.map (fun (file, _, _) -> file))
                |> List.sort

            Expect.equal actual expected "every adoption file is accounted for without relocating the original pin"
        }

        for file, source, expected in adoptionInputs do
            test $"The adoption input {file} has its approved SHA-256" {
                let path = Path.Combine(adoptionDirectory, file.Replace('/', Path.DirectorySeparatorChar))
                Expect.isTrue (File.Exists path) $"missing approved input from {AdoptionCommit}:{source}"
                Expect.equal (sha256 path) expected $"exact approved bytes from {AdoptionCommit}:{source}"
            }
    ]
