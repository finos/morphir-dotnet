// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The C# frontend reads a C# 15 union as a Morphir custom type, and refuses a union that cannot convert (SUM-1, D15).
/// The sources are the inputs of the union spike in spikes/csharp-language-support/union/api/inputs/.
module Morphir.Lang.CSharp.Tests.FrontendUnionTests

open System.IO
open Expecto
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.Compilation
open Morphir.IR
open Morphir.IR.V4
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private inputs =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "spikes", "csharp-language-support", "union", "api", "inputs"))

/// An input of the spike in the namespace of the test package, without the named types and methods.
let private input (file: string) (without: string list) =
    let text = File.ReadAllText(Path.Combine(inputs, file)).Replace("namespace Acme.Domain;", "namespace Acme.Trading.Domain;")
    let root = CSharpSyntaxTree.ParseText(text, CSharpParseOptions(LanguageVersion.Preview)).GetRoot()

    let named (node: SyntaxNode) =
        match node with
        | :? MethodDeclarationSyntax as method' -> List.contains method'.Identifier.Text without
        | :? BaseTypeDeclarationSyntax as type' -> List.contains type'.Identifier.Text without
        | _ -> false

    match root.RemoveNodes(root.DescendantNodes() |> Seq.filter named, SyntaxRemoveOptions.KeepNoTrivia) with
    | null -> ""
    | kept -> kept.ToFullString()

/// The methods of usage.cs that reach the state with no case, and the one that misses an arm.
let private refusedInUsage = [ "Raw"; "Default"; "NullArm"; "Missing" ]

let private usage () = model (compileSource (input "usage.cs" refusedInUsage))

let private calc (name: string) = valueText (usage ()) "domain.calc" name

let private errors (source: string) =
    Checked.diagnostics (compileSource source) |> List.filter (fun d -> d.IsError)

/// The errors of a source as (code, line of the start), in the order of the frontend.
let private located (source: string) =
    errors source
    |> List.map (fun d ->
        let code = Diagnostic.code d |> Option.map (fun c -> c.Value) |> Option.defaultValue "?"
        let line = Diagnostic.location d |> Option.map (fun l -> l.Range.Start.Line) |> Option.defaultValue -1
        code, line)

/// The zero-based line of the first line of a source that contains a text.
let private lineOf (source: string) (text: string) =
    source.Split('\n') |> Array.findIndex (fun line -> line.Contains text)

let private constructors (distribution: Distribution) (typeName: string) =
    match moduleOf distribution "domain" with
    | None -> failtest "no module domain"
    | Some modul ->
        match modul.Types |> OrderedMap.toList |> List.tryFind (fun (n, _) -> Name.toKebabCase n = typeName) with
        | Some(_, access) ->
            match access.Value.Value with
            | CustomTypeDefinition(_, _, cases) ->
                cases
                |> OrderedMap.toList
                |> List.map (fun (name, args) ->
                    Name.toKebabCase name, args |> List.map (fun (n, t) -> Name.toKebabCase n + ":" + renderType t))
            | other -> failtest $"Expected a custom type, got {other}"
        | None -> failtest $"no type {typeName}"

[<Tests>]
let lowering =
    testList
        "C# frontend unions: lowering"
        [ test "a union of two records is a custom type with one constructor for each case type" {
              Expect.equal
                  (constructors (usage ()) "shape")
                  [ "circle", [ "value:circle" ]; "rect", [ "value:rect" ] ]
                  "the constructor has the local name of the case type and that type as its one argument"
          }

          test "a union of SDK types takes the local names of the SDK types" {
              Expect.equal (constructors (usage ()) "amount") [ "int", [ "value:int" ]; "string", [ "value:string" ] ] "Int and String"
          }

          test "a declaration pattern on a union is a constructor pattern with an as pattern" {
              let text = calc "area"
              Expect.stringContains text "(match s with (ctor circle (as _ c)) => " "Circle c"
              Expect.stringContains text "(c.radius)" "c is the record Circle"
              Expect.stringContains text "| (ctor rect (as _ r)) => " "Rect r"
          }

          test "a positional and a property pattern on a case bind the fields of the case value" {
              Expect.equal
                  (calc "positional")
                  "(match s with (ctor circle (as _ circle-value)) => (circle-value.radius) | (ctor rect (as _ rect-value)) => (rect-value.w))"
                  "Circle(var radius) and Rect { W: var w }"
          }

          test "a switch statement on a union is a pattern match" {
              Expect.stringContains (calc "statement") "(match s with (ctor circle (as _ c)) => (c.radius) | (ctor rect (as _ r)) => (r.w) | _ => " "the cases, then the statements after the switch"
          }

          test "an implicit conversion, a local, a creation and a cast apply the constructor of the case" {
              let applied = "((new circle) (record radius=r))"
              Expect.equal (calc "implicit") applied "Shape s = new Circle(r)"
              Expect.equal (calc "local") ("(let s = " + applied + " in s)") "a local of the union type"
              Expect.equal (calc "explicit") applied "new Shape(new Circle(r))"
              Expect.equal (calc "cast") applied "(Shape)new Circle(r)"
          }

          test "a constant of an SDK case applies the constructor of that case" {
              Expect.equal (calc "number") "((new int) 5)" "Amount a = 5"
          }

          test "an is pattern is a pattern match that gives a Bool" {
              Expect.equal (calc "is-circle") "(match s with (ctor circle _) => true | _ => false)" "s is Circle"
              Expect.equal (signature (usage ()) "domain.calc" "is-circle") "(s:shape) -> bool" "the result type"
          }

          test "Equals on two unions is the equality of Morphir" {
              Expect.equal (calc "same") "((morphir.sdk:basics#equal a) b)" "a.Equals(b)"
          }

          test "a union of a generic record keeps the type parameter" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic sealed record Box<T>(T Item);\npublic sealed record Empty;\npublic union Holder<T>(Box<T>, Empty);\npublic static class Calc\n{\n    public static Holder<int> One() => new Box<int>(1);\n}\n"

              let distribution = model (compileSource source)
              Expect.equal (constructors distribution "holder") [ "box", [ "value:(box t)" ]; "empty", [ "value:empty" ] ] "Box<T> is a case"
              Expect.equal (valueText distribution "domain.calc" "one") "((new box) (record item=1))" "the case of Holder<int>"
          } ]

[<Tests>]
let refusals =
    testList
        "C# frontend unions: refusals"
        [ test "the Value property of a union is refused at the expression" {
              let source = input "usage.cs" [ "Default"; "NullArm"; "Missing" ]
              // The result type object has no Morphir type either.
              Expect.equal
                  (located source)
                  [ "csharp.unsupported_reference", lineOf source "s.Value"; "csharp.unsupported_union_value", lineOf source "s.Value" ]
                  "Raw"
          }

          test "default of a union is refused at the expression" {
              let source = input "usage.cs" [ "Raw"; "NullArm"; "Missing" ]
              Expect.equal (located source) [ "csharp.unsupported_union_default", lineOf source "=> default" ] "Default"
          }

          test "a union with no argument is refused as default" {
              let source = input "usage.cs" refusedInUsage + "\npublic static class More\n{\n    public static Shape Empty() => new Shape();\n}\n"
              Expect.equal (located source) [ "csharp.unsupported_union_default", lineOf source "new Shape()" ] "new Shape()"
          }

          test "a null arm on a union is refused at the arm" {
              let source = input "usage.cs" [ "Raw"; "Default"; "Missing" ]
              Expect.equal (located source) [ "csharp.unsupported_union_null", lineOf source "null => 3" ] "NullArm"
          }

          test "a switch that misses a case of a union is a compiler error" {
              let source = input "usage.cs" [ "Raw"; "Default"; "NullArm" ]
              Expect.equal (located source) [ "csharp.compiler", lineOf source "Missing" ] "CS8509"
          }

          test "IUnion is refused" {
              let source =
                  input "usage.cs" refusedInUsage
                  + "\npublic static class More\n{\n    internal static int Size(System.Runtime.CompilerServices.IUnion u) => 1;\n}\n"

              // The polyfill type is internal, so the method is internal too.
              Expect.equal (located source) [ "csharp.unsupported_union_interface", lineOf source "IUnion u" ] "IUnion"
          }

          test "a union with a case type written two times is a compiler error" {
              let source = input "shapes.cs" []
              Expect.contains (located source) ("csharp.compiler", lineOf source "union Dup") "CS0111"
          }

          test "each union of shapes.cs that cannot convert has one refusal for each reason" {
              let source = input "shapes.cs" [ "Dup" ]
              let at (code: string) (declaration: string) = "csharp." + code, lineOf source declaration

              // Each union after Other shares the case Nothing with Other, and WithBody shares Circle and Rect with Shape.
              let expected =
                  [ at "name_collision" "union Other"
                    at "unsupported_reference" "union MaybeInt"
                    at "name_collision" "union MaybeInt"
                    at "unsupported_union_case" "union Tup"
                    at "name_collision" "union Tup"
                    at "unsupported_union_case" "union Opt"
                    at "name_collision" "union Opt"
                    at "name_collision" "union Nested"
                    at "unsupported_reference" "union WithList"
                    at "name_collision" "union WithList"
                    at "unsupported_union_member" "union WithBody"
                    at "name_collision" "union WithBody"
                    at "name_collision" "union WithBody" ]

              Expect.equal
                  (List.sort (located source))
                  (List.sort expected)
                  "a shared case type, int? and List<int> with no Morphir type, a tuple and T with no name, and a member"
          }

          test "a type parameter case names the case that has no name" {
              let source = "namespace Acme.Trading.Domain;\npublic sealed record Nothing;\npublic union Opt<T>(T, Nothing);\n"

              match errors source with
              | [ diagnostic ] ->
                  Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "csharp.unsupported_union_case") "the code"
                  Expect.stringContains diagnostic.Message "'T'" "the case"
                  Expect.stringContains diagnostic.Message "'Opt'" "the union"
              | other -> failtest $"Expected one error, got {other}"
          }

          test "two unions of one module with the case Circle give an error that names both unions" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic sealed record Circle(double R);\npublic sealed record Rect(double W);\npublic union Shape(Circle, Rect);\npublic union Other(Circle, Rect);\n"

              let found = errors source |> List.filter (fun d -> d.Message.Contains "'Circle'")

              match found with
              | [ diagnostic ] ->
                  Expect.equal (Diagnostic.code diagnostic |> Option.map (fun c -> c.Value)) (Some "csharp.name_collision") "the code"
                  Expect.stringContains diagnostic.Message "'Shape'" "the first union"
                  Expect.stringContains diagnostic.Message "'Other'" "the second union"
              | other -> failtest $"Expected one error for Circle, got {other}"
          }

          test "two cases of one union with one constructor name are refused" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic union Lists(Morphir.SDK.CSharp.List<int>, Morphir.SDK.CSharp.List<string>);\n"

              Expect.equal (located source) [ "csharp.name_collision", lineOf source "union Lists" ] "List<int> and List<string> are both the constructor List"
          }

          test "a case record of a closed record is refused as a case of a union" {
              let source =
                  "namespace Acme.Trading.Domain;\npublic closed record Order;\npublic sealed record Market(decimal Q) : Order;\npublic sealed record Nothing;\npublic union Wrap(Market, Nothing);\n"

              Expect.equal (located source) [ "csharp.unsupported_union_case", lineOf source "union Wrap" ] "Market is a constructor, not a type"
          }

          test "the IEEE form of the spike is refused: its interface and its members have no place in the model" {
              Expect.contains (located (input "equality.cs" []) |> List.map fst) "csharp.unsupported_type_kind" "the interface IMorphirValue"
          } ]
