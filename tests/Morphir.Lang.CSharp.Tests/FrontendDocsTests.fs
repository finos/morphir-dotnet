// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The C# examples of the sum type section of the frontend page compile as the page says.
module Morphir.Lang.CSharp.Tests.FrontendDocsTests

open System.IO
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private page =
    File
        .ReadAllText(Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "docs", "content", "docs", "frontends", "csharp", "_index.md")))
        .Replace("\r\n", "\n")

/// The csharp blocks of a section of the page, from its heading to the next heading of the same level.
let sectionBlocks (heading: string) =
    let start = page.IndexOf("\n" + heading + "\n")
    Expect.isGreaterThanOrEqual start 0 $"the page has the section {heading}"
    let rest = page.Substring(start + heading.Length + 2)
    let level = heading.Substring(0, heading.IndexOf ' ' + 1)

    let section =
        match rest.IndexOf("\n" + level) with
        | -1 -> rest
        | finish -> rest.Substring(0, finish)

    section.Split("```csharp\n")
    |> Array.skip 1
    |> Array.map (fun block -> block.Substring(0, block.IndexOf "```"))
    |> List.ofArray

let private words (name: Name) = String.concat "-" (Name.toList name)

let private constructorsOf (distribution: Distribution) (typeName: string) =
    match moduleOf distribution "domain" with
    | None -> failtest "no module domain"
    | Some modul ->
        match modul.Types |> OrderedMap.toList |> List.tryFind (fun (n, _) -> words n = typeName) with
        | Some(_, access) ->
            match access.Value.Value with
            | CustomTypeDefinition(_, _, cases) ->
                cases |> OrderedMap.toList |> List.map (fun (name, fields) -> words name, fields |> List.map (fst >> words))
            | other -> failtest $"Expected a custom type, got {other}"
        | None -> failtest $"no type {typeName}"

[<Tests>]
let tests =
    // The tests read a file of the repository, so they run on each operating system.
    testLabel "PlatformSpecific"
    <| testList
        "C# frontend documentation"
        [ test "the sum type section has the example of the two forms, the older encoding and the fixed form" {
              Expect.equal (List.length (sectionBlocks "### Sum types")) 3 "three C# examples"
          }

          test "the example of the two forms compiles to the custom types that the page names" {
              let distribution = model (compileSource (List.item 0 (sectionBlocks "### Sum types")))
              Expect.equal (constructorsOf distribution "price") [ "quote", [ "value" ]; "no-quote", [] ] "the closed record Price"
              Expect.equal (constructorsOf distribution "shape") [ "circle", [ "value" ]; "rect", [ "value" ] ] "the union Shape"
              Expect.stringStarts (valueText distribution "domain.geometry" "unit") "((new circle) (record radius=" "Unit applies Circle to the record"
              Expect.stringContains (valueText distribution "domain.geometry" "area") "(match s with (ctor circle (as _ c)) => " "Area matches the constructors"
          }

          test "the older encoding is refused with MORPHIR1009" {
              Expect.equal (codes (compileSource (List.item 1 (sectionBlocks "### Sum types")))) [ "csharp.unsupported_abstract_record" ] "the rule of the page"
          }

          test "the fixed form compiles to the custom type Side with the constructors Buy and Sell" {
              let distribution = model (compileSource (List.item 2 (sectionBlocks "### Sum types")))
              Expect.equal (constructorsOf distribution "side") [ "buy", [ "quantity" ]; "sell", [ "quantity" ] ] "the cases and their fields"
          } ]
