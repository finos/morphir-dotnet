// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The plan digest's canonical JSON (manager-result page, "The plan digest").
module Morphir.Toolchain.Tests.CanonicalJsonTests

open System.Text
open Expecto
open Morphir.Toolchain

let private canonical value =
    match Canonical.bytes value with
    | Ok bytes -> Encoding.UTF8.GetString bytes
    | Error refusal -> failtestf "refused at %s" (DocumentPath.render refusal.Path)

let private refusedAt value =
    match Canonical.bytes value with
    | Ok _ -> failtest "a number that is not a plain integer must not encode"
    | Error refusal -> Code.readerName refusal.Code, DocumentPath.render refusal.Path

let private resultWithUnknownInteger (spelling: string) (updateDigest: bool) =
    let original =
        match Syntax.parseJson (Fixtures.bytes "result/setup-plan-escaping.json") with
        | Ok(Value.Object members) -> members
        | _ -> failtest "the result fixture is an object"

    let plan =
        match original |> List.find (fst >> (=) "plan") |> snd with
        | Value.Object members -> Value.Object(("unknown", Value.Array [ Value.Object [ "x", Value.Number spelling ] ]) :: members)
        | _ -> failtest "the plan is an object"

    let digest = Sha256.ofBytes (Canonical.bytes plan |> Result.defaultWith (fun _ -> failtest "the plan has canonical bytes"))

    original
    |> List.map (fun (name, item) ->
        match name with
        | "plan" -> name, plan
        | "planSha256" when updateDigest -> name, Value.String digest.Text
        | _ -> name, item)
    |> Value.Object
    |> JsonText.toBytes

[<Tests>]
let tests =
    testList "Canonical JSON" [
        testCase "members sort by the UTF-8 bytes of their quoted keys" (fun () ->
            let value =
                Value.Object
                    [ "a", Value.Number "1"
                      "a!", Value.Number "2"
                      "e\u0301", Value.Number "3"
                      "\uFF76", Value.Number "4"
                      "\U0001D11E", Value.Number "5"
                      "Z", Value.Number "6" ]

            Expect.equal (canonical value) "{\"Z\":6,\"a!\":2,\"a\":1,\"\u00E9\":3,\"\uFF76\":4,\"\U0001D11E\":5}" "byte order with quotes")

        testCase "only the quotation mark and the backslash are escaped" (fun () ->
            let value = Value.String "<>&+' \"\\ line\nfeed \t tab"
            Expect.equal (canonical value) "\"<>&+' \\\"\\\\ line\nfeed \t tab\"" "raw UTF-8 otherwise")

        testCase "strings are written in NFC" (fun () ->
            let bytes =
                match Canonical.bytes (Value.String "e\u0301") with
                | Ok bytes -> bytes
                | Error _ -> failtest "refused"

            Expect.equal bytes [| 0x22uy; 0xC3uy; 0xA9uy; 0x22uy |] "a precomposed é")

        testCase "no whitespace and literal true, false and null" (fun () ->
            let value = Value.Array [ Value.Boolean true; Value.Boolean false; Value.Null; Value.Object [] ]
            Expect.equal (canonical value) "[true,false,null,{}]" "compact")

        testCase "a fraction, an exponent or a sign cannot be encoded" (fun () ->
            for spelling in [ "41943040.0"; "1e3"; "-1"; "01" ] do
                let value = Value.Object [ "plan", Value.Array [ Value.Object [ "downloadBytes", Value.Number spelling ] ] ]
                Expect.equal (refusedAt value) ("invalid_document", "plan[0].downloadBytes") spelling)

        testCase "integers inside unknown plan members obey the same bound at every depth" (fun () ->
            for spelling in [ "9007199254740992"; "18446744073709551616"; System.String('9', 80) ] do
                match ResultDocument.read (resultWithUnknownInteger spelling true) with
                | Error refusal ->
                    Expect.equal (Code.readerName refusal.Code, DocumentPath.render refusal.Path) ("invalid_value", "plan.unknown[0].x") spelling
                | Ok _ -> failtestf "the plan accepted %s" spelling

            Expect.isOk (ResultDocument.read (resultWithUnknownInteger "9007199254740991" true)) "the bound is inclusive")

        testCase "the result reader bounds unknown plan integers before checking the plan digest" (fun () ->
            match ResultDocument.read (resultWithUnknownInteger "9007199254740992" false) with
            | Error refusal ->
                Expect.equal (Code.readerName refusal.Code, DocumentPath.render refusal.Path) ("invalid_value", "plan.unknown[0].x") "the exact member"
            | Ok _ -> failtest "a plan cannot carry an integer past the bound")

        testCase "the escaping fixture's plan digest is the SHA-256 of its canonical bytes" (fun () ->
            let canonical = Fixtures.bytes "result/setup-plan-escaping.plan.canonical.json"

            match ResultDocument.read (Fixtures.bytes "result/setup-plan-escaping.json") |> Result.map _.Plan with
            | Ok(Some digested) ->
                Expect.sequenceEqual digested.Canonical canonical "canonical bytes"
                // The reader already refused a planSha256 that is not this digest.
                Expect.equal digested.Sha256 (Sha256.ofBytes canonical) "planSha256"
            | other -> failtestf "the escaping fixture does not read with a plan: %A" other)
    ]
