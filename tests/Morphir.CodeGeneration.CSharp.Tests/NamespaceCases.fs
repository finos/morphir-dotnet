module Morphir.CodeGeneration.CSharp.Tests.NamespaceCases

open System
open System.Threading
open System.Threading.Tasks
open System.IO
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Expecto
open Morphir.CodeGeneration.CSharp
open Morphir.IR
open Morphir.IR.Model

let valid text =
    NamespaceOverride.TryCreate text
    |> Result.defaultWith (fun error -> failwithf "%A" error)

let generate model language naming =
    Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
        model,
        { LanguageVersion = language },
        valid naming,
        CancellationToken.None
    )

let typeConsumer naming =
    $"""
using {naming}.Data;
using Morphir.SDK.CSharp;
public static class Observation {{ public static int Run() {{
  var person = new Person(7,"Ada");
  var pair = new People(person,new Person(8,"Grace"));
  var holder = new BoxHolder(new Box<int>(List<int>.Singleton(5),(2,"two")));
  var nested = new NestedHolder(new NestedBox<NestedBox<int>>(new NestedBox<int>(42)));
  var equal = new NestedHolder(new NestedBox<NestedBox<int>>(new NestedBox<int>(42)));
  Choice<Person> choice = new Value<Person>(person);
  if (!Basics.Equal((object)nested,(object)equal) || MorphirValue.Hash(nested) != MorphirValue.Hash(equal)) throw new System.Exception("SDK protocol");
  if (choice is not Value<Person> item || item.Item.Age != 7) throw new System.Exception("Constructor qualification");
  return pair.First.Age + pair.Second.Age + holder.Box.Items[0] + nested.Payload.Value.Value;
}} }}
"""

let carConsumer naming =
    $"""
using {naming};
using {naming}.Domain;
using Morphir.SDK.CSharp;
public static class Observation {{ public static int Run() {{
  var before = Inventory.Car();
  var billed = Workflow.Rent(3).Match(e => throw new System.Exception("Lifecycle refusal"), x => x);
  var invoice = Billing.Invoice(billed).Match(x => x, () => throw new System.Exception("Missing invoice"));
  if (Inventory.Availability(before) is not Available) throw new System.Exception("Original mutated");
  if (Inventory.Availability(billed) is not Available) throw new System.Exception("Wrong final case");
  if (!Basics.Equal(billed,Workflow.Rent(3).Match(e => throw new System.Exception(),x => x))) throw new System.Exception("Record protocol");
  return invoice.TotalCents;
}} }}
"""

let compile result consumer =
    Expect.equal result.Status GenerationStatus.Success (sprintf "%A" result.Diagnostics)

    let references =
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") :?> string)
            .Split(Path.PathSeparator)
         |> Array.append [| typeof<Morphir.SDK.CSharp.Unit>.Assembly.Location |])
        |> Array.distinct
        |> Array.map (fun path -> MetadataReference.CreateFromFile(path) :> MetadataReference)

    let sources = [ for source in result.Sources -> CSharpSyntaxTree.ParseText(source.Text) ]

    let compilation =
        CSharpCompilation.Create(
            "NamespaceConsumer"
            + Guid.NewGuid().ToString("N"),
            CSharpSyntaxTree.ParseText(consumer: string)
            :: sources,
            references,
            CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption = ReportDiagnostic.Error
            )
        )

    use stream = new MemoryStream()
    let emitted = compilation.Emit(stream)
    Expect.isTrue emitted.Success (String.Join("\n", emitted.Diagnostics))
    let assembly = Reflection.Assembly.Load(stream.ToArray())
    assembly.GetType("Observation", true).GetMethod("Run").Invoke(null, [||]) :?> int

// Also called by isolated SDK consumers through FSI; generated sources are written unchanged.
let export directory language family naming =
    let model =
        if family = "classic" then
            TypeCases.classic ()
        else
            TypeCases.v4 ()

    let result = generate model language naming
    Expect.equal result.Status GenerationStatus.Success "typed namespace generation"

    for source in result.Sources do
        let path = Path.Combine(directory, source.RelativePath)

        Directory.CreateDirectory(Path.GetDirectoryName path)
        |> ignore

        File.WriteAllText(path, source.Text, Text.UTF8Encoding(false))

    File.WriteAllText(
        Path.Combine(directory, "Observation.cs"),
        typeConsumer naming,
        Text.UTF8Encoding(false)
    )

let tests =
    testList "Namespace override" [
        for words, expected in
            [
                [ null ], "Name[null]"
                [ "bad\nword" ], "Name[\"bad\\u000aword\"]"
                [ "a\"b" ], "Name[\"a\\\"b\"]"
                [ "x\\y" ], "Name[\"x\\\\y\"]"
            ] do
            testCase (
                "Diagnostic raw name escaping "
                + expected
            )
            <| fun _ ->
                let original = RefusalCases.classic [] (Morphir.IR.Classic.Type.Unit RefusalCases.a)

                let model =
                    match original.Distribution with
                    | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(_,
                                                                                        dependencies,
                                                                                        definition)) ->
                        { original with
                            Distribution =
                                DistributionModel.Classic(
                                    Morphir.IR.Classic.Distribution.Library(
                                        PackageName(Path [ Name words ]),
                                        dependencies,
                                        definition
                                    )
                                )
                        }
                    | _ -> failwith "Expected classic fixture"

                let result =
                    Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "invalid raw package name"
                Expect.equal result.Diagnostics.Head.Rule "invalid-name" "stable rule"

                Expect.equal
                    result.Diagnostics.Head.Location
                    "$package"
                    "stable structural location"

                Expect.stringContains
                    result.Diagnostics.Head.Message
                    expected
                    "explicit raw words and escapes"

                Expect.isEmpty result.Sources "no artifacts"

        testCase "Collision diagnostics distinguish raw name words"
        <| fun _ ->
            let model =
                RefusalCases.classic
                    [ Name [ "a" ]; Name [ "A" ] ]
                    (Morphir.IR.Classic.Type.Unit RefusalCases.a)

            let result =
                Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable collision rule"
            Expect.stringContains result.Diagnostics.Head.Message "Name[\"a\"]" "first raw identity"

            Expect.stringContains
                result.Diagnostics.Head.Message
                "Name[\"A\"]"
                "second raw identity"

        testCase "Type mismatch diagnostics describe explicit shape constructors"
        <| fun _ ->
            let _, definition =
                FunctionCases.refusals
                |> List.find (fun (rule, _) -> rule = "type-mismatch")

            let result =
                Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                    FunctionCases.classicWith [ definition ],
                    GenerationOptions.Default,
                    CancellationToken.None
                )

            Expect.equal result.Status GenerationStatus.Failure "semantic mismatch"
            Expect.equal result.Diagnostics.Head.Rule "type-mismatch" "stable mismatch rule"

            Expect.stringContains
                result.Diagnostics.Head.Message
                "Named(FQName("
                "explicit named shape"

            Expect.isEmpty result.Sources "no sources"

        for spelling, shape in
            [
                "Unit", FunctionCases.UnitTy
                "Product(", FunctionCases.TupleTy [ FunctionCases.Int; FunctionCases.Bool ]
                "Fields[", FunctionCases.RecordTy [ "field", FunctionCases.Int ]
                "Arrow(", FunctionCases.Fn(FunctionCases.Int, FunctionCases.Int)
                "Parameter(", FunctionCases.ListTy(FunctionCases.Generic "a")
            ] do
            for family in [ "classic"; "v4" ] do
                testCase (
                    "Public mismatch spelling "
                    + family
                    + " "
                    + spelling
                )
                <| fun _ ->
                    let definition: FunctionCases.Definition = {
                        Name = "mismatch"
                        Inputs = [ "x", shape ]
                        Output = FunctionCases.Int
                        Body = FunctionCases.Variable "x"
                    }

                    let model =
                        if family = "classic" then
                            FunctionCases.classicWith [ definition ]
                        else
                            FunctionCases.v4With [ definition ]

                    let result =
                        Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                            model,
                            GenerationOptions.Default,
                            CancellationToken.None
                        )

                    Expect.equal result.Status GenerationStatus.Failure "semantic refusal"
                    Expect.equal result.Diagnostics.Head.Rule "type-mismatch" "stable rule"

                    Expect.stringContains
                        result.Diagnostics.Head.Message
                        spelling
                        "explicit shape constructor"

                    Expect.isEmpty result.Sources "no sources"

        for text in
            [
                null
                ""
                " "
                ".A"
                "A."
                "A..B"
                "A B"
                "A;class Injected{}"
                "class"
                "A.class"
                "@"
                "1A"
                "A\\u0042"
                "e\u0301"
                "A\uD800"
            ] do
            testCase (
                "Invalid literal namespace: "
                + sprintf "%A" text
            )
            <| fun _ ->
                match NamespaceOverride.TryCreate text with
                | Ok _ -> failtest "Expected a typed namespace refusal"
                | Error error ->
                    Expect.equal error.Rule "invalid-namespace" "stable rule"
                    Expect.equal error.Location "$options/namespace" "option location"

        for text in
            [
                "Example.Generated"
                "@class.@namespace"
                "Δοκιμή.模型"
                "Café.A_2"
                "A\u20D0.B"
                "A\u200C.B"
                "global.record"
            ] do
            testCase (
                "Literal namespace accepted: "
                + text
            )
            <| fun _ -> Expect.equal (valid text).Value text "literal spelling retained"

        testCase "A C# caller's default namespace receives a located refusal"
        <| fun _ ->
            let model = BusinessCases.read "daily-rate-v3.json"

            let result =
                Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                    model,
                    GenerationOptions.Default,
                    Unchecked.defaultof<NamespaceOverride>,
                    CancellationToken.None
                )

            Expect.equal
                result.Status
                GenerationStatus.Failure
                "null is an invalid explicit override"

            Expect.isEmpty result.Sources "failure is atomic"
            Expect.equal result.Diagnostics.Head.Rule "invalid-namespace" "typed boundary"
            Expect.equal result.Diagnostics.Head.Location "$options/namespace" "located boundary"

        testCase "Caller cancellation precedes an invalid explicit namespace"
        <| fun _ ->
            use cancelled = new CancellationTokenSource()
            cancelled.Cancel()

            let result =
                Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                    BusinessCases.read "daily-rate-v4.json",
                    GenerationOptions.Default,
                    Unchecked.defaultof<NamespaceOverride>,
                    cancelled.Token
                )

            Expect.equal result.Status GenerationStatus.Cancelled "cancellation wins"
            Expect.isEmpty result.Diagnostics "cancel has no diagnostic"

        for file in [ "daily-rate-v3.json"; "daily-rate-v4.json" ] do
            testCase (
                "Request-local names preserve source paths and the model: "
                + file
            )
            <| fun _ ->
                let model = BusinessCases.read file

                let original =
                    Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                let a =
                    Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                        model,
                        GenerationOptions.Default,
                        valid "First.Generated",
                        CancellationToken.None
                    )

                let b =
                    Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                        model,
                        GenerationOptions.Default,
                        valid "Second.Generated",
                        CancellationToken.None
                    )

                let again =
                    Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal a.Status GenerationStatus.Success "first request"
                Expect.equal b.Status GenerationStatus.Success "second request"

                Expect.equal
                    (a.Sources
                     |> List.map (fun source -> source.RelativePath))
                    (original.Sources
                     |> List.map (fun source -> source.RelativePath))
                    "paths retain package identity"

                Expect.stringContains
                    a.Sources.Head.Text
                    "namespace First.Generated;"
                    "first namespace"

                Expect.stringContains
                    b.Sources.Head.Text
                    "namespace Second.Generated;"
                    "second namespace"

                Expect.equal again original "default is byte-identical after overrides"

        for family, make in [ "classic", TypeCases.classic; "v4", TypeCases.v4 ] do
            for naming in
                [
                    "Example.Generated"
                    "@class.@namespace"
                    "Δοκιμή.模型"
                    "Café.A_2"
                    "A\u20D0.B"
                    "A\u200C.B"
                    "global.record"
                ] do
                testCase (
                    "Compiled generic aliases and constructors "
                    + family
                    + " "
                    + naming
                )
                <| fun _ ->
                    Expect.equal
                        (compile (generate (make ()) 15 naming) (typeConsumer naming))
                        62
                        "independent type observation"
                |> testLabel "PlatformSpecific"

        for label, make in
            [
                "classic", (fun () -> BusinessCases.read "car-rental-v3.json")
                "v4", BusinessCases.synthesizedV4Car
            ] do
            testCase (
                "Compiled cross-module namespace "
                + label
            )
            <| fun _ ->
                Expect.equal
                    (compile
                        (generate (make ()) 15 "Pricing.Generated")
                        (carConsumer "Pricing.Generated"))
                    15000
                    "independent invoice"
            |> testLabel "PlatformSpecific"

        testCase "Concurrent namespace calls cannot share naming state"
        <| fun _ ->
            let model = TypeCases.classic ()

            let baseline =
                Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(model, GenerationOptions.Default, CancellationToken.None)

            let names = [| "First.Generated"; "Second.Generated" |]

            let results =
                names
                |> Array.map (fun naming -> Task.Run(fun () -> generate model 15 naming))
                |> Task.WhenAll
                |> fun task -> task.GetAwaiter().GetResult()

            Array.iter2
                (fun naming result ->
                    Expect.equal result.Status GenerationStatus.Success "concurrent status"

                    for source in result.Sources |> List.filter (fun source -> source.RelativePath <> "Morphir.Polyfill.g.cs") do
                        Expect.stringContains
                            source.Text
                            ("namespace "
                             + naming)
                            "request-local namespace"
                )
                names
                results

            Expect.equal
                (Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(model, GenerationOptions.Default, CancellationToken.None))
                baseline
                "default unchanged"
    ]
