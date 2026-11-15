// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Analyzers.Tests.AdmissionAnalyzerTests

open Expecto
open Morphir.Lang.CSharp.Analyzers
open Morphir.Lang.CSharp.Analyzers.Tests.AnalyzerSupport

let private ids source = diagnose source |> List.map (fun (id, _, _) -> id)

/// The analyzer diagnostics of a model that calls <c>Helper.One()</c> of a second assembly with the given name.
let private callInto (assemblyName: string) =
    let other =
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            assemblyName,
            [ Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText "public static class Helper { public static int One() => 1; }" ],
            references,
            Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
        )

    let main =
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "model",
            [ Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText "static class M { static int F() => Helper.One(); }" ],
            Seq.append references [ other.ToMetadataReference() ],
            Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
        )

    diagnoseWith true main |> List.map (fun (id, _, _) -> id)

let private onlyRule (rule: AdmissionRule) source =
    Expect.isEmpty (compileErrors source) "the source is valid C#"
    Expect.equal (ids source) [ rule.Id ] $"exactly one {rule.Id}"

[<Tests>]
let tests =
    testList
        "AdmissionAnalyzer"
        [ testList
              "rules"
              [ test "a foreach loop is refused with MORPHIR1001" {
                    onlyRule Rules.Loop "static class M { static int F(int[] xs) { foreach (var x in xs) { } return 0; } }"
                }

                test "a while loop is refused" {
                    onlyRule Rules.Loop "static class M { static int F(int n) { while (n > 0) { } return n; } }"
                }

                test "an assignment to a parameter is a mutation" {
                    onlyRule Rules.Mutation "static class M { static int F(int a) { a = 1; return a; } }"
                }

                test "an increment is a mutation" {
                    onlyRule Rules.Mutation "static class M { static int F(int a) { var b = a; b++; return b; } }"
                }

                test "a with expression and an object initializer are not mutations" {
                    let source =
                        "public sealed record R(int X) { }\nstatic class M { static R F(R r) => r with { X = 2 }; static R G() => new R(1) { X = 3 }; }"

                    Expect.isEmpty (compileErrors source) "valid C#"
                    Expect.isEmpty (ids source) "no diagnostic"
                }

                test "dynamic is refused" {
                    onlyRule Rules.Dynamic "static class M { static int F(dynamic d) => 1; }"
                }

                test "a property with a set accessor is refused" {
                    onlyRule Rules.MutableProperty "public sealed record R { public int X { get; set; } }"
                }

                test "an init property is accepted" {
                    let source = "public sealed record R { public int X { get; init; } }"
                    Expect.isEmpty (ids source) "no diagnostic"
                }

                test "a mutable field is refused" {
                    onlyRule Rules.MutableField "static class M { public static int X; }"
                }

                test "a class that is not a record is refused" {
                    onlyRule Rules.TypeKind "class C { }"
                }

                test "an interface is refused" {
                    onlyRule Rules.TypeKind "interface I { }"
                }

                test "records, closed records, unions, enums and static classes are accepted" {
                    let source =
                        "public enum Side { Buy, Sell }\npublic closed record Order;\npublic sealed record Market(Side Side) : Order;\npublic record Cat(string Name);\npublic record Dog(string Name);\npublic union Pet(Cat, Dog);\npublic static class Pricing { public static int F(int a) => a; }"

                    Expect.isEmpty (compileErrors source) "valid C# 15"
                    Expect.isEmpty (ids source) "no diagnostic"
                }

                test "a type from an assembly outside the reference set is refused" {
                    Expect.equal (callInto "Acme.Other") [ Rules.OutsideReference.Id ] "the call into Acme.Other is refused"
                }

                test "a package with a System name outside the reference set is refused" {
                    // System.Reactive is a NuGet package, not a framework assembly: morphir compile cannot bind it.
                    Expect.equal (callInto "System.Reactive") [ Rules.OutsideReference.Id ] "the call into System.Reactive is refused"
                }

                test "a deconstruction declaration is not a mutation" {
                    let source = "static class M { static int F((int, int) t) { var (a, b) = t; (int c, var d) = t; return a + b + c + d; } }"
                    Expect.isEmpty (compileErrors source) "valid C#"
                    Expect.isEmpty (ids source) "it declares new locals"
                }

                test "a deconstruction into a local that exists is a mutation" {
                    onlyRule Rules.Mutation "static class M { static int F((int, int) t) { var a = 0; (a, var b) = t; return a + b; } }"
                } ]

          testList
              "model project switch"
              [ test "no diagnostic outside a model project" {
                    let source = "class C { static int F(int[] xs) { foreach (var x in xs) { } return 0; } }"
                    Expect.isEmpty (diagnoseOutsideModelProject source) "silent"
                }

                test "the assembly attribute marks a model project" {
                    let source =
                        "[assembly: Morphir.MorphirModel]\nclass C { }"

                    let tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText source

                    let attributes =
                        Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                            "model",
                            [ tree ],
                            Seq.append
                                references
                                [ Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof<Morphir.MorphirModelAttribute>.Assembly.Location) ],
                            Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
                        )

                    let found = diagnoseWith false attributes |> List.map (fun (id, _, _) -> id)
                    Expect.equal found [ Rules.TypeKind.Id ] "reports because of the attribute"
                } ]

          testList
              "suppression"
              [ test "the specific diagnostic options of an .editorconfig suppress by id" {
                    let source = "class C { }"
                    Expect.equal (diagnoseWithSuppression source Rules.TypeKind.Id) [] "suppressed"
                }

                test "a pragma suppresses by id" {
                    let source = "#pragma warning disable MORPHIR1006\nclass C { }\n#pragma warning restore MORPHIR1006\nclass D { }\n"
                    let found = diagnose source |> List.map (fun (id, line, _) -> id, line)
                    Expect.equal found [ "MORPHIR1006", 4 ] "only the class after the restore is reported"
                } ]

          testList
              "catalog"
              [ test "ids are unique and in order" {
                    let ids = Rules.All |> Seq.map _.Id |> List.ofSeq
                    Expect.equal (List.sort ids) ids "sorted"
                    Expect.equal (List.distinct ids) ids "unique"
                }

                test "each rule has an IRI help link and a frontend code" {
                    for rule in Rules.All do
                        Expect.isTrue (rule.Iri.StartsWith "https://morphir.finos.org/rules/csharp/") rule.Id
                        Expect.equal rule.Descriptor.HelpLinkUri rule.Iri rule.Id
                        Expect.isTrue (rule.Code.StartsWith "csharp.unsupported_") rule.Id
                }

                test "each rule example gives exactly its own rule" {
                    for rule in Rules.All do
                        if rule.Id <> Rules.OutsideReference.Id then
                            Expect.isEmpty (compileErrors rule.Example) $"{rule.Id}: valid C#"
                            let found = ids rule.Example |> List.distinct
                            Expect.equal found [ rule.Id ] $"{rule.Id}: its example"
                } ] ]
