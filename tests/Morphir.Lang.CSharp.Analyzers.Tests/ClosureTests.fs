// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Analyzers.Tests.ClosureTests

open Expecto
open Morphir.Lang.CSharp.Analyzers

let private analyzerAssembly = typeof<AnalyzerIds>.Assembly

let private referencedNames () =
    analyzerAssembly.GetReferencedAssemblies()
    |> Array.choose (fun name ->
        match name.Name with
        | null -> None
        | text -> Some text)

[<Tests>]
let tests =
    testList
        "Analyzer assembly closure"
        [ test "the assembly targets netstandard2.0" {
              Expect.contains (referencedNames ()) "netstandard" "references the netstandard facade"
          }

          test "the assembly depends only on netstandard and Roslyn" {
              let allowed = set [ "netstandard"; "Microsoft.CodeAnalysis"; "Microsoft.CodeAnalysis.CSharp" ]

              let others = referencedNames () |> Array.filter (fun name -> not (allowed.Contains name))

              Expect.isEmpty others "no other assembly is referenced"
          }

          test "no Workspaces assembly is referenced" {
              let workspaces = referencedNames () |> Array.filter (fun name -> name.Contains "Workspaces")

              Expect.isEmpty workspaces "the Workspaces layer does not run under Native AOT"
          }

          test "rule ids have the Morphir prefix" {
              Expect.equal AnalyzerIds.Prefix "MORPHIR" "prefix"
              Expect.equal AnalyzerIds.Category "Morphir" "category"
          } ]
