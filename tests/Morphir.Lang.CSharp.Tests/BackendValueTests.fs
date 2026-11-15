// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendValueTests

open Expecto
open Morphir.Lang.CSharp.Tests.BackendSupport

let private nan = System.Double.NaN

let private compileErrorsOf (files: Map<string, string>) : string list =
    let documents =
        [ for KeyValue(path, text) in files ->
              ({ Uri = path; LanguageId = Morphir.Lang.CSharp.CSharpFrontend.languageId; Version = 0UL; Text = text }: Morphir.Compilation.SourceDocument) ]

    match Morphir.Lang.CSharp.CSharpCompiler.compile documents with
    | Error failure -> [ failure.Message ]
    | Ok compiled ->
        Morphir.Lang.CSharp.CSharpCompiler.compilerDiagnostics compiled System.Threading.CancellationToken.None
        |> List.filter (fun diagnostic -> diagnostic.IsError)
        |> List.map (fun diagnostic -> $"{diagnostic.CodeOrNull}: {diagnostic.Message}")

let private add = sdkRef "basics#add"
let private subtract = sdkRef "basics#subtract"
let private multiply = sdkRef "basics#multiply"
let private atMost = sdkRef "basics#lessthanorequal"

let private loops (values: (string * Morphir.IR.V4.ValueDefinition) list) = library [ "loops", moduleOf [] values ]

let private tailRecursiveSum =
    // sum n = let go acc k = if k <= 0 then acc else go (acc + k) (k - 1) in go 0 n
    let go =
        definition
            [ "acc", intT; "k", intT ]
            intT
            (ifThenElse
                (call2 atMost (variable "k") (int 0))
                (variable "acc")
                (call2 (variable "go") (call2 add (variable "acc") (variable "k")) (call2 subtract (variable "k") (int 1))))

    definition [ "n", intT ] intT (letRec [ "go", go ] (call2 (variable "go") (int 0) (variable "n")))

let private countByOne =
    // count n = let go acc k = if k <= 0 then acc else go (acc + 1) (k - 1) in go 0 n
    let go =
        definition
            [ "acc", intT; "k", intT ]
            intT
            (ifThenElse
                (call2 atMost (variable "k") (int 0))
                (variable "acc")
                (call2 (variable "go") (call2 add (variable "acc") (int 1)) (call2 subtract (variable "k") (int 1))))

    definition [ "n", intT ] intT (letRec [ "go", go ] (call2 (variable "go") (int 0) (variable "n")))

let private factorial =
    // factorial n = let fact k = if k <= 1 then 1 else k * fact (k - 1) in fact n
    let fact =
        definition
            [ "k", intT ]
            intT
            (ifThenElse
                (call2 atMost (variable "k") (int 1))
                (int 1)
                (call2 multiply (variable "k") (apply (variable "fact") (call2 subtract (variable "k") (int 1)))))

    definition [ "n", intT ] intT (letRec [ "fact", fact ] (apply (variable "fact") (variable "n")))

let private evenOdd =
    // isEven n = let even k = if k <= 0 then true else odd (k - 1); odd k = if k <= 0 then false else even (k - 1) in even n
    let even =
        definition [ "k", intT ] boolT (ifThenElse (call2 atMost (variable "k") (int 0)) (Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.BoolLiteral true)) (apply (variable "odd") (call2 subtract (variable "k") (int 1))))

    let odd =
        definition [ "k", intT ] boolT (ifThenElse (call2 atMost (variable "k") (int 0)) (Morphir.IR.V4.Value.Literal(va, Morphir.IR.V4.BoolLiteral false)) (apply (variable "even") (call2 subtract (variable "k") (int 1))))

    definition [ "n", intT ] boolT (letRec [ "even", even; "odd", odd ] (apply (variable "even") (variable "n")))

let private listT = sdk "list#list" [ intT ]
let private lambda (parameter: string) (body: Morphir.IR.V4.Value) = Morphir.IR.V4.Value.Lambda(va, asPattern parameter, body)
let private greaterThan = sdkRef "basics#greaterthan"

let private listValues =
    [ "positives", definition [ "xs", listT ] listT (call2 (sdkRef "list#filter") (lambda "x" (call2 greaterThan (variable "x") (int 0))) (variable "xs"))
      "total", definition [ "xs", listT ] intT (apply (call2 (sdkRef "list#foldl") (lambda "x" (lambda "acc" (call2 add (variable "x") (variable "acc")))) (int 0)) (variable "xs"))
      "doubled-positives",
      definition
          [ "xs", listT ]
          listT
          (call2
              (sdkRef "list#filtermap")
              (lambda
                  "x"
                  (ifThenElse
                      (call2 greaterThan (variable "x") (int 0))
                      (apply (Morphir.IR.V4.Value.Constructor(va, fq "morphir/SDK:maybe#just")) (call2 multiply (variable "x") (int 2)))
                      (Morphir.IR.V4.Value.Constructor(va, fq "morphir/SDK:maybe#nothing"))))
              (variable "xs")) ]

let private ints (items: int list) : objnull = box (Morphir.SDK.CSharp.List<int>.FromEnumerable items)

[<Tests>]
let tests =
    testList "C# backend on Roslyn: local recursion" [
        test "a tail call becomes a while loop, and the program gives the model result" {
            let files = generate (loops [ "sum", tailRecursiveSum; "count", countByOne ])
            let text = files["p_Acme.Geo/m_LoopsModule.g.cs"]
            Expect.stringContains text "while (true)" "a loop"
            Expect.stringContains text "continue;" "the tail call goes round again"
            Expect.equal (run files "Acme.Geo.Loops" "Sum" [ box 100 ]) "5050" "sum of 1 to 100"
            // A depth that would overflow the stack as a recursive call.
            Expect.equal (run files "Acme.Geo.Loops" "Count" [ box 5_000_000 ]) "5000000" "a depth that would overflow the stack as a recursive call"
        }

        test "any other recursion is a local function, and the program gives the model result" {
            let files = generate (loops [ "factorial", factorial ])
            let text = files["p_Acme.Geo/m_LoopsModule.g.cs"]
            Expect.isFalse (text.Contains "while (true)") "no loop"
            Expect.equal (run files "Acme.Geo.Loops" "Factorial" [ box 5 ]) "120" "5!"
        }

        test "mutually recursive local functions call each other" {
            let files = generate (loops [ "is-even", evenOdd ])
            Expect.equal (run files "Acme.Geo.Loops" "IsEven" [ box 10 ]) "True" "10 is even"
            Expect.equal (run files "Acme.Geo.Loops" "IsEven" [ box 7 ]) "False" "7 is odd"
        }
    
        test "the list functions of the transform table run: filter, filterMap and foldl" {
            let files = generate (library [ "lists", moduleOf [] listValues ])
            Expect.equal (run files "Acme.Geo.Lists" "Positives" [ ints [ 1; -2; 3 ] ]) "[1, 3]" "filter"
            Expect.equal (run files "Acme.Geo.Lists" "Total" [ ints [ 1; 2; 3 ] ]) "6" "foldl"
            Expect.equal (run files "Acme.Geo.Lists" "DoubledPositives" [ ints [ 1; -2; 3 ] ]) "[2, 6]" "filterMap"
        }
    ]

let private readings =
    library [ "sensors", moduleOf [ "reading", alias [] (recordT [ "value", floatT ]) ] [] ]

let private readingName = fq "acme/geo:sensors#reading"

[<Tests>]
let equalityTests =
    testList "C# backend on Roslyn: equality" [
        test "a record has the equality of plain C# by default: NaN is equal to NaN" {
            let files = generate readings
            Expect.isFalse (files["p_Acme.Geo/m_SensorsModule.g.cs"].Contains "MorphirEquals") "no Morphir equality member"
            let assembly = load files
            let a = make assembly "Acme.Geo.Sensors.Reading" [ box nan ]
            let b = make assembly "Acme.Geo.Sensors.Reading" [ box nan ]
            Expect.isTrue (a.Equals b) "plain equality: NaN equals NaN"
        }

        test "with the IEEE option a record has the four members and a NaN field makes two records unequal" {
            let files = generateIeee readings (fun name -> name = readingName)
            let text = files["p_Acme.Geo/m_SensorsModule.g.cs"]

            for member' in [ "MorphirEquals"; "MorphirHashCode"; "Equals"; "GetHashCode" ] do
                Expect.stringContains text member' $"the member {member'}"

            let assembly = load files
            let a = make assembly "Acme.Geo.Sensors.Reading" [ box nan ]
            let b = make assembly "Acme.Geo.Sensors.Reading" [ box nan ]
            Expect.isFalse (a.Equals b) "IEEE equality: NaN is not equal to NaN"
            let c = make assembly "Acme.Geo.Sensors.Reading" [ box 1.5 ]
            let d = make assembly "Acme.Geo.Sensors.Reading" [ box 1.5 ]
            Expect.isTrue (c.Equals d) "equal values are equal"
            Expect.equal (c.GetHashCode()) (d.GetHashCode()) "and have the same hash"
        }

        test "with the IEEE option a union has the members and the operators of equality" {
            let model =
                library
                    [ "sensors",
                      moduleOf
                          [ "reading", alias [] (recordT [ "value", floatT ])
                            "fault", alias [] (recordT [ "code", intT ])
                            "signal", custom [] [ "reading", [ "value", own "sensors" "reading" [] ]; "fault", [ "value", own "sensors" "fault" [] ] ] ]
                          [] ]

            let files = generateIeee model (fun name -> name = fq "acme/geo:sensors#signal")
            let text = files["p_Acme.Geo/m_SensorsModule.g.cs"]
            Expect.stringContains text "public union Signal" "a union"
            Expect.stringContains text "operator ==" "the operator =="
            Expect.stringContains text "operator !=" "the operator !="
            Expect.stringContains text "MorphirEquals" "the Morphir equality member"
            Expect.equal (compileErrorsOf files) [] "the union with its members compiles"
        }
    ]
