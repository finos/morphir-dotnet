// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The rest of the implementation gates of finos/morphir's contract crate (crates/morphir-toolchain-contract, at
/// <c>dde74f94</c>) that read the corpus: every fixture document is a case, every committed signature is the one its
/// public TEST-ONLY label gives, the repository identity is the one the recipes name, and a zero floor admits no
/// prerelease. The metadata-only gates (contract files, links, pages and check-table coverage) stay in finos/morphir.
/// </summary>
module Morphir.Toolchain.Tests.CorpusGateTests

open System.IO
open System.Text.Json
open Expecto
open Morphir.Toolchain

/// Every envelope (an object with signed and signatures) in a value, with its path.
let rec private envelopes (path: string) (value: Value) : (string * Value) list =
    match value with
    | Value.Object members ->
        let isEnvelope = members |> List.exists (fst >> (=) "signed") && members |> List.exists (fst >> (=) "signatures")

        [ if isEnvelope then
              yield path, value
          else
              for name, item in members do
                  yield! envelopes (if path = "" then name else $"{path}.{name}") item ]
    | Value.Array items -> [ for index, item in List.indexed items do yield! envelopes $"{path}[{index}]" item ]
    | _ -> []

[<Tests>]
let tests =
    testList "Former contract crate gates" [
        testCase "every fixture document is a listed case" (fun () ->
            // conformance.rs every_fixture_document_is_a_listed_case.
            use manifest = JsonDocument.Parse(Fixtures.bytes "cases.json")
            let root = manifest.RootElement

            let listed =
                [ for case in root.GetProperty("documents").EnumerateArray() -> nonNull (case.GetProperty("document").GetString())
                  for case in root.GetProperty("bindings").EnumerateArray() do
                      for key in [ "lock"; "request"; "receipt"; "recipe"; "result"; "canonical" ] do
                          match case.TryGetProperty key with
                          | true, value when value.ValueKind = JsonValueKind.String -> yield nonNull (value.GetString())
                          | _ -> () ]
                |> set

            let unlisted =
                [ for contract in [ "recipe"; "request"; "lock"; "receipt"; "result" ] do
                      for file in Directory.GetFiles(Path.Combine(Fixtures.unsigned, contract), "*", SearchOption.AllDirectories) do
                          let relative = Path.GetRelativePath(Fixtures.unsigned, file).Replace('\\', '/')

                          if not (listed.Contains relative) then
                              yield relative ]

            Expect.isEmpty unlisted "fixtures without a case")

        testCase "every committed signature is the one its key's public TEST-ONLY label gives" (fun () ->
            // signed_repository.rs committed_fixtures_are_reproducible_from_their_labels and
            // every_fixture_key_is_derived_from_its_test_only_label, for the signatures: Ed25519 is deterministic, so
            // each signature over the canonical bytes of signed is computed again from the seed SHA-256(label) and
            // compared byte for byte. The committed bytes are never written.
            let repository = TufFixture.repository ()
            let keys = TufFixture.fixtureKeys.Value
            let found = envelopes "" repository
            Expect.isGreaterThan found.Length 10 "envelopes in the signed repository"

            let mismatches =
                [ for path, envelope in found do
                      let signed = TufFixture.memberOf "signed" envelope

                      match TufFixture.memberOf "signatures" envelope with
                      | Value.Array signatures ->
                          for signature in signatures do
                              let field name = match TufFixture.memberOf name signature with Value.String s -> s | _ -> failtestf "%s: %s" path name
                              let keyId = field "keyid"

                              match keys |> List.tryFind (fun key -> key.KeyId = keyId) with
                              | None -> yield $"{path}: a signature by {keyId}, which no published label derives"
                              | Some key ->
                                  let recomputed = TufFixture.signatureOf key signed
                                  // keyOrderReferenceTuf is signed over the reference TUF encoder's bytes on purpose,
                                  // so its targets signature is not the one over this profile's canonical bytes.
                                  let reference = path.StartsWith "keyOrderReferenceTuf.targets"
                                  let same = TufFixture.memberOf "sig" recomputed = Value.String(field "sig")

                                  if same = reference then
                                      let what = if reference then "reproduces over canonical bytes" else "does not reproduce"
                                      yield $"{path}: signature by {keyId} {what}"
                      | _ -> yield $"{path}: signatures is not an array" ]

            Expect.isEmpty mismatches "signatures that do not reproduce")

        testCase "the F# recipe's providers name the repository whose canonical root identifies it" (fun () ->
            // signed_repository.rs the_repository_identity_is_the_digest_of_the_canonical_root, with TufTests' anchor
            // and identity checks.
            let root = TufFixture.memberOf "signed" (TufFixture.memberOf "trustedRoot" (TufFixture.repository ()))
            let identity = Sha256.ofBytes (TufFixture.canonical root)
            Expect.equal identity (SignedCaseTests.repository ()) "repositoryId is the SHA-256 of the canonical root"
            let recipe = SignedCaseTests.signedRecipe "toolchains/recipes/fsharp/1.0.0.json"

            for provider in recipe.Providers do
                Expect.equal provider.Release.RepositoryId identity "each provider names the repository")

        testCase "a zero floor matches no prerelease CLI" (fun () ->
            // signed_repository.rs a_zero_floor_matches_no_prerelease_cli: why offerings publish <0.0.0-0, not >=0.0.0.
            let floor = match CliRequirement.tryParse ">=0.0.0" with Some c -> c | None -> failtest "a comparator"
            let version (text: string) = match SemVer.tryParse text with Ok v -> v | Error _ -> failtest text
            Expect.isFalse (Comparator.allMet floor (version "0.4.0-beta.12")) "no prerelease"
            Expect.isTrue (Comparator.allMet floor (version "0.4.0")) "a final version")
    ]
