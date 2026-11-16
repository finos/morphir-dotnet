// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The C# frontend lowers a loop by its shape, with the loop recognizer of the analyzer assembly (csharp-loop-lowering).
module Morphir.Lang.CSharp.Tests.FrontendLoopTests

open System.Threading
open Expecto
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private source (members: string) =
    "using System.Collections.Generic;\nnamespace Acme.Trading.Loops;\npublic sealed record Order(int Quantity, int Price);\npublic static class Calc\n{\n    public static int Notional(Order order) => order.Quantity * order.Price;\n"
    + members
    + "\n}\n"

let private body (members: string) (name: string) =
    valueText (model (compileSource (source members))) "loops.calc" name

let private notional = "acme.trading:loops.calc#notional"

/// The diagnostics of the lowering alone: the source is compiled and planned, and the admission analyzer does not run.
let private loweringDiagnostics (text: string) =
    let documents = [ document "Model.cs" text ]

    match CSharpCompiler.compile documents with
    | Error diagnostic -> [ diagnostic ]
    | Ok compiled ->
        let request = request documents

        let lowered =
            Planning.plan request.Package request.ExposedModules compiled CancellationToken.None
            |> Checked.bind (fun plan -> (V4FamilyBuilder() :> IFamilyBuilder).Build(PackageLowering plan))

        Checked.diagnostics lowered

let private loopDiagnostics (diagnostics: Diagnostic list) =
    diagnostics
    |> List.filter (fun d -> Diagnostic.code d |> Option.exists (fun c -> c.Value = "csharp.unsupported_loop"))
    |> List.map (fun d -> string d.LocationOrNull, d.Message)
    |> List.sort

[<Tests>]
let tests =
    testList
        "C# frontend: loops"
        [ test "a foreach that adds a projection is List.map with the lowered function" {
              let members =
                  "    public static List<int> Notionals(IReadOnlyList<Order> orders)\n    {\n        var result = new List<int>();\n        foreach (var o in orders) { result.Add(Notional(o)); }\n        return result;\n    }"

              Expect.equal (body members "notionals") $"((morphir.sdk:list#map {notional}) orders)" "List.map notional"
          }

          test "a foreach that adds the element under a condition is List.filter" {
              let members =
                  "    public static List<Order> Large(IReadOnlyList<Order> orders)\n    {\n        var result = new List<Order>();\n        foreach (var o in orders) { if (o.Price > 10) result.Add(o); }\n        return result;\n    }"

              Expect.equal
                  (body members "large")
                  "((morphir.sdk:list#filter (fun (as _ o) -> ((morphir.sdk:basics#greater-than (o.price)) 10))) orders)"
                  "List.filter"
          }

          test "a foreach that adds a projection under a condition is List.filterMap" {
              let members =
                  "    public static List<int> LargeNotionals(IReadOnlyList<Order> orders)\n    {\n        var result = new List<int>();\n        foreach (var o in orders) { if (o.Price > 10) result.Add(Notional(o)); }\n        return result;\n    }"

              Expect.equal
                  (body members "large-notionals")
                  $"((morphir.sdk:list#filter-map (fun (as _ o) -> (if ((morphir.sdk:basics#greater-than (o.price)) 10) then ((new just) ({notional} o)) else (new nothing)))) orders)"
                  "List.filterMap"
          }

          test "a foreach that writes one local is List.foldl" {
              let members =
                  "    public static int Total(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { total += Notional(o); }\n        return total;\n    }"

              Expect.equal
                  (body members "total")
                  $"(((morphir.sdk:list#foldl (fun (as _ o) -> (fun (as _ total) -> ((morphir.sdk:basics#add total) ({notional} o))))) 0) orders)"
                  "List.foldl"
          }

          test "a foreach that writes two locals is List.foldl with a tuple of two as its state" {
              let members =
                  "    public static (int, int) SumAndCount(IReadOnlyList<Order> orders)\n    {\n        var sum = 0; var count = 0;\n        foreach (var o in orders) { sum += o.Price; count++; }\n        return (sum, count);\n    }"

              Expect.equal
                  (body members "sum-and-count")
                  "(((morphir.sdk:list#foldl (fun (as _ o) -> (fun (tuple (as _ sum) (as _ count)) -> (tuple ((morphir.sdk:basics#add sum) (o.price)) ((morphir.sdk:basics#add count) 1))))) (tuple 0 0)) orders)"
                  "a tuple state"
          }

          test "a while loop that writes acc and n is a LetRecursion with the parameters acc and n" {
              let members =
                  "    public static int Factorial(int n)\n    {\n        var acc = 1;\n        while (n > 1) { acc *= n; n--; }\n        return acc;\n    }"

              Expect.equal
                  (body members "factorial")
                  "(letrec loop(acc, n) = (if ((morphir.sdk:basics#greater-than n) 1) then ((loop ((morphir.sdk:basics#multiply acc) n)) ((morphir.sdk:basics#subtract n) 1)) else acc) in ((loop 1) n))"
                  "LetRecursion"
          }

          test "a break is a base case of the recursion" {
              let members =
                  "    public static int UntilNegative(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { if (o.Price < 0) break; total += o.Price; }\n        return total;\n    }"

              Expect.equal
                  (body members "until-negative")
                  "(letrec loop(total, remaining) = (match remaining with [] => total | ((as _ o) :: (as _ remaining)) => (if ((morphir.sdk:basics#less-than (o.price)) 0) then total else ((loop ((morphir.sdk:basics#add total) (o.price))) remaining))) in ((loop 0) orders))"
                  "one base case for the break"
          }

          test "a for loop has the variable of its initializer as a parameter" {
              let members =
                  "    public static int Sum(int n)\n    {\n        var t = 0;\n        for (var i = 0; i < n; i++) { t += i; }\n        return t;\n    }"

              Expect.equal
                  (body members "sum")
                  "(letrec loop(i, t) = (if ((morphir.sdk:basics#less-than i) n) then ((loop ((morphir.sdk:basics#add i) 1)) ((morphir.sdk:basics#add t) i)) else t) in ((loop 0) 0))"
                  "i and t"
          }

          test "a return in a foreach is a base case" {
              let members =
                  "    public static int FirstLarge(IReadOnlyList<Order> orders)\n    {\n        foreach (var o in orders) { if (o.Price > 1) return o.Price; }\n        return 0;\n    }"

              Expect.equal
                  (body members "first-large")
                  "(letrec loop(remaining) = (match remaining with [] => 0 | ((as _ o) :: (as _ remaining)) => (if ((morphir.sdk:basics#greater-than (o.price)) 1) then (o.price) else (loop remaining))) in (loop orders))"
                  "the return"
          }

          test "a do loop tests its condition after the body" {
              let members = "    public static int Down(int n)\n    {\n        do { n--; } while (n > 0);\n        return n;\n    }"

              Expect.equal
                  (body members "down")
                  "(letrec loop(n) = (if ((morphir.sdk:basics#greater-than ((morphir.sdk:basics#subtract n) 1)) 0) then (loop ((morphir.sdk:basics#subtract n) 1)) else ((morphir.sdk:basics#subtract n) 1)) in (loop n))"
                  "the body, then the condition"
          }

          test "an if with no else in a fold keeps the state on the other path" {
              let members =
                  "    public static int Positive(IReadOnlyList<int> xs)\n    {\n        var t = 0;\n        foreach (var x in xs) { if (x > 0) t += x; }\n        return t;\n    }"

              Expect.equal
                  (body members "positive")
                  "(((morphir.sdk:list#foldl (fun (as _ x) -> (fun (as _ t) -> (if ((morphir.sdk:basics#greater-than x) 0) then ((morphir.sdk:basics#add t) x) else t)))) 0) xs)"
                  "the state on both paths"
          }

          test "a map can declare a local before the add" {
              let members =
                  "    public static List<int> Twice(IReadOnlyList<Order> orders)\n    {\n        var r = new List<int>();\n        foreach (var o in orders) { var p = o.Price; r.Add(p * 2); }\n        return r;\n    }"

              Expect.equal
                  (body members "twice")
                  "((morphir.sdk:list#map (fun (as _ o) -> (let p = (o.price) in ((morphir.sdk:basics#multiply p) 2)))) orders)"
                  "a let in the lambda"
          }

          test "the state after a fold is a let when more code reads it" {
              let members =
                  "    public static int TotalPlusOne(IReadOnlyList<int> xs)\n    {\n        var t = 0;\n        foreach (var x in xs) { t += x; }\n        return t + 1;\n    }"

              Expect.equal
                  (body members "total-plus-one")
                  "(let t = (((morphir.sdk:list#foldl (fun (as _ x) -> (fun (as _ t) -> ((morphir.sdk:basics#add t) x)))) 0) xs) in ((morphir.sdk:basics#add t) 1))"
                  "a let"
          }

          test "a change that later code reads is a let with the same name" {
              let members =
                  "    public static int Steps(int n)\n    {\n        var acc = 0;\n        while (n > 0) { n--; acc += n; }\n        return acc;\n    }"

              Expect.equal
                  (body members "steps")
                  "(letrec loop(n, acc) = (if ((morphir.sdk:basics#greater-than n) 0) then (let n = ((morphir.sdk:basics#subtract n) 1) in ((loop n) ((morphir.sdk:basics#add acc) n))) else acc) in ((loop n) 0))"
                  "the new n"
          }

          test "an empty list that no loop fills is an empty list" {
              let members = "    public static List<int> None()\n    {\n        var r = new List<int>();\n        return r;\n    }"
              Expect.equal (body members "none") "(list )" "an empty list"
          }

          test "Loop option and IR: a fold with MorphirLoop(Recursion) is a LetRecursion and not List.foldl" {
              let members =
                  "    [Morphir.MorphirLoop(Morphir.LoopLowering.Recursion)]\n    public static int Total(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { total += Notional(o); }\n        return total;\n    }"

              Expect.equal
                  (body members "total")
                  $"(letrec loop(total, remaining) = (match remaining with [] => total | ((as _ o) :: (as _ remaining)) => ((loop ((morphir.sdk:basics#add total) ({notional} o))) remaining)) in ((loop 0) orders))"
                  "LetRecursion"
          }

          test "MorphirLoop(Recursion) on the class applies to its methods, and a list that a loop builds stays a list function" {
              let text =
                  "using System.Collections.Generic;\nnamespace Acme.Trading.Loops;\n[Morphir.MorphirLoop(Morphir.LoopLowering.Recursion)]\npublic static class Calc\n{\n    public static int Sum(IReadOnlyList<int> xs) { var t = 0; foreach (var x in xs) { t += x; } return t; }\n    public static List<int> Twice(IReadOnlyList<int> xs) { var r = new List<int>(); foreach (var x in xs) { r.Add(x * 2); } return r; }\n}\n"

              let distribution = model (compileSource text)
              Expect.stringStarts (valueText distribution "loops.calc" "sum") "(letrec loop(t, remaining) = " "the class attribute"
              Expect.stringStarts (valueText distribution "loops.calc" "twice") "((morphir.sdk:list#map " "a map has no recursion form of its own"
          }

          test "Same effect: the [frontend.csharp] key loop-lowering=recursion gives the IR of the assembly attribute" {
              let members =
                  "    public static int Total(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { total += Notional(o); }\n        return total;\n    }"

              let byAttribute = model (compileSource ((source members).Replace("namespace ", "[assembly: Morphir.MorphirLoop(Morphir.LoopLowering.Recursion)]\nnamespace ")))
              let byKey = model (compileSourceWith [ "loop-lowering", "\"recursion\"" ] (source members))
              let total = valueText byKey "loops.calc" "total"
              Expect.equal total (valueText byAttribute "loops.calc" "total") "the key and the attribute give the same IR"
              Expect.stringStarts total "(letrec loop(total, remaining) = " "a LetRecursion"
          }

          test "Member over key: MorphirLoop(Traversal) on the method wins over the key loop-lowering=recursion" {
              let members =
                  "    [Morphir.MorphirLoop(Morphir.LoopLowering.Traversal)]\n    public static int Total(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { total += Notional(o); }\n        return total;\n    }\n    public static int Count(IReadOnlyList<Order> orders)\n    {\n        var n = 0;\n        foreach (var o in orders) { n += 1; }\n        return n;\n    }"

              let distribution = model (compileSourceWith [ "loop-lowering", "\"recursion\"" ] (source members))
              Expect.stringStarts (valueText distribution "loops.calc" "total") "(((morphir.sdk:list#foldl " "the attribute on the member"
              Expect.stringStarts (valueText distribution "loops.calc" "count") "(letrec loop(n, remaining) = " "the key"
          }

          test "the key loop-lowering=traversal gives the IR of no option" {
              let members =
                  "    public static int Total(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { total += Notional(o); }\n        return total;\n    }"

              Expect.equal
                  (valueText (model (compileSourceWith [ "loop-lowering", "\"traversal\"" ] (source members))) "loops.calc" "total")
                  (body members "total")
                  "the default"
          }

          test "an extra option that the C# frontend does not know, or a value that is not a word of its key, fails before lowering" {
              let text = source "    public static int One() => 1;"

              for options, expected in
                  [ [ "loop-lowering", "\"sideways\"" ], "'loop-lowering' accepts traversal, recursion; received 'sideways'"
                    [ "loop-lowering", "true" ], "'loop-lowering' accepts traversal, recursion"
                    [ "traversal", "\"functional\"" ], "'traversal' is not an option of the C# frontend"
                    [ "unknown", "1" ], "'unknown' is not an option of the C# frontend" ] do
                  let result = compileSourceWith options text
                  Expect.isNone (Checked.tryValue result) $"{options} has no model"
                  Expect.equal (codes result) [ "csharp.invalid_option" ] $"{options} has the code"

                  Expect.stringContains
                      (Checked.diagnostics result |> List.map (fun d -> d.Message) |> String.concat " ")
                      expected
                      $"{options} has the message"
          }

          test "a loop that writes a field is refused with the name of the field" {
              let members =
                  "    private static int counter;\n    public static int Counted(IReadOnlyList<Order> orders)\n    {\n        var total = 0;\n        foreach (var o in orders) { total += o.Price; counter++; }\n        return total;\n    }"

              let result = compileSource (source members)
              let messages = Checked.diagnostics result |> List.map (fun d -> d.Message)
              Expect.contains messages "The 'foreach' loop is not in the Morphir subset: it writes the field 'counter'" "the field"
          }

          test "the analyzer and the lowering refuse the same loops with the same reasons" {
              let members =
                  String.concat
                      "\n"
                      [ "    public static int Total(IReadOnlyList<Order> orders) { var total = 0; foreach (var o in orders) { total += Notional(o); } return total; }"
                        "    public static List<int> Notionals(IReadOnlyList<Order> orders) { var result = new List<int>(); foreach (var o in orders) { result.Add(Notional(o)); } return result; }"
                        "    public static List<Order> Large(IReadOnlyList<Order> orders) { var result = new List<Order>(); foreach (var o in orders) { if (o.Price > 10) result.Add(o); } return result; }"
                        "    public static List<int> LargeNotionals(IReadOnlyList<Order> orders) { var result = new List<int>(); foreach (var o in orders) { if (o.Price > 10) result.Add(Notional(o)); } return result; }"
                        "    public static (int, int) SumAndCount(IReadOnlyList<Order> orders) { var sum = 0; var count = 0; foreach (var o in orders) { sum += o.Price; count++; } return (sum, count); }"
                        "    public static int Factorial(int n) { var acc = 1; while (n > 1) { acc *= n; n--; } return acc; }"
                        "    public static int Sum(int n) { var t = 0; for (var i = 0; i < n; i++) { t += i; } return t; }"
                        "    public static List<int> Counted(IReadOnlyList<Order> orders) { var r = new List<int>(); var n = 0; foreach (var o in orders) { r.Add(o.Price); n++; } return r; }"
                        "    public static List<int> Down(int n) { var r = new List<int>(); while (n > 0) { r.Add(n); n--; } return r; }"
                        "    public static int UntilNegative(IReadOnlyList<Order> orders) { var total = 0; foreach (var o in orders) { if (o.Price < 0) break; total += o.Price; } return total; }"
                        "    public static int Nothing(IReadOnlyList<Order> orders) { foreach (var o in orders) { } return 0; }" ]

              let text = source members
              let fromCompile = loopDiagnostics (Checked.diagnostics (compileSource text))
              let fromLowering = loopDiagnostics (loweringDiagnostics text)
              // The lowering runs with no admission analyzer, so the planning must accept the source: the field case is
              // in the test above, because the planning refuses a field that can change.
              Expect.equal (List.length fromCompile) 3 "three loops are refused"
              Expect.equal fromLowering fromCompile "the same loops, ranges and reasons"
          } ]

[<Tests>]
let functional =
    testList
        "C# frontend: functional forms"
        [ test "Select is List.map" {
              let members =
                  "    public static IReadOnlyList<int> Notionals(IReadOnlyList<Order> orders) => orders.Select(o => Notional(o)).ToList();"

              Expect.equal (body members "notionals") $"((morphir.sdk:list#map {notional}) orders)" "the same IR as the loop"
          }

          test "Where is List.filter" {
              let members = "    public static IReadOnlyList<Order> Large(IReadOnlyList<Order> orders) => orders.Where(o => o.Price > 10).ToList();"

              Expect.equal
                  (body members "large")
                  "((morphir.sdk:list#filter (fun (as _ o) -> ((morphir.sdk:basics#greater-than (o.price)) 10))) orders)"
                  "the same IR as the loop"
          }

          test "Where then Select is List.map over List.filter, not List.filterMap" {
              let members =
                  "    public static IReadOnlyList<int> LargeNotionals(IReadOnlyList<Order> orders) => orders.Where(o => o.Price > 10).Select(o => Notional(o)).ToList();"

              Expect.equal
                  (body members "large-notionals")
                  $"((morphir.sdk:list#map {notional}) ((morphir.sdk:list#filter (fun (as _ o) -> ((morphir.sdk:basics#greater-than (o.price)) 10))) orders))"
                  "map over filter"
          }

          test "Aggregate is List.foldl with the element first" {
              let members =
                  "    public static int Total(IReadOnlyList<Order> orders) => orders.Aggregate(0, (total, o) => total + Notional(o));"

              Expect.equal
                  (body members "total")
                  $"(((morphir.sdk:list#foldl (fun (as _ o) -> (fun (as _ total) -> ((morphir.sdk:basics#add total) ({notional} o))))) 0) orders)"
                  "the same IR as the loop"
          }

          test "Aggregate of a function of the element and the state is the function" {
              let members =
                  "    public static int Sum(Morphir.SDK.CSharp.List<int> xs) => xs.Aggregate(0, (acc, x) => Morphir.SDK.CSharp.Basics.Add(x, acc));"

              Expect.equal (body members "sum") "(((morphir.sdk:list#foldl morphir.sdk:basics#add) 0) xs)" "foldl add"
          }

          test "Select, Where and FilterMap of an SDK list are the list functions" {
              let members =
                  String.concat
                      "\n"
                      [ "    public static Morphir.SDK.CSharp.List<int> Twice(Morphir.SDK.CSharp.List<int> xs) => xs.Select<int>(x => x * 2);"
                        "    public static Morphir.SDK.CSharp.List<int> Positive(Morphir.SDK.CSharp.List<int> xs) => xs.Where(x => x > 0);"
                        "    public static Morphir.SDK.CSharp.List<int> Halves(Morphir.SDK.CSharp.List<int> xs) => xs.FilterMap<int>(x => x > 0 ? Morphir.SDK.CSharp.Maybe<int>.Just(x / 2) : Morphir.SDK.CSharp.Maybe<int>.Nothing);" ]

              let distribution = model (compileSource (source members))
              let text = valueText distribution "loops.calc"
              Expect.equal (text "twice") "((morphir.sdk:list#map (fun (as _ x) -> ((morphir.sdk:basics#multiply x) 2))) xs)" "map"
              Expect.equal (text "positive") "((morphir.sdk:list#filter (fun (as _ x) -> ((morphir.sdk:basics#greater-than x) 0))) xs)" "filter"

              Expect.equal
                  (text "halves")
                  "((morphir.sdk:list#filter-map (fun (as _ x) -> (if ((morphir.sdk:basics#greater-than x) 0) then ((new just) ((morphir.sdk:basics#integer-divide x) 2)) else (new nothing)))) xs)"
                  "filterMap"
          }

          test "FromEnumerable of a list is the list" {
              let members = "    public static Morphir.SDK.CSharp.List<int> Same(IReadOnlyList<int> xs) => Morphir.SDK.CSharp.List<int>.FromEnumerable(xs);"
              Expect.equal (body members "same") "xs" "the list"
          }

          test "a local recursive function is a LetRecursion with its name" {
              let members =
                  "    public static int SumTo(int n)\n    {\n        int go(int k) => k <= 0 ? 0 : k + go(k - 1);\n        return go(n);\n    }"

              Expect.equal
                  (body members "sum-to")
                  "(letrec go(k) = (if ((morphir.sdk:basics#less-than-or-equal k) 0) then 0 else ((morphir.sdk:basics#add k) (go ((morphir.sdk:basics#subtract k) 1)))) in (go n))"
                  "LetRecursion"
          }

          test "local functions next to each other are one group" {
              let members =
                  "    public static bool IsEven(int n)\n    {\n        bool even(int k) => k == 0 || odd(k - 1);\n        bool odd(int k) => k != 0 && even(k - 1);\n        return even(n);\n    }"

              let text = body members "is-even"
              Expect.stringStarts text "(letrec even(k) = " "the first function"
              Expect.stringContains text "; odd(k) = " "the second function in the same group"
          }

          test "a local function that is called before its declaration is refused" {
              let members = "    public static int F(int n)\n    {\n        return go(n);\n        int go(int k) => k;\n    }"
              Expect.contains (codes (compileSource (source members))) "csharp.unsupported_local_function" "declare it first"
          } ]
