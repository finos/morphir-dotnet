// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.PassRunnerTests

open System.Numerics
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd
open Morphir.MiddleEnd.Tests.ModelSupport

let private pricing = pathOf "orders/pricing"
let private tax = pathOf "orders/tax"

let private rateReference = Value.Reference(va, fq "acme/orders:orders/tax#rate")
let private literal (n: int) = Value.Literal(va, IntegerLiteral(BigInteger n))

let private model =
    CurrentModel.ofDistribution (
        library
            [ "orders/pricing",
              moduleOf
                  []
                  [ "net", expression [] intType (Value.Tuple(va, [ rateReference; Value.Variable(va, name "a") ]))
                    "gross", expression [] intType (Value.Variable(va, name "a")) ]
              "orders/tax", moduleOf [] [ "rate", expression [] intType (literal 7) ] ]
    )

let private input: PipelineInput =
    {
        Artifact = artifact
        Format = format
        Snapshot = snapshot
        Upstream = []
    }

/// One record of an upgrade that ran before the passes: the node `gross` of the input, at the same place in the model.
let private upstream: RewriteApplication list =
    let place = NodeRoot.Value(NodeOwner.OwnPackage, pricing, name "gross")
    let address revision = NodeAddress.unchecked artifact format place [ NodeStep.Body ] revision

    [ {
          Rule = RuleId.Create "https://example.org/steps/upgrade"
          Before = address (ArtifactRevision.Pinned snapshot)
          After = address ArtifactRevision.Current
          Origin = None
      } ]

let private ruleOf (id: string) (apply: RewriteContext -> Value -> Value option) : RewriteRule =
    {
        Id = RuleId.Create $"https://example.org/rules/{id}"
        Title = id
        Example = None
        Removes = Set.empty
        Adds = Set.empty
        Apply = apply
    }

let private renameRule (id: string) (from: string) (target: string) =
    ruleOf id (fun _ value ->
        match value with
        | Value.Variable(_, n) when n = name from -> Some(Value.Variable(va, name target))
        | _ -> None)

/// A rule that inlines the value of another module through the package view.
let private inlineRate =
    ruleOf "inline-rate" (fun context value ->
        match value with
        | Value.Reference(_, reference) ->
            let (ModulePath path) = reference.ModulePath

            match context.Package.TryValue(path, reference.LocalName) with
            | Some { Body = ExpressionBody(_, constant) } -> Some constant
            | _ -> None
        | _ -> None)

let private pass (id: string) (stage: PassStage) (rules: RewriteRule list) =
    RewritePass.create id id "A test pass." stage Strategy.BottomUp rules

let private run passes =
    PassRunner.run input passes model |> Checked.tryValue |> Option.get

let private bodyOf (outcome: PassRunOutcome) (path: Path) (valueName: string) =
    match outcome.Model.Distribution with
    | Library(_, _, definition) ->
        let modul = definition.Modules |> OrderedMap.toList |> List.find (fun (ModulePath p, _) -> p = path) |> snd
        match (OrderedMap.tryFind (name valueName) modul.Value.Values |> Option.get).Value.Value.Body with
        | ExpressionBody(_, value) -> value
        | _ -> failtest "expression body expected"
    | _ -> failtest "library expected"

[<Tests>]
let tests =
    testList "Pass runner" [
        test "a stage selects the passes of its target only, in the order given" {
            let a = pass "a" (PassStage.BeforeBackend "csharp") []
            let b = pass "b" (PassStage.BeforeBackend "fsharp") []
            let c = pass "c" (PassStage.BeforeBackend "csharp") []
            let d = pass "d" PassStage.AfterFrontend []

            let ids stage =
                PassRunner.select stage [ a; b; c; d ] |> List.map (fun p -> p.Id)

            Expect.equal (ids (PassStage.BeforeBackend "csharp")) [ "a"; "c" ] "csharp, in order"
            Expect.equal (ids (PassStage.BeforeBackend "fsharp")) [ "b" ] "fsharp"
            Expect.equal (ids PassStage.AfterFrontend) [ "d" ] "after frontend"
            Expect.equal (ids (PassStage.BeforeBackend "csharp")) (ids (PassStage.BeforeBackend "csharp")) "stable"
        }

        test "the report pins Before to the input of the pipeline and After to the output" {
            let outcome = run [ pass "rename" PassStage.AfterFrontend [ renameRule "a-to-b" "a" "b" ] ]
            let records = outcome.Report.Applications
            Expect.equal records.Length 2 "a appears in two values"

            let record = records |> List.find (fun r -> r.Before.Root = NodeRoot.Value(NodeOwner.OwnPackage, pricing, name "gross"))
            let digest = (Morphir.MiddleEnd.Snapshot.ofDistribution outcome.Model.Distribution |> Result.toOption).Value

            Expect.equal record.Before.Revision (ArtifactRevision.Pinned snapshot) "Before is pinned to the input"
            Expect.equal record.After.Revision (ArtifactRevision.Pinned digest) "After is pinned to the output"
            Expect.equal (NodeAddress.toUri record.Before |> fun uri -> uri.Substring(uri.IndexOf '#')) "#/module/orders%2Fpricing/value/gross/body" "the node"
            Expect.equal outcome.Report.Passes [ "rename" ] "the pass is listed"
        }

        test "two passes on one node: the second record names the original node as its origin" {
            let first = pass "first" PassStage.AfterFrontend [ renameRule "a-to-b" "a" "b" ]
            let second = pass "second" PassStage.AfterFrontend [ renameRule "b-to-c" "b" "c" ]
            let outcome = run [ first; second ]
            Expect.equal outcome.Report.Applications.Length 2 "one record for each value"

            for record in outcome.Report.Applications do
                Expect.equal record.Rule (RuleId.Create "https://example.org/rules/b-to-c") "the last rule"
                Expect.equal record.Before.Revision (ArtifactRevision.Pinned snapshot) "the original node of the input"

            Expect.equal (bodyOf outcome pricing "gross") (Value.Variable(va, name "c")) "both passes ran"
        }

        test "a rule that reads another module lists the lookup" {
            let outcome = run [ pass "inline" PassStage.AfterFrontend [ inlineRate ] ]
            Expect.equal (bodyOf outcome pricing "net") (Value.Tuple(va, [ literal 7; Value.Variable(va, name "a") ])) "inlined"

            Expect.equal
                outcome.Report.Lookups
                [ { Pass = "inline"; Read = { From = pricing; Module = tax; Value = Some(name "rate") } } ]
                "the read of the other module"
        }

        test "reads of the own module are not lookups" {
            let selfRead =
                ruleOf "self-read" (fun context value ->
                    match value with
                    | Value.Variable(_, n) when n = name "a" ->
                        context.Package.TryValue(pricing, name "net") |> ignore
                        None
                    | _ -> None)

            let outcome = run [ pass "self" PassStage.AfterFrontend [ selfRead ] ]
            Expect.isEmpty outcome.Report.Lookups "no lookup"
        }

        test "other modules show as before the pass, whatever the order of the modules" {
            let rename = renameRule "rate-to-nine" "never" "never"

            let touchTax =
                ruleOf "tax-to-nine" (fun _ value ->
                    match value with
                    | Value.Literal(_, IntegerLiteral n) when n = BigInteger 7 -> Some(literal 9)
                    | _ -> None)

            let outcome = run [ pass "both" PassStage.AfterFrontend [ touchTax; inlineRate; rename ] ]
            // The pricing module reads the tax module before the pass, so it inlines 7, not 9.
            Expect.equal (bodyOf outcome pricing "net") (Value.Tuple(va, [ literal 7; Value.Variable(va, name "a") ])) "reads the module as before the pass"
            Expect.equal (bodyOf outcome tax "rate") (literal 9) "the tax module changed"
        }

        test "an error from a pass stops the run and gives no model" {
            let failing =
                { new IModelTransform with
                    member _.Id = "failing"
                    member _.Name = "failing"
                    member _.Description = "Fails."
                    member _.Stage = PassStage.AfterFrontend
                    member _.Removes = Set.empty
                    member _.Run(_, _) = Checked.fail [ Diagnostic.error (DiagnosticCode.Create "test.failed") "The pass failed." ] }

            let after = pass "after" PassStage.AfterFrontend [ renameRule "a-to-b" "a" "b" ]
            let result = PassRunner.run input [ failing; after ] model
            Expect.isFalse result.IsPassed "failed"
            Expect.equal (Checked.diagnostics result |> Seq.map (fun d -> d.Message) |> List.ofSeq) [ "The pass failed." ] "the diagnostic"
        }

        test "no pass gives the model and the upstream records unchanged" {
            let outcome = PassRunner.run { input with Upstream = upstream } [] model |> Checked.tryValue |> Option.get
            Expect.equal outcome.Model model "same model"
            Expect.equal outcome.Report.Applications upstream "the upstream records"
            Expect.isEmpty outcome.Report.Passes "no pass ran"
        }

        test "the pipeline keeps the upstream records when no pass is selected" {
            let other = pass "other" (PassStage.BeforeBackend "fsharp") []

            let result =
                PassPipeline.Run(PassStage.BeforeBackend "csharp", [ other ], model.Model, artifact, Versioning.Classic 3, snapshot, upstream)
                |> Checked.tryValue
                |> Option.get

            match result.ReportOrNull with
            | null -> failtest "a report with the upstream records was expected"
            | report ->
                Expect.equal report.Applications upstream "the upstream records"
                Expect.isEmpty report.Passes "no pass ran"
        }

        test "the pipeline keeps the upstream records when the passes are skipped" {
            let classic = { model.Model with SourceVersion = Morphir.IR.Model.SourceFormatVersion.Create "3" }
            let selected = pass "selected" (PassStage.BeforeBackend "csharp") []

            let result =
                PassPipeline.Run(PassStage.BeforeBackend "csharp", [ selected ], classic, artifact, Versioning.Classic 3, snapshot, upstream)
                |> Checked.tryValue
                |> Option.get

            Expect.isNotNull result.SkippedOrNull "the skip note"

            match result.ReportOrNull with
            | null -> failtest "a report with the upstream records was expected"
            | report -> Expect.equal report.Applications upstream "the upstream records"
        }

        test "the pipeline gives no report when no pass is selected and no record came in" {
            let result =
                PassPipeline.Run(PassStage.BeforeBackend "csharp", [], model.Model, artifact, Versioning.Classic 4, snapshot, [])
                |> Checked.tryValue
                |> Option.get

            Expect.isNull result.ReportOrNull "no report"
        }

        test "an extensible record type fails a profile that cannot take it" {
            let openModel =
                CurrentModel.ofDistribution (
                    library
                        [ "orders/pricing",
                          moduleOf
                              [ "row", TypeAliasDefinition([ name "r" ], Type.ExtensibleRecord(ta, name "r", [ name "price", intType ])) ]
                              [] ]
                )

            let profile = { Target = "csharp"; CannotTake = Set.singleton IrFeature.ExtensibleRecordType }
            let result = FeatureOrder.check profile openModel
            Expect.isFalse result.IsPassed "fails"
            Expect.stringContains (Checked.diagnostics result |> Seq.head).Message "type.extensible-record" "the feature"
            Expect.isTrue (FeatureOrder.check profile model).IsPassed "a model with closed records passes"
        }

        test "a feature that the backend cannot take and no pass removed fails and names the feature and the backend" {
            let profile = { Target = "csharp"; CannotTake = Set.singleton (IrFeature.Value ValueKind.Tuple) }
            let result = FeatureOrder.check profile model
            Expect.isFalse result.IsPassed "fails"
            let message = (Checked.diagnostics result |> Seq.head).Message
            Expect.stringContains message "value.tuple" "the feature"
            Expect.stringContains message "csharp" "the backend"
            Expect.stringContains message "orders/pricing" "the module"
        }

        test "a feature that a pass removed does not fail the check" {
            let drop =
                ruleOf "tuple-to-variable" (fun _ value ->
                    match value with
                    | Value.Tuple _ -> Some(Value.Variable(va, name "t"))
                    | _ -> None)

            let outcome = run [ pass "drop" (PassStage.BeforeBackend "csharp") [ drop ] ]
            let profile = { Target = "csharp"; CannotTake = Set.singleton (IrFeature.Value ValueKind.Tuple) }
            Expect.isTrue (FeatureOrder.check profile outcome.Model).IsPassed "passes"
        }
    ]
