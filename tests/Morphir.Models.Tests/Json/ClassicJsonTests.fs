module Morphir.Json.Tests.ClassicJsonTests

open System.IO
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Distribution
open Morphir.IR.Classic.Documented
open Morphir.Json.Classic

let private fixture name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Fixtures", "MorphirIR", name))

let private carRental name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "examples", "car-rental", "models", name))

let private read (json: string) =
    use document = JsonDocument.Parse json
    ClassicJson.readDistribution document.RootElement

let private ok json =
    match read json with
    | Ok document -> document
    | Error error -> failtestf "Expected a distribution, got %A" error

let private rule json =
    match read json with
    | Ok _ -> failtest "Expected an error."
    | Error error -> error.Rule

let private pathOf (words: string list list) =
    Path.fromList (words |> List.map (fun name -> Name name))

let private packageOf words = PackageName.packageName (pathOf words)

/// The legacy word grammar is ^[a-z0-9]+$, and morphir-elm writes names such as ["arg", "1"].
[<Tests>]
let wordTests =
    let withName (name: string) =
        $"""{{ "formatVersion": 3, "distribution": ["Library", [["p"]], [], {{ "modules": [[[{name}], {{ "access": "Public", "value": {{ "types": [], "values": [] }} }}]] }}] }}"""

    testList "Classic name words" [
        testCase "A word may be digits"
        <| fun () ->
            match (ok (withName """["arg", "1"]""")).Distribution with
            | Library(_, _, definition) ->
                Expect.equal
                    (definition.Modules |> Map.toList |> List.map fst)
                    [ ModulePath.modulePath (pathOf [ [ "arg"; "1" ] ]) ]
                    "Both words"
            | other -> failtestf "Expected a library, read %A" other

        testCase "A word holds only lowercase letters and digits"
        <| fun () ->
            for name in [ """["Arg"]"""; """["a b"]"""; """[""]"""; """[]""" ] do
                Expect.equal (rule (withName name)) "invalid-name" name
    ]

let private dependency =
    """
    [ [["other"], ["pkg"]],
      { "modules": [
          [ [["api"]],
            { "types": [
                [ ["alias"], { "doc": "An alias.", "value": ["TypeAliasSpecification", [["a"]], ["Variable", {}, ["a"]]] } ],
                [ ["hidden"], { "doc": null, "value": ["OpaqueTypeSpecification", []] } ],
                [ ["choice"], { "doc": "Choices.", "value":
                    ["CustomTypeSpecification", [], [ [["yes"], [[["why"], ["Unit", {}]]]], [["no"], []] ]] } ],
                [ ["money"], { "doc": "Derived.", "value":
                    ["DerivedTypeSpecification", [], {
                       "baseType": ["Unit", {}],
                       "fromBaseType": [[["other"], ["pkg"]], [["api"]], ["from"]],
                       "toBaseType": [[["other"], ["pkg"]], [["api"]], ["to"]] }] } ]
              ],
              "values": [
                [ ["run"], { "doc": "Runs.", "value": { "inputs": [[["x"], ["Unit", {}]]], "output": ["Unit", {}] } } ]
              ],
              "doc": "The API."
            } ]
        ] } ]
    """

let private library version dependencies =
    $$"""{ "formatVersion": {{version}}, "distribution": ["Library", [["my"], ["pkg"]], [{{dependencies}}], { "modules": [] }] }"""

let private specs version =
    $$"""
    { "formatVersion": {{version}},
      "distribution": ["Specs", [["my"], ["pkg"]], [],
        { "modules": [ [ [["api"]], { "types": [], "values": [] } ] ] } ] }
    """

let private literalValue literal =
    $$"""
    { "formatVersion": 3,
      "distribution": ["Library", [["my"]], [],
        { "modules": [ [ [["m"]],
          { "access": "Public", "value": {
              "types": [],
              "values": [ [ ["v"], { "access": "Public", "value": {
                  "doc": "",
                  "value": { "inputTypes": [], "outputType": ["Unit", {}], "body": ["Literal", {}, {{literal}}] } } } ] ] } } ] ] } ] }
    """

[<Tests>]
let tests =
    testList
        "classic JSON reader"
        [ testCase "reference fixture"
          <| fun _ ->
              let document = ok (fixture "daily-rate-v3.json")
              Expect.equal document.Version 3 "version"
              Expect.equal (Distribution.packageName document.Distribution) (packageOf [ [ "car" ]; [ "rental" ] ]) "package"
              Expect.isEmpty (Distribution.dependencies document.Distribution) "dependencies"

              match Distribution.packageDefinition document.Distribution with
              | None -> failtest "A library has a definition."
              | Some definition ->
                  let modulePath = ModulePath.modulePath (pathOf [ [ "inventory" ] ])
                  let inventory = definition.Modules[modulePath]
                  Expect.equal inventory.Access Public "module access"
                  Expect.isTrue (inventory.Value.Values.ContainsKey(Name [ "daily"; "rate" ])) "the value is present"
          |> testLabel "PlatformSpecific"

          testCase "every classic version reads"
          <| fun _ ->
              for version, file in [ 1, "car-rental-v1.json"; 2, "car-rental-v2.json"; 3, "car-rental-v3.json" ] do
                  Expect.equal (ok (carRental file)).Version version file
          |> testLabel "PlatformSpecific"

          testCase "dependency specifications"
          <| fun _ ->
              let document = ok (library "3" dependency)

              let package =
                  (Distribution.dependencies document.Distribution)[packageOf [ [ "other" ]; [ "pkg" ] ]]

              let api = package.Modules[ModulePath.modulePath (pathOf [ [ "api" ] ])]
              Expect.equal api.Doc (Some "The API.") "module doc"

              match api.Types[Name [ "alias" ]] with
              | WithDocumentation("An alias.", Type.TypeAliasSpecification([ Name [ "a" ] ], Type.Variable(_, Name [ "a" ]))) -> ()
              | other -> failtestf "alias: %A" other

              match api.Types[Name [ "hidden" ]] with
              | WithoutDocumentation(Type.OpaqueTypeSpecification []) -> ()
              | other -> failtestf "a null doc means no documentation: %A" other

              match api.Types[Name [ "choice" ]] with
              | WithDocumentation("Choices.", Type.CustomTypeSpecification([], constructors)) ->
                  Expect.equal (constructors |> Map.toList |> List.map fst) [ Name [ "no" ]; Name [ "yes" ] ] "constructors"
              | other -> failtestf "custom: %A" other

              match api.Types[Name [ "money" ]] with
              | WithDocumentation("Derived.", Type.DerivedTypeSpecification([], details)) ->
                  Expect.equal details.FromBaseType.LocalName (Name [ "from" ]) "from"
                  Expect.equal details.ToBaseType.LocalName (Name [ "to" ]) "to"
              | other -> failtestf "derived: %A" other

              match api.Values[Name [ "run" ]] with
              | WithDocumentation("Runs.", { Inputs = [ Name [ "x" ], Type.Unit _ ]; Output = Type.Unit _ }) -> ()
              | other -> failtestf "value: %A" other

          testCase "a Specs distribution holds a specification"
          <| fun _ ->
              let document = ok (specs "\"3.1.0\"")
              Expect.equal (Distribution.packageName document.Distribution) (packageOf [ [ "my" ]; [ "pkg" ] ]) "package"
              Expect.isNone (Distribution.packageDefinition document.Distribution) "no definition"

              match document.Distribution with
              | Specs(_, _, specification) ->
                  Expect.isTrue (specification.Modules.ContainsKey(ModulePath.modulePath (pathOf [ [ "api" ] ]))) "module"
              | other -> failtestf "Expected Specs: %A" other

          testCase "Specs version floor"
          <| fun _ ->
              Expect.equal (rule (specs "3")) "specs_before_3_1" "integer 3"
              Expect.equal (rule (specs "\"3.0.0\"")) "specs_before_3_1" "3.0.0"

          testCase "release strings read for v3"
          <| fun _ ->
              Expect.equal (ok (library "\"3.0.0\"" "")).Version 3 "3.0.0"
              Expect.equal (ok (library "\"3.1.0\"" "")).Version 3 "3.1.0"

          testCase "unsupported versions"
          <| fun _ ->
              for version in [ "0"; "4"; "\"4.0.0\""; "\"3.2.0\""; "\"3\""; "\"x\""; "true" ] do
                  Expect.equal (rule (library version "")) "unsupported-version" version

          testCase "unsupported distribution"
          <| fun _ ->
              let json = (library "3" "").Replace("\"Library\"", "\"DocumentTree\"")

              match read json with
              | Error error ->
                  Expect.equal error.Rule "unsupported-distribution" "rule"
                  Expect.equal error.Location "$.distribution" "location"
              | Ok _ -> failtest "Expected an error."

          testCase "out-of-range literals are not rounded"
          <| fun _ ->
              Expect.equal
                  (rule (literalValue """["WholeNumberLiteral", 9223372036854775808]"""))
                  "literal_out_of_range"
                  "whole number"

              Expect.equal
                  (rule (literalValue """["DecimalLiteral", "99999999999999999999999999999999"]"""))
                  "literal_out_of_range"
                  "decimal"

          testCase "a decimal that needs more than 28 digits is out of range, as a string or a number"
          <| fun _ ->
              Expect.equal
                  (rule (literalValue """["DecimalLiteral", "1.00000000000000000000000000000001"]"""))
                  "literal_out_of_range"
                  "string"

              Expect.equal
                  (rule (literalValue """["DecimalLiteral", 1.00000000000000000000000000000001]"""))
                  "literal_out_of_range"
                  "number"

          testCase "a decimal string outside the lexeme grammar is a decode failure"
          <| fun _ ->
              for text in [ "1,000.5"; " 1"; "1 "; "1.5 "; "\\t2"; ""; "abc"; "1e"; "Infinity" ] do
                  Expect.equal
                      (rule (literalValue $"""["DecimalLiteral", "{text}"]"""))
                      "decode"
                      $"'{text}'"

          testCase "decimals the grammar allows are read"
          <| fun _ ->
              for text, expected in [ "1.50", 1.50m; "-2", -2m; "1e2", 100m; "+.5", 0.5m; "5.", 5m ] do
                  match (ok (literalValue $"""["DecimalLiteral", "{text}"]""")).Distribution with
                  | Library(_, _, definition) ->
                      let value =
                          definition.Modules.Values |> Seq.head |> fun m -> m.Value.Values.Values |> Seq.head

                      match value.Value with
                      | WithDocumentation(_, d)
                      | WithoutDocumentation d ->
                          match d.Body with
                          | Value.Literal(_, DecimalLiteral held) ->
                              Expect.equal held expected text
                          | other -> failtestf "%A" other
                  | other -> failtestf "%A" other

          testCase "literals report the wrong kind at their own location"
          <| fun _ ->
              for literal in [
                  """["BoolLiteral", "yes"]"""
                  """["WholeNumberLiteral", "7"]"""
                  """["FloatLiteral", "1.5"]"""
              ] do
                  match read (literalValue literal) with
                  | Error error ->
                      Expect.equal error.Rule "decode" literal
                      Expect.notEqual error.Location "$" literal
                  | Ok _ -> failtest "Expected an error."

          testCase "a whole number literal that is not an integer is a decode failure"
          <| fun _ ->
              Expect.equal (rule (literalValue """["WholeNumberLiteral", 42.0]""")) "decode" "42.0"
              Expect.equal (rule (literalValue """["WholeNumberLiteral", 1.5]""")) "decode" "1.5"

          testCase "wrong types return an error, not an exception"
          <| fun _ ->
              Expect.isError (read (literalValue """["BoolLiteral", "yes"]""")) "bool"
              Expect.isError (read "[]") "root"
              Expect.isError (read "{}") "empty"

          testCase "the result outlives the JSON document"
          <| fun _ ->
              let document = ok (fixture "daily-rate-v3.json")

              match Distribution.packageDefinition document.Distribution with
              | Some definition ->
                  let inventory = definition.Modules[ModulePath.modulePath (pathOf [ [ "inventory" ] ])]

                  let definition =
                      match inventory.Value.Values[Name [ "daily"; "rate" ]].Value with
                      | WithDocumentation(_, definition)
                      | WithoutDocumentation definition -> definition

                  match definition.OutputType with
                  | Type.Reference(attributes, _, _) ->
                      // Reading from a disposed document would throw here.
                      Expect.isNotNull (attributes.GetRawText()) "attributes"
                  | other -> failtestf "Expected a reference: %A" other
              | None -> failtest "A library has a definition."
          |> testLabel "PlatformSpecific" ]
