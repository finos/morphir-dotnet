// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance

open System.Threading
open System.Text.Json

module LibraryAdapter =
    let create identity observe : Adapter = {
        Name = "library"
        Identity = identity
        Observe = observe
    }

module Runner =
    let private compare (expected: ExpectedOutcome) (actual: CompileObservation) =
        if
            expected.Success
            <> actual.Success
        then
            Failed "Compilation success differs from the expectation"
        elif
            not actual.Success
            && actual.ModelJson.IsSome
        then
            Failed "A failed compilation returned a model"
        elif
            actual.Success
            && actual.ModelJson.IsNone
        then
            Failed "A successful compilation did not return a model"
        elif
            actual.Diagnostics
            |> List.exists (fun diagnostic ->
                not (List.contains diagnostic.Severity [ "error"; "warning"; "info"; "hint" ])
            )
        then
            Failed "An observation contains an invalid diagnostic severity"
        else
            let relevant =
                if expected.AllowNonErrorDiagnostics then
                    actual.Diagnostics
                    |> List.filter (fun diagnostic -> diagnostic.Severity = "error")
                else
                    actual.Diagnostics

            if
                expected.Diagnostics
                <> relevant
            then
                Failed "Diagnostic severity, code or source range differs from the expectation"
            else
                match expected.ModelJson, actual.ModelJson with
                | Some _, None -> Failed "Expected an IR model, but none was returned"
                | Some expected, Some actual ->
                    try
                        match JsonComparison.firstDifference expected actual with
                        | Some difference -> Failed difference
                        | None -> Passed
                    with :? JsonException as error ->
                        Failed(
                            "Invalid JSON model: "
                            + error.Message
                        )
                | None, Some actual when expected.Success ->
                    try
                        use document = JsonDocument.Parse actual
                        Passed
                    with :? JsonException as error ->
                        Failed(
                            "Invalid JSON model: "
                            + error.Message
                        )
                | None, _ -> Passed

    let run (suite: CaseSet) (adapter: Adapter) (cancellationToken: CancellationToken) = task {
        let results = ResizeArray<CaseResult>()

        for case in suite.Cases do
            cancellationToken.ThrowIfCancellationRequested()

            match case.Exclusion with
            | Some reason ->
                results.Add
                    {
                        Case = case
                        Observation = None
                        Verdict = Excluded reason
                    }
            | None ->
                let! observation = adapter.Observe case.Input cancellationToken

                match observation with
                | Error error ->
                    results.Add
                        {
                            Case = case
                            Observation = error.Partial
                            Verdict = Failed error.Message
                        }
                | Ok actual ->
                    results.Add
                        {
                            Case = case
                            Observation = Some actual
                            Verdict = compare case.Expected actual
                        }

        let results = Seq.toList results
        let runner = typeof<CaseSet>.Assembly.GetName()

        return {
            RunnerName = runner.Name
            RunnerVersion = runner.Version.ToString()
            CaseSetSha256 = suite.Sha256
            Implementation = adapter.Identity
            Adapter = adapter.Name
            Results = results
            Qualified =
                (results
                 |> List.exists (fun result -> result.Verdict = Passed))
                && (results
                    |> List.forall (fun result ->
                        match result.Verdict with
                        | Failed _ -> false
                        | _ -> true
                    ))
        }
    }
