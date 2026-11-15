// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendRoundTripTests

open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Tests.BackendSupport

let private package =
    match CanonicalNames.parsePath "acme/geo" with
    | Ok path -> PackageName path
    | Error message -> failwith message

/// The model that the frontend gives for the C# that the backend wrote.
let private throughCSharp (model: VersionedModel) : Distribution =
    let files = generate model

    let documents =
        // The frontend has its own copy of the polyfill types, so the file of the backend is not given to it.
        [ for KeyValue(path, text) in files do
              if path <> "Morphir.Polyfill.g.cs" then
                  yield
                      ({ Uri = path; LanguageId = CSharpFrontend.languageId; Version = 0UL; Text = text }: SourceDocument) ]

    let target: IrTarget = { Family = ModelFamily.V4; Version = Versioning.Classic 4 }

    match CompileRequest.Parse(CSharpFrontend.languageId, null, documents, package, null, target, "") with
    | Error failure -> failwithf "%A" failure
    | Ok request ->
        match CSharpFrontend.registration.CompileAsync(request, CancellationToken.None).GetAwaiter().GetResult() with
        | FrontendOutcome.Failed failure -> failwithf "The frontend failed: %s" failure.Message
        | FrontendOutcome.Completed result ->
            match Checked.tryValue result with
            | Some compiled ->
                match compiled.Model with
                | FamilyModel.V4 distribution -> distribution
                | _ -> failwith "a V4 model is expected"
            | None ->
                failwithf "The frontend refused the C# of the backend: %s" (Checked.diagnostics result |> List.map (fun d -> $"{d.CodeOrNull}: {d.Message}") |> String.concat "; ")

let private distributionOf (model: VersionedModel) =
    match model.Distribution with
    | DistributionModel.V4 distribution -> distribution
    | _ -> failwith "a V4 model is expected"

/// The values of a distribution as text: each value of each module, with its signature and its body.
let private valuesText (distribution: Distribution) : string list =
    match distribution with
    | Library(_, _, definition) ->
        [ for path, access in OrderedMap.toList definition.Modules |> List.sortBy fst do
              let (ModulePath p) = path
              let moduleName = Path.toString Name.toKebabCase "/" p

              for name, value in OrderedMap.toList access.Value.Values |> List.sortBy fst do
                  let valueName = String.concat "-" (Name.toList name)
                  yield moduleName + "." + valueName + ": " + FrontendSupport.signature distribution moduleName valueName + " = " + FrontendSupport.valueText distribution moduleName valueName ]
    | _ -> []

let private typeDefinitionText (definition: TypeDefinition) : string =
    let parameters (names: Name list) = names |> List.map (fun n -> String.concat "-" (Name.toList n)) |> String.concat " "

    match definition with
    | TypeAliasDefinition(ps, body) -> "alias [" + parameters ps + "] = " + FrontendSupport.renderType body
    | CustomTypeDefinition(ps, _, cases) ->
        let rendered =
            OrderedMap.toList cases
            |> List.map (fun (name, fields) ->
                let args = fields |> List.map (fun (n, t) -> String.concat "-" (Name.toList n) + ":" + FrontendSupport.renderType t) |> String.concat ", "
                String.concat "-" (Name.toList name) + "(" + args + ")")
            |> List.sort
            |> String.concat " | "

        "custom [" + parameters ps + "] = " + rendered
    | IncompleteTypeDefinition _ -> "incomplete"

let private typeNames (distribution: Distribution) : string list =
    match distribution with
    | Library(_, _, definition) ->
        [ for path, access in OrderedMap.toList definition.Modules |> List.sortBy fst do
              let (ModulePath p) = path
              let moduleName = Path.toString Name.toKebabCase "/" p

              for name, definition in OrderedMap.toList access.Value.Types |> List.sortBy fst do
                  yield moduleName + "." + String.concat "-" (Name.toList name) + ": " + typeDefinitionText definition.Value.Value ]
    | _ -> []

/// The class words on each type variable of the signature of each value, as the carrier of EQL-2 gives them.
let private carriersText (distribution: Distribution) : string list =
    match distribution with
    | Library(_, _, definition) ->
        [ for _, access in OrderedMap.toList definition.Modules |> List.sortBy fst do
              for name, value in OrderedMap.toList access.Value.Values |> List.sortBy fst do
                  let found =
                      FrontendValueClassTests.occurrences value.Value.Value
                      |> List.map (fun (variable, classes) -> variable + ":" + String.concat "," classes)

                  yield String.concat "-" (Name.toList name) + " " + String.concat " " found ]
    | _ -> []

[<Tests>]
let tests =
    testList "C# backend on Roslyn: IR to C# to IR" [
        test "a generic function of each value class comes back with its class in the carrier" {
            let classified = BackendValueClassTests.classified
            let comparable = classified "comparable" [ "comparable" ]
            let number = classified "number" [ "number" ]
            let a = classified "a" [ "equality" ]

            let model =
                library
                    [ "generic",
                      moduleOf
                          []
                          [ "maximum", definition [ "x", comparable; "y", comparable ] comparable (call2 (sdkRef "basics#max") (variable "x") (variable "y"))
                            "twice", definition [ "x", number ] number (call2 (sdkRef "basics#add") (variable "x") (variable "x"))
                            "same", definition [ "x", a; "y", a ] boolT (call2 (sdkRef "basics#equal") (variable "x") (variable "y")) ] ]

            let original = distributionOf model
            let back = throughCSharp model
            Expect.equal (valuesText back) (valuesText original) "the same values"
            Expect.equal (carriersText back) (carriersText original) "the same classes on each type variable"

            Expect.equal
                (carriersText original)
                [ "maximum comparable:comparable comparable:comparable comparable:comparable"
                  "same A:equality A:equality"
                  "twice number:number number:number" ]
                "the model has a carrier on each type variable"
        }

        test "Equals and CompareTo on a type parameter go from C# through the model to C# that runs" {
            let source =
                String.concat "\n" [
                    "namespace Acme.Trading.Domain;"
                    "using System;"
                    "public static class Calc"
                    "{"
                    "    public static bool Same<T>(T a, T b) where T : notnull => a.Equals(b);"
                    "    public static int Cmp<T>(T a, T b) where T : IComparable => a.CompareTo(b);"
                    "    public static bool UseSame() => Same(1, 1);"
                    "    public static int UseCmp() => Cmp(\"B\", \"a\");"
                    "}"
                ]

            let distribution = FrontendSupport.model (FrontendSupport.compileSource source)

            let model: VersionedModel = {
                SourceVersion = SourceFormatVersion.Create "4"
                Distribution = DistributionModel.V4 distribution
            }

            let files = generate model
            Expect.equal (run files "Acme.Trading.Domain.Calc" "UseSame" []) "True" "Same(1, 1)"
            // The order of the model is the order of the SDK: ordinal strings, so "B" is before "a".
            Expect.equal (run files "Acme.Trading.Domain.Calc" "UseCmp" []) "-1" "Cmp(\"B\", \"a\")"
        }

        test "a generic function with no carrier generates, and the frontend writes the class of its constraint" {
            let comparable = variableT "comparable"

            let model =
                library [ "generic", moduleOf [] [ "maximum", definition [ "x", comparable; "y", comparable ] comparable (call2 (sdkRef "basics#max") (variable "x") (variable "y")) ] ]

            let back = throughCSharp model
            Expect.equal (valuesText back) (valuesText (distributionOf model)) "the same values"
            Expect.equal (carriersText back) [ "maximum comparable:comparable comparable:comparable comparable:comparable" ] "the class of where TComparable : IComparable<TComparable>"
        }

        test "types and values come back from the C# of the backend" {
            let model =
                library
                    [ "shapes",
                      moduleOf
                          [ "point", alias [] (recordT [ "x", intT; "y", intT ])
                            "shape", custom [] [ "circle", [ "radius", floatT ]; "rect", [ "w", floatT; "h", floatT ] ] ]
                          [ "one", definition [] intT (int 1) ]
                      "shapes/geometry",
                      moduleOf
                          []
                          [ "size",
                            definition
                                [ "s", own "shapes" "shape" [] ]
                                floatT
                                (patternMatch
                                    (variable "s")
                                    [ constructorPattern "shapes#circle" [ asPattern "r" ], variable "r"
                                      constructorPattern "shapes#rect" [ asPattern "w"; asPattern "h" ], variable "w" ]) ] ]

            let original = distributionOf model
            let back = throughCSharp model
            Expect.equal (typeNames back) (typeNames original) "the same types"
            Expect.equal (valuesText back) (valuesText original) "the same values"
        }
    
        test "generic records, calls, lets, conditionals, tuples, lists and record operations come back" {
            let sdkAdd = sdkRef "basics#add"
            let sdkLess = sdkRef "basics#less-than"

            let model =
                library
                    [ "pairs",
                      moduleOf
                          [ "pair", alias [ "a"; "b" ] (recordT [ "first", variableT "a"; "second", variableT "b" ])
                            "point", alias [] (recordT [ "x", intT; "y", intT ]) ]
                          [ "origin", definition [] (own "pairs" "point" []) (Morphir.IR.V4.Value.Record(va, [ name "x", int 0; name "y", int 0 ]))
                            "moved",
                            definition
                                [ "p", own "pairs" "point" [] ]
                                (own "pairs" "point" [])
                                (Morphir.IR.V4.Value.UpdateRecord(va, variable "p", [ name "x", call2 sdkAdd (Morphir.IR.V4.Value.Field(va, variable "p", name "x")) (int 1) ]))
                            "describe",
                            definition
                                [ "n", intT ]
                                stringT
                                (ifThenElse (call2 sdkLess (variable "n") (int 0)) (Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.StringLiteral "negative")) (Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.StringLiteral "other")))
                            "pair-up", definition [ "n", intT ] (Morphir.IR.V4.Type.Tuple(ta, [ intT; intT ])) (Morphir.IR.V4.Value.Tuple(va, [ variable "n"; call2 sdkAdd (variable "n") (int 1) ])) ] ]

            let original = distributionOf model
            let back = throughCSharp model
            Expect.equal (typeNames back) (typeNames original) "the same types"
            Expect.equal (valuesText back) (valuesText original) "the same values"
        }
    ]
