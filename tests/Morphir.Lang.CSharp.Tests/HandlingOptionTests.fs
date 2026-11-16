// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The handling options of OPT-1 (task 6.5): the attributes and the configured layer feed one option record, and the
/// nearest carrier wins. One test for each scenario of the csharp-handling-options spec, except the loop lowering to
/// recursion, which comes with the loop recognizer (tasks 6.2 to 6.4).
module Morphir.Lang.CSharp.Tests.HandlingOptionTests

open System
open System.Text.RegularExpressions
open System.Threading
open Expecto
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Backend
open Morphir.Lang.CSharp.Emit
open Morphir.Lang.CSharp.Tests.FrontendSupport

/// A document of the namespace Acme.Trading.Domain. The lines of the body that start with <c>[assembly:</c> go before
/// the namespace, as C# needs.
let private source (body: string) =
    let lines = body.Split('\n') |> List.ofArray
    let assembly = lines |> List.filter (fun line -> line.StartsWith "[assembly:") |> String.concat "\n"
    let rest = lines |> List.filter (fun line -> not (line.StartsWith "[assembly:")) |> String.concat "\n"
    $"using System;\nusing Morphir;\n{assembly}\nnamespace Acme.Trading.Domain;\n{rest}\n"

let private modelOf (body: string) : VersionedModel = {
    SourceVersion = SourceFormatVersion.Create "4"
    Distribution = DistributionModel.V4(model (compileSource (source body)))
}

let private handling (pairs: (string * string) list) : HandlingOptions =
    pairs
    |> List.fold
        (fun options (key, word) ->
            match HandlingOptions.trySet key word options with
            | Ok updated -> updated
            | Error message -> failtest message)
        HandlingOptions.empty

let private optionsWith (commandLine: (string * string) list) (configured: (string * string) list) : GenerationOptions = {
    GenerationOptions.Default with
        CommandLine = handling commandLine
        Configured = handling configured
}

let private generateWith (options: GenerationOptions) (model: VersionedModel) : Map<string, string> =
    let result = CSharpBackend.Generate(model, options, CancellationToken.None)

    match result.Status with
    | GenerationStatus.Success -> result.Sources |> List.map (fun s -> s.RelativePath, s.Text) |> Map.ofList
    | _ -> failtestf "generation failed: %A" result.Diagnostics

let private allText (files: Map<string, string>) = files |> Map.toList |> List.map snd |> String.concat "\n"

/// The files without the option attributes that the backend writes back from the carrier.
let private withoutOptionAttributes (files: Map<string, string>) =
    files |> Map.map (fun _ text -> Regex.Replace(text, @"\[global::Morphir\.Morphir(Equality|Emit)\([^\]]*\)\]\s*", ""))

let private quotes =
    "[MorphirEquality(FloatEquality.Ieee)]\npublic partial record Quote([MorphirEquality(FloatEquality.Clr)] double Bid, double Ask);"

/// Whether a Quote with the two fields is equal to a second Quote with the same fields, in the generated C#.
let private quoteEqualsCopy (files: Map<string, string>) (bid: float) (ask: float) =
    let assembly = BackendSupport.load files
    let first = BackendSupport.make assembly "Acme.Trading.Domain.Quote" [ box bid; box ask ]
    let second = BackendSupport.make assembly "Acme.Trading.Domain.Quote" [ box bid; box ask ]
    first.Equals second

/// Whether a record with one double is equal to its copy in the generated C#.
let private priceEqualsCopy (files: Map<string, string>) (value: float) =
    let assembly = BackendSupport.load files
    let first = BackendSupport.make assembly "Acme.Trading.Domain.Price" [ box value ]
    let second = BackendSupport.make assembly "Acme.Trading.Domain.Price" [ box value ]
    first.Equals second

let private outputOf (distribution: Distribution) (modulePath: string) (valueName: string) : Type =
    match moduleOf distribution modulePath with
    | None -> failtest $"No module {modulePath}"
    | Some modul ->
        match modul.Values |> OrderedMap.toList |> List.tryFind (fun (n, _) -> Name.toKebabCase n = valueName) with
        | Some(_, access) ->
            match access.Value.Value.Body with
            | ExpressionBody(output, _) -> output
            | _ -> failtest "an expression body is expected"
        | None -> failtest $"No value {valueName}"

let private distributionOf (model: VersionedModel) =
    match model.Distribution with
    | DistributionModel.V4 distribution -> distribution
    | _ -> failtest "a V4 model is expected"

let private calc =
    "public static class Calc\n{\n    [MorphirEmit(Traversal = TraversalForm.Functional)]\n    public static int Twice(int x) => x + x;\n}"

[<Tests>]
let tests =
    testList "Handling options" [
        testList "precedence" [
            test "the nearest carrier wins: command line, member, type, assembly, morphir.toml, profile default" {
                let layer pairs = handling pairs
                let commandLine = layer [ "float-equality", "clr" ]
                let member' = layer [ "float-equality", "ieee"; "traversal", "loop" ]
                let type' = layer [ "traversal", "functional"; "tail-recursion", "local-function" ]
                let assembly = layer [ "loop-lowering", "recursion" ]
                let configured = layer [ "profile", "functional" ]
                let effective = HandlingOptions.resolve [ commandLine; member'; type'; assembly; configured ]

                Expect.equal effective.FloatEquality FloatEquality.Clr "the command line wins over the member"
                Expect.equal effective.Traversal TraversalForm.Loop "the member wins over the type and the profile"
                Expect.equal effective.TailRecursion TailRecursionForm.LocalFunction "the type sets what the member does not"
                Expect.equal effective.LoopLowering LoopLowering.Recursion "the assembly sets what the others do not"
                Expect.equal effective.Profile Profile.Functional "morphir.toml sets the profile"
            }

            test "the profile gives the default traversal form, and the other defaults are the same in each profile" {
                let imperative = HandlingOptions.resolve []
                let functional = HandlingOptions.resolve [ handling [ "profile", "functional" ] ]

                Expect.equal imperative.Traversal TraversalForm.Loop "the imperative profile writes loops"
                Expect.equal functional.Traversal TraversalForm.Functional "the functional profile writes functional forms"
                Expect.equal functional.TailRecursion TailRecursionForm.Loop "a tail call is a loop in every profile"
                Expect.equal functional.FloatEquality FloatEquality.Clr "plain equality is the default"
                Expect.equal functional.LoopLowering LoopLowering.Traversal "a loop lowers by shape by default"
            }

            test "the options print their words with no reflection printer" {
                let options = optionsWith [ "float-equality", "ieee"; "profile", "functional" ] [ "traversal", "loop" ]

                Expect.equal (string options) "C# 15, command line [profile=functional float-equality=ieee], configured [traversal=loop]" "the text of the options"
                Expect.equal (string (HandlingOptions.resolve [])) "profile=imperative traversal=loop tail-recursion=loop float-equality=clr loop-lowering=traversal" "the effective options"
                Expect.equal (string FloatEquality.Ieee) "ieee" "a value prints its word"
            }

            test "the backend takes only its own handling options: loop lowering is a frontend option" {
                match BackendHandling.parse [ "loop-lowering", "recursion" ] with
                | Ok _ -> failtest "loop lowering is not a backend option"
                | Error diagnostic ->
                    Expect.equal diagnostic.Rule "invalid-backend-option" "the rule"
                    Expect.stringContains diagnostic.Message "'float-equality' (clr or ieee)" "the message names the options"
            }

            test "a word that is not a value of its key names the accepted words" {
                match HandlingOptions.trySet "float-equality" "maybe" HandlingOptions.empty with
                | Ok _ -> failtest "the word is not a value"
                | Error message -> Expect.stringContains message "clr, ieee" "the message names the words"
            }
        ]

        testList "carriers" [
            test "Same effect: an attribute and a configuration key give the same C#, apart from the attribute that the backend writes back" {
                let byAttribute = modelOf "[MorphirEquality(FloatEquality.Ieee)]\npublic partial record Price(double Value);"
                let byKey = modelOf "public partial record Price(double Value);"

                let fromAttribute = generateWith GenerationOptions.Default byAttribute
                let fromKey = generateWith (optionsWith [] [ "float-equality", "ieee" ]) byKey

                Expect.equal (withoutOptionAttributes fromAttribute) fromKey "the two projects give the same C#"
                Expect.isFalse (priceEqualsCopy fromKey nan) "the key gives IEEE equality"
            }

            test "Same effect for the traversal form: the attribute and the key give the same C#, apart from the attribute" {
                let byAttribute = modelOf calc
                let byKey = modelOf "public static class Calc\n{\n    public static int Twice(int x) => x + x;\n}"

                let fromAttribute = generateWith GenerationOptions.Default byAttribute
                let fromKey = generateWith (optionsWith [] [ "traversal", "functional" ]) byKey

                Expect.equal (withoutOptionAttributes fromAttribute) fromKey "the two projects give the same C#"
            }

            test "Member over type: the member has plain equality and the rest of the type has IEEE equality" {
                let files = generateWith GenerationOptions.Default (modelOf quotes)

                Expect.isTrue (quoteEqualsCopy files nan 1.0) "Bid has plain equality: NaN is equal to NaN"
                Expect.isFalse (quoteEqualsCopy files 1.0 nan) "Ask has the IEEE equality of the type"
            }

            test "Command line over attribute: the command-line value is used" {
                let files = generateWith (optionsWith [ "float-equality", "clr" ] []) (modelOf quotes)

                Expect.isTrue (quoteEqualsCopy files 1.0 nan) "plain equality from the command line"
                Expect.isFalse ((allText files).Contains "MorphirEquals") "no equality members are written"
            }

            test "an attribute wins over the configured key, and the assembly attribute applies to each type" {
                let model =
                    modelOf "[assembly: MorphirEquality(FloatEquality.Ieee)]\npublic partial record Quote(double Bid, double Ask);\n[MorphirEquality(FloatEquality.Clr)]\npublic record Price(double Value);"

                let files = generateWith (optionsWith [] [ "float-equality", "clr" ]) model

                Expect.isFalse (quoteEqualsCopy files 1.0 nan) "the assembly attribute wins over the key"
                Expect.isTrue (priceEqualsCopy files nan) "the type attribute wins over the assembly attribute"
            }

            test "the attribute of a static class applies to each method, and the attribute of a method wins over it" {
                let model =
                    modelOf
                        "[MorphirEmit(Traversal = TraversalForm.Functional)]\npublic static class Calc\n{\n    [MorphirEmit(Traversal = TraversalForm.Loop, TailRecursion = TailRecursionForm.LocalFunction)]\n    public static int Once(int x) => x;\n    public static int Twice(int x) => x + x;\n}"

                let distribution = distributionOf model
                let once = HandlingCarrier.read (outputOf distribution "domain.calc" "once")
                let twice = HandlingCarrier.read (outputOf distribution "domain.calc" "twice")

                Expect.equal once { HandlingOptions.empty with Traversal = Some TraversalForm.Loop; TailRecursion = Some TailRecursionForm.LocalFunction } "the method attribute wins"
                Expect.equal twice { HandlingOptions.empty with Traversal = Some TraversalForm.Functional } "the class attribute applies"
            }

            test "a value with no attribute has no carrier" {
                let distribution = distributionOf (modelOf "public static class Calc\n{\n    public static int Twice(int x) => x + x;\n}")
                Expect.equal (HandlingCarrier.read (outputOf distribution "domain.calc" "twice")) HandlingOptions.empty "no carrier"
            }
        ]

        testList "frontend and backend options" [
            test "Backend option and IR: two traversal forms give the same model code, and the generated C# differs" {
                let loop = modelOf (calc.Replace("TraversalForm.Functional", "TraversalForm.Loop"))
                let functional = modelOf calc

                let text (model: VersionedModel) = valueText (distributionOf model) "domain.calc" "twice", signature (distributionOf model) "domain.calc" "twice"
                Expect.equal (text loop) (text functional) "the bodies and the signatures are equal"
                Expect.notEqual (generateWith GenerationOptions.Default loop) (generateWith GenerationOptions.Default functional) "the C# differs"
            }

            test "Backend option and IR: a float equality option changes the C# and not the model" {
                let model = modelOf "public record Price(double Value);"
                let plain = generateWith GenerationOptions.Default model
                let ieee = generateWith (optionsWith [ "float-equality", "ieee" ] []) model

                Expect.notEqual plain ieee "the C# differs"
                Expect.isTrue (priceEqualsCopy plain nan) "plain equality"
                Expect.isFalse (priceEqualsCopy ieee nan) "IEEE equality"
            }

            test "Round trip of an option: the generated C# has the attribute, and the frontend reads the same carrier back" {
                let files = generateWith GenerationOptions.Default (modelOf calc)

                Expect.stringContains (allText files) "[global::Morphir.MorphirEmit(Traversal = global::Morphir.TraversalForm.Functional)]" "the attribute is written back"

                let documents = [ for KeyValue(path, text) in files do if path <> "Morphir.Polyfill.g.cs" then document path text ]
                let again = model (compile documents)
                let carrier = HandlingCarrier.read (outputOf again "domain.calc" "twice")

                Expect.equal carrier { HandlingOptions.empty with Traversal = Some TraversalForm.Functional } "the same carrier comes back"
            }

            test "Round trip of an option: a loop with the functional traversal is generated in the functional form, with the attribute" {
                let loops =
                    "public static class Calc\n{\n    [MorphirEmit(Traversal = TraversalForm.Functional)]\n    public static int Sum(System.Collections.Generic.IReadOnlyList<int> xs)\n    {\n        var t = 0;\n        foreach (var x in xs) { t += x; }\n        return t;\n    }\n}"

                let first = modelOf loops
                let files = generateWith GenerationOptions.Default first
                let text = allText files

                Expect.stringContains text "[global::Morphir.MorphirEmit(Traversal = global::Morphir.TraversalForm.Functional)]" "the attribute is written back"
                Expect.stringContains text ".Aggregate<int>(" "the functional form of the fold, under the default imperative profile"
                Expect.isFalse (text.Contains "foreach") "no loop form"

                let documents = [ for KeyValue(path, text) in files do if path <> "Morphir.Polyfill.g.cs" then document path text ]
                let again = model (compile documents)
                Expect.equal (valueText again "domain.calc" "sum") (valueText (distributionOf first) "domain.calc" "sum") "the same IR"
                Expect.equal (HandlingCarrier.read (outputOf again "domain.calc" "sum")) { HandlingOptions.empty with Traversal = Some TraversalForm.Functional } "the same carrier"
            }

            test "the backend resolves the options of each value: the command line, the carrier, the key, the profile" {
                let distribution = distributionOf (modelOf calc)
                let output = outputOf distribution "domain.calc" "twice"

                let effective commandLine configured = BackendHandling.value (optionsWith commandLine configured) output

                Expect.equal (effective [] []).Traversal TraversalForm.Functional "the carrier"
                Expect.equal (effective [ "traversal", "loop" ] []).Traversal TraversalForm.Loop "the command line wins over the carrier"
                Expect.equal (effective [] [ "traversal", "loop" ]).Traversal TraversalForm.Functional "the carrier wins over the key"
                Expect.equal (effective [ "profile", "functional" ] [ "tail-recursion", "local-function" ]).TailRecursion TailRecursionForm.LocalFunction "the key sets what the carrier does not"
            }

            test "Refused loop with an option: the result has the same refusal as without the attribute" {
                let loop (attribute: string) =
                    source $"public static class Counter\n{{\n    {attribute}\n    public static int Last(int[] xs, int[] ys)\n    {{\n        foreach (var x in xs) {{ ys[0] = x; }}\n        return ys[0];\n    }}\n}}"

                let refused = codes (compileSource (loop ""))
                Expect.isNonEmpty refused "the loop is refused"
                Expect.equal (codes (compileSource (loop "[MorphirLoop(LoopLowering.Recursion)]"))) refused "the same refusal"
            }

            test "the loop lowering of a method comes from the method, its class and the assembly" {
                match CSharpCompiler.compile [ document "Model.cs" (source "[assembly: MorphirLoop(LoopLowering.Recursion)]\npublic static class Counter\n{\n    public static int One() => 1;\n    [MorphirLoop(LoopLowering.Traversal)]\n    public static int Two() => 2;\n}") ] with
                | Error failure -> failtest failure.Message
                | Ok compiled ->
                    let counter =
                        match compiled.Compilation.GetTypeByMetadataName "Acme.Trading.Domain.Counter" with
                        | null -> failtest "No type Counter"
                        | found -> found

                    let lowering (name: string) = (OptionAttributes.ofMember (counter.GetMembers(name) |> Seq.head)).LoopLowering

                    Expect.equal (lowering "One") (Some LoopLowering.Recursion) "the assembly attribute"
                    Expect.equal (lowering "Two") (Some LoopLowering.Traversal) "the method attribute wins"
            }
        ]

        testList "equality" [
            test "Default equality: two generated records that hold NaN are equal" {
                Expect.isTrue (priceEqualsCopy (generateWith GenerationOptions.Default (modelOf "public record Price(double Value);")) nan) "plain C# equality"
            }

            test "IEEE equality: a project with attributes and no morphir.toml gets the attribute values" {
                let files = generateWith GenerationOptions.Default (modelOf "[MorphirEquality(FloatEquality.Ieee)]\npublic partial record Price(double Value);")

                Expect.isFalse (priceEqualsCopy files nan) "NaN is not equal to NaN"
                Expect.stringContains (allText files) "[global::Morphir.MorphirEquality(global::Morphir.FloatEquality.Ieee)]" "the attribute is written back"
            }

            test "Type that is not partial: the frontend refuses the type and asks for partial" {
                let result = compileSource (source "[MorphirEquality(FloatEquality.Ieee)]\npublic record Price(double Value);")
                Expect.contains (codes result) "csharp.unsupported_non_partial_equality" "the rule of the analyzer"
            }
        ]

        testList "value classes" [
            test "the attribute of a type parameter is the first source of its class" {
                let distribution =
                    distributionOf (modelOf "public static class Calc\n{\n    public static TValue Same<[MorphirValueClass(ValueClass.Comparable)] TValue>(TValue a, TValue b) where TValue : IEquatable<TValue> => a;\n}")

                let definition =
                    match moduleOf distribution "domain.calc" with
                    | Some modul -> (modul.Values |> OrderedMap.toList |> List.head |> snd).Value.Value
                    | None -> failtest "No module domain.calc"

                Expect.equal (FrontendValueClassTests.occurrences definition) [ "value", [ "comparable" ]; "value", [ "comparable" ]; "value", [ "comparable" ] ] "the attribute wins over the constraint"
            }
        ]
    ]
