// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// What the writers produce reads back as the same document.
module Morphir.Toolchain.Tests.WriterTests

open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest
open Morphir.Toolchain.Tests.BindingCaseTests

let private validDocuments contract =
    documents () |> List.filter (fun case -> case.Contract = contract && case.Expected = Valid) |> List.map _.Document

[<Tests>]
let tests =
    testList "Writers" [
        testList "locks" [
            for document in validDocuments "lock" ->
                testCase document (fun () ->
                    let lock = lockAt document
                    let written = LockDocument.write lock
                    let read = LockDocument.read written |> valid "the written lock"
                    Expect.equal { read with Identity = lock.Identity } lock "the same lock"
                    Expect.equal read.Identity (Sha256.ofBytes written) "its identity is the digest of the written bytes")
        ]

        testList "receipts" [
            for document in validDocuments "receipt" ->
                testCase document (fun () ->
                    let receipt = ReceiptDocument.read (Fixtures.bytes document) |> valid document
                    let read = ReceiptDocument.read (ReceiptDocument.write receipt) |> valid "the written receipt"
                    Expect.equal read receipt "the same receipt")
        ]

        testList "results" [
            for document in validDocuments "result" ->
                testCase document (fun () ->
                    let result = ResultDocument.read (Fixtures.bytes document) |> valid document
                    let read = ResultDocument.read (ResultDocument.write result) |> valid "the written result"
                    Expect.equal read.Diagnostics result.Diagnostics "the same diagnostics"
                    Expect.equal (read.Command, read.Outcome, read.Selection) (result.Command, result.Outcome, result.Selection) "the same members"
                    Expect.equal (read.Plan |> Option.map _.Plan) (result.Plan |> Option.map _.Plan) "the same plan")
        ]

        testCase "a request written from selector options reads as that request" (fun () ->
            let host =
                match Token.tryParse "dotnet", Token.tryParse "native-aot", Channel.tryParse "stable" with
                | Ok implementation, Ok deployment, Ok channel ->
                    { Implementation = implementation
                      Deployment = deployment
                      Release = HostRelease.Channel channel }
                | _ -> failwith "unreachable"

            let preset =
                match Token.tryParse "fsharp" with
                | Ok preset -> preset
                | Error _ -> failwith "unreachable"

            let bytes = RequestDocument.write host preset None
            let request = RequestDocument.read bytes |> valid "the written request"
            Expect.equal (request.Host, request.Toolset.Preset) (host, preset) "the same request"
            Expect.sequenceEqual bytes (Fixtures.bytes "request/approved.toml") "the approved example's exact bytes")
    ]
    |> testLabel "PlatformSpecific"
