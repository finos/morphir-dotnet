// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.FeatureReaderTests

open System.IO
open System.Text
open Expecto
open Morphir.Conformance
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository

let private suite () =
    FeatureReader.read (corpus.Force()) (committed ())
    |> requireOk "The committed suite"

/// Replaces the first occurrence of a fragment after the scenario of a case, and gives the
/// 1-based line of the replacement.
let private editCase (caseId: string) (fragment: string) (replacement: string) (text: string) =
    let start =
        text.IndexOf(
            " "
            + caseId
            + "\n"
        )

    let index = text.IndexOf(fragment, start)
    Expect.isGreaterThan index 0 $"{fragment} is in the scenario of {caseId}"

    let line =
        (text.Substring(0, index)
         |> Seq.filter (fun character -> character = '\n')
         |> Seq.length)
        + 1

    text.Substring(0, index)
    + replacement
    + text.Substring(
        index
        + fragment.Length
    ),
    line

[<Tests>]
let tests =
    testList "WASM host feature reader (task 1.5)" [
        // Spec scenario: Both formats give the same cases.
        test "The generated features and the pinned JSON give equal case models for every case" {
            let corpus = corpus.Force()
            let suite = suite ()

            Expect.equal
                suite.Cases.Length
                (corpusCaseCount ())
                "Every corpus case is in the features"

            Expect.equal suite.Cases.Length PinnedCaseCount "58 cases at the pin"

            let small =
                suite.Cases
                |> List.find (fun case -> case.Case.CaseId = "bounds/small-budget")

            Expect.equal
                small.Case.Worker.Instructions
                (Some 1000000L)
                "The budget step reads back as worker.instructions"

            let spin =
                suite.Cases
                |> List.find (fun case -> case.Case.CaseId = "bounds/spin-extism")

            match spin.Case.Runs with
            | [ {
                    Expect = RunExpectation.Alternatives alternatives
                } ] ->
                Expect.equal
                    (alternatives
                     |> List.map (fun alternative -> alternative.When))
                    [
                        Condition.Bound(Bound.InstructionBudget, true)
                        Condition.Bound(Bound.InstructionBudget, true)
                        Condition.Bound(Bound.InstructionBudget, false)
                    ]
                    "Two scenarios with the same tags are two alternatives"
            | other -> failtest $"Unexpected runs %A{other}"

            Expect.equal suite.Pin corpus.Pin "The same pin"
            Expect.equal suite.Kit corpus.Kit "The same kit"

            let fromFeatures =
                suite.Cases
                |> List.map (fun case -> case.Case.CaseId, case.Case)
                |> Map.ofList

            let fromJson =
                corpus.Cases
                |> List.map (fun case -> case.CaseId, case)
                |> Map.ofList

            Expect.equal
                (Map.keys fromFeatures
                 |> Set.ofSeq)
                (Map.keys fromJson
                 |> Set.ofSeq)
                "No case in one and not in the other"

            for KeyValue(caseId, case) in fromJson do
                Expect.equal fromFeatures[caseId] case $"{caseId} is equal in both formats"

            Expect.equal
                (suite.Cases
                 |> List.map (fun case -> case.Id)
                 |> List.distinct
                 |> List.length)
                PinnedCaseCount
                "Ids are distinct"

            Expect.all
                suite.Cases
                (fun case -> case.Id.StartsWith "wasm-host-")
                "Ids have the suite prefix"
        }

        // Spec scenario: Hand edit.
        test "A feature whose expectation is edited by hand is refused" {
            let text = featureText "bounds.feature"

            let edited, line =
                editCase
                    "bounds/trap"
                    "Then the worker exits with status 1"
                    "Then the worker exits with status 0"
                    text

            let artifacts = committedWith "bounds.feature" edited

            let issues =
                FeatureReader.read (corpus.Force()) artifacts
                |> requireError "hand edit"

            Expect.exists
                issues
                (fun issue ->
                    issue.Path = Generation.SuiteDirectory
                                 + "/bounds.feature"
                    && issue.Message.Contains "bounds/trap"
                    && issue.Line = line
                )
                $"The edit is refused with its file, line and case: %A{issues}"

            Expect.isNonEmpty
                (Generation.check
                    (Generation.generate
                        (corpus.Force())
                        (Some(
                            CaseIds.parse Suite.IdMapFile (committedMap ())
                            |> requireOk "map"
                        ))
                     |> requireOk "generate")
                    artifacts)
                "The stale check fails too"
        }

        test "A feature that is not in the generator's form is refused" {
            let text = featureText "bounds.feature"
            let edited = text.Replace("\n  Scenario:", "\n  Scenario:  ")

            let issues =
                FeatureReader.parse (committedWith "bounds.feature" edited)
                |> requireError "not generated"

            Expect.exists
                issues
                (fun issue ->
                    issue.Code = "mck.not_generated"
                    && issue.Path.EndsWith "bounds.feature"
                )
                $"The file is named: %A{issues}"
        }

        // Spec scenario: Step outside the vocabulary.
        test "A feature with an unknown step is refused with its file and line" {
            let text = featureText "bounds.feature"
            let step = "And the worker is restarted after a minute"

            let edited, line =
                editCase
                    "bounds/trap"
                    "Then the worker exits"
                    (step
                     + "\n    Then the worker exits")
                    text

            let issues =
                FeatureReader.parse (committedWith "bounds.feature" edited)
                |> requireError "unknown step"

            Expect.exists
                issues
                (fun issue ->
                    issue.Code = "mck.unknown_step"
                    && issue.Path = Generation.SuiteDirectory
                                    + "/bounds.feature"
                    && issue.Line = line
                    && issue.Message.Contains "the worker is restarted after a minute"
                )
                $"The step is named with its location: %A{issues}"
        }

        // Spec scenario: Extism-only host (requirement "Cases follow the profiles a host declares").
        test
            "For an Extism-only host the byte-profile cases do not apply, and the byte-module case expects the refusal" {
            let suite = suite ()
            let declared = [ Profile.ExtismKernelV1 ]
            let enforcedAll (_: Bound) = true

            let byteCases =
                suite.Cases
                |> List.filter (fun case -> case.Case.AppliesTo = Profile.MorphirBytesV1)

            Expect.isNonEmpty byteCases "The corpus has byte-profile cases"

            Expect.all
                byteCases
                (fun case -> not (Selection.applies declared case.Case))
                "Byte-profile cases are not applicable"

            Expect.exists
                byteCases
                (fun case -> case.Case.CaseId = "bytes/notification-code-for-request")
                "The byte-profile result-code cases are among them"

            let offered =
                suite.Cases
                |> List.find (fun case ->
                    case.Case.CaseId = "classification/bytes-profile-unsupported"
                )

            Expect.isTrue
                (Selection.applies declared offered.Case)
                "The case that offers a byte module applies"

            match
                offered.Case.Runs
                |> List.map (Selection.applicable declared enforcedAll)
            with
            | [ Ok [ { Expect = expect } ] ] ->
                Expect.equal expect.ExitCode (Some 3) "The worker refuses before any request"

                Expect.equal
                    expect.Events
                    [
                        EventMatcher.Refused(
                            Some "extension.abi_unsupported",
                            [ "morphir-bytes-v1" ]
                        )
                    ]
                    "The refusal names the profile"
            | other -> failtest $"Unexpected selection %A{other}"

            match
                offered.Case.Runs
                |> List.map (
                    Selection.applicable
                        [ Profile.ExtismKernelV1; Profile.MorphirBytesV1 ]
                        enforcedAll
                )
            with
            | [ Ok [ { Expect = expect } ] ] ->
                Expect.equal
                    expect.ExitCode
                    (Some 0)
                    "A host that runs the profile serves the module"
            | other -> failtest $"Unexpected selection %A{other}"
        }

        test
            "The F# frontend suite still reads the shipped conformance directory beside the wasm-host suite" {
            let conformance = Path.Combine(root, "conformance")

            let artifacts = [
                for file in Directory.GetFiles(conformance, "*", SearchOption.AllDirectories) ->
                    {
                        Path = Path.GetRelativePath(conformance, file).Replace('\\', '/')
                        Bytes = File.ReadAllBytes file
                    }
            ]

            Expect.exists
                artifacts
                (fun artifact -> artifact.Path.StartsWith "suites/wasm-host/")
                "The wasm-host suite is present"

            let options = {
                Suite = "frontend-fsharp"
                VocabularyVersion = "1.1"
                IrVersions = [ "1"; "2"; "3"; "4" ]
                InputKinds = [ "files"; "directory"; "project file" ]
            }

            let cases =
                Corpus.read options artifacts
                |> requireOk "The F# frontend suite"

            Expect.all
                cases.Cases
                (fun case -> case.Id.StartsWith "frontend-fsharp-")
                "Only frontend cases are read"
        }
    ]
