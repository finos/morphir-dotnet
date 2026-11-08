// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.PlanningTests

open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Frontend.FSharp.Analysis
open Morphir.Frontend.FSharp.Tests.AnalysisSupport
open Morphir.IR

let private carRental =
    match CanonicalNames.parsePath "acme/car-rental" with
    | Ok path -> PackageName path
    | Error message -> failwith message

let private planWith exposed documents =
    let evidence = passed (analyze documents)

    Planning.plan
        {
            Package = carRental
            ExposedModules = exposed
        }
        evidence

let private plan documents = planWith None documents

let private source uri (lines: string list) =
    document
        uri
        ((String.concat "\n" lines)
         + "\n")

let private words (name: Name) = Name.toList name

let private modulePath (planned: PlannedModule) =
    let (ModulePath path) = planned.Plan.Path

    Path.toList path
    |> List.map Name.toList

let private find (result: FrontendPlan) path =
    result.Modules
    |> List.find (fun planned -> modulePath planned = path)

[<Tests>]
let tests =
    testList "F# planning" [
        test "names split into Morphir words and the package path is removed from module paths" {
            let result =
                passed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let dailyRateFor (days: int) = days"
                        ]
                    ]
                )

            let (PackageName packagePath) = result.Package.Package

            Expect.equal
                (Path.toList packagePath
                 |> List.map Name.toList)
                [ [ "acme" ]; [ "car"; "rental" ] ]
                "package"

            let pricing = List.exactlyOne result.Modules
            Expect.equal (modulePath pricing) [ [ "pricing" ] ] "module path"
            let value = List.exactlyOne pricing.Values
            Expect.equal (words value.Name) [ "daily"; "rate"; "for" ] "value name"

            let fqName =
                result.TryValue value.Symbol
                |> Option.get

            Expect.equal (Name.toList fqName.LocalName) [ "daily"; "rate"; "for" ] "the names table"
        }

        test "a module outside the package path is refused at its declaration" {
            let text = "module Other.Pricing\n\nlet rate = 1\n"
            let diagnostics = failed (plan [ document "Pricing.fs" text ])
            let refusal = List.exactlyOne diagnostics
            Expect.equal (codeOf refusal) "fsharp.module_outside_package" "code"
            Expect.equal refusal.LocationOrNull.Range.Start.Line 0 "at the declaration"
            Expect.equal refusal.LocationOrNull.Range.Start.Character 7 "the module name"
            Expect.equal refusal.LocationOrNull.Range.End.Character 20 "Other.Pricing"
        }

        test "declarations directly in the package namespace have no module path" {
            let diagnostics =
                failed (
                    plan [ source "Types.fs" [ "namespace Acme.CarRental"; "type Plate = string" ] ]
                )

            Expect.equal
                (diagnostics
                 |> List.map codeOf)
                [ "fsharp.module_outside_package" ]
                "code"
        }

        test "colliding names fail with a related location for the first declaration" {
            let diagnostics =
                failed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let taxRate = 1"
                            "    let TaxRate = 2"
                        ]
                    ]
                )

            let collision = List.exactlyOne diagnostics
            Expect.equal (codeOf collision) "fsharp.name_collision" "code"
            Expect.equal collision.LocationOrNull.Range.Start.Line 3 "at the second declaration"
            let related = List.exactlyOne (List.ofSeq collision.Related)
            Expect.equal related.Location.Range.Start.Line 2 "the first declaration"
        }

        test "constructors and record fields collide in their own scopes" {
            let diagnostics =
                failed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    type Status = Rented | Returned"
                            "    type Event = Rented | Lost"
                            "    type Car = { plateNumber: string; PlateNumber: string }"
                        ]
                    ]
                )

            Expect.equal
                (diagnostics
                 |> List.map (fun diagnostic ->
                     codeOf diagnostic, diagnostic.LocationOrNull.Range.Start.Line
                 ))
                [ "fsharp.name_collision", 3; "fsharp.name_collision", 4 ]
                "the constructor, then the field"
        }

        test "selected exposure makes other modules and private values private" {
            let result =
                passed (
                    planWith (Some [ "Pricing" ]) [
                        source "Rates.fs" [
                            "namespace Acme.CarRental"
                            "module Rates ="
                            "    let daily = 42"
                        ]
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let price (days: int) = days * Rates.daily"
                            "    let private secret = 1"
                        ]
                    ]
                )

            Expect.equal (find result [ [ "pricing" ] ]).Plan.Access Access.Public "exposed"
            Expect.equal (find result [ [ "rates" ] ]).Plan.Access Access.Private "not exposed"

            Expect.equal
                ((find result [ [ "pricing" ] ]).Values
                 |> List.map (fun value -> words value.Name, value.Access))
                [ [ "price" ], Access.Public; [ "secret" ], Access.Private ]
                "value access"
        }

        test "no list exposes every module and an empty list exposes none" {
            let input = [
                source "Pricing.fs" [
                    "namespace Acme.CarRental"
                    "module Pricing ="
                    "    let one = 1"
                ]
            ]

            Expect.equal
                (passed (planWith None input)).Modules.Head.Plan.Access
                Access.Public
                "absent list"

            Expect.equal
                (passed (planWith (Some []) input)).Modules.Head.Plan.Access
                Access.Private
                "empty list"
        }

        test "an exposed module that matches no compiled module is an error" {
            let diagnostics =
                failed (
                    planWith (Some [ "Pricing"; "Billing" ]) [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let one = 1"
                        ]
                    ]
                )

            let unknown = List.exactlyOne diagnostics
            Expect.equal (codeOf unknown) "compile.unknown_exposed_module" "code"
            Expect.stringContains unknown.Message "Billing" "names the module"
        }

        test "a private F# module is private even when exposed" {
            let result =
                passed (
                    planWith (Some [ "Helpers" ]) [
                        source "Helpers.fs" [
                            "namespace Acme.CarRental"
                            "module private Helpers ="
                            "    let one = 1"
                        ]
                    ]
                )

            Expect.equal result.Modules.Head.Plan.Access Access.Private "private module"
        }

        test "nested modules have nested paths, in document and source order" {
            let result =
                passed (
                    plan [
                        source "Rates.fs" [
                            "namespace Acme.CarRental"
                            "module Rates ="
                            "    let daily = 1"
                        ]
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    type Plan = Basic | Premium"
                            "    let first = 1"
                            "    module Discounts ="
                            "        let weekly = 2"
                            "    let second = 3"
                        ]
                    ]
                )

            Expect.equal
                (result.Modules
                 |> List.map modulePath)
                [ [ [ "rates" ] ]; [ [ "pricing" ] ]; [ [ "pricing" ]; [ "discounts" ] ] ]
                "module order"

            Expect.equal
                ((find result [ [ "pricing" ] ]).Values
                 |> List.map (fun value -> words value.Name))
                [ [ "first" ]; [ "second" ] ]
                "value order"

            let plan = find result [ [ "pricing" ] ]
            let planType = List.exactlyOne plan.Types

            Expect.equal
                (planType.Entity.UnionCases
                 |> Seq.map (fun case ->
                     result.TryConstructor case
                     |> Option.map (fun name -> Name.toList name.LocalName)
                 )
                 |> List.ofSeq)
                [ Some [ "basic" ]; Some [ "premium" ] ]
                "constructor names"
        }

        test "namespace-level declarations of one namespace in two documents form one module" {
            let result =
                passed (
                    plan [
                        source "Plates.fs" [
                            "namespace Acme.CarRental.Shared"
                            "type Plate = string"
                        ]
                        source "Seats.fs" [ "namespace Acme.CarRental.Shared"; "type Seats = int" ]
                    ]
                )

            let shared = List.exactlyOne result.Modules
            Expect.equal (modulePath shared) [ [ "shared" ] ] "one module"

            Expect.equal
                (shared.Types
                 |> List.map (fun planned -> words planned.Name))
                [ [ "plate" ]; [ "seats" ] ]
                "both types"

            for planned in shared.Types do
                Expect.isSome (result.TryType planned.Entity) "each type has a Morphir name"
        }

        test "an operator definition gives no Morphir name" {
            let diagnostics =
                failed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let (+++) a b = a + b + 1"
                        ]
                    ]
                )

            Expect.equal
                (diagnostics
                 |> List.map codeOf)
                [ "fsharp.unsupported_name" ]
                "code"
        }

        test "a module-level tuple binding is refused" {
            let diagnostics =
                failed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let a, b = 1, 2"
                        ]
                    ]
                )

            Expect.equal
                (diagnostics
                 |> List.map codeOf)
                [ "fsharp.unsupported_declaration" ]
                "code"
        }

        test "documentation comments become documentation" {
            let result =
                passed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "/// Prices for rentals."
                            "module Pricing ="
                            "    /// A car's plate."
                            "    type Plate = string"
                            "    /// The daily rate."
                            "    let daily = 42"
                        ]
                    ]
                )

            let pricing = List.exactlyOne result.Modules
            Expect.equal pricing.Plan.Documentation (Some "Prices for rentals.") "module"
            Expect.equal pricing.Types.Head.Documentation (Some "A car's plate.") "type"
            Expect.equal pricing.Values.Head.Documentation (Some "The daily rate.") "value"
        }

        test "planning reads symbols through the analysis copy when the program has matches" {
            let result =
                passed (
                    plan [
                        source "Pricing.fs" [
                            "namespace Acme.CarRental"
                            "module Pricing ="
                            "    let describe (n: int) = match n with | 0 -> \"zero\" | _ -> \"other\""
                            "    let total = 1"
                        ]
                    ]
                )

            Expect.equal
                (result.Modules.Head.Values
                 |> List.map (fun value -> words value.Name))
                [ [ "describe" ]; [ "total" ] ]
                "both values"

            for value in result.Modules.Head.Values do
                Expect.isSome (result.TryValue value.Symbol) "each value has a Morphir name"
        }
    ]
