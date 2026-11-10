// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Rules the fixtures do not pin, from the independent review of the first slice.
module Morphir.Toolchain.Tests.HardeningTests

open System.Text
open Expecto
open Morphir.Toolchain

let private refusal (result: Result<'T, ReaderRefusal>) =
    match result with
    | Ok _ -> failtest "expected a refusal"
    | Error refusal -> Code.readerName refusal.Code, DocumentPath.render refusal.Path

/// The approved lock fragment with its critical list replaced.
let private lockWithCritical (critical: string) =
    let text = Encoding.UTF8.GetString(Fixtures.bytes "lock/approved-fragment.json")
    Encoding.UTF8.GetBytes(text.Replace("\"critical\": [\"host\", \"platforms\"]", $"\"critical\": [{critical}]"))

/// A result with one diagnostic whose path member is the given text.
let private resultWithPath (path: string) =
    Encoding.UTF8.GetBytes(
        $"""{{"schemaVersion":"0.1.0-draft.1","command":"doctor","outcome":"failed","diagnostics":[{{"kind":"unsupported-contract","code":"invalid_value","severity":"error","message":"m","document":"lock","path":"{path}"}}]}}"""
    )

[<Tests>]
let tests =
    testList "Review hardening" [
        testList "a refused critical path is reported exactly as written" [
            for critical in [ ".host"; "..x"; "host."; "a..b"; "platforms.*.unknownMember" ] ->
                testCase critical (fun () ->
                    Expect.equal
                        (refusal (LockDocument.read (lockWithCritical $"\"{critical}\"")))
                        ("unsupported_critical_member", critical)
                        "the diagnostic path is the critical entry")
        ]

        testList "a result's diagnostic path round-trips exactly" [
            for path in [ ".x"; "a..b"; "providers[0].identity"; "x."; "" ] ->
                testCase $"'{path}'" (fun () ->
                    match ResultDocument.read (resultWithPath path) with
                    | Ok read ->
                        let again = ResultDocument.read (ResultDocument.write read) |> Result.map _.Diagnostics

                        match again with
                        | Ok [ { Detail = DiagnosticDetail.UnsupportedContract(_, _, written) } ] ->
                            Expect.equal (DocumentPath.render written) path "the path text"
                        | other -> failtestf "unexpected %A" other
                    | Error refusal -> failtestf "refused: %A" refusal)
        ]

        testList "deep nesting is refused before any parser recurses" [
            let approved = Encoding.UTF8.GetString(Fixtures.bytes "request/approved.toml")

            for name, text in
                [ "TOML arrays 10,000 deep", "a = " + String.replicate 10000 "[" + String.replicate 10000 "]"
                  "TOML inline tables 10,000 deep", "a = " + String.replicate 10000 "{b = " + "1" + String.replicate 10000 "}"
                  "TOML arrays 65 deep", "a = " + String.replicate 65 "[" + String.replicate 65 "]" ] ->
                testCase name (fun () ->
                    Expect.equal (refusal (Syntax.parseToml (Encoding.UTF8.GetBytes text))) ("invalid_document", "") "refused at the document")

            yield testCase "TOML arrays 60 deep still parse" (fun () ->
                let text = "a = " + String.replicate 60 "[" + String.replicate 60 "]"
                Expect.isOk (Syntax.parseToml (Encoding.UTF8.GetBytes text)) "within the bound")

            yield testCase "JSON 10,000 deep" (fun () ->
                let text = String.replicate 10000 "{\"a\":" + "1" + String.replicate 10000 "}"
                Expect.equal (refusal (Syntax.parseJson (Encoding.UTF8.GetBytes text))) ("invalid_document", "") "refused at the document")

            yield testCase "brackets inside TOML strings and comments do not count" (fun () ->
                let deep = String.replicate 10000 "["
                let text =
                    approved
                    + $"\nnote = \"{deep}\"\nliteral = '{deep}'\nmulti = \"\"\"\n{deep}\"\"\"\"\nraw = '''{deep}'''\n# {deep}\n"
                Expect.isOk (RequestDocument.read (Encoding.UTF8.GetBytes(text.Replace("[host]", "x = 1\n[host]")))) "a valid request")
        ]
        testCase "U+FFFE, which string.Normalize refuses, is its own NFC and never stops a document" (fun () ->
            let noncharacter = string (char 0xFFFE)
            let decomposed = "e" + string (char 0x0301)
            let precomposed = string (char 0x00E9)

            match Canonical.bytes (Value.String("a" + noncharacter + decomposed)) with
            | Ok bytes ->
                Expect.equal (Encoding.UTF8.GetString bytes) ("\"a" + noncharacter + precomposed + "\"") "normalized around it"
            | Error refusal -> failtestf "refused: %A" refusal

            Expect.isOk (TargetPath.tryParse ("notes/a" + noncharacter + ".txt")) "an NFC target path that holds it"
            Expect.equal (TargetPath.tryParse ("notes/a" + noncharacter + decomposed) |> Result.map ignore) (Error ReaderCode.NonPortablePath) "and one that is not NFC")
        testList "a built-in with a protocol member is inconsistent_document at that member" [
            // toolchain-lock.md and installation-receipt.md: a built-in has no MEP session. The fixtures pin
            // protocolVersion in a lock and modelTuple in a recipe; these pin the other members and the receipt.
            let tuple =
                "{\"operation\": \"morphir.frontend.compileModel\", \"profile\": \"morphir-ir\", \"codec\": \"morphir-json\", \"envelopeVersion\": \"0.2.0\", \"irVersion\": \"4.0.0\"}"

            let withMember (fixture: string) (name: string) (value: string) =
                let text = Encoding.UTF8.GetString(Fixtures.bytes fixture)
                let marker = "\"delivery\": \"built-in\","
                let at = text.IndexOf marker
                Expect.isGreaterThan at 0 "the first built-in"
                Encoding.UTF8.GetBytes(text.Insert(at + marker.Length, $" \"{name}\": {value},"))

            let refusedAt (read: byte array -> Result<'T, ReaderRefusal>) (bytes: byte array) (path: string) =
                match read bytes with
                | Error refusal -> Expect.equal (refusal.Code, DocumentPath.render refusal.Path) (ReaderCode.InconsistentDocument, path) path
                | Ok _ -> failtestf "a built-in with %s was read" path

            for name, value in [ "protocolVersion", "\"0.2.0\""; "modelTuple", tuple ] do
                yield
                    testCase $"{name} in a lock" (fun () ->
                        refusedAt LockDocument.read (withMember "lock/built-in-frontend.json" name value) $"platforms.win-x64.providers[0].{name}")

                yield
                    testCase $"{name} in a receipt" (fun () ->
                        refusedAt ReceiptDocument.read (withMember "receipt/win-x64-qualified.json" name value) $"providers[1].identity.{name}")
        ]
        testCase "a host plan component with a recipe-digest source is inconsistent_document at its source" (fun () ->
            // manager-result.md: a host's source is repository-target or external-owner. No fixture pins recipe-digest.
            let text = Encoding.UTF8.GetString(Fixtures.bytes "result/setup-plan.json")
            let at = text.IndexOf "\"repository-target\""
            Expect.isGreaterThan at 0 "the host's source"
            let edited = text.Substring(0, at) + "\"recipe-digest\"" + text.Substring(at + "\"repository-target\"".Length)

            match ResultDocument.read (Encoding.UTF8.GetBytes edited) with
            | Error refusal ->
                Expect.equal
                    (refusal.Code, DocumentPath.render refusal.Path)
                    (ReaderCode.InconsistentDocument, "plan.components[0].source")
                    "refused at the host's source"
            | Ok _ -> failtest "a host authenticated by the recipe's digest was read")
        testCase "only the exact draft text is supported; build metadata is refused" (fun () ->
            Expect.equal (ContractVersion.check "0.1.0-draft.1") (Ok()) "the draft"
            Expect.equal (ContractVersion.check "0.1.0-draft.1+meta") (Error ReaderCode.UnsupportedContractVersion) "build metadata"
            Expect.equal (ContractVersion.check "0.1.0-draft.01") (Error ReaderCode.InvalidValue) "not SemVer")

        testList "mirrors need an explicit http or https scheme on every operating system" [
            for text, expected in
                [ "https://mirror.example.invalid/morphir/", Ok()
                  "HTTPS://mirror.example.invalid/", Ok()
                  "http://mirror.internal.invalid/morphir/", Ok()
                  "/srv/mirror", Error ReaderCode.InvalidValue
                  "file:///srv/mirror", Error ReaderCode.InvalidValue
                  @"c:\mirror", Error ReaderCode.InvalidValue
                  "c:/mirror", Error ReaderCode.InvalidValue
                  "ftp://mirror.example.invalid/", Error ReaderCode.InvalidValue
                  "https:///no-host", Error ReaderCode.InvalidValue
                  "mirror.example.invalid", Error ReaderCode.InvalidValue
                  "https://ada:s3cret@mirror.example.invalid/", Error ReaderCode.CredentialInDocument
                  "https://mirror.example.invalid/?token=x", Error ReaderCode.CredentialInDocument
                  "https://mirror.example.invalid/#top", Error ReaderCode.CredentialInDocument ] ->
                testCase text (fun () -> Expect.equal (Mirror.tryParse text |> Result.map ignore) expected text)
        ]
    ]
