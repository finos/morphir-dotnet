// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.FrontendTests

open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private typeNames (modul: ModuleDefinition) =
    modul.Types |> OrderedMap.toList |> List.map (fun (name, _) -> Name.toKebabCase name)

let private valueNames (modul: ModuleDefinition) =
    modul.Values |> OrderedMap.toList |> List.map (fun (name, _) -> Name.toKebabCase name)

let private typeDefinition (modul: ModuleDefinition) (name: string) =
    modul.Types
    |> OrderedMap.toList
    |> List.find (fun (n, _) -> Name.toKebabCase n = name)
    |> snd
    |> fun access -> access.Value.Value

let private theModule distribution path =
    match moduleOf distribution path with
    | Some modul -> modul
    | None -> failtest $"No module {path} in {moduleNames distribution}"

[<Tests>]
let contract =
    testList
        "C# frontend contract"
        [ test "the frontend reads csharp and writes the current IR only" {
              let capability = frontend.Capability
              Expect.equal (capability.Languages |> Seq.map (fun l -> l.Id.Value) |> List.ofSeq) [ "csharp" ] "language"
              Expect.equal (capability.Languages[0].FileExtensions |> List.ofSeq) [ ".cs" ] "extension"
              Expect.equal (capability.Outputs |> List.ofSeq) [ v4Target ] "only the current IR"
              Expect.isTrue capability.MultiDocument "multi-document"
          }

          test "a record in a namespace below the package is a module with a record type" {
              let result =
                  compileSource "namespace Acme.Trading.Domain;\npublic sealed record Quote(decimal Price, int Quantity);\n"

              let distribution = model result
              Expect.equal (moduleNames distribution) [ "domain" ] "module"
              let modul = theModule distribution "domain"
              Expect.equal (typeNames modul) [ "quote" ] "type"

              match typeDefinition modul "quote" with
              | TypeAliasDefinition(parameters, Type.Record(_, fields)) ->
                  Expect.isEmpty parameters "no type parameters"
                  Expect.equal (fields |> List.map (fun (name, _) -> Name.toKebabCase name)) [ "price"; "quantity" ] "fields in order"
              | other -> failtest $"Expected a record alias, got {other}"
          }

          test "two documents give the modules of both, in order" {
              let result =
                  compile
                      [ document "A.cs" "namespace Acme.Trading.Alpha;\npublic sealed record A(int X);\n"
                        document "B.cs" "namespace Acme.Trading.Beta;\npublic sealed record B(int Y);\n" ]

              Expect.equal (moduleNames (model result)) [ "alpha"; "beta" ] "both modules, in document order"
          }

          test "a C# type error stops compilation with csharp.compiler and the C# id" {
              let result = compileSource "namespace Acme.Trading.Domain;\npublic static class M { public static int F() { int x = \"a\"; return x; } }\n"
              Expect.isTrue (Checked.diagnostics result |> List.exists (fun d -> d.IsError)) "failed"
              Expect.isNone (Checked.tryValue result) "no model"
              let diagnostic = Checked.diagnostics result |> List.find (fun d -> d.IsError)
              Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "csharp.compiler") "code"
              Expect.stringContains (string (Diagnostic.data diagnostic).Value) "CS0029" "the C# id is in the data"
              Expect.equal (Diagnostic.location diagnostic |> Option.map (fun l -> l.Uri)) (Some "Model.cs") "the document URI"
          }

          test "a C# error is not repeated as an unsupported construct" {
              let result = compileSource "namespace Acme.Trading.Domain;\npublic static class M { public static int F(int[] xs) { int x = \"a\"; foreach (var y in xs) { } return x; } }\n"
              Expect.equal (codes result |> List.distinct) [ "csharp.compiler" ] "only compiler diagnostics"
          }

          test "a warning accompanies the model" {
              let result = compileSource "namespace Acme.Trading.Domain;\npublic sealed record Quote(string Name);\npublic static class M { public static int F() { string? s = null; return s.Length; } }\n"
              // The nullable warning keeps the model out of the way of the admission rules? It gives a result with warnings only
              // when the body is lowerable; the body uses a member that has no Morphir mapping, so this source fails later.
              // The warning itself is what this test pins: the compile warning comes first.
              let diagnostics = Checked.diagnostics result
              Expect.isTrue (diagnostics |> List.exists (fun d -> Diagnostic.code d = Some CSharpCodes.compiler && not d.IsError)) "a compiler warning"
          }

          test "the same input gives the same result twice" {
              let source = "namespace Acme.Trading.Domain;\npublic sealed record Quote(decimal Price);\npublic static class Pricing { public const int Rate = 40; }\n"
              let render result = (model result).ToString()
              Expect.equal (render (compileSource source)) (render (compileSource source)) "deterministic"
          }

          test "a namespace outside the package gives csharp.module_outside_package" {
              let result = compileSource "namespace Other.Place;\npublic sealed record Quote(int X);\n"
              Expect.contains (codes result) "csharp.module_outside_package" "code"
          }

          test "types in the package namespace itself have no module" {
              let result = compileSource "namespace Acme.Trading;\npublic sealed record Quote(int X);\n"
              Expect.contains (codes result) "csharp.module_outside_package" "code"
          } ]

[<Tests>]
let types =
    testList
        "C# frontend types and modules"
        [ test "types in a namespace and a static class below it are two modules" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic enum Side { Buy, Sell }\npublic closed record Order;\npublic sealed record Market(Side Side, decimal Quantity) : Order;\npublic sealed record Limit(Side Side, decimal Quantity, decimal Price) : Order;\npublic static class Pricing { public const int Rate = 40; }\n"

              let distribution = model (compileSource source)
              Expect.equal (moduleNames distribution) [ "domain"; "domain.pricing" ] "two modules"
              let domain = theModule distribution "domain"
              Expect.equal (typeNames domain) [ "side"; "order" ] "types; the cases are constructors, not types"
              Expect.equal (valueNames (theModule distribution "domain.pricing")) [ "rate" ] "constant"
          }

          test "a closed record is a custom type with the cases as constructors" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic closed record Order;\npublic sealed record Market(decimal Quantity) : Order;\npublic sealed record Limit(decimal Quantity, decimal Price) : Order;\n"

              let modul = theModule (model (compileSource source)) "domain"

              match typeDefinition modul "order" with
              | CustomTypeDefinition(_, _, constructors) ->
                  let shape =
                      constructors
                      |> OrderedMap.toList
                      |> List.map (fun (name, args) -> Name.toKebabCase name, args |> List.map (fst >> Name.toKebabCase))

                  Expect.equal shape [ "market", [ "quantity" ]; "limit", [ "quantity"; "price" ] ] "constructors and fields"
              | other -> failtest $"Expected a custom type, got {other}"
          }

          test "an enum is a custom type with constructors that have no arguments" {
              let modul =
                  theModule (model (compileSource "namespace Acme.Trading.Domain;\npublic enum Side { Buy, Sell }\n")) "domain"

              match typeDefinition modul "side" with
              | CustomTypeDefinition(_, _, constructors) ->
                  let shape = constructors |> OrderedMap.toList |> List.map (fun (name, args) -> Name.toKebabCase name, args.Length)
                  Expect.equal shape [ "buy", 0; "sell", 0 ] "constructors"
              | other -> failtest $"Expected a custom type, got {other}"
          }

          test "a static class nested in a static class is a child module" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic static class Outer { public const int A = 1; public static class Inner { public const int B = 2; } }\n"

              let distribution = model (compileSource source)
              Expect.equal (moduleNames distribution) [ "domain.outer"; "domain.outer.inner" ] "child module"
          }

          test "a generic record keeps its type parameter" {
              let modul =
                  theModule (model (compileSource "namespace Acme.Trading.Domain;\npublic sealed record Box<T>(T Value);\n")) "domain"

              match typeDefinition modul "box" with
              | TypeAliasDefinition(parameters, _) -> Expect.equal (parameters |> List.map Name.toList) [ [ "t" ] ] "type parameter, in lowercase words"
              | other -> failtest $"Expected an alias, got {other}"
          }

          test "two types with one Morphir name collide" {
              let result =
                  compileSource "namespace Acme.Trading.Domain;\npublic sealed record TaxRate(int X);\npublic sealed record taxRate(int Y);\n"

              Expect.contains (codes result) "csharp.name_collision" "collision"
          }

          test "a type outside the subset is refused by an admission rule" {
              let result = compileSource "namespace Acme.Trading.Domain;\npublic class Plain { }\n"
              Expect.contains (codes result) "csharp.unsupported_type_kind" "class refused"
          } ]
