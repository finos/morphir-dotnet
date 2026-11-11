// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Behavioral RED for parent SEL-1/TIER-1, using the existing runner and a recording test peer.
module Morphir.Conformance.Tests.GuestKitPlanTests

open System
open System.Text
open Expecto
open Morphir.Conformance.GuestKit
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.GuestKitFakeGuest
open Morphir.Conformance.Tests.GuestKitPlanFixture

// JSON object member order is immaterial. Array order, id tokens and optional-member presence stay exact.
let rec private objectOrder value =
    match value with
    | JsonValue.Object members ->
        members
        |> List.map (fun (name, item) -> name, objectOrder item)
        |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
        |> JsonValue.Object
    | JsonValue.Array values -> values |> List.map objectOrder |> JsonValue.Array
    | other -> other

let private assertWire expected (trace: RunTrace) =
    let actual = trace.Exchanges |> List.map (message >> objectOrder)
    Expect.equal actual (expected |> List.map objectOrder) "complete wire messages, ids, methods, params and notification positions"

let private caseShape (id, method) =
    let notification = List.contains id [ "model-initialized-silent"; "unknown-notification-silent"; "model-exit" ]
    id, method, (if notification then 0 else 1), (if notification then 1 else 0)

[<Tests>]
let pureSelectionTests =
    testList "Generic guest kit pure selection adoption (task 2.2)" [
        for vector in selectionVectors do
            test vector.Name {
                match ModelTierPlan.checkDescribe kit.Value vector.Claim with
                | Error reason ->
                    Expect.isFalse vector.DescribeSucceeds $"the fixed claim was refused: {reason}"
                | Ok selection ->
                    Expect.isTrue vector.DescribeSucceeds "the fixed negative claim must be refused"
                    Expect.equal selection.Language.Id "python" "shared frontend language and backend target"
                    Expect.equal (selection.Versions |> List.map _.WireValue) vector.Versions "no unusable broad version"
                    Expect.equal selection.ModelVersions selection.Versions "one selected version set"
                    let offered = ModelTierPlan.offer selection |> at [ "tuples" ] |> objectOrder
                    let expected =
                        vector.Versions
                        |> List.collect (fun version ->
                            [ tuple "morphir.frontend.compileModel" version; tuple "morphir.backend.generateModel" version ])
                        |> arr
                        |> objectOrder
                    Expect.equal offered expected "only complete selected JSON tuple pairs, in IR 3 then IR 4 order"
            }

        for side, path in [ "frontend", [ "capabilities"; "frontend"; "languages" ]; "backend", [ "capabilities"; "backend"; "targets" ] ] do
            test $"Missing {side} language support refuses otherwise usable tuples" {
                let claim = claimBoth |> replaceAt path (arr [])
                match ModelTierPlan.checkDescribe kit.Value claim with
                | Error reason -> Expect.stringContains reason "language" "language intersection refusal"
                | Ok _ -> failtest "tuple pairs cannot replace shared language support"
            }

        for name, claim, expected in [ "Both versions", claimBoth, bothCases; "IR 3", claimThree, threeCases; "IR 4", claimFour, fourCases ] do
            test $"{name} has the complete ordered required plan data" {
                match ModelTierPlan.checkDescribe kit.Value claim with
                | Error reason -> failtest reason
                | Ok selection ->
                    Expect.equal (ModelTierPlan.requiredCases selection) expected "independent fixed case ids and methods"
            }
    ]

[<Tests>]
let selectionTests =
    testList "Generic guest kit selection adoption (task 2.2)" [
        for vector in selectionVectors do
            test vector.Name {
                let trace = runClaim vector.Claim
                let describe = trace.Result.RequiredStep "describe"
                if vector.DescribeSucceeds then
                    Expect.equal describe.Outcome StepOutcome.Passed "the fixed model claim must pass describe"
                else
                    match describe.Outcome with
                    | StepOutcome.Failed _ -> ()
                    | other -> failtest $"describe must fail this fixed claim; observed {other}"

                Expect.sequenceEqual trace.Result.IrVersions vector.Versions "selected versions require both claims and both tuples"
                Expect.sequenceEqual trace.Result.ModelIrVersions vector.Versions "the required session has the same selected versions"
                let actual = trace.Result.Steps |> Seq.map (fun step -> step.Id, step.Method, step.Requests, step.Notifications) |> Seq.toList
                Expect.equal actual (vector.Cases |> List.map caseShape) "fixed ordered case ids, methods and request/notification counts"
                Expect.isEmpty trace.Result.LegacyTier "no legacy-tier cases remain in the adopted plan"
            }
    ]

[<Tests>]
let transcriptTests =
    testList "Generic guest kit transcript adoption (task 2.2)" [
        test "One factory opening carries describe through exit" {
            let trace = runClaim claimBoth
            Expect.equal trace.Sessions 1 "the factory represents one session, not a describe process followed by a model process"
            Expect.isTrue (trace.Exchanges |> List.forall (fun exchange -> exchange.Session = 1)) "all observed messages use that session"
        }

        test "Both versions send the complete fixed 21-message transcript" {
            runClaim claimBoth |> assertWire (expectedWire bothCases (threeTuples @ fourTuples) "3.0.0")
        }

        test "IR 3 sends the complete fixed 16-message transcript" {
            runClaim claimThree |> assertWire (expectedWire threeCases threeTuples "3.0.0")
        }

        test "IR 4 sends the complete fixed 16-message transcript" {
            runClaim claimFour |> assertWire (expectedWire fourCases fourTuples "4.0.0")
        }

        test "Request ids are numeric 1 through 18 without a second-session reset" {
            let trace = runClaim claimBoth
            let ids = trace.Exchanges |> List.choose (message >> memberOf "id")
            Expect.equal ids ([ 1 .. 18 ] |> List.map num) "notifications have no id; eighteen requests share one numeric sequence"
        }

        test "A dual-protocol peer is offered only MEP 0.2 in describe and initialize" {
            let trace = runClaim claimDualProtocol
            let negotiations =
                trace.Exchanges |> List.map message
                |> List.filter (fun packet -> List.contains (at [ "method" ] packet) [ str "morphir.extension.describe"; str "morphir.initialize" ])
            Expect.isGreaterThanOrEqual negotiations.Length 2 "both describe and initialize must reach the scripted peer"
            for packet in negotiations do
                Expect.equal (at [ "params"; "protocolVersions" ] packet) (arr [ str "0.2.0-draft.1" ]) "no legacy offer on the wire"
        }

        test "Initialize offers only the selected IR 4 compile and generate pair" {
            let trace = runClaim claimPairOnlyFour
            let initializations = trace.Exchanges |> List.map message |> List.filter (fun packet -> at [ "method" ] packet = str "morphir.initialize")
            Expect.equal initializations.Length 1 "one initialize request"
            let actual = initializations.Head |> at [ "params"; "modelTransport"; "tuples" ] |> objectOrder
            Expect.equal actual (arr fourTuples |> objectOrder) "exact selected tuple pair and operation order"
        }

        test "Post-shutdown refusal is observed in an actual scripted-peer reply" {
            let trace = runClaim claimBoth
            let pings = trace.Exchanges |> List.filter (fun exchange -> at [ "method" ] (message exchange) = str "morphir.ping")
            Expect.equal pings.Length 1 "a local -32014 is insufficient: the post-shutdown request must reach the peer"
            match pings.Head.ReplyBytes with
            | Some bytes -> Expect.equal (parse (Encoding.UTF8.GetString bytes) |> at [ "error"; "code" ]) (num -32014) "the peer, not HostSession, returned the lifecycle refusal"
            | None -> failtest "no peer reply was observed"
        }
    ]
