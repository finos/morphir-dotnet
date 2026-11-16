// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The frontend gives the model that the C# means, or it refuses the C#. It never gives a model that drops or moves a part.
module Morphir.Lang.CSharp.Tests.FrontendFidelityTests

open System.Collections.Immutable
open System.Threading
open Expecto
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Diagnostics
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private domain (types: string) (members: string) =
    $"namespace Acme.Trading.Domain;\npublic closed record Order;\npublic sealed record Market(decimal Quantity) : Order;\npublic sealed record Limit(decimal Quantity, decimal Price) : Order;\npublic sealed record Point(int X, int Y);\n{types}\npublic static class Calc\n{{\n{members}\n}}\n"

let private body (types: string) (members: string) (name: string) =
    valueText (model (compileSource (domain types members))) "domain.calc" name

let private refusal (types: string) (members: string) = codes (compileSource (domain types members))

let private errors (types: string) (members: string) =
    Checked.diagnostics (compileSource (domain types members)) |> List.filter (fun d -> d.IsError)

let private withCode (code: string) (diagnostics: Diagnostic list) =
    diagnostics |> List.filter (fun d -> Diagnostic.code d |> Option.exists (fun c -> c.Value = code))

/// An analyzer that fails on each syntax tree, as an analyzer with a defect does.
type private CrashingAnalyzer() =
    inherit DiagnosticAnalyzer()

    static let descriptor =
        DiagnosticDescriptor("TEST0001", "Test", "Test", "Test", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true)

    override _.SupportedDiagnostics = ImmutableArray.Create<DiagnosticDescriptor>(descriptor)

    override _.Initialize(context: AnalysisContext) =
        context.ConfigureGeneratedCodeAnalysis GeneratedCodeAnalysisFlags.None
        context.RegisterSyntaxTreeAction(fun _ -> invalidOp "The analyzer has a defect.")

[<Tests>]
let arguments =
    testList
        "C# frontend fidelity: arguments and initializers"
        [ test "named arguments bind to their parameters, not to their positions" {
              let members = "    public static int Sub(int a, int b) => a - b;\n    public static int Use() => Sub(b: 1, a: 10);"
              Expect.stringContains (body "" members "use") "#sub 10) 1)" "a is 10 and b is 1"
          }

          test "named arguments of a record creation bind to their fields" {
              let members = "    public static Point Make() => new Point(Y: 1, X: 2);"
              Expect.equal (body "" members "make") "(record x=2 y=1)" "x is 2 and y is 1"
          }

          test "an object initializer sets the field after the constructor" {
              let members = "    public static Point Make() => new Point(1, 2) { X = 5 };"
              Expect.equal (body "" members "make") "(record x=5 y=2)" "the initializer wins"
          }

          test "an object initializer of a case record sets the field of the constructor" {
              let members = "    public static Order Make() => new Market(1m) { Quantity = 2m };"
              Expect.equal (body "" members "make") "((new market) DecimalLiteral (DecimalLexeme \"2\"))" "the initializer wins"
          }

          test "a call of a generic method has the type of the call, not a type variable" {
              let members = "    public static T Id<T>(T x) => x;\n    public static int G() => Id(3);"

              match valueBody (model (compileSource (domain "" members))) "domain.calc" "g" with
              | Value.Apply(attributes, Value.Reference(reference, _), _) ->
                  Expect.equal (attributes.InferredType |> Option.map renderType) (Some "int") "the call gives an Int"
                  Expect.equal (reference.InferredType |> Option.map renderType) (Some "(int -> int)") "Id is used at Int"
              | other -> failtest $"Expected an application of Id, got {renderValue other}"
          }

          test "a record creation that leaves out a field is refused" {
              let types = "public sealed record Box { public int A { get; init; } public int B { get; init; } }"
              let members = "    public static Box Make() => new Box { A = 1 };"
              Expect.contains (refusal types members) "csharp.unsupported_record_initializer" "B has no value"
          } ]

[<Tests>]
let patterns =
    testList
        "C# frontend fidelity: patterns"
        [ test "a switch that names every member of an enum is a match on its constructors" {
              let types = "public enum Side { Buy, Sell }"
              let members = "    public static int Sign(Side s) => s switch\n    {\n        Side.Buy => 1,\n        Side.Sell => -1,\n    };"
              let text = body types members "sign"
              Expect.stringContains text "(ctor buy) => 1" "the Buy constructor"
              Expect.stringContains text "(ctor sell) =>" "the Sell constructor"
          }

          test "the variable of a case field does not capture a parameter with the same Morphir name" {
              let members =
                  "    public static decimal Total(Order o, decimal mQuantity) => o switch\n    {\n        Market m => m.Quantity + mQuantity,\n        Limit l => l.Price,\n    };"

              let text = body "" members "total"
              Expect.isFalse (text.Contains "(as _ m-quantity)") "the case variable has another name than the parameter"
              Expect.stringContains text "m-quantity)" "the parameter is still used"
              Expect.isFalse (text.Contains "m-quantity) m-quantity)") "the sum has two different operands"
          } ]

[<Tests>]
let statements =
    testList
        "C# frontend fidelity: statements"
        [ test "a switch statement whose sections return is a match with the arms in order" {
              let members =
                  "    public static int Q(Order o)\n    {\n        switch (o)\n        {\n            case Market m:\n                return 1;\n            case Limit l:\n                return 2;\n            default:\n                return 0;\n        }\n    }"

              Expect.equal
                  (body "" members "q")
                  "(match o with (ctor market (as _ m-quantity)) => 1 | (ctor limit (as _ l-quantity) (as _ l-price)) => 2 | _ => 0)"
                  "one arm for each section"
          }

          test "the default section of a switch statement is the last arm" {
              let types = "public enum Side { Buy, Sell, Hold }"

              let members =
                  "    public static int Sign(Side s)\n    {\n        switch (s)\n        {\n            default:\n                return 0;\n            case Side.Buy:\n                return 1;\n            case Side.Sell:\n                return -1;\n        }\n    }"

              let text = body types members "sign"
              Expect.stringStarts text "(match s with (ctor buy) => 1 | (ctor sell) =>" "the cases in source order"
              Expect.stringEnds text "| _ => 0)" "the default is last, because C# tests it last"
          }

          test "a switch statement with no default goes on with the statements after it" {
              let members =
                  "    public static int F(int n)\n    {\n        switch (n)\n        {\n            case 1:\n                return 10;\n        }\n        return 0;\n    }"

              Expect.equal (body "" members "f") "(match n with (lit IntegerLiteral 1) => 10 | _ => 0)" "the statements after the switch"
          }

          test "a switch section that does not return is refused at the section" {
              let members =
                  "    public static int F(int n)\n    {\n        switch (n)\n        {\n            case 1:\n                break;\n        }\n        return 0;\n    }"

              let found = errors "" members |> withCode "csharp.unsupported_missing_return"
              Expect.isNonEmpty found "the section has no return"

              for diagnostic in found do
                  Expect.equal (Diagnostic.location diagnostic |> Option.map (fun l -> l.Uri)) (Some "Model.cs") "in the document"
          }

          test "a deconstruction declaration is a destructure of the tuple" {
              let members = "    public static int F((int, int) t)\n    {\n        var (a, b) = t;\n        return a + b;\n    }"
              let text = body "" members "f"
              Expect.stringStarts text "(let (tuple (as _ a) (as _ b)) = t in" "a destructure, not a mutation"
          }

          test "a deconstruction into a discard binds no name" {
              let members = "    public static int F((int, int) t)\n    {\n        var (a, _) = t;\n        return a;\n    }"
              Expect.equal (body "" members "f") "(let (tuple (as _ a) _) = t in a)" "the discard is a wildcard"
          }

          test "a deconstruction of a record is refused with its own code" {
              let members = "    public static int F(Point p)\n    {\n        var (x, y) = p;\n        return x;\n    }"
              let found = refusal "" members
              Expect.contains found "csharp.unsupported_deconstruction" "only a tuple can be deconstructed"
              Expect.isFalse (List.contains "csharp.unsupported_mutation" found) "not a mutation"
          }

          test "a missing return has a location in the document" {
              let members = "    public static int F(bool c)\n    {\n        if (c) { }\n        return 1;\n    }"
              let found = errors "" members |> withCode "csharp.unsupported_missing_return"
              Expect.isNonEmpty found "the empty branch does not return"

              for diagnostic in found do
                  Expect.equal (Diagnostic.location diagnostic |> Option.map (fun l -> l.Uri)) (Some "Model.cs") "in the document"
          } ]

[<Tests>]
let diagnostics =
    testList
        "C# frontend fidelity: diagnostics"
        [ test "two refusals at one position come in the order of their ids" {
              let members = "    public static dynamic X;"
              let refusals = refusal "" members |> List.filter (fun code -> code.StartsWith "csharp.unsupported_")
              Expect.equal refusals [ "csharp.unsupported_dynamic"; "csharp.unsupported_mutable_field" ] "MORPHIR1003, then MORPHIR1007"
          }

          test "a message names the construct with stable words, not with the name of a Roslyn enum" {
              let members = "    public static int F(int a)\n    {\n        lock (\"x\") { }\n        return a;\n    }"

              match errors "" members with
              | [ diagnostic ] ->
                  Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "csharp.unsupported_lock") "code"
                  Expect.stringContains diagnostic.Message "'lock' statement" "the construct in C# words"
                  Expect.isFalse (diagnostic.Message.Contains "LockStatement") "no enum name"
              | other -> failtest $"Expected one error, got {other}"
          }

          test "an analyzer that fails gives an error, not a success" {
              let compiled =
                  match CSharpCompiler.compile [ document "Model.cs" (domain "" "") ] with
                  | Ok compiled -> compiled
                  | Error diagnostic -> failtest diagnostic.Message

              let found = CSharpCompiler.analyzerDiagnostics [ CrashingAnalyzer() ] compiled CancellationToken.None
              Expect.isNonEmpty found "the failure is reported"

              for diagnostic in found do
                  Expect.isTrue diagnostic.IsError "an error"
                  Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "csharp.compiler") "code"
                  Expect.stringContains (string (Diagnostic.data diagnostic).Value) "AD0001" "the id of the analyzer failure"
          } ]

[<Tests>]
let admission =
    testList
        "C# frontend fidelity: admission"
        [ test "a pragma does not switch off an admission rule in the compile" {
              let members = "#pragma warning disable MORPHIR1001\n    public static int Sum(int n) { var t = 0; for (var i = 0; i < n; i++) { t += i; } return t; }\n#pragma warning restore MORPHIR1001"
              Expect.contains (refusal "" members) "csharp.unsupported_loop" "the loop is refused"
          }

          test "an auto-generated header does not switch off an admission rule in the compile" {
              let source = "// <auto-generated/>\n" + domain "" "    public static int Sum(int n) { var t = 0; for (var i = 0; i < n; i++) { t += i; } return t; }"
              Expect.contains (codes (compileSource source)) "csharp.unsupported_loop" "the loop is refused"
          }

          test "a pragma does not switch off a missing switch case" {
              let members = "#pragma warning disable CS8509\n    public static decimal Q(Order o) => o switch { Market m => m.Quantity };\n#pragma warning restore CS8509"
              Expect.contains (refusal "" members) "csharp.compiler" "the escalated CS8509"
          } ]

[<Tests>]
let declarations =
    testList
        "C# frontend fidelity: declarations"
        [ test "a computed property of a record is refused" {
              let types = "public sealed record Pair(int A, int B) { public int Sum => A + B; }"
              Expect.contains (refusal types "") "csharp.unsupported_record_member" "Sum is not a field"
          }

          test "a property of a positional record outside its parameters is refused" {
              let types = "public sealed record Pair(int A) { public int B { get; init; } }"
              Expect.contains (refusal types "") "csharp.unsupported_record_member" "B is not a parameter"
          }

          test "a record that inherits from another record is refused" {
              let types = "public record Base(int A);\npublic sealed record Derived(int A, int B) : Base(A);"
              Expect.contains (refusal types "") "csharp.unsupported_record_inheritance" "the inherited field would be lost"
          }

          test "an abstract record is refused as an older sum type encoding, with the rule of the analyzer" {
              let types = "public abstract record Shape\n{\n    private Shape() { }\n    public sealed record Circle(double R) : Shape;\n}"

              match errors types "" with
              | [ diagnostic ] ->
                  Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "csharp.unsupported_abstract_record") "the code of MORPHIR1009"
                  Expect.stringContains diagnostic.Message "older encoding of a sum type" "the message of the rule"

                  match Diagnostic.location diagnostic with
                  | Some location -> Expect.equal (location.Range.Start.Line, location.Range.Start.Character) (5, 23) "at the name of the record"
                  | None -> failtest "no location"
              | other -> failtest $"Expected one error, got {other}"
          }

          test "a static property of a static class is refused" {
              let members = "    public static int Rate => 40;"
              Expect.contains (refusal "" members) "csharp.unsupported_static_member" "not dropped"
          }

          test "an enum member and a case record with one name collide" {
              let types = "public enum Kind { Market, Other }"
              let found = errors types "" |> withCode "csharp.name_collision"

              match found with
              | [ diagnostic ] ->
                  Expect.stringContains diagnostic.Message "'Market'" "names the constructor"
                  Expect.equal diagnostic.Related.Count 1 "the first declaration is related"
              | other -> failtest $"Expected one collision, got {other}"
          }

          test "two cases of two closed records with one name collide" {
              let types =
                  "public closed record B\n{\n    public sealed record X(int N) : B;\n}\npublic closed record C\n{\n    public sealed record X(int N) : C;\n}"

              Expect.contains (refusal types "") "csharp.name_collision" "B.X and C.X are both the constructor x"
          }

          test "a partial record is one type" {
              let types = "public partial record Pair(int A, int B);\npublic partial record Pair;"
              let found = refusal types ""
              Expect.isFalse (List.contains "csharp.name_collision" found) "the two parts are one declaration"
              Expect.isEmpty found "no diagnostic"
          }

          test "a static readonly field of a static class is refused" {
              let members = "    public static readonly int Rate = 40;"
              Expect.contains (refusal "" members) "csharp.unsupported_static_member" "not dropped"
          } ]

[<Tests>]
let usings =
    testList
        "C# frontend fidelity: implicit usings"
        [ test "a document binds the names of the implicit usings of an SDK project" {
              let members = "    public static int Larger(int a, int b) => Math.Max(a, b);"
              let result = compileSource (domain "" members)
              Expect.isFalse (codes result |> List.contains "csharp.compiler") "Math binds without a using"
          } ]
