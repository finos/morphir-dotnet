// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.CodeFixes.Tests.OlderSumTypeCodeFixTests

open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Analyzers
open Morphir.Lang.CSharp.CodeFixes
open Morphir.Lang.CSharp.CodeFixes.Tests.CodeFixSupport

let private older =
    "namespace Acme.Trading.Domain;\n"
    + "public abstract record Shape\n"
    + "{\n"
    + "    private Shape() { }\n"
    + "    public sealed record Circle(double Radius) : Shape;\n"
    + "    public sealed record Rect(double W, double H) : Shape;\n"
    + "}\n"

let private closed =
    "namespace Acme.Trading.Domain;\n"
    + "public closed record Shape\n"
    + "{\n"
    + "    public sealed record Circle(double Radius) : Shape;\n"
    + "    public sealed record Rect(double W, double H) : Shape;\n"
    + "}\n"

let private fixer = OlderSumTypeCodeFix()

let private fixedText (source: string) =
    let original = document source

    match actions fixer original with
    | [ action ] -> text (apply original action)
    | other -> failtest $"Expected one code action, got {List.length other}"

/// The constructors of the custom type Shape that the C# frontend reads from a source, as name(field:type, ...).
let private shapeOf (source: string) =
    let package =
        match CanonicalNames.parsePath "acme/trading" with
        | Ok path -> PackageName path
        | Error message -> failwith message

    let documents: SourceDocument list =
        [ { Uri = "Model.cs"; LanguageId = CSharpFrontend.languageId; Version = 0UL; Text = source } ]

    let target: IrTarget = { Family = ModelFamily.V4; Version = Versioning.Classic 4 }

    let result =
        match CompileRequest.Parse(CSharpFrontend.languageId, null, documents, package, null, target, "") with
        | Error failure -> failtest $"{failure}"
        | Ok request ->
            match CSharpFrontend.registration.CompileAsync(request, CancellationToken.None).GetAwaiter().GetResult() with
            | FrontendOutcome.Completed result -> result
            | FrontendOutcome.Failed failure -> failtest failure.Message

    let words (name: Name) = String.concat "-" (Name.toList name)

    let typeText (t: Type) =
        match t with
        | Type.Reference(_, name, _) -> words name.LocalName
        | other -> string other

    match Checked.tryValue result with
    | Some { Model = FamilyModel.V4(Library(_, _, definition)) } ->
        [ for _, modul in OrderedMap.toList definition.Modules do
              for name, typ in OrderedMap.toList modul.Value.Types do
                  if words name = "shape" then
                      match typ.Value.Value with
                      | CustomTypeDefinition(_, _, cases) ->
                          for case, fields in OrderedMap.toList cases do
                              let shown = fields |> List.map (fun (n, t) -> words n + ":" + typeText t) |> String.concat ", "
                              yield words case + "(" + shown + ")"
                      | other -> failtest $"Expected a custom type, got {other}" ]
    | Some other -> failtest $"Expected a v4 library, got {other.Model}"
    | None ->
        let messages = Checked.diagnostics result |> List.map (fun d -> $"{d.CodeOrNull}: {d.Message}") |> String.concat "; "
        failtest $"The frontend refused the source: {messages}"

[<Tests>]
let tests =
    testList
        "Older sum type code fix"
        [ test "the fix fixes MORPHIR1009 only" {
              Expect.sequenceEqual fixer.FixableDiagnosticIds [ Rules.OlderSumType.Id ] "the older sum type encoding"
          }

          test "the fix changes the abstract record to a closed record and keeps the cases and their fields" {
              Expect.isEmpty (compileErrors (document older)) "the older encoding is valid C#"
              Expect.equal (fixedText older) closed "closed in place of abstract, and no private constructor"
          }

          test "the fixed source is valid C# 15 and the analyzer reports nothing in it" {
              let result = document (fixedText older)
              Expect.isEmpty (compileErrors result) "valid C# 15"
              Expect.isEmpty (diagnostics result) "no Morphir rule"
          }

          test "the frontend reads the fixed source as the custom type that the older encoding means" {
              Expect.equal (shapeOf (fixedText older)) [ "circle(radius:float)"; "rect(w:float, h:float)" ] "one constructor for each case"
              Expect.equal (shapeOf (fixedText older)) (shapeOf closed) "the same custom type as the closed record written by hand"
          }

          test "the fix keeps the cases that stand beside the abstract record" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic abstract record Shape;\npublic sealed record Circle(double Radius) : Shape;\n"

              Expect.equal
                  (fixedText source)
                  "namespace Acme.Trading.Domain;\npublic closed record Shape;\npublic sealed record Circle(double Radius) : Shape;\n"
                  "only the modifier changes"
          }

          test "the fix gives the fixed form of the frontend page for the older encoding of the page" {
              // The sum type section of docs/content/docs/frontends/csharp/_index.md has the two forms, the older
              // encoding and the fixed form, in this order.
              let page =
                  System.IO.File
                      .ReadAllText(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "docs", "content", "docs", "frontends", "csharp", "_index.md"))
                      .Replace("\r\n", "\n")

              let section = page.Substring(page.IndexOf "\n### Sum types\n")
              let section = section.Substring(0, section.IndexOf("\n### ", 1))

              match section.Split("```csharp\n") |> Array.skip 1 |> Array.map (fun block -> block.Substring(0, block.IndexOf "```")) with
              | [| _; older; fixedForm |] -> Expect.equal (fixedText older) fixedForm "the documented result of the fix"
              | blocks -> failtest $"Expected three C# examples, got {blocks.Length}"
          }

          test "no fix is offered for a closed record or a union" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic sealed record Cat(string Name);\npublic sealed record Dog(string Name);\npublic union Pet(Cat, Dog);\n"
                  + "public closed record Shape;\npublic sealed record Circle(double Radius) : Shape;\n"

              let model = document source
              Expect.isEmpty (compileErrors model) "valid C# 15"
              Expect.isEmpty (actions fixer model) "the union and the closed record stay as they are"
          } ]
