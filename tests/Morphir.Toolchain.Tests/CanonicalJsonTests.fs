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

        testCase "the escaping fixture's plan digest is the SHA-256 of its canonical bytes" (fun () ->
            let canonical = Fixtures.bytes "result/setup-plan-escaping.plan.canonical.json"

            match ResultDocument.read (Fixtures.bytes "result/setup-plan-escaping.json") with
            | Ok { Plan = Some digested } ->
                Expect.sequenceEqual digested.Canonical canonical "canonical bytes"
                Expect.equal digested.Sha256.Text "5d4e4779706cc887c83cf3042285b71c6c1ef0cada1dee41634828e4f287a801" "planSha256"
            | other -> failtestf "the escaping fixture does not read with a plan: %A" other)
    ]
