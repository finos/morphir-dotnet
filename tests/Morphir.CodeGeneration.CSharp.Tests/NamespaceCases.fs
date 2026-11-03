module Morphir.CodeGeneration.CSharp.Tests.NamespaceCases

open System
open System.Threading
open System.Threading.Tasks
open System.IO
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Expecto
open Morphir.CodeGeneration.CSharp

let valid text =
    NamespaceOverride.TryCreate text
    |> Result.defaultWith (fun error -> failwithf "%A" error)

let generate model language naming =
    CSharpGenerator.Generate(
        model,
        { LanguageVersion = language },
        valid naming,
        CancellationToken.None
    )

let typeConsumer naming =
    $"""
using {naming};
using Morphir.SDK.CSharp;
public static class Observation {{ public static int Run() {{
  var person = new DataModule.Person(7,"Ada");
  var pair = new DataModule.People(person,new DataModule.Person(8,"Grace"));
  var holder = new DataModule.BoxHolder(new DataModule.Box<int>(List<int>.Singleton(5),(2,"two")));
  var nested = new DataModule.NestedHolder(new DataModule.NestedBox<DataModule.NestedBox<int>>(new DataModule.NestedBox<int>(42)));
  var equal = new DataModule.NestedHolder(new DataModule.NestedBox<DataModule.NestedBox<int>>(new DataModule.NestedBox<int>(42)));
  DataModule.Choice<DataModule.Person> choice = new DataModule.Choice<DataModule.Person>.Value(person);
  if (!Basics.Equal((object)nested,(object)equal) || MorphirValue.Hash(nested) != MorphirValue.Hash(equal)) throw new System.Exception("SDK protocol");
  if (choice is not DataModule.Choice<DataModule.Person>.Value item || item.Item.Age != 7) throw new System.Exception("Constructor qualification");
  return pair.First.Age + pair.Second.Age + holder.Box.Items[0] + nested.Payload.Value.Value;
}} }}
"""

let carConsumer naming =
    $"""
using {naming};
using Morphir.SDK.CSharp;
public static class Observation {{ public static int Run() {{
  var before = InventoryModule.Car();
  var billed = WorkflowModule.Rent(3).Match(e => throw new System.Exception("Lifecycle refusal"), x => x);
  var invoice = BillingModule.Invoice(billed).Match(x => x, () => throw new System.Exception("Missing invoice"));
  if (InventoryModule.Availability(before) is not DomainModule.Availability.Available) throw new System.Exception("Original mutated");
  if (InventoryModule.Availability(billed) is not DomainModule.Availability.Available) throw new System.Exception("Wrong final case");
  if (!Basics.Equal(billed,WorkflowModule.Rent(3).Match(e => throw new System.Exception(),x => x))) throw new System.Exception("Record protocol");
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
                CSharpGenerator.Generate(
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
                CSharpGenerator.Generate(
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
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                let a =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        valid "First.Generated",
                        CancellationToken.None
                    )

                let b =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        valid "Second.Generated",
                        CancellationToken.None
                    )

                let again =
                    CSharpGenerator.Generate(
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
                        (compile (generate (make ()) 14 naming) (typeConsumer naming))
                        62
                        "independent type observation"

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
                        (generate (make ()) 14 "Pricing.Generated")
                        (carConsumer "Pricing.Generated"))
                    15000
                    "independent invoice"

        testCase "Concurrent namespace calls cannot share naming state"
        <| fun _ ->
            let model = TypeCases.classic ()

            let baseline =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            let names = [| "First.Generated"; "Second.Generated" |]

            let results =
                names
                |> Array.map (fun naming -> Task.Run(fun () -> generate model 14 naming))
                |> Task.WhenAll
                |> fun task -> task.GetAwaiter().GetResult()

            Array.iter2
                (fun naming result ->
                    Expect.equal result.Status GenerationStatus.Success "concurrent status"

                    for source in result.Sources do
                        Expect.stringContains
                            source.Text
                            ("namespace "
                             + naming
                             + ";")
                            "request-local namespace"
                )
                names
                results

            Expect.equal
                (CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None))
                baseline
                "default unchanged"
    ]
