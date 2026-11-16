// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The backend writes each row of the transform table in the form of the profile, and the frontend reads the C# back to
/// the same IR (csharp-loop-lowering).
module Morphir.Lang.CSharp.Tests.BackendLoopTests

open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Backend
open Morphir.Lang.CSharp.Tests.BackendSupport

let private package =
    match CanonicalNames.parsePath "acme/geo" with
    | Ok path -> PackageName path
    | Error message -> failwith message

let private listT (item: Type) = sdk "list#list" [ item ]
let private lambda (parameter: string) (body: Value) = Value.Lambda(va, asPattern parameter, body)
let private tuplePattern (names: string list) = TuplePattern(va, [ for n in names -> asPattern n ])
let private sdkCtor (text: string) = Value.Constructor(va, fq ("morphir/SDK:" + text))
let private add a b = call2 (sdkRef "basics#add") a b
let private multiply a b = call2 (sdkRef "basics#multiply") a b
let private subtract a b = call2 (sdkRef "basics#subtract") a b
let private greaterThan a b = call2 (sdkRef "basics#greater-than") a b

/// The rows of the transform table, each as one value of the module "loops".
let private rows: (string * ValueDefinition) list =
    let xs = [ "xs", listT intT ]

    [ "twice", definition xs (listT intT) (call2 (sdkRef "list#map") (lambda "x" (multiply (variable "x") (int 2))) (variable "xs"))
      "doubled", definition xs (listT intT) (call2 (sdkRef "list#map") (reference "loops#double") (variable "xs"))
      "double", definition [ "n", intT ] intT (multiply (variable "n") (int 2))
      "positive", definition xs (listT intT) (call2 (sdkRef "list#filter") (lambda "x" (greaterThan (variable "x") (int 0))) (variable "xs"))
      "halves",
      definition
          xs
          (listT intT)
          (call2
              (sdkRef "list#filter-map")
              (lambda
                  "x"
                  (ifThenElse
                      (greaterThan (variable "x") (int 0))
                      (apply (sdkCtor "maybe#just") (call2 (sdkRef "basics#integer-divide") (variable "x") (int 2)))
                      (sdkCtor "maybe#nothing")))
              (variable "xs"))
      "total",
      definition
          xs
          intT
          (apply (call2 (sdkRef "list#foldl") (lambda "x" (lambda "acc" (add (variable "acc") (variable "x")))) (int 0)) (variable "xs"))
      "sum-count",
      definition
          xs
          (Type.Tuple(ta, [ intT; intT ]))
          (apply
              (call2
                  (sdkRef "list#foldl")
                  (lambda "x" (Value.Lambda(va, tuplePattern [ "s"; "c" ], Value.Tuple(va, [ add (variable "s") (variable "x"); add (variable "c") (int 1) ]))))
                  (Value.Tuple(va, [ int 0; int 0 ])))
              (variable "xs"))
      "factorial",
      definition
          [ "n", intT ]
          intT
          (letRec
              [ "loop",
                definition
                    [ "acc", intT; "k", intT ]
                    intT
                    (ifThenElse
                        (greaterThan (variable "k") (int 1))
                        (call2 (variable "loop") (multiply (variable "acc") (variable "k")) (subtract (variable "k") (int 1)))
                        (variable "acc")) ]
              (call2 (variable "loop") (int 1) (variable "n")))
      "sum-to",
      definition
          [ "n", intT ]
          intT
          (letRec
              [ "go",
                definition
                    [ "k", intT ]
                    intT
                    (ifThenElse
                        (call2 (sdkRef "basics#less-than-or-equal") (variable "k") (int 0))
                        (int 0)
                        (add (variable "k") (apply (variable "go") (subtract (variable "k") (int 1))))) ]
              (apply (variable "go") (variable "n"))) ]

let private model = library [ "loops", moduleOf [] rows ]

/// The model that the frontend gives for the C# that the backend wrote.
let private throughCSharp (files: Map<string, string>) : Distribution =
    let documents =
        [ for KeyValue(path, text) in files do
              if path <> "Morphir.Polyfill.g.cs" then
                  yield ({ Uri = path; LanguageId = CSharpFrontend.languageId; Version = 0UL; Text = text }: SourceDocument) ]

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
                let messages = Checked.diagnostics result |> List.map (fun d -> $"{d.CodeOrNull}: {d.Message}") |> String.concat "; "
                failwithf "The frontend refused the C# of the backend: %s\n%s" messages (String.concat "\n" files.Values)

let private original =
    match model.Distribution with
    | DistributionModel.V4 distribution -> distribution
    | _ -> failwith "a V4 model is expected"

let private valueOf (distribution: Distribution) (name: string) =
    FrontendSupport.signature distribution "loops" name + " = " + FrontendSupport.valueText distribution "loops" name

let private source (files: Map<string, string>) = files |> Map.toList |> List.filter (fun (path, _) -> path <> "Morphir.Polyfill.g.cs") |> List.map snd |> String.concat "\n"

/// The method of a value in the C# text: from its name to the next method.
let private methodText (files: Map<string, string>) (methodName: string) =
    let text = source files
    let start = text.IndexOf(" " + methodName + "(")

    if start < 0 then
        failtest $"no method {methodName} in:\n{text}"

    let next = text.IndexOf("public static", start + 1)
    if next < 0 then text.Substring start else text.Substring(start, next - start)

let private profiles = [ "imperative", Profile.Imperative; "functional", Profile.Functional ]

let private rowNames = [ "twice"; "doubled"; "positive"; "halves"; "total"; "sum-count"; "factorial"; "sum-to" ]

[<Tests>]
let tests =
    testList
        "C# backend: profiles and the transform table"
        [ for profileName, profile in profiles do
              let files = lazy (generateWithProfile profile model)
              let back = lazy (throughCSharp files.Value)

              for row in rowNames do
                  test $"{row} comes back with the same IR under the {profileName} profile" {
                      Expect.equal (valueOf back.Value row) (valueOf original row) "IR to C# to IR"
                  }

              test $"the C# of the {profileName} profile compiles with no error" {
                  Expect.isEmpty (compileErrors files.Value) "no error"
              }

          test "the imperative profile writes List.map as a foreach that adds to a list" {
              let text = methodText (generateWithProfile Profile.Imperative model) "Twice"
              Expect.stringContains text "foreach (var x in xs)" "a foreach"
              Expect.stringContains text ".Add(" "it adds to a list"
              Expect.isFalse (text.Contains ".Select") "no Select"
          }

          test "the functional profile writes List.map as a Select call" {
              let text = methodText (generateWithProfile Profile.Functional model) "Twice"
              Expect.stringContains text ".Select<int>(" "a Select call"
              Expect.isFalse (text.Contains "foreach") "no loop"
          }

          test "the functional profile writes List.filterMap as one FilterMap call, not Where then Select" {
              let text = methodText (generateWithProfile Profile.Functional model) "Halves"
              Expect.stringContains text ".FilterMap<int>(" "one FilterMap call"
              Expect.isFalse (text.Contains ".Where(") "no Where"
              Expect.isFalse (text.Contains ".Select") "no Select"
          }

          test "the functional profile writes List.filter as Where and List.foldl as Aggregate" {
              let files = generateWithProfile Profile.Functional model
              Expect.stringContains (methodText files "Positive") ".Where(" "Where"
              Expect.stringContains (methodText files "Total") ".Aggregate<int>(" "Aggregate"
          }

          test "a tail-recursive LetRecursion is a while loop with state locals in each profile" {
              for _, profile in profiles do
                  let text = methodText (generateWithProfile profile model) "Factorial"
                  Expect.stringContains text "while (" "a while loop"
                  Expect.stringContains text "int acc = 1;" "a state local"
                  Expect.isFalse (text.Contains "loop(") "no local function"
          }

          test "a LetRecursion that is not a tail call is a local recursive function in each profile" {
              for _, profile in profiles do
                  let text = methodText (generateWithProfile profile model) "SumTo"
                  Expect.stringContains text "int go(int k)" "a local function"
                  Expect.isFalse (text.Contains "while") "no loop"
          }

          test "the code of each profile gives the model results" {
              let samples =
                  library
                      [ "loops",
                        moduleOf
                            []
                            (rows
                             @ [ "twice-sample", definition [] (listT intT) (apply (reference "loops#twice") (Value.List(va, [ int 1; int 2; int 3 ])))
                                 "halves-sample", definition [] (listT intT) (apply (reference "loops#halves") (Value.List(va, [ int -2; int 4; int 6 ])))
                                 "total-sample", definition [] intT (apply (reference "loops#total") (Value.List(va, [ int 1; int 2; int 3 ])))
                                 "sum-count-sample",
                                 definition [] (Type.Tuple(ta, [ intT; intT ])) (apply (reference "loops#sum-count") (Value.List(va, [ int 1; int 2; int 3 ])))
                                 "factorial-sample", definition [] intT (apply (reference "loops#factorial") (int 5))
                                 "sum-to-sample", definition [] intT (apply (reference "loops#sum-to") (int 4)) ]) ]

              for profileName, profile in profiles do
                  let files = generateWithProfile profile samples
                  let run' methodName = run files "Acme.Geo.Loops" methodName []
                  Expect.equal (run' "TwiceSample") "[2, 4, 6]" $"{profileName}: map"
                  Expect.equal (run' "HalvesSample") "[2, 3]" $"{profileName}: filterMap"
                  Expect.equal (run' "TotalSample") "6" $"{profileName}: foldl"
                  Expect.equal (run' "SumCountSample") "(6, 3)" $"{profileName}: foldl with a tuple"
                  Expect.equal (run' "FactorialSample") "120" $"{profileName}: tail recursion"
                  Expect.equal (run' "SumToSample") "10" $"{profileName}: recursion"
          } ]
