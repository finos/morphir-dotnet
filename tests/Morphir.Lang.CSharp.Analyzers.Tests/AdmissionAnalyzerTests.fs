// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Analyzers.Tests.AdmissionAnalyzerTests

open Expecto
open Microsoft.CodeAnalysis
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

                test "a loop that the recognizer accepts gives no diagnostic" {
                    let source =
                        "using System.Collections.Generic;\nstatic class M\n{\n    static int Sum(IReadOnlyList<int> xs) { var t = 0; foreach (var x in xs) { t += x; } return t; }\n    static List<int> Twice(IReadOnlyList<int> xs) { var r = new List<int>(); foreach (var x in xs) { r.Add(x * 2); } return r; }\n    static int Fact(int n) { var acc = 1; while (n > 1) { acc *= n; n--; } return acc; }\n    static (int, int) Two(IReadOnlyList<int> xs) { var s = 0; var c = 0; foreach (var x in xs) { (s, c) = (s + x, c + 1); } return (s, c); }\n}"

                    Expect.isEmpty (compileErrors source) "valid C#"
                    Expect.isEmpty (ids source) "the loops and the changes of their state are accepted"
                }

                test "a refused loop gives MORPHIR1001 with the reason and no mutation diagnostic" {
                    let source =
                        "static class M\n{\n    static int F(int[] a) { var t = 0; for (var i = 0; i < a.Length; i++) { a[i] = t; } return t; }\n}"

                    Expect.isEmpty (compileErrors source) "valid C#"
                    let found = diagnose source
                    Expect.equal (found |> List.map (fun (id, _, _) -> id)) [ Rules.Loop.Id ] "only the loop rule"
                    let _, _, message = List.head found
                    Expect.equal message "The 'for' loop is not in the Morphir subset: it writes an array element" "the reason"
                }

                test "a loop that writes a field names the field" {
                    let source =
                        "using System.Collections.Generic;\nstatic class M\n{\n    static int counter;\n    static int F(IReadOnlyList<int> xs) { var t = 0; foreach (var x in xs) { t += x; counter++; } return t; }\n}"

                    let loops = diagnose source |> List.filter (fun (id, _, _) -> id = Rules.Loop.Id)
                    Expect.equal (loops |> List.map (fun (_, _, m) -> m)) [ "The 'foreach' loop is not in the Morphir subset: it writes the field 'counter'" ] "the field"
                }

                test "a change in a lambda in a loop is a mutation" {
                    let source =
                        "using System;\nusing System.Collections.Generic;\nstatic class M\n{\n    static int F(IReadOnlyList<int> xs) { var t = 0; foreach (var x in xs) { Func<int> f = () => { var u = 0; u = x; return u; }; t += f(); } return t; }\n}"

                    Expect.isEmpty (compileErrors source) "valid C#"
                    Expect.equal (ids source) [ Rules.Mutation.Id ] "the change in the lambda"
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

                test "an abstract record with a private constructor and nested sealed record cases is an older sum type encoding" {
                    let source =
                        "public abstract record Shape\n{\n    private Shape() { }\n    public sealed record Circle(double Radius) : Shape;\n    public sealed record Rect(double W, double H) : Shape;\n}"

                    onlyRule Rules.OlderSumType source
                    Expect.equal (diagnose source |> List.map (fun (_, line, _) -> line)) [ 1 ] "at the abstract record"
                }

                test "an abstract record with its cases beside it is an older sum type encoding" {
                    onlyRule Rules.OlderSumType "public abstract record Shape;\npublic sealed record Circle(double Radius) : Shape;"
                }

                test "a closed record and a union are not reported as an older sum type encoding" {
                    let source =
                        "public closed record Shape;\npublic sealed record Circle(double Radius) : Shape;\npublic sealed record Cat(string Name);\npublic sealed record Dog(string Name);\npublic union Pet(Cat, Dog);"

                    Expect.isEmpty (compileErrors source) "valid C# 15"
                    Expect.isEmpty (ids source) "the preferred forms"
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

                test "the MSBuild property MorphirModel, the configuration key of the switch, marks a model project with no attribute" {
                    let source = "static class M { static int F(int[] xs) { foreach (var x in xs) { } return 0; } }"
                    Expect.isEmpty (diagnoseOutsideModelProject source) "the property is not set"

                    Expect.equal
                        (diagnose source |> List.map (fun (id, _, _) -> id))
                        [ Rules.Loop.Id ]
                        "build_property.MorphirModel = true and no [assembly: MorphirModel]"
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
              [ test "the specific diagnostic options of an .editorconfig or NoWarn do not suppress a rule" {
                    let source = "class C { }"
                    let found = diagnoseWithSuppression source Rules.TypeKind.Id |> List.map (fun (id, _, _) -> id)
                    Expect.equal found [ Rules.TypeKind.Id ] "the rule is not configurable"
                }

                test "a pragma does not suppress a rule" {
                    let source = "#pragma warning disable MORPHIR1006\nclass C { }\n#pragma warning restore MORPHIR1006\nclass D { }\n"
                    let found = diagnose source |> List.map (fun (id, line, _) -> id, line)
                    Expect.equal found [ "MORPHIR1006", 2; "MORPHIR1006", 4 ] "both classes are reported"
                }

                test "each rule is not configurable" {
                    for rule in Rules.All do
                        Expect.contains rule.Descriptor.CustomTags WellKnownDiagnosticTags.NotConfigurable rule.Id
                }

                test "a SuppressMessage attribute that names a Morphir rule is refused at the attribute" {
                    let source =
                        "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Morphir\", \"MORPHIR1001:loop\")]\nstatic class M\n{\n    public static int F() => 1;\n}\n"

                    let found = diagnose source |> List.map (fun (id, line, _) -> id, line)
                    Expect.equal found [ Rules.Suppression.Id, 1 ] "the suppression is refused"
                }

                test "a SuppressMessage attribute for another id is not refused" {
                    let source =
                        "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Style\", \"IDE0060\")]\nstatic class M\n{\n    public static int F() => 1;\n}\n"

                    Expect.equal (diagnose source) [] "no refusal"
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
                            // A model project references the attribute assembly, and the example of a rule may use it.
                            Expect.isEmpty (compileErrorsWithAttributes rule.Example) $"{rule.Id}: valid C#"
                            let found = diagnoseWithAttributes rule.Example |> List.map (fun (id, _, _) -> id) |> List.distinct
                            Expect.equal found [ rule.Id ] $"{rule.Id}: its example"
                } ] ]
