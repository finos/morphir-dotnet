// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.Tests.Program

open System
open Expecto
open Morphir.TestSupport

/// <summary>
/// The tests that run the contract corpus through Morphir.Toolchain: the unsigned document and binding cases, the
/// signed repository's cases and declared mutations, the schemas' claims and the writers' output against them, the
/// contract tables of the pages and schemas, canonical JSON, and the readiness workflow case. With an external corpus
/// only these run: the other tests use the pinned fixtures as data and assume their shapes, and the provenance tests
/// check the pin itself.
/// </summary>
let corpusTests () =
    // A module whose manifest cannot be read, for instance because it lists a case kind or member this suite does
    // not run, fails as one test that says why, instead of ending the run in a type initializer.
    let guarded (name: string) (tests: unit -> Test) =
        try
            tests ()
        with error ->
            let rec innermost (e: exn) =
                match e.InnerException with
                | null -> e
                | inner -> innermost inner

            let reason = innermost error
            testCase $"{name} cannot be built from the corpus" (fun () -> failtestf "%s: %s" (reason.GetType().Name) reason.Message)

    testList "Toolchain contract corpus" [
        guarded "Unsigned document cases" (fun () -> DocumentCaseTests.tests)
        guarded "Unsigned binding cases" (fun () -> BindingCaseTests.tests)
        guarded "Signed cases" (fun () -> SignedCaseTests.tests)
        guarded "JSON Schemas" (fun () -> SchemaTests.tests)
        guarded "The readiness workflow case" (fun () -> SchemaTests.workflowCaseTests)
        guarded "Required and optional members" (fun () -> RequiredMemberTests.tests)
        guarded "The readiness workflow case's data" (fun () -> WorkflowCaseTests.tests)
        guarded "Contract tables" (fun () -> ContractTableTests.tests)
        guarded "Canonical JSON" (fun () -> CanonicalJsonTests.tests)
        guarded "Writers" (fun () -> WriterTests.tests)
    ]

/// The next slices write under Morphir Home, so the run fails when a test changed the machine folders outside its
/// sandbox. With <c>MORPHIR_TOOLCHAIN_CONTRACT_ROOT</c> set, the run first prints the external corpus marker, or, when
/// the root is not a contract corpus, says why on standard error and exits 2 without running anything.
[<EntryPoint>]
let main args =
    match Fixtures.external with
    | Error reason ->
        eprintfn "Morphir.Toolchain.Tests: %s" reason
        2
    | Ok corpus ->
        let tripwire = MachineTripwire.Capture()

        let result =
            match corpus with
            | None -> runTestsInAssemblyWithCLIArgs [] args
            | Some root ->
                Console.Out.WriteLine(Fixtures.marker root)
                Console.Out.Flush()
                runTestsWithCLIArgs [] args (corpusTests ())

        tripwire.AssertUnchanged()
        result
