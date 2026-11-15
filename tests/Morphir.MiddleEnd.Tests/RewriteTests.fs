// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.MiddleEnd.Tests.RewriteTests

open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.MiddleEnd
open Morphir.MiddleEnd.Tests.ModelSupport

let private source (line: int) : ValueAttributes =
    { va with
        Source = Some { StartLine = line; StartColumn = 1; EndLine = line; EndColumn = 9 } }

let private price = Value.Variable(va, name "price")
let private identity = Value.Lambda(va, AsPattern(va, WildcardPattern va, name "x"), Value.Variable(va, name "x"))
let private applyIdentity (attributes: ValueAttributes) (argument: Value) = Value.Apply(attributes, identity, argument)

let private emptyView =
    { new IPackageView with
        member _.ModulePaths = []
        member _.TryModule _ = None
        member _.TryValue(_, _) = None }

let private context = { Module = pathOf "orders/pricing"; Package = emptyView }

/// The rule of the documentation: an identity lambda applied to an argument becomes the argument.
let private identityRule : RewriteRule =
    {
        Id = RuleId.Create "https://morphir.finos.org/rules/identity-lambda"
        Title = "An identity lambda becomes its argument"
        Example = Some { Before = applyIdentity va price; After = price }
        Removes = Set.ofList [ IrFeature.Value ValueKind.Lambda; IrFeature.Value ValueKind.Apply ]
        Adds = Set.empty
        Apply =
            fun _ value ->
                match value with
                | Value.Apply(_, Value.Lambda(_, AsPattern(_, WildcardPattern _, bound), Value.Variable(_, used)), argument) when bound = used ->
                    Some argument
                | _ -> None
    }

let private variableRule (id: string) (from: string) (target: string) : RewriteRule =
    {
        Id = RuleId.Create $"https://example.org/rules/{id}"
        Title = $"{from} to {target}"
        Example = Some { Before = Value.Variable(va, name from); After = Value.Variable(va, name target) }
        Removes = Set.empty
        Adds = Set.empty
        Apply =
            fun _ value ->
                match value with
                | Value.Variable(_, n) when n = name from -> Some(Value.Variable(va, name target))
                | _ -> None
    }

let private valueRoot = NodeRoot.Value(NodeOwner.OwnPackage, pathOf "orders/pricing", name "net")

let private rewrite strategy rules value =
    RuleEngine.rewriteValue strategy rules context valueRoot [ NodeStep.Body ] value

let private errorText (result: Checked<_>) =
    Checked.diagnostics result |> Seq.map (fun d -> d.Message) |> String.concat "\n"

[<Tests>]
let tests =
    testList "Rewrite engine" [
        test "an example runs: the rule changes the example before to the example after" {
            Expect.isTrue (RuleCatalog.check [ identityRule ]).IsPassed "catalog passes"
        }

        test "a rule without an example fails the catalog check and the message names the rule" {
            let result = RuleCatalog.check [ { identityRule with Example = None } ]
            Expect.isFalse result.IsPassed "catalog fails"
            Expect.stringContains (errorText result) "identity-lambda" "names the rule"
            Expect.equal (Checked.diagnostics result |> Seq.head |> _.CodeOrNull) (RewriteCodes.ruleWithoutExample) "code"
        }

        test "an example that the rule does not turn into its after-model fails the catalog check" {
            let wrong = { identityRule with Example = Some { Before = applyIdentity va price; After = Value.Unit va } }
            let result = RuleCatalog.check [ wrong ]
            Expect.isFalse result.IsPassed "catalog fails"
            Expect.equal (Checked.diagnostics result |> Seq.head |> _.CodeOrNull) RewriteCodes.exampleMismatch "code"
        }

        test "a feature claim that the example does not show fails the catalog check" {
            let result = RuleCatalog.check [ { identityRule with Removes = Set.singleton (IrFeature.Value ValueKind.LetRecursion) } ]
            Expect.isFalse result.IsPassed "catalog fails"
            Expect.stringContains (errorText result) "value.let-recursion" "names the feature"
        }

        test "two rules with one id fail the catalog check" {
            let result = RuleCatalog.check [ identityRule; identityRule ]
            Expect.isFalse result.IsPassed "catalog fails"
            Expect.equal (Checked.diagnostics result |> Seq.head |> _.CodeOrNull) RewriteCodes.duplicateRule "code"
        }

        test "a rule id is an absolute IRI" {
            Expect.isError (RuleId.Parse "identity-lambda") "no scheme"
        }

        test "bottom-up rewrites an inner node and the application has its place" {
            let body = Value.Tuple(va, [ price; applyIdentity va price ])

            match rewrite Strategy.BottomUp [ identityRule ] body |> Checked.tryValue with
            | Some(result, applications) ->
                Expect.equal result (Value.Tuple(va, [ price; price ])) "rewritten"
                Expect.equal applications.Length 1 "one application"
                Expect.equal applications.Head.Before.Steps [ NodeStep.Body; NodeStep.TupleElement 1 ] "place"
                Expect.equal applications.Head.After applications.Head.Before "replaced in place"
            | None -> failtest "rewrite failed"
        }

        test "top-down meets the parent before its children" {
            let order = System.Collections.Generic.List<string>()

            let spy (label: string) : RewriteRule =
                { identityRule with
                    Id = RuleId.Create $"https://example.org/rules/{label}"
                    Example = None
                    Apply =
                        fun _ value ->
                            match value with
                            | Value.Tuple _ -> order.Add "tuple"; None
                            | Value.Variable _ -> order.Add "variable"; None
                            | _ -> None }

            rewrite Strategy.TopDown [ spy "top" ] (Value.Tuple(va, [ price ])) |> ignore
            Expect.equal (List.ofSeq order) [ "tuple"; "variable" ] "parent first"
            order.Clear()
            rewrite Strategy.BottomUp [ spy "bottom" ] (Value.Tuple(va, [ price ])) |> ignore
            Expect.equal (List.ofSeq order) [ "variable"; "tuple" ] "children first"
        }

        test "a replacement keeps the attributes of the node that it replaces" {
            let attributes = source 7
            let body = applyIdentity attributes price

            match rewrite Strategy.BottomUp [ identityRule ] body |> Checked.tryValue with
            | Some(result, applications) ->
                Expect.equal (ValueTraversal.attributesOf result) attributes "the source of the user stays"
                Expect.equal applications.Head.Origin attributes.Source "the record has the source location"
            | None -> failtest "rewrite failed"
        }

        test "a fixed point applies a rule until nothing changes" {
            let nested = applyIdentity va (applyIdentity va price)
            let limit = RoundLimit.Create 5

            match rewrite (Strategy.FixedPoint limit) [ identityRule ] nested |> Checked.tryValue with
            | Some(result, _) -> Expect.equal result price "both lambdas are gone"
            | None -> failtest "rewrite failed"
        }

        test "two rules that undo each other do not settle: an error names both rules and gives no model" {
            let a = variableRule "a-to-b" "a" "b"
            let b = variableRule "b-to-a" "b" "a"
            let result = rewrite (Strategy.FixedPoint(RoundLimit.Create 3)) [ a; b ] (Value.Variable(va, name "a"))

            Expect.isFalse result.IsPassed "no model"
            Expect.equal (Checked.diagnostics result |> Seq.head |> _.CodeOrNull) RewriteCodes.notSettled "code"
            Expect.stringContains (errorText result) "a-to-b" "names the first rule"
            Expect.stringContains (errorText result) "b-to-a" "names the second rule"
        }

        test "a round limit is at least one" {
            Expect.isNone (RoundLimit.TryCreate 0) "zero"
            Expect.isSome (RoundLimit.TryCreate 1) "one"
        }

        test "a second pass keeps the first origin" {
            let source1 = source 4
            let a = variableRule "a-to-b" "a" "b"
            let b = variableRule "b-to-c" "b" "c"
            let body = Value.Tuple(va, [ Value.Variable(source1, name "a") ])

            let first = rewrite Strategy.BottomUp [ a ] body |> Checked.tryValue |> Option.get
            let second = rewrite Strategy.BottomUp [ b ] (fst first) |> Checked.tryValue |> Option.get
            let composed = PassApplications.compose (snd first) (snd second)

            Expect.equal composed.Length 1 "one record for the node"
            Expect.equal composed.Head.Rule b.Id "the last rule"
            Expect.equal composed.Head.Before (snd first).Head.Before "the origin is the first match"
            Expect.equal composed.Head.Origin source1.Source "the first source location"
            Expect.equal (fst second) (Value.Tuple(va, [ Value.Variable(source1, name "c") ])) "the attributes survived both passes"
        }

        test "an earlier record below a node that a later pass replaces is dropped" {
            let inner = variableRule "a-to-b" "a" "b"

            let outer: RewriteRule =
                { identityRule with
                    Id = RuleId.Create "https://example.org/rules/tuple-to-unit"
                    Example = None
                    Apply = fun _ value -> match value with Value.Tuple _ -> Some(Value.Unit va) | _ -> None }

            let body = Value.Tuple(va, [ Value.Variable(va, name "a") ])
            let first = rewrite Strategy.BottomUp [ inner ] body |> Checked.tryValue |> Option.get
            let second = rewrite Strategy.BottomUp [ outer ] (fst first) |> Checked.tryValue |> Option.get
            let composed = PassApplications.compose (snd first) (snd second)
            Expect.equal (composed |> List.map (fun record -> record.Rule)) [ outer.Id ] "only the outer record"
        }

        test "a module is rewritten value by value and the applications name the value root" {
            let definition =
                moduleOf
                    []
                    [ "net", expression [ "price", intType ] intType (applyIdentity va price)
                      "gross", expression [ "price", intType ] intType price ]

            let view = RecordingPackageView({ Modules = orderedModules [ ModulePath(pathOf "orders/pricing"), { Access = Public; Value = definition } ] }, pathOf "orders/pricing")

            match
                RuleEngine.rewriteModule Strategy.BottomUp [ identityRule ] view (pathOf "orders/pricing") definition
                |> Checked.tryValue
            with
            | Some(rewritten, applications) ->
                let body (n: string) =
                    match (OrderedMap.tryFind (name n) rewritten.Values |> Option.get).Value.Value.Body with
                    | ExpressionBody(_, value) -> value
                    | _ -> failtest "expression body expected"

                Expect.equal (body "net") price "net rewritten"
                Expect.equal (body "gross") price "gross untouched"
                Expect.equal applications.Length 1 "one application"
                Expect.equal applications.Head.Before.Root valueRoot "the root is the value net"
            | None -> failtest "rewrite failed"
        }

        test "traversal visits the body of a let definition" {
            let inner = expression [] intType (applyIdentity va price)

            let body =
                Value.LetDefinition(va, name "tmp", { Doc = None; Value = inner }, Value.Variable(va, name "tmp"))

            match rewrite Strategy.BottomUp [ identityRule ] body |> Checked.tryValue with
            | Some(_, applications) ->
                Expect.equal applications.Head.Before.Steps [ NodeStep.Body; NodeStep.LetDefinition(name "tmp"); NodeStep.Body ] "steps"
            | None -> failtest "rewrite failed"
        }

        test "features of a value include the patterns and the nested bodies" {
            let features = ValueTraversal.features (applyIdentity va price)
            Expect.isTrue (features.Contains(IrFeature.Value ValueKind.Lambda)) "lambda"
            Expect.isTrue (features.Contains(IrFeature.Pattern PatternKind.As)) "as pattern"
            Expect.equal (IrFeature.Value ValueKind.LetRecursion).Text "value.let-recursion" "text"
        }

        test "a current model is built from a current-IR distribution only" {
            let distribution = library [ "orders/pricing", moduleOf [] [] ]
            let model = CurrentModel.ofDistribution distribution
            Expect.equal model.Distribution distribution "the distribution"
            Expect.isTrue (CurrentModel.tryOf model.Model).IsSome "round trip"

            let classic = { model.Model with SourceVersion = Morphir.IR.Model.SourceFormatVersion.Create "3" }
            Expect.isTrue (CurrentModel.tryOf classic).IsNone "an older version is not current"
        }
    ]
