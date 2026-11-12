// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Behavioral RED for parent SEL-1/TIER-1, using the existing runner and a recording test peer.
module Morphir.Conformance.Tests.GuestKitPlanTests

open System
open System.Text
open System.Threading.Tasks
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

[<Tests>]
let lifecycleEvidenceTests =
    testList "Generic guest kit lifecycle evidence (task 3.2)" [
        test "A previously described factory session cannot reuse another operation's receipt" {
            let prepare (session: Morphir.Mep.HostSession) cancellation =
                task {
                    let! _ = session.DescribeAsync cancellation
                    return ()
                } :> Task
            let trace = runClaimPrepared prepare (fun _ response -> response) claimBoth
            Expect.equal (trace.Result.RequiredStep "describe").Outcome.WireValue "failed" "the runner's first case must own its original winning describe operation and id 1"
            Expect.equal trace.Result.Steps.Count 1 "do not bind an earlier receipt and execute a shifted plan"
        }

        test "An undecodable original claim fails describe without inventing a receipt" {
            let claim =
                match claimBoth with
                | JsonValue.Object fields -> obj (("requires", num 1) :: fields)
                | _ -> failtest "Fixed description is an object"
            let trace =
                try runClaim claim
                with error -> failtest $"Expected a recorded describe refusal, not a runner exception: {error.Message}"
            Expect.equal (trace.Result.RequiredStep "describe").Outcome.WireValue "failed" "typed admission cannot be bypassed using raw result fields"
            Expect.equal (trace.Result.Steps |> Seq.map _.Id |> Seq.toList) [ "describe" ] "no selected plan without the original receipt"
            Expect.isFalse (trace.Exchanges |> List.exists (fun exchange -> at [ "method" ] (message exchange) = str "morphir.initialize")) "no initialization after refused description"
        }

        test "Original describe retains received JSON rather than typed reserialization" {
            let claim =
                match claimBoth with
                | JsonValue.Object fields -> obj (("x-mck-opaque", obj [ "proof", str "original-receipt" ]) :: fields)
                | _ -> failtest "Fixed description is an object"
            let trace = runClaim claim
            let response = (trace.Result.RequiredStep "describe").Response
            Expect.isNotNull response "the original peer result was received"
            Expect.equal (parse response |> at [ "x-mck-opaque"; "proof" ]) (str "original-receipt") "preserve original received members"
            Expect.equal (trace.Result.RequiredStep "describe-immutable").Outcome StepOutcome.Passed "compare against that original received result"
        }

        test "Initialize retains the actual correlated result rather than a reconstructed one" {
            let edit request response =
                if memberOf "method" request = Some(str "morphir.initialize") then
                    match at [ "result" ] response with
                    | JsonValue.Object fields -> replaceAt [ "result" ] (obj (("x-mck-opaque", str "actual-initialize") :: fields)) response
                    | _ -> failtest "Fixed initialization is an object"
                else response
            let trace = runClaimWith edit claimBoth
            let response = (trace.Result.RequiredStep "model-initialize").Response
            Expect.isNotNull response "the correlated peer result was received"
            Expect.equal (parse response |> at [ "x-mck-opaque" ]) (str "actual-initialize") "keep observed result members"
        }

        test "A scripted adapter return does not establish either guest notification silence" {
            let trace = runClaim claimBoth
            for id in [ "model-initialized-silent"; "unknown-notification-silent" ] do
                match (trace.Result.RequiredStep id).Outcome with
                | StepOutcome.NotRun reason -> Expect.stringContains reason "delivery" "missing inner delivery remains explicit"
                | other -> failtest $"{id}: a scripted adapter cannot establish actual guest delivery: {other}"
        }

        test "Adapter exit completion does not establish actual process exit and stdout EOF" {
            let trace = runClaim claimBoth
            match (trace.Result.RequiredStep "model-exit").Outcome with
            | StepOutcome.NotRun reason -> Expect.stringContains reason "process" "no process ownership facts were supplied"
            | other -> failtest $"no process was launched by this peer: {other}"
        }

        test "Malformed source refuses an explicitly null model member" {
            let edit request response =
                if memberOf "method" request = Some(str "morphir.frontend.compileModel")
                   && (memberOf "result" response |> Option.bind (memberOf "success")) = Some(JsonValue.Bool false) then
                    match at [ "result" ] response with
                    | JsonValue.Object fields -> replaceAt [ "result" ] (obj (("model", JsonValue.Null) :: fields)) response
                    | _ -> failtest "No malformed result"
                else response
            let trace = runClaimWith edit claimBoth
            Expect.equal (trace.Result.RequiredStep "malformed-source").Outcome.WireValue "failed" "model must be absent, including no null member"
        }

        test "A changed describe result fails the immutable case" {
            let mutable describes = 0
            let edit request response =
                if memberOf "method" request = Some(str "morphir.extension.describe") then
                    describes <- describes + 1
                    if describes = 2 then replaceAt [ "result"; "extension"; "version" ] (str "9.9.9") response else response
                else response
            let trace = runClaimWith edit claimBoth
            Expect.equal (trace.Result.RequiredStep "describe-immutable").Outcome.WireValue "failed" "must equal original same-session describe"
        }

        test "An actual post-shutdown reply with another error code fails" {
            let edit request response =
                if memberOf "method" request = Some(str "morphir.ping") then replaceAt [ "error"; "code" ] (num -32601) response else response
            let trace = runClaimWith edit claimBoth
            Expect.equal (trace.Result.RequiredStep "after-shutdown").Outcome.WireValue "failed" "actual peer error must be exactly -32014"
        }

        test "The unknown request requires exactly peer error -32601" {
            let edit request response =
                if memberOf "method" request = Some(str "morphir.mck.unknown") then replaceAt [ "error"; "code" ] (num -32602) response else response
            let trace = runClaimWith edit claimBoth
            Expect.equal (trace.Result.RequiredStep "unknown-method").Outcome.WireValue "failed" "another peer error is not unknown-method success"
        }

        test "Malformed source cannot pass with only a warning diagnostic" {
            let edit request response =
                if memberOf "method" request = Some(str "morphir.frontend.compileModel")
                   && at [ "result"; "success" ] response = JsonValue.Bool false then
                    replaceAt [ "result"; "diagnostics" ] (arr [ obj [ "severity", str "warning"; "message", str "Not an error" ] ]) response
                else response
            let trace = runClaimWith edit claimBoth
            Expect.equal (trace.Result.RequiredStep "malformed-source").Outcome.WireValue "failed" "an actual error diagnostic is required"
        }
    ]
