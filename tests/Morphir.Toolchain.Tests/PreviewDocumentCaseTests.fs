// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Behavioral adoption of the candidate family through the existing document readers.
/// Selection and workflow-case adoption are separate, unfinished reader slices.
module Morphir.Toolchain.Tests.PreviewDocumentCaseTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Expecto
open Morphir.Toolchain

type private Case =
    { Id: string
      Contract: string
      File: string
      Sha256: string
      Expected: string
      Reason: string option }

let private text (name: string) (element: JsonElement) =
    element.GetProperty(name).GetString()
    |> Option.ofObj
    |> Option.defaultWith (fun () -> failwithf "manifest member %s must be a string" name)

let private readCases root =
    use document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "docs/spec/toolchain/preview/corpus/cases.json")))

    document.RootElement.GetProperty("documents").EnumerateArray()
    |> Seq.map (fun row ->
        let expected = row.GetProperty("expected")
        let outcome = text "outcome" expected

        { Id = text "id" row
          Contract = text "contract" row
          File = text "file" row
          Sha256 = text "sha256" row
          Expected = outcome
          Reason = if outcome = "refuse" then Some(text "reason" expected) else None })
    |> Seq.toList

let private read contract bytes : Result<unit, ReaderRefusal> =
    match contract with
    | "request" -> RequestDocument.readPreview bytes |> Result.map ignore
    | "recipe" -> RecipeDocument.readPreview bytes |> Result.map ignore
    | "lock" -> LockDocument.readPreview bytes |> Result.map ignore
    | "receipt" -> ReceiptDocument.readPreview bytes |> Result.map ignore
    | "result" -> ResultDocument.readPreview bytes |> Result.map ignore
    | other -> failtestf "No existing-document reader is registered for %s" other

let private assertOutcome case actual =
    match case.Expected, case.Reason, actual with
    | "accept", None, Ok () -> ()
    | "refuse", Some reason, Error refusal ->
        // The parent names rules, not new command diagnostic codes. Check the
        // existing typed refusal for that rule, without creating another parser.
        let matches =
            match reason, refusal.Code with
            | "unsupported-schema", ReaderCode.UnsupportedContractVersion
            | "missing-schema", ReaderCode.InvalidDocument
            | "unsupported-critical", ReaderCode.UnsupportedCriticalMember
            | "conflicting-host-selection", ReaderCode.InconsistentDocument -> true
            | _ -> false

        Expect.isTrue matches $"{case.Id}: expected {reason}, got {Code.readerName refusal.Code} at {DocumentPath.render refusal.Path}"
    | _ -> failtestf "%s: expected %s (%A), got %A" case.Id case.Expected case.Reason actual

/// Registered only in external-corpus mode. The historical pin has no preview
/// inputs and remains unchanged. A selected external corpus must contain them.
let tests () =
    match Fixtures.external with
    | Ok(Some root) ->
        let cases = readCases root
        let existing = set [ "request"; "recipe"; "lock"; "receipt"; "result" ]
        let selected = cases |> List.filter (fun row -> existing.Contains row.Contract)

        testList "Preview family existing document readers" [
            testCase "the manifest partition keeps the two unfinished readers explicit" (fun () ->
                Expect.equal (cases |> List.map _.Contract |> set) (Set.union existing (set [ "selection"; "workflow-case" ])) "unknown contracts cannot disappear"
                Expect.equal (cases |> List.map _.Id |> List.distinct |> List.length) cases.Length "case identities are unique"
                Expect.equal selected.Length 18 "the five existing readers have eighteen fixed inputs"
                Expect.equal (cases |> List.filter (fun row -> row.Contract = "selection") |> List.length) 16 "selection adoption remains a separate sixteen-input slice"
                Expect.equal (cases |> List.filter (fun row -> row.Contract = "workflow-case") |> List.length) 3 "workflow-case adoption remains a separate three-input slice")

            for case in selected do
                testCase case.Id (fun () ->
                    let path = Path.GetFullPath(Path.Combine(root, case.File))
                    let relative = Path.GetRelativePath(root, path)
                    Expect.isFalse (Path.IsPathRooted relative || relative = ".." || relative.StartsWith(".." + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) "input stays within the selected corpus"
                    let bytes = File.ReadAllBytes path
                    Expect.equal (Convert.ToHexStringLower(SHA256.HashData bytes)) case.Sha256 "read the exact approved input, without parsing and reserializing it"
                    read case.Contract bytes |> assertOutcome case)
        ]
    | _ -> testCase "Preview reader adoption requires an explicit external corpus" (fun () -> failtest "select MORPHIR_TOOLCHAIN_CONTRACT_ROOT; no preview inputs are copied into the historical pin")

/// Regression inputs for family isolation and the adopted extension identity
/// grammar. These are in-memory requests, not changes to any fixed corpus bytes.
let isolationTests () =
    let request declaration identity =
        String.concat "\n" [
            declaration
            "[host]"
            "implementation = \"dotnet\""
            "deployment = \"native-aot\""
            "version = \"1.2.3\""
            "[toolset]"
            "preset = \"fsharp\""
            "[[toolset.providers]]"
            "operation = \"compile\""
            "language = \"fsharp\""
            $"extensionId = \"{identity}\""
        ]
        |> Text.Encoding.UTF8.GetBytes

    let directive = "#:schema https://morphir.finos.org/schemas/toolchains/0.1.0-preview.1/request.schema.json"
    let accepted bytes =
        match RequestDocument.readPreview bytes with
        | Ok value -> value
        | Error refusal -> failtestf "preview request was refused: %s at %s" (Code.readerName refusal.Code) (DocumentPath.render refusal.Path)

    testList "Preview family envelope and identity isolation" [
        testCase "preview extension identity may use one full 238-character segment" (fun () ->
            let identity = String('a', 238)
            let value = request directive identity |> accepted
            Expect.equal (value.Toolset.Providers.Head |> ProviderChoice.extensionId |> Option.map _.Text) (Some identity) "the published total limit is not a historical per-segment limit")

        testCase "preview extension identity refuses a reserved first device segment" (fun () ->
            for first in [ "con"; "prn"; "aux"; "nul"; "com1"; "com9"; "lpt1"; "lpt9" ] do
                match request directive (first + ".frontend") |> RequestDocument.readPreview with
                | Error refusal -> Expect.equal refusal.Code ReaderCode.InvalidValue "the published extension grammar refuses the first segment"
                | Ok _ -> failtestf "accepted reserved first segment %s" first)

        testCase "preview device words in a later segment remain valid identities" (fun () ->
            request directive "acme.con" |> accepted |> ignore)

        testCase "historical requests retain their original identity grammar" (fun () ->
            let historical = "schemaVersion = \"0.1.0-draft.1\""
            Expect.isError (request historical (String('a', 238)) |> RequestDocument.read) "the historical parser still has its original segment bound"
            Expect.isOk (request historical "con.frontend" |> RequestDocument.read) "the historical corpus is not silently moved to another grammar")

        testCase "preview directive is first-line authority and never historical fallback" (fun () ->
            let bytes = request directive "acme.frontend"
            let later = Array.append [| 10uy |] bytes
            Expect.isError (RequestDocument.readPreview later) "a later comment cannot establish the document family"
            Expect.isError (request "schemaVersion = \"0.1.0-draft.1\"" "acme.frontend" |> RequestDocument.readPreview) "historical declaration alone never selects the preview codec"
            let mixed = request (directive + "\nschemaVersion = \"0.1.0-draft.1\"") "acme.frontend"
            Expect.isError (RequestDocument.readPreview mixed) "a second family declaration is not ignored")

        testCase "preview request identity includes original directive and line-ending bytes" (fun () ->
            let lf = request directive "acme.frontend"
            let crlf = Text.Encoding.UTF8.GetString(lf).Replace("\n", "\r\n") |> Text.Encoding.UTF8.GetBytes
            let first = accepted lf
            let second = accepted crlf
            Expect.equal first.Identity (Sha256.ofBytes lf) "hash the original LF input"
            Expect.equal second.Identity (Sha256.ofBytes crlf) "hash the original CRLF input"
            Expect.notEqual first.Identity second.Identity "no input normalization or schema relabeling")
    ]
