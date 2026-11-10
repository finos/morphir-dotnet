// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// One test for each source mutation the review expected to survive the suite. Each test fails under its mutation;
/// where the contract does not pin the rule, the test pins this implementation's reading, and the reading is listed
/// as a contract gap.
/// </summary>
module Morphir.Toolchain.Tests.MutationTests

open System.Text
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.BindingCaseTests
open Morphir.Toolchain.Tests.Manifest

let private text (relative: string) = Encoding.UTF8.GetString(Fixtures.bytes relative)

let private refusalOf (result: Result<'T, ReaderRefusal>) =
    match result with
    | Ok _ -> failtest "expected a refusal"
    | Error refusal -> Code.readerName refusal.Code, DocumentPath.render refusal.Path

let private resolutionFailure (result: Result<Resolved, ResolutionFailure>) =
    match result with
    | Ok _ -> failtest "expected a resolution failure"
    | Error failure -> failure

[<Tests>]
let tests =
    testList "Review mutations" [
        testCase "a WASM provider that lacks both its ABI and its protocol reports the ABI" (fun () ->
            // The preferred provider is the extism-kernel-v1 guest. A host that offers no protocol at all makes it
            // misfit twice. Such a recipe fails its consistency rules, so it is built in memory, after reading.
            let read = recipeAt "recipe/wasm-abi-alternatives.json"
            let recipe = { read with Hosts = read.Hosts |> List.map (fun host -> { host with ProtocolVersions = [] }) }
            let request = RequestDocument.read (Fixtures.bytes "request/wasm-example-default.toml") |> valid "the request"
            let failure = resolutionFailure (Resolution.resolve recipe request Platform.WinX64)

            match failure.Problem with
            | ArtifactProblem.AbiUnavailable abi -> Expect.equal abi.Text "extism-kernel-v1" "the missing ABI"
            | other -> failtestf "the ABI is checked before the protocol, got %A" other

            Expect.equal (DocumentPath.render failure.Path) "toolset" "the preferred provider")

        testCase "the credential scan reports an object before anything inside it" (fun () ->
            let lock = text "lock/approved-fragment.json"
            let bytes = Encoding.UTF8.GetBytes("{\"token\":{\"apiKey\":1}," + lock.Substring(lock.IndexOf '{' + 1))

            Expect.equal (refusalOf (LockDocument.read bytes)) ("credential_in_document", "token") "pre-order: token, not token.apiKey")

        testList "an exact host version matches the offering's text, not its precedence" [
            let request (version: string) =
                (text "request/exact-host-version.toml").Replace("version = \"1.2.3\"", $"version = \"{version}\"")
                |> Encoding.UTF8.GetBytes
                |> RequestDocument.read
                |> valid version

            // Read inside each test, so a refused fixture fails that test and not the whole run.
            let recipe () = recipeAt "recipe/fsharp-synthetic.json"

            testCase "1.2.3 resolves" (fun () ->
                Expect.isOk (Resolution.resolve (recipe ()) (request "1.2.3") Platform.WinX64) "the offering's version")

            testCase "1.2.3+build.7 has the same precedence and is refused" (fun () ->
                let failure = resolutionFailure (Resolution.resolve (recipe ()) (request "1.2.3+build.7") Platform.WinX64)
                Expect.equal (failure.Code, DocumentPath.render failure.Path) (ArtifactCode.UnsupportedArtifact, "host.version") "not the exact release")
        ]

        testList "a repeated JSON member name is refused" [
            testCase "in a bare value" (fun () ->
                Expect.equal (refusalOf (Syntax.parseJson (Encoding.UTF8.GetBytes "{\"a\":1,\"a\":1}"))) ("invalid_document", "") "the document")

            testCase "in a lock, even with the same value" (fun () ->
                let lock = text "lock/approved-fragment.json"
                let bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"0.1.0-draft.1\"," + lock.Substring(lock.IndexOf '{' + 1))
                Expect.equal (refusalOf (LockDocument.read bytes)) ("invalid_document", "") "the document")
        ]

        testCase "a receipt for other lock bytes and another recipe reports the lock bytes first" (fun () ->
            let receipt =
                ReceiptDocument.read (Fixtures.bytes "receipt/binding/recipe-substituted.json") |> valid "the receipt"

            let lock = lockAt "lock/approved-fragment.json"
            Expect.notEqual receipt.LockSha256 lock.Identity "precondition: other lock bytes"
            Expect.notEqual receipt.Recipe lock.Recipe "precondition: another recipe"

            Expect.equal
                (Bindings.receiptBindsLock receipt lock |> bindingOutcome)
                (Refused("lock_changed", "lockSha256"))
                "lockSha256 is compared before recipe")

        testCase "a WASM provider's readiness probe runs in the host" (fun () ->
            match
                Planning.plan
                    { Recipe = recipeAt "recipe/wasm-abi-alternatives.json"
                      RecipeRepository = PlanningTests.repository
                      RecipeTarget = PlanningTests.recipeTarget
                      Request = RequestDocument.read (Fixtures.bytes "request/wasm-example-bytes-abi.toml") |> valid "the request"
                      Platform = Platform.WinX64
                      Sizes = PlanningTests.everySize
                      MetadataRefresh = MetadataRefresh.Required }
            with
            | Ok(digested, _) ->
                match digested.Plan.Components |> List.last with
                | PlannedComponent.Provider(_, _, PlannedDelivery.Wasm, _) as guest ->
                    Expect.equal (PlannedComponent.probe guest) Probe.InHost "in-host"
                | other -> failtestf "the guest is planned as %A" other
            | Error failure -> failtestf "no plan: %A" failure)
    ]
