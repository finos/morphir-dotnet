// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.ValueLoweringTests

open Expecto
open Morphir.Compilation
open Morphir.IR.V4
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private pricing (members: string) =
    $"namespace Acme.Trading.Domain;\npublic closed record Order;\npublic sealed record Market(decimal Quantity) : Order;\npublic sealed record Limit(decimal Quantity, decimal Price) : Order;\npublic sealed record Point(int X, int Y);\npublic static class Calc\n{{\n{members}\n}}\n"

let private lowered members = model (compileSource (pricing members))

let private body members name = valueText (lowered members) "domain.calc" name

[<Tests>]
let tests =
    testList
        "C# value lowering"
        [ test "literals, variables and a constant of the package" {
              let members = "    public const int Rate = 40;\n    public static int Same(int a) => a;\n    public static int Fixed() => 7;\n    public static int UseRate() => Rate;"
              Expect.equal (body members "rate") "40" "const"
              Expect.equal (body members "same") "a" "variable"
              Expect.equal (body members "fixed") "7" "literal"
              Expect.equal (body members "use-rate") "acme.trading:domain.calc#rate" "reference to the const"
          }

          test "arithmetic maps to SDK functions with the operands in order" {
              let members = "    public static int Add(int a, int b) => a + b;\n    public static int Mod(int a, int b) => a % b;"
              Expect.equal (body members "add") "((morphir.sdk:basics#add a) b)" "add"
              Expect.stringContains (body members "mod") "remainder-by" "remainder"
              Expect.stringContains (body members "mod") "b) a)" "remainderBy takes the divisor first"
          }

          test "a call of a function of the package is a reference applied to its arguments" {
              let members = "    public static int Add(int a, int b) => a + b;\n    public static int Twice(int x) => Add(x, x);"
              let text = body members "twice"
              Expect.stringContains text "#add" "reference"
              Expect.stringContains text "x) x)" "arguments in order"
          }

          test "an object creation of a case record is a constructor applied to its fields" {
              let members = "    public static Order Make(decimal q) => new Market(q);"
              Expect.equal (body members "make") "((new market) q)" "constructor application"
          }

          test "a plain record creation is a record value" {
              let members = "    public static Point Origin() => new Point(1, 2);"
              Expect.equal (body members "origin") "(record x=1 y=2)" "record"
          }

          test "a field of a record is a field access" {
              let members = "    public static int GetX(Point p) => p.X;"
              Expect.equal (body members "get-x") "(p.x)" "field"
          }

          test "a with expression is a record update" {
              let members = "    public static Point Move(Point p) => p with { X = 5 };"
              Expect.equal (body members "move") "(update p x=5)" "update"
          }

          test "a conditional expression is an if" {
              let members = "    public static int Pick(bool c) => c ? 1 : 2;"
              Expect.equal (body members "pick") "(if c then 1 else 2)" "if"
          }

          test "a switch expression on a closed record is a pattern match, and a case field is a bound variable" {
              let members =
                  "    public static decimal Notional(Order order, decimal last) => order switch\n    {\n        Market m => m.Quantity * last,\n        Limit l => l.Quantity * l.Price,\n    };"

              let text = body members "notional"
              Expect.stringContains text "(match order with" "match on the parameter"
              Expect.stringContains text "(ctor market (as _ m-quantity))" "the case binds its field to a variable named after the local"
              Expect.stringContains text "(ctor limit (as _ l-quantity) (as _ l-price))" "second case"
              Expect.stringContains text "m-quantity" "the field of m is that variable"
          }

          test "a switch that misses a case is an error" {
              let members =
                  "    public static decimal Notional(Order order) => order switch\n    {\n        Market m => m.Quantity,\n    };"

              let result = compileSource (pricing members)
              Expect.isNone (Checked.tryValue result) "no model"
              Expect.contains (codes result) "csharp.compiler" "the escalated CS8509"
          }

          test "a local variable is a let, and a return ends the body" {
              let members = "    public static int Calc2(int a)\n    {\n        var b = a + 1;\n        return b + 2;\n    }"
              let text = body members "calc-2"
              Expect.stringContains text "(let b =" "let"
              Expect.stringContains text "in " "in"
          }

          test "an if statement with returns is a conditional" {
              let members = "    public static int Sign(int a)\n    {\n        if (a < 0) { return -1; }\n        return 1;\n    }"
              Expect.stringContains (body members "sign") "(if " "conditional"
          }

          test "a lambda is a function with one pattern for each parameter" {
              let members = "    public static System.Func<int, int, int> Adder() => (a, b) => a + b;"
              let text = body members "adder"
              Expect.stringContains text "(fun (as _ a) -> (fun (as _ b) ->" "curried lambdas"
          }

          test "a call of the C# SDK maps to the Morphir SDK name, and the SDK reference binds" {
              let members = "    public static int Plus(int a, int b) => Morphir.SDK.CSharp.Basics.Add(a, b);\n    public static double Absolute(double x) => Morphir.SDK.CSharp.Basics.Abs(x);"
              Expect.equal (body members "plus") "((morphir.sdk:basics#add a) b)" "Basics.Add"
              Expect.equal (body members "absolute") "(morphir.sdk:basics#abs x)" "Basics.Abs"
          }

          test "a collection expression is a list" {
              let members = "    public static System.Collections.Generic.IReadOnlyList<int> Three() => [1, 2, 3];"
              Expect.equal (body members "three") "(list 1 2 3)" "list"
          }

          test "a call of Morphir.SDK.CSharp.Decimal, as the backend writes a decimal operation, is the SDK function" {
              let members = "    public static decimal Times(decimal a, decimal b) => Morphir.SDK.CSharp.Decimal.Mul(a, b);"
              Expect.equal (body members "times") "((morphir.sdk:decimal#mul a) b)" "Decimal.Mul"
          }

          test "List.FromEnumerable of an array, as the backend writes a list value, is a list" {
              let members = "    public static Morphir.SDK.CSharp.List<int> Three() => Morphir.SDK.CSharp.List<int>.FromEnumerable(new int[] { 1, 2, 3 });"
              Expect.equal (body members "three") "(list 1 2 3)" "list"
          }

          test "List.FromEnumerable of a value with no Morphir list type is refused" {
              // A list (IReadOnlyList<T>) is the list itself (FrontendLoopTests); a HashSet has no Morphir type.
              let members = "    public static Morphir.SDK.CSharp.List<int> Copy(System.Collections.Generic.HashSet<int> xs) => Morphir.SDK.CSharp.List<int>.FromEnumerable(xs);"
              Expect.contains (codes (compileSource (pricing members))) "csharp.unsupported_reference" "not a list value"
          }

          test "a tuple is a tuple" {
              let members = "    public static (int, int) Pair(int a) => (a, 1);"
              Expect.equal (body members "pair") "(tuple a 1)" "tuple"
          }

          test "boolean && is a conditional that keeps the evaluation order" {
              let members = "    public static bool Both(bool a, bool b) => a && b;"
              Expect.equal (body members "both") "(if a then b else false)" "and"
          }

          test "inferred types are on the node: the signature has the lowered types" {
              let members = "    public static Order Make(decimal q, int n) => new Market(q);"
              let text = signature (lowered members) "domain.calc" "make"
              Expect.equal text "(q:decimal, n:int) -> order" "signature"
          }

          test "an unsupported call names its symbol" {
              let members = "    public static int Len(string s) => s.Length;"
              let result = compileSource (pricing members)
              Expect.contains (codes result) "csharp.unsupported_reference" "no mapping for string.Length"
          }

          test "a throw is refused with its construct name" {
              let members = "    public static int Boom(int a) { throw new System.Exception(); }"
              let result = compileSource (pricing members)
              Expect.isNone (Checked.tryValue result) "no model"
          } ]
