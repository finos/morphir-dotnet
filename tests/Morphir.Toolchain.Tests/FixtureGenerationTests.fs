// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The two former contract crate gates that need the TEST-ONLY generator (finos/morphir
/// <c>crates/morphir-toolchain-contract/tests/signed_repository.rs</c> at <c>dde74f94</c>):
/// <list type="bullet">
/// <item><c>committed_fixtures_are_reproducible_from_their_labels</c>: the committed repository and locks equal the
/// generated ones as parsed JSON values (the old assertion compared <c>serde_json::Value</c>s, not bytes), and each
/// committed request is the generated TOML text exactly.</item>
/// <item><c>the_reference_tuf_encoder_writes_the_recorded_bytes</c>: the reference TUF encoder writes each
/// <c>canonical-json</c> case's <c>referenceTuf</c> text exactly.</item>
/// </list>
/// The generator never reads the committed output: every expectation is built from its own labels and constants.
/// <para>The generator is the one at <c>dde74f94</c>, so these gates run in external corpus mode only, over that
/// corpus and its successors. The pinned corpus predates it (no <c>canonical-json</c> cases and no
/// <c>caseVariantMember</c> set), and the pin is not rewritten to match.</para>
/// </summary>
module Morphir.Toolchain.Tests.FixtureGenerationTests

open System
open System.IO
open System.Text
open Expecto
open Morphir.Toolchain

/// The first difference between two parsed values, by path, comparing objects as maps (member order ignored) and
/// arrays in order, as serde_json's Value equality does.
let rec firstDifference (path: string) (expected: Value) (actual: Value) : string option =
    match expected, actual with
    | Value.Object left, Value.Object right ->
        let names members = members |> List.map fst |> Set.ofList
        let missing = Set.difference (names left) (names right)
        let extra = Set.difference (names right) (names left)

        if not missing.IsEmpty then
            Some $"{path}: missing {Seq.head missing}"
        elif not extra.IsEmpty then
            Some $"{path}: unexpected {Seq.head extra}"
        elif left.Length <> right.Length then
            Some $"{path}: a repeated member"
        else
            left
            |> List.tryPick (fun (name, item) ->
                let other = right |> List.find (fst >> (=) name) |> snd
                firstDifference $"{path}.{name}" item other)
    | Value.Array left, Value.Array right ->
        if left.Length <> right.Length then
            Some $"{path}: {left.Length} items expected, {right.Length} found"
        else
            List.zip left right |> List.indexed |> List.tryPick (fun (index, (l, r)) -> firstDifference $"{path}[{index}]" l r)
    | _ when expected = actual -> None
    | _ -> Some $"{path}: expected %A{expected}, found %A{actual}"

let private parse (what: string) (bytes: byte array) =
    match Syntax.parseJson bytes with
    | Ok value -> value
    | Error refusal -> failtestf "%s does not parse: %A" what refusal

/// The committed file under signed/ as a parsed value, held equal to the generated one.
let expectSameValue (relative: string) (generated: Value) =
    let committed = parse relative (Fixtures.bytesIn Fixtures.signed relative)

    match firstDifference "$" generated committed with
    | None -> ()
    | Some difference -> failtestf "%s is not the generated value: %s" relative difference

/// The generated repository and locks, computed once.
let private generated =
    lazy
        (let targets = FixtureGenerator.published ()
         let locks = FixtureGenerator.frozenLocks targets
         FixtureGenerator.toValue (FixtureGenerator.fixture ()), targets, locks)

/// Registered in Program.corpusTests only, not with [<Tests>]: see the module summary.
let tests =
    testList "Former contract crate reproduction gates" [
        testCase "the reference TUF encoder writes each recorded referenceTuf text exactly" (fun () ->
            let cases =
                [ for case in Manifest.bindings () do
                      match case with
                      | Manifest.CanonicalJson(name, value, _, referenceTuf) -> yield name, value, referenceTuf
                      | _ -> () ]

            Expect.isGreaterThanOrEqual cases.Length 3 "canonical-json cases"

            for name, value, referenceTuf in cases do
                let written = FixtureGenerator.referenceTufCanonical (parse name (Encoding.UTF8.GetBytes value))
                Expect.equal (Encoding.UTF8.GetString written) referenceTuf $"{name}: the reference TUF text")
        |> testLabel "PlatformSpecific"

        testCase "the reference TUF encoder keeps the port's own rules the recorded cases do not reach" (fun () ->
            // No recorded case holds a backslash or a key pair whose UTF-8 and UTF-16 orders differ, so these rules of
            // reference_tuf_canonical are checked directly: only " and \ are escaped, and keys sort by UTF-8 bytes
            // (U+E000 before U+1F600, which UTF-16 would put first).
            let write value = Encoding.UTF8.GetString(FixtureGenerator.referenceTufCanonical value)
            let backslash = string (char 92)
            Expect.equal (write (Value.String("a" + backslash + "b"))) ("\"a" + backslash + backslash + "b\"") "a backslash is escaped"
            let privateUse = string (char 0xE000)
            let emoji = Char.ConvertFromUtf32 0x1F600
            let value = Value.Object [ emoji, Value.Number "2"; privateUse, Value.Number "1" ]
            Expect.equal (write value) ("{\"" + privateUse + "\":1,\"" + emoji + "\":2}") "UTF-8 byte order")

        testCase "repository.json is the generated repository, as parsed values" (fun () ->
            let repository, _, _ = generated.Value
            expectSameValue "repository.json" repository)
        |> testLabel "PlatformSpecific"

        testCase "each frozen request is the generated TOML text exactly" (fun () ->
            let _, _, locks = generated.Value
            Expect.equal (locks |> List.map (fun (name, _, _) -> name)) (FixtureGenerator.Frozen |> List.map fst) "every frozen request"

            for name, request, _ in locks do
                let committed = Encoding.UTF8.GetString(Fixtures.bytesIn Fixtures.signed $"{name}.toml")
                Expect.equal committed request $"{name}.toml")
        |> testLabel "PlatformSpecific"

        testCase "each frozen lock is the generated lock, as parsed values" (fun () ->
            let _, _, locks = generated.Value

            for name, _, lock in locks do
                expectSameValue $"{name}.lock.json" (FixtureGenerator.toValue lock))
        |> testLabel "PlatformSpecific"

        testCase "each negative lock is the generated variant of the Native AOT lock, as parsed values" (fun () ->
            let _, targets, locks = generated.Value
            let native = locks |> List.pick (fun (name, _, lock) -> if name = "fsharp-native-aot" then Some lock else None)

            let variants =
                ("lock-provider-not-in-recipe.lock.json", FixtureGenerator.lockWithUnboundProvider native)
                :: FixtureGenerator.lockRecipeNegatives native targets

            for name, lock in variants do
                expectSameValue (Path.Combine("negative", name)) (FixtureGenerator.toValue lock))
        |> testLabel "PlatformSpecific"
    ]
