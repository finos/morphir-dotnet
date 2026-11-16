// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.CodeFixes.Tests.ClosureTests

open System.Collections.Generic
open System.Reflection
open Expecto
open Morphir.Lang.CSharp.CodeFixes

let private names (assembly: Assembly) =
    assembly.GetReferencedAssemblies() |> Array.choose (fun name -> Option.ofObj name.Name) |> List.ofArray

/// The names of the assemblies that an assembly needs, directly and through the assemblies that it needs.
let private closure (root: Assembly) =
    let seen = HashSet<string>()
    let pending = Queue<AssemblyName>(root.GetReferencedAssemblies())

    while pending.Count > 0 do
        let next = pending.Dequeue()

        match next.Name with
        | null -> ()
        | name when seen.Add name ->
            try
                for reference in Assembly.Load(next).GetReferencedAssemblies() do
                    pending.Enqueue reference
            with :? System.IO.FileNotFoundException ->
                // A reference assembly of the framework that the runtime does not have (netstandard facades) has no
                // further references of interest.
                ()
        | _ -> ()

    List.ofSeq seen

let private isWorkspaces (name: string) = name.Contains "Workspaces"

[<Tests>]
let tests =
    testList
        "Code fix assembly closure"
        [ test "the code fix assembly targets netstandard2.0 and uses Workspaces and the analyzer assembly" {
              let referenced = names typeof<OlderSumTypeCodeFix>.Assembly
              Expect.contains referenced "netstandard" "the netstandard facade"
              Expect.contains referenced "Microsoft.CodeAnalysis.Workspaces" "a code fix needs Workspaces"
              Expect.contains referenced "Morphir.Lang.CSharp.Analyzers" "the rule ids"
          }

          test "the frontend, the passes and the backend need no Workspaces assembly and no code fix assembly" {
              let needed = closure typeof<Morphir.Lang.CSharp.Emit.CSharpBackend>.Assembly

              Expect.contains needed "Microsoft.CodeAnalysis.CSharp" "the closure is read"
              Expect.isEmpty (needed |> List.filter isWorkspaces) "the Workspaces layer does not run under Native AOT"
              Expect.isFalse (List.contains "Morphir.Lang.CSharp.CodeFixes" needed) "only an IDE loads the code fixes"
          }

          test "the analyzer assembly needs no Workspaces assembly and no code fix assembly" {
              let needed = closure typeof<Morphir.Lang.CSharp.Analyzers.AdmissionAnalyzer>.Assembly
              Expect.isEmpty (needed |> List.filter isWorkspaces) "the compiler loads it"
              Expect.isFalse (List.contains "Morphir.Lang.CSharp.CodeFixes" needed) "no code fix"
          } ]
