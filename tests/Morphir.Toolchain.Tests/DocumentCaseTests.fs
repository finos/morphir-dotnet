// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Every document case of the unsigned manifest: the reader accepts it or refuses it with the exact code and path.
module Morphir.Toolchain.Tests.DocumentCaseTests

open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest

/// The outcome of reading a document with its contract's reader, in manifest terms.
let outcome (contract: string) (bytes: byte array) =
    let refused (result: Result<'T, ReaderRefusal>) =
        match result with
        | Ok _ -> Valid
        | Error refusal -> Refused(Code.readerName refusal.Code, DocumentPath.render refusal.Path)

    match contract with
    | "request" -> refused (RequestDocument.read bytes)
    | "lock" -> refused (LockDocument.read bytes)
    | "recipe" -> refused (RecipeDocument.read bytes)
    | "receipt" -> refused (ReceiptDocument.read bytes)
    | "result" -> refused (ResultDocument.read bytes)
    | other -> failwithf "the manifest names contract %s, which this suite does not read" other

[<Tests>]
let tests =
    let cases = documents ()

    testList "Unsigned document cases" [
        testCase "the manifest lists document cases for all five contracts" (fun () ->
            let contracts = cases |> List.map _.Contract |> set
            Expect.equal contracts (set [ "request"; "lock"; "recipe"; "receipt"; "result" ]) "every contract has cases")

        testCase "a schema gap is recorded exactly where the schema accepts a refused document" (fun () ->
            for case in cases do
                let refused = case.Expected <> Valid
                let accepts = case.Schema = Some "accepts"
                Expect.equal case.SchemaGap.IsSome (refused && accepts) $"{case.Document}: schemaGap iff the schema accepts a refusal")

        testList "cases" [
            for case in cases ->
                testCase $"{case.Contract} {case.Document}" (fun () ->
                    let actual = outcome case.Contract (Fixtures.bytes case.Document)
                    Expect.equal actual case.Expected $"{case.Document} has the manifest's outcome")
        ]
    ]
