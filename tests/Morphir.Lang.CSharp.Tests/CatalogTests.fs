// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.CatalogTests

open Expecto
open Morphir.Compilation
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Analyzers
open Morphir.Lang.CSharp.Tests.FrontendSupport

/// The example of a rule as a document of the package. An example is a body of declarations.
let private asDocument (rule: AdmissionRule) =
    document "Example.cs" ($"namespace Acme.Trading.Domain;\n" + rule.Example + "\n")

// The rule that needs a second assembly cannot be shown to the frontend: its fixed references cannot bind the symbol,
// so the compiler reports it first. The analyzer tests cover that rule.
let private frontendRules = Rules.All |> Seq.filter (fun rule -> rule.Id <> Rules.OutsideReference.Id) |> List.ofSeq

[<Tests>]
let tests =
    testList
        "C# rule catalog"
        [ test "each rule example gives its own code and a range through the frontend" {
              for rule in frontendRules do
                  let result = compile [ asDocument rule ]
                  let diagnostics = Checked.diagnostics result |> List.filter (fun d -> d.IsError)

                  Expect.isNonEmpty diagnostics $"{rule.Id} gives a diagnostic"

                  let codes = diagnostics |> List.choose Diagnostic.code |> List.map (fun c -> c.Value) |> List.distinct
                  Expect.equal codes [ rule.Code ] $"{rule.Id}: only its own code"

                  for diagnostic in diagnostics do
                      match Diagnostic.location diagnostic with
                      | Some location ->
                          Expect.equal location.Uri "Example.cs" $"{rule.Id}: the document"
                          Expect.isGreaterThanOrEqual location.Range.Start.Line 0 $"{rule.Id}: a position"
                      | None -> failtest $"{rule.Id}: a diagnostic without a location"
          }

          test "the frontend codes are csharp.unsupported_ and the slug of the rule" {
              for rule in Rules.All do
                  Expect.equal rule.Code ("csharp.unsupported_" + rule.Slug) rule.Id
                  Expect.isTrue (rule.Iri.EndsWith("unsupported_" + rule.Slug)) rule.Id
          }

          test "the code is a valid diagnostic code" {
              for rule in Rules.All do
                  match DiagnosticCode.Parse rule.Code with
                  | Ok _ -> ()
                  | Error failure -> failtest $"{rule.Id}: {failure}"
          } ]
