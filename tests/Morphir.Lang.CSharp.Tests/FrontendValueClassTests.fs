// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The frontend finds the value class of a type parameter (EQL-2) and writes it in the IR carrier.
module Morphir.Lang.CSharp.Tests.FrontendValueClassTests

open Expecto
open Morphir.IR
open Morphir.IR.V4
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private calc (members: string) =
    $"namespace Acme.Trading.Domain;\nusing System;\nusing System.Collections.Generic;\nusing System.Numerics;\nusing Morphir.SDK.CSharp;\npublic static class Calc\n{{\n{members}\n}}\n"

let private definitionOf (distribution: Distribution) (name: string) : ValueDefinition =
    match moduleOf distribution "domain.calc" with
    | None -> failtest "No module domain.calc"
    | Some modul ->
        match modul.Values |> OrderedMap.toList |> List.tryFind (fun (n, _) -> Name.toKebabCase n = name) with
        | Some(_, access) -> access.Value.Value
        | None -> failtest ("No value " + name)

/// The name and the class words of each type variable of the signature, in the order of the signature.
let occurrences (definition: ValueDefinition) : (string * string list) list =
    let rec go (t: Type) =
        match t with
        | Type.Variable(attributes, name) -> [ Name.toKebabCase name, ValueClasses.ofExtensions attributes.Extensions |> List.map ValueClasses.word ]
        | Type.Reference(_, _, items)
        | Type.Tuple(_, items) -> List.collect go items
        | Type.Record(_, fields)
        | Type.ExtensibleRecord(_, _, fields) -> fields |> List.collect (snd >> go)
        | Type.Function(_, input, output) -> go input @ go output
        | Type.Unit _ -> []

    let output =
        match definition.Body with
        | ExpressionBody(t, _) -> [ t ]
        | _ -> []

    (definition.InputTypes |> List.map snd) @ output |> List.collect go

let private classesOf (members: string) (name: string) =
    occurrences (definitionOf (model (compileSource (calc members))) name)

/// The classes that the frontend finds for the type parameters of each method of a source, by the name of the method.
let private methodClasses (source: string) : Map<string, (string * ValueClass list) list> =
    match CSharpCompiler.compile [ document "Model.cs" source ] with
    | Error failure -> failtest failure.Message
    | Ok compiled ->
        [ for tree in compiled.Compilation.SyntaxTrees do
              let semantic = compiled.Compilation.GetSemanticModel(tree, false)

              for declaration in tree.GetRoot().DescendantNodes() |> Seq.choose (function :? Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax as m -> Some m | _ -> None) do
                  match Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(semantic, declaration) with
                  | null -> ()
                  | symbol -> symbol.Name, TypeParameterClasses.ofMethod compiled.Compilation symbol declaration ]
        |> Map.ofList

let private each (variable: string) (classes: string list) (count: int) = List.replicate count (variable, classes)

[<Tests>]
let tests =
    testList "C# frontend value classes" [
        test "an IComparable constraint gives the type variable key with the class comparable on each occurrence" {
            let members = "public static TKey Max<TKey>(TKey a, TKey b) where TKey : IComparable<TKey> => Basics.Max(a, b);"
            Expect.equal (classesOf members "max") (each "key" [ "comparable" ] 3) "the carrier on each type variable of the signature"
        }

        test "an IComparisonOperators constraint gives the class comparable" {
            let members = "public static T Max<T>(T a, T b) where T : IComparisonOperators<T, T, bool> => Basics.Max(a, b);"
            Expect.equal (classesOf members "max") (each "T" [ "comparable" ] 3) "comparable"
        }

        test "an INumber constraint gives the class number" {
            let members = "public static TNumber Twice<TNumber>(TNumber x) where TNumber : INumber<TNumber> => Basics.Add(x, x);"
            Expect.equal (classesOf members "twice") (each "number" [ "number" ] 2) "number"
        }

        test "an IEquatable constraint gives the class equality" {
            let members = "public static bool Same<T>(T a, T b) where T : IEquatable<T> => Basics.Equal(a, b);"
            Expect.equal (classesOf members "same") (each "T" [ "equality" ] 2) "equality"
        }

        test "an IEqualityOperators constraint and the use of == give the class equality" {
            let members = "public static bool Same<T>(T a, T b) where T : IEqualityOperators<T, T, bool> => a == b;"
            Expect.equal (classesOf members "same") (each "T" [ "equality" ] 2) "equality"
        }

        test "a call of the SDK equality on values of the type parameter gives the class equality" {
            let members = "public static bool Same<TA>(TA x, TA y) where TA : notnull => Basics.Equal(x, y);"
            Expect.equal (classesOf members "same") (each "A" [ "equality" ] 2) "equality from the use"
        }

        test "a constraint wins over a use" {
            let members = "public static bool Same<T>(T a, T b) where T : IComparable<T> => Basics.Equal(a, b);"
            Expect.equal (classesOf members "same") (each "T" [ "comparable" ] 2) "the constraint is the first source"
        }

        test "a type parameter with no constraint and no use has no carrier and keeps its name" {
            let members = "public static TComparable First<TComparable>(TComparable a, TComparable b) => a;"
            Expect.equal (classesOf members "first") (each "comparable" [] 3) "no carrier; the name stays for the name rule of the backend"
        }

        test "the uses Equals, EqualityComparer.Default, CompareTo and Comparer.Default give a class" {
            let source =
                calc (
                    String.concat "\n" [
                        "public static bool A<T>(T a, T b) => a.Equals(b);"
                        "public static bool B<T>(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);"
                        "public static int C<T>(T a, T b) where T : IComparable => a.CompareTo(b);"
                        "public static int D<T>(T a, T b) => Comparer<T>.Default.Compare(a, b);"
                    ]
                )

            let found = methodClasses source
            Expect.equal found["A"] [ "T", [ Equality ] ] "Equals"
            Expect.equal found["B"] [ "T", [ Equality ] ] "EqualityComparer<T>.Default"
            Expect.equal found["C"] [ "T", [ Comparable ] ] "CompareTo"
            Expect.equal found["D"] [ "T", [ Comparable ] ] "Comparer<T>.Default"
        }

        test "Equals and EqualityComparer<T>.Default.Equals on values of a type parameter lower as Basics.equal" {
            let members =
                String.concat "\n" [
                    "public static bool A<T>(T a, T b) where T : notnull => a.Equals(b);"
                    "public static bool B<T>(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);"
                ]

            let distribution = model (compileSource (calc members))
            Expect.equal (valueText distribution "domain.calc" "a") (valueText distribution "domain.calc" "b") "the same value"
            Expect.equal (valueText distribution "domain.calc" "a") "((morphir.sdk:basics#equal a) b)" "a call of Basics.equal"
        }

        test "CompareTo and Comparer<T>.Default.Compare on values of a type parameter lower as Basics.compare matched to -1, 0 and 1" {
            let members =
                String.concat "\n" [
                    "public static int C<T>(T a, T b) where T : IComparable => a.CompareTo(b);"
                    "public static int D<T>(T a, T b) => Comparer<T>.Default.Compare(a, b);"
                ]

            let distribution = model (compileSource (calc members))
            let text = valueText distribution "domain.calc" "c"
            Expect.equal (valueText distribution "domain.calc" "d") text "the same value"
            Expect.equal
                text
                "(match ((morphir.sdk:basics#compare a) b) with (ctor l-t) => -1 | (ctor e-q) => 0 | (ctor g-t) => 1)"
                "Basics.compare matched to -1, 0 and 1"
        }

        test "Equals on a value whose type is not a type parameter is still refused" {
            Expect.contains (codes (compileSource (calc "public static bool A(double a, double b) => a.Equals(b);"))) "csharp.unsupported_reference" "double.Equals keeps NaN equal to NaN, so it is not Basics.equal"
        }
    ]
