module Morphir.Compilation.Tests.CheckedTests

open System
open Expecto
open FsCheck
open Morphir.Compilation
open Morphir.Compilation.Tests.Generators

let private hasError diagnostics =
    diagnostics
    |> List.exists (fun (d: Diagnostic) -> d.IsError)

[<Tests>]
let tests =
    testList "Checked" [
        testProperty "a passed result never holds an error; a failed result always holds one"
        <| Prop.forAll
            (Arb.fromGen checkedInt)
            (fun result ->
                match result with
                | Passed(_, diagnostics) -> not (hasError diagnostics)
                | Failed diagnostics -> hasError diagnostics
            )

        testProperty "let! keeps diagnostics in emission order"
        <| Prop.forAll
            (Arb.fromGen (Gen.two (Gen.listOf nonError)))
            (fun (first, second) ->
                let result = check {
                    let! a = Checked.ofDiagnostics 1 first
                    let! b = Checked.ofDiagnostics 2 second
                    return a + b
                }

                Checked.diagnostics result = first
                                             @ second
                && Checked.tryValue result = Some 3
            )

        testProperty "let! stops at the first failure and keeps the warnings gathered before it"
        <| Prop.forAll
            (Arb.fromGen (
                gen {
                    let! before = Gen.listOf nonError
                    let! failure = error
                    let! after = Gen.listOf diagnostic
                    return before, failure, after
                }
            ))
            (fun (before, failure, after) ->
                let mutable laterStepRan = false

                let result = check {
                    do! Checked.ofDiagnostics () before
                    do! Checked.note failure
                    laterStepRan <- true
                    return! Checked.ofDiagnostics 0 after
                }

                not laterStepRan
                && Checked.diagnostics result = before
                                                @ [ failure ]
                && not result.IsPassed
            )

        testProperty "and! collects every diagnostic from both branches, left first"
        <| Prop.forAll
            (Arb.fromGen (Gen.two checkedInt))
            (fun (left, right) ->
                let result = check {
                    let! a = left
                    and! b = right
                    return a + b
                }

                Checked.diagnostics result = Checked.diagnostics left
                                             @ Checked.diagnostics right
                && result.IsPassed = (left.IsPassed
                                      && right.IsPassed)
            )

        testProperty "traverse collects every diagnostic from every item, in item order"
        <| Prop.forAll
            (Arb.fromGen (Gen.listOf checkedInt))
            (fun results ->
                let combined = Checked.traverse id results

                Checked.diagnostics combined = (results
                                                |> List.collect Checked.diagnostics)
                && combined.IsPassed = (results
                                        |> List.forall (fun x -> x.IsPassed))
                && (not combined.IsPassed
                    || Checked.tryValue combined = Some(
                        results
                        |> List.choose Checked.tryValue
                    ))
            )

        test "a note that is not an error passes" {
            let result =
                Checked.note (Diagnostic.warning (DiagnosticCode.Create "test.warning") "careful")

            Expect.isTrue result.IsPassed "passed"
            Expect.equal result.Diagnostics.Count 1 "kept"
        }

        test "a failed result needs an error" {
            Expect.throwsT<ArgumentException>
                (fun () ->
                    Checked.fail [
                        Diagnostic.warning (DiagnosticCode.Create "test.warning") "only a warning"
                    ]
                    |> ignore
                )
                "no error"
        }

        test "C# members agree with the F# module" {
            let passed = Checked<int>.Pass 7
            let mutable value = 0
            Expect.isTrue (passed.TryGetValue &value) "has a value"
            Expect.equal value 7 "value"

            let failed =
                Checked<int>.Fail [ Diagnostic.error (DiagnosticCode.Create "test.error") "broken" ]

            Expect.isFalse (failed.TryGetValue &value) "no value"

            let described =
                failed.Match(
                    Func<_, _, _>(fun v _ -> $"passed {v}"),
                    Func<_, _>(fun ds -> $"failed {ds.Count}")
                )

            Expect.equal described "failed 1" "match"
        }
    ]
