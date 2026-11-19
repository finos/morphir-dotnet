// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Analyzers.Tests.LoopRecognizerTests

open System
open System.IO
open Expecto
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Microsoft.CodeAnalysis.FlowAnalysis
open Microsoft.CodeAnalysis.Operations
open Morphir.Lang.CSharp.Analyzers
open Morphir.Lang.CSharp.Analyzers.Tests.AnalyzerSupport

/// The ten loops of the probe in spikes/csharp-language-support/roslyn-loop/Program.cs.
let probeSource =
    String.concat
        "\n"
        [ "using System.Collections.Generic;"
          "namespace Acme.Trading;"
          "public record Order(decimal Quantity, decimal Price);"
          "public static class Trading"
          "{"
          "    private static int counter;"
          "    public static decimal Notional(Order order) => order.Quantity * order.Price;"
          ""
          "    public static decimal Total(IReadOnlyList<Order> orders)"
          "    {"
          "        var total = 0m;"
          "        foreach (var o in orders) { total += Notional(o); }"
          "        return total;"
          "    }"
          "    public static List<decimal> Notionals(IReadOnlyList<Order> orders)"
          "    {"
          "        var result = new List<decimal>();"
          "        foreach (var o in orders) { result.Add(Notional(o)); }"
          "        return result;"
          "    }"
          "    public static List<Order> Large(IReadOnlyList<Order> orders)"
          "    {"
          "        var result = new List<Order>();"
          "        foreach (var o in orders) { if (o.Price > 10m) result.Add(o); }"
          "        return result;"
          "    }"
          "    public static List<decimal> LargeNotionals(IReadOnlyList<Order> orders)"
          "    {"
          "        var result = new List<decimal>();"
          "        foreach (var o in orders) { if (o.Price > 10m) result.Add(Notional(o)); }"
          "        return result;"
          "    }"
          "    public static (decimal, int) SumAndCount(IReadOnlyList<Order> orders)"
          "    {"
          "        var sum = 0m; var count = 0;"
          "        foreach (var o in orders) { sum += o.Price; count++; }"
          "        return (sum, count);"
          "    }"
          "    public static int Factorial(int n)"
          "    {"
          "        var acc = 1;"
          "        while (n > 1) { acc *= n; n--; }"
          "        return acc;"
          "    }"
          "    public static int FirstIndexOver(IReadOnlyList<Order> orders, decimal limit)"
          "    {"
          "        for (var i = 0; i < orders.Count; i++) { if (orders[i].Price > limit) return i; }"
          "        return -1;"
          "    }"
          "    public static decimal Counted(IReadOnlyList<Order> orders)"
          "    {"
          "        var total = 0m;"
          "        foreach (var o in orders) { total += o.Price; counter++; }"
          "        return total;"
          "    }"
          "    public static decimal UntilNegative(IReadOnlyList<Order> orders)"
          "    {"
          "        var total = 0m;"
          "        foreach (var o in orders) { if (o.Price < 0m) break; total += o.Price; }"
          "        return total;"
          "    }"
          "    public static void Fill(decimal[] target, decimal value)"
          "    {"
          "        for (var i = 0; i < target.Length; i++) { target[i] = value; }"
          "    }"
          "}" ]

/// The value, or a failed test when it is null.
let private required (value: 'T | null) : 'T =
    match value with
    | null -> failtest "a value is expected"
    | found -> found

let private expectedPath =
    Path.GetFullPath(
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "spikes", "csharp-language-support", "roslyn-loop", "expected-output.txt")
    )

let private compileProbe (source: string) =
    let tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions(LanguageVersion.Preview), path = "Trading.cs")

    let compilation =
        CSharpCompilation.Create("Model", [ tree ], references, CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))

    let errors =
        compilation.GetDiagnostics() |> Seq.filter (fun d -> d.Severity = DiagnosticSeverity.Error) |> Seq.map string |> List.ofSeq

    Expect.isEmpty errors "the probe is valid C#"
    tree, compilation.GetSemanticModel(tree, false)

/// The loops of each method, with the shape that the recognizer gives.
let private loopsOf (source: string) =
    let tree, model = compileProbe source

    [ for method in tree.GetRoot().DescendantNodes() |> Seq.choose (function :? MethodDeclarationSyntax as m -> Some m | _ -> None) do
          for node: SyntaxNode in method.DescendantNodes() do
              match node with
              | :? ForEachStatementSyntax
              | :? WhileStatementSyntax
              | :? ForStatementSyntax
              | :? DoStatementSyntax ->
                  let loop = required (model.GetOperation node) :?> ILoopOperation
                  yield method, loop, LoopRecognizer.Recognize(loop, Threading.CancellationToken.None)
              | _ -> () ]

let private names (symbols: ISymbol seq) =
    symbols |> Seq.map _.Name |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b)) |> String.concat ","

/// The decision in the words of the probe.
let private decision (shape: LoopShape) =
    match shape.Form with
    | LoopForm.Map -> "List.map"
    | LoopForm.Filter -> "List.filter"
    | LoopForm.FilterMap -> "List.filterMap"
    | LoopForm.Fold when shape.State.Length = 1 -> "List.foldl"
    | LoopForm.Fold -> $"List.foldl (state is a tuple of {shape.State.Length})"
    | LoopForm.Recursion ->
        $"LetRecursion (parameters: {names (Seq.cast shape.State)}; exits: {shape.Breaks} break, {shape.Returns} return)"
    | _ ->
        let refusal = required shape.Refusal

        match refusal.Kind with
        | LoopRefusalKind.WritesField
        | LoopRefusalKind.WritesProperty -> $"REFUSE (writes {refusal.Subject})"
        | LoopRefusalKind.WritesArrayElement -> "REFUSE (writes array element)"
        | _ -> $"REFUSE ({refusal.Reason})"

let private line (method: MethodDeclarationSyntax) (loop: ILoopOperation) (shape: LoopShape) =
    let model = required loop.SemanticModel
    let cfg = required (ControlFlowGraph.Create(method, model))

    sprintf
        "%-15s %-8s state=[%s] declared=[%s] flowsOut=[%s] cfgBlocks=%d => %s"
        method.Identifier.Text
        (string loop.LoopKind)
        (names (Seq.cast shape.State))
        (names (Seq.cast shape.Declared))
        (names (Seq.cast shape.FlowsOut))
        cfg.Blocks.Length
        (decision shape)

/// The shape of the only loop of a source.
let private shapeOf (source: string) =
    match loopsOf source with
    | [ _, _, shape ] -> shape
    | other -> failtest $"one loop expected, found {List.length other}"

let private inClass (members: string) =
    "using System.Collections.Generic;\npublic record Order(decimal Quantity, decimal Price);\npublic static class M\n{\n"
    + members
    + "\n}\n"

[<Tests>]
let tests =
    testList
        "LoopRecognizer"
        [ test "the ten loops of the probe give the expected output of the spike" {
              let expected =
                  File.ReadAllLines expectedPath |> Array.filter (fun l -> l.Trim() <> "") |> List.ofArray

              let actual = loopsOf probeSource |> List.map (fun (method, loop, shape) -> line method loop shape)
              Expect.sequenceEqual actual expected "the spike output"
          }

          test "the state is in the order of the first write" {
              let shape = shapeOf (inClass "static int F(int n) { var acc = 1; while (n > 1) { acc *= n; n--; } return acc; }")
              Expect.equal (shape.State |> Seq.map _.Name |> List.ofSeq) [ "acc"; "n" ] "acc is written first"
          }

          test "the variables of a for initializer are state before the locals of the body" {
              let shape = shapeOf (inClass "static int F(int n) { var t = 0; for (var i = 0; i < n; i++) { t += i; } return t; }")
              Expect.equal shape.Form LoopForm.Recursion "a for loop is recursion"
              Expect.equal (shape.State |> Seq.map _.Name |> List.ofSeq) [ "i"; "t" ] "i, then t"
          }

          test "a map gives the list, the added value and no condition" {
              let shape =
                  shapeOf (inClass "static List<decimal> F(IReadOnlyList<Order> os) { var r = new List<decimal>(); foreach (var o in os) { r.Add(o.Price); } return r; }")

              Expect.equal shape.Form LoopForm.Map "map"
              Expect.equal (required shape.Builder).Name "r" "the list"
              Expect.equal (required shape.Variable).Name "o" "the loop variable"
              Expect.isNull shape.Condition "no condition"
              Expect.isNotNull shape.Added "the added value"
          }

          test "a map can declare locals before the add" {
              let shape =
                  shapeOf (inClass "static List<decimal> F(IReadOnlyList<Order> os) { var r = new List<decimal>(); foreach (var o in os) { var p = o.Price; r.Add(p); } return r; }")

              Expect.equal shape.Form LoopForm.Map "map"
          }

          test "a do loop is recursion" {
              let shape = shapeOf (inClass "static int F(int n) { do { n--; } while (n > 0); return n; }")
              Expect.equal shape.Form LoopForm.Recursion "recursion"
              Expect.equal shape.Keyword "do" "the keyword"
          }

          test "a foreach that returns early is recursion with one return" {
              let shape =
                  shapeOf (inClass "static decimal F(IReadOnlyList<Order> os) { foreach (var o in os) { if (o.Price > 1m) return o.Price; } return 0m; }")

              Expect.equal shape.Form LoopForm.Recursion "recursion"
              Expect.equal shape.Returns 1 "one return"
              Expect.isEmpty shape.State "no state"
          }

          test "a loop that changes nothing is refused" {
              let shape = shapeOf (inClass "static int F(IReadOnlyList<int> xs) { foreach (var x in xs) { } return 0; }")
              Expect.equal shape.Form LoopForm.Refused "refused"
              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.NoEffect "no effect"
          }

          test "a loop that reads a field that can change is refused with the name of the field" {
              let shape =
                  shapeOf (inClass "static int seed;\nstatic int F(IReadOnlyList<int> xs) { var t = 0; foreach (var x in xs) { t += seed; } return t; }")

              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.ReadsField "reads a field"
              Expect.equal (required shape.Refusal).Subject "seed" "the field"
              Expect.stringContains (required shape.Refusal).Reason "'seed'" "the reason names the field"
          }

          test "a loop that writes a property is refused" {
              let shape =
                  shapeOf (inClass "public sealed class Box { public int V { get; set; } }\nstatic int F(IReadOnlyList<int> xs, Box b) { foreach (var x in xs) { b.V = x; } return 0; }")

              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.WritesProperty "writes a property"
              Expect.equal (required shape.Refusal).Subject "V" "the property"
          }

          test "an array element write names the effect" {
              let shape = shapeOf (inClass "static void F(decimal[] t) { for (var i = 0; i < t.Length; i++) { t[i] = 0m; } }")
              Expect.equal (required shape.Refusal).Reason "writes an array element" "the reason"
          }

          test "a list that is built and a local that changes in one loop are refused" {
              let shape =
                  shapeOf (inClass "static List<int> F(IReadOnlyList<int> xs) { var r = new List<int>(); var n = 0; foreach (var x in xs) { r.Add(x); n++; } return r; }")

              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.NoRule "no rule"
          }

          test "two adds in one loop are refused" {
              let shape =
                  shapeOf (inClass "static List<int> F(IReadOnlyList<int> xs) { var r = new List<int>(); foreach (var x in xs) { if (x > 0) r.Add(x); else r.Add(0); } return r; }")

              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.NoRule "no rule"
          }

          test "a while loop that builds a list is refused" {
              let shape =
                  shapeOf (inClass "static List<int> F(int n) { var r = new List<int>(); while (n > 0) { r.Add(n); n--; } return r; }")

              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.NoRule "no rule"
          }

          test "a list that has items before the loop is refused" {
              let shape =
                  shapeOf (inClass "static List<int> F(IReadOnlyList<int> xs) { var r = new List<int> { 1 }; foreach (var x in xs) { r.Add(x); } return r; }")

              Expect.equal (required shape.Refusal).Kind LoopRefusalKind.NoRule "no rule"
          }

          test "the recognizer gives the same answer two times" {
              let first = loopsOf probeSource |> List.map (fun (m, l, s) -> line m l s)
              let second = loopsOf probeSource |> List.map (fun (m, l, s) -> line m l s)
              Expect.equal second first "deterministic"
          } ]
