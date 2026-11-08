// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphir.Frontend.FSharp.Tests.LoweringSupport

open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.Frontend.FSharp.Analysis
open Morphir.Frontend.FSharp.Tests.AnalysisSupport
open Morphir.IR

let carRental =
    match CanonicalNames.parsePath "acme/car-rental" with
    | Ok path -> PackageName path
    | Error message -> failwith message

/// Plans the documents for package acme/car-rental with every module exposed.
let planOf documents =
    let evidence = passed (analyze documents)

    Planning.plan
        {
            Package = carRental
            ExposedModules = None
        }
        evidence
    |> passed

let private writers =
    WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ]

/// Builds the plan with one family and writes it at one IR version.
let buildAndWrite
    (family: IFamilyBuilder)
    (version: Versioning.FormatVersion)
    (plan: FrontendPlan)
    =
    family.Build(PackageLowering plan)
    |> Checked.bind (fun model ->
        writers.Resolve version
        |> Checked.bind (fun resolved ->
            resolved.Writer.Write(resolved.Target, WriteOptions.Compact, model)
        )
    )

let classic = ClassicFamilyBuilder() :> IFamilyBuilder
let v4 = V4FamilyBuilder() :> IFamilyBuilder

let v3 = Versioning.Classic 3
let version4 = Versioning.Classic 4

let jsonOf (bytes: byte array) =
    JsonNode.Parse(Encoding.UTF8.GetString bytes)

/// Compares two JSON values structurally, and names the first difference.
let expectJson (actual: JsonNode | null) (expected: string) message =
    let expectedNode = JsonNode.Parse expected

    if not (JsonNode.DeepEquals(actual, expectedNode)) then
        let options = JsonSerializerOptions(WriteIndented = true)

        failtestf
            "%s\nexpected:\n%s\nactual:\n%s"
            message
            (expectedNode.ToJsonString options)
            (match actual with
             | null -> "null"
             | node -> node.ToJsonString options)
