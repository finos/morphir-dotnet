// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.TargetPassTests

open System.Numerics
open System.Text.Json
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.MiddleEnd

let private name (text: string) =
    match CanonicalNames.parseName text with
    | Ok parsed -> parsed
    | Error message -> failwith message

let private pathOf (text: string) =
    match CanonicalNames.parsePath text with
    | Ok parsed -> parsed
    | Error message -> failwith message

let private va = ValueAttributes.empty
let private ta = TypeAttributes.empty

let private intType =
    match CanonicalNames.parseFQName "morphir/SDK:basics#int" with
    | Ok fq -> Type.Reference(ta, fq, [])
    | Error message -> failwith message

let private ordered entries =
    match OrderedMap.ofList entries with
    | Ok map -> map
    | Error key -> failwith $"duplicate key {key}"

let private value (body: Value) : AccessControlled<Documented<ValueDefinition>> =
    { Access = Public; Value = { Doc = None; Value = { InputTypes = []; Body = ExpressionBody(intType, body) } } }

let private alias (fields: (string * Type) list) : AccessControlled<Documented<TypeDefinition>> =
    {
        Access = Public
        Value = { Doc = None; Value = TypeAliasDefinition([], Type.Record(ta, [ for n, t in fields -> name n, t ])) }
    }

let private moduleOf types values : ModuleDefinition =
    {
        Types = ordered [ for n, d in types -> name n, d ]
        Values = ordered [ for n, d in values -> name n, d ]
        Doc = None
    }

let private model (modules: (string * ModuleDefinition) list) =
    CurrentModel.ofDistribution (
        Library(
            PackageName(pathOf "acme/orders"),
            OrderedMap.empty,
            { Modules = ordered [ for p, m in modules -> ModulePath(pathOf p), { Access = Public; Value = m } ] }
        )
    )

let private input: PipelineInput =
    {
        Artifact = ArtifactSelector.Workspace "input"
        Format = FormatTriplet.ofVersion CurrentIr.version
        Snapshot =
            match Sha256Digest.Parse("sha256:" + System.String('a', 64)) with
            | Ok digest -> digest
            | Error message -> failwith message
        Upstream = []
    }

let private run (modules: (string * ModuleDefinition) list) =
    PassRunner.run input CSharpTargetPasses.all (model modules)

let private literal = Value.Literal(va, IntegerLiteral(BigInteger 7))

let private clean = moduleOf [] [ "rate", value literal ]

let private firstError (result: Checked<_>) =
    Checked.diagnostics result |> Seq.find (fun diagnostic -> diagnostic.IsError)

[<Tests>]
let tests =
    testList "C# target-side passes" [
        test "the admission pass is the first pass of the stage before the csharp backend" {
            let ids = CSharpTargetPasses.all |> List.map (fun pass -> pass.Id)
            Expect.equal ids [ "morphir-dotnet.csharp.admission" ] "ids"
            Expect.equal CSharpTargetPasses.admission.Stage (PassStage.BeforeBackend "csharp") "stage"
            Expect.equal (PassRunner.select (PassStage.BeforeBackend "csharp") CSharpTargetPasses.all).Length 1 "selected for csharp"
            Expect.isEmpty (PassRunner.select (PassStage.BeforeBackend "fsharp") CSharpTargetPasses.all) "not for fsharp"
        }

        test "a model that fits the backend passes and is not changed" {
            let result = run [ "orders/pricing", clean ]
            Expect.isTrue result.IsPassed "passes"
            let outcome = Checked.tryValue result |> Option.get
            Expect.equal outcome.Model (model [ "orders/pricing", clean ]) "unchanged"
            Expect.isEmpty outcome.Report.Applications "no application"
        }

        test "a value that shares its identity with an emitted type reports name-collision, as the backend did" {
            let colliding = moduleOf [ "rate", alias [ "amount", intType ] ] [ "rate", value literal ]
            let result = run [ "orders/pricing", colliding ]
            Expect.isFalse result.IsPassed "fails"
            let error = firstError result
            Expect.equal (error.CodeOrNull |> string) "name-collision" "the rule of the backend is the code"
            Expect.stringContains error.Message "shares an emitted type and value identity" "the message of the backend"
        }

        test "a module of values below a module of values with the name of a type of that module reports name-collision" {
            // The class Pricing has the record Rules and the nested class Rules of the module pricing/rules.
            let result =
                run [ "pricing", moduleOf [ "rules", alias [ "amount", intType ] ] [ "base", value literal ]; "pricing/rules", clean ]

            Expect.isFalse result.IsPassed "fails before the backend"
            let error = firstError result
            Expect.equal (error.CodeOrNull |> string) "name-collision" "the rule of the backend is the code"
            Expect.stringContains error.Message "the type Rules of the module" "names the type"
            Expect.stringContains error.Message "the class of the module" "names the module"
        }

        test "the location of the backend is kept as data" {
            let colliding = moduleOf [ "rate", alias [ "amount", intType ] ] [ "rate", value literal ]
            let error = firstError (run [ "orders/pricing", colliding ])
            let data = Diagnostic.data error
            Expect.isSome data "has data"
            Expect.isFalse (System.String.IsNullOrEmpty(data.Value.GetProperty("location").GetString())) "a location"
        }

        test "an unbound variable in a value reports unbound-variable, from the lowering check" {
            let broken = moduleOf [] [ "rate", value (Value.Variable(va, name "missing")) ]
            let error = firstError (run [ "orders/pricing", broken ])
            Expect.equal (error.CodeOrNull |> string) "unbound-variable" "code"
        }

        test "the checks run once for a package of several modules and read the others through the view" {
            let result = run [ "orders/pricing", clean; "orders/tax", clean ]
            let outcome = Checked.tryValue result |> Option.get

            Expect.equal
                outcome.Report.Lookups
                [ { Pass = "morphir-dotnet.csharp.admission"
                    Read = { From = pathOf "orders/pricing"; Module = pathOf "orders/tax"; Value = None } } ]
                "the one read of the other module"
        }

        test "a refusal in a later module is found from the unit of the first module" {
            let broken = moduleOf [] [ "rate", value (Value.Variable(va, name "missing")) ]
            let result = run [ "orders/pricing", clean; "orders/tax", broken ]
            Expect.isFalse result.IsPassed "fails"
            Expect.equal ((firstError result).CodeOrNull |> string) "unbound-variable" "code"
            Expect.equal (Checked.diagnostics result |> Seq.filter (fun d -> d.IsError) |> Seq.length) 1 "reported once"
        }
    ]
