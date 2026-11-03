module Morphir.CodeGeneration.CSharp.Tests.Program

open System
open System.Threading
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.Classic
open Morphir.CodeGeneration.CSharp
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Text
open System.Text
open System.Collections.Immutable

let classicEmpty version : VersionedModel = {
    SourceVersion =
        SourceFormatVersion.parse version
        |> Result.defaultWith (fun x -> failwithf "%A" x)
    Distribution =
        DistributionModel.Classic(
            Distribution.Library(
                PackageName.packageNameFromString "example",
                Map.empty,
                { Modules = Map.empty }
            )
        )
}

let constantModel () =
    let attribute = Unchecked.defaultof<JsonElement>

    let fq =
        FQName.fqName
            (PackageName.packageNameFromString "morphir.s_d_k")
            (ModulePath.modulePathFromString "basics")
            (Name.fromString "int")

    let definition: ValueDefinition<JsonElement, JsonElement> = {
        InputTypes = []
        OutputType = Type.Reference(attribute, fq, [])
        Body = Value.Literal(attribute, WholeNumberLiteral 42L)
    }

    let moduleDefinition: Module.ModuleDefinition<JsonElement, JsonElement> = {
        Types = Map.empty
        Values =
            Map.ofList [
                Name.fromString "answer",
                AccessControlled.public' (Documented.WithoutDocumentation definition)
            ]
        Doc = None
    }

    { classicEmpty "3" with
        Distribution =
            DistributionModel.Classic(
                Distribution.Library(
                    PackageName.packageNameFromString "example",
                    Map.empty,
                    {
                        Modules =
                            Map.ofList [
                                ModulePath.modulePathFromString "sample",
                                AccessControlled.public' moduleDefinition
                            ]
                    }
                )
            )
    }

let mapClassicModules transform model =
    match model.Distribution with
    | DistributionModel.Classic(Distribution.Library(pkg, dependencies, definition)) ->
        { model with
            Distribution =
                DistributionModel.Classic(
                    Distribution.Library(
                        pkg,
                        dependencies,
                        {
                            Modules = transform definition.Modules
                        }
                    )
                )
        }
    | _ -> failwith "Expected a classic library fixture."

type InMemoryAdapter(model: VersionedModel) =
    interface IIncrementalGenerator with
        member _.Initialize context =
            context.RegisterSourceOutput(
                context.CompilationProvider,
                fun context _ ->
                    let result =
                        CSharpGenerator.Generate(
                            model,
                            GenerationOptions.Default,
                            context.CancellationToken
                        )

                    if result.Status = GenerationStatus.Cancelled then
                        context.CancellationToken.ThrowIfCancellationRequested()

                    if
                        result.Status
                        <> GenerationStatus.Success
                    then
                        failwithf "Generation failed: %A" result.Diagnostics

                    for source in result.Sources do
                        context.AddSource(
                            source.RelativePath,
                            SourceText.From(source.Text, Encoding.UTF8)
                        )
            )

let references () =
    let paths =
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") :?> string)
            .Split(IO.Path.PathSeparator)

    paths
    |> Array.distinct
    |> Array.map (fun p -> MetadataReference.CreateFromFile(p) :> MetadataReference)

let hostSmoke model =
    let compilation =
        CSharpCompilation.Create(
            "HostSmoke",
            references = references (),
            options = CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        )

    let driver =
        CSharpGeneratorDriver.Create(
            (InMemoryAdapter(model) :> IIncrementalGenerator).AsSourceGenerator()
        )

    let mutable output = Unchecked.defaultof<Compilation>
    let mutable diagnostics = ImmutableArray<Diagnostic>.Empty

    let completed =
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            &output,
            &diagnostics,
            CancellationToken.None
        )

    Expect.isEmpty diagnostics "generator diagnostics"
    Expect.equal (completed.GetRunResult().GeneratedTrees.Length) 1 "AddSource called"
    use stream = new IO.MemoryStream()
    let emitted = output.Emit(stream)
    Expect.isTrue emitted.Success (String.Join("\n", emitted.Diagnostics))
    let assembly = Reflection.Assembly.Load(stream.ToArray())
    let generated = assembly.GetType("Example.Sample", true)
    Expect.equal (generated.GetMethod("Answer").Invoke(null, [||]) :?> int) 42 "executed constant"

let executeConsumer model language consumer =
    let result =
        CSharpGenerator.Generate(model, { LanguageVersion = language }, CancellationToken.None)

    Expect.equal result.Status GenerationStatus.Success (sprintf "%A" result.Diagnostics)
    let runtime = typeof<Morphir.SDK.CSharp.Unit>.Assembly.Location

    let refs =
        Array.append (references ()) [|
            MetadataReference.CreateFromFile(runtime) :> MetadataReference
        |]

    let trees = [ for source in result.Sources -> CSharpSyntaxTree.ParseText(source.Text) ]

    let trees =
        CSharpSyntaxTree.ParseText(consumer: string)
        :: trees

    let compilation =
        CSharpCompilation.Create(
            "Consumer"
            + Guid.NewGuid().ToString("N"),
            trees,
            refs,
            CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption = ReportDiagnostic.Error
            )
        )

    use stream = new IO.MemoryStream()
    let emitted = compilation.Emit(stream)
    Expect.isTrue emitted.Success (String.Join("\n", emitted.Diagnostics))
    let assembly = Reflection.Assembly.Load(stream.ToArray())
    assembly.GetType("Observation", true).GetMethod("Run").Invoke(null, [||]) :?> int

let externalDiagnostics model (consumer: string) =
    let result =
        CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

    Expect.equal result.Status GenerationStatus.Success (sprintf "%A" result.Diagnostics)

    let refs =
        Array.append (references ()) [|
            MetadataReference.CreateFromFile(typeof<Morphir.SDK.CSharp.Unit>.Assembly.Location)
            :> MetadataReference
        |]

    let trees =
        result.Sources
        |> List.map (fun source -> CSharpSyntaxTree.ParseText(source.Text))

    let library =
        CSharpCompilation.Create(
            "GeneratedLibrary"
            + Guid.NewGuid().ToString("N"),
            trees,
            refs,
            CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption = ReportDiagnostic.Error
            )
        )

    use stream = new IO.MemoryStream()
    let emitted = library.Emit(stream)
    Expect.isTrue emitted.Success (String.Join("\n", emitted.Diagnostics))

    let libraryReference =
        MetadataReference.CreateFromImage(ImmutableArray.CreateRange<byte>(stream.ToArray()))
        :> MetadataReference

    let compilation =
        CSharpCompilation.Create(
            "External",
            [ CSharpSyntaxTree.ParseText(consumer) ],
            Array.append refs [| libraryReference |],
            CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        )

    compilation.GetDiagnostics()
    |> Seq.toList

[<Tests>]
let tests =
    testList "C# generation" [
        for label, rule, model in
            RefusalCases.classicCases
            @ RefusalCases.v4Cases do
            testCase (
                "Located type refusal "
                + label
            )
            <| fun _ ->
                let result =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "no success-shaped result"
                Expect.isEmpty result.Sources "no partial output"
                Expect.equal result.Diagnostics.Head.Rule rule "stable refusal"
                Expect.isNonEmpty result.Diagnostics.Head.Location "model location"
        for family, model in [ "classic", TypeCases.classic; "v4", TypeCases.v4 ] do
            testCase (
                "Private constructors remain inaccessible to a separate consumer "
                + family
            )
            <| fun _ ->
                let diagnostics =
                    externalDiagnostics
                        (model ()
                         |> TypeCases.hideConstructors)
                        "public class External { public static object Run() => new Example.Data.Choice<int>.Value(3); }"

                Expect.isTrue
                    (diagnostics
                     |> List.exists (fun diagnostic -> diagnostic.Id = "CS0122"))
                    "consumer cannot construct a hidden case"
        testCase "Generation is invariant under Turkish culture"
        <| fun _ ->
            let current = Globalization.CultureInfo.CurrentCulture

            try
                Globalization.CultureInfo.CurrentCulture <-
                    Globalization.CultureInfo.InvariantCulture

                let first =
                    CSharpGenerator.Generate(
                        constantModel (),
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Globalization.CultureInfo.CurrentCulture <-
                    Globalization.CultureInfo.GetCultureInfo("tr-TR")

                let second =
                    CSharpGenerator.Generate(
                        constantModel (),
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal
                    second.Status
                    GenerationStatus.Success
                    "culture must not change admission"

                Expect.equal second.Sources first.Sources "same paths and bytes"
            finally
                Globalization.CultureInfo.CurrentCulture <- current
        testCase "Two public records mapping to one C# identity are refused"
        <| fun _ ->
            let model =
                TypeCases.classic ()
                |> mapClassicModules (
                    Map.map (fun _ controlled ->
                        let person =
                            controlled.Value.Types
                            |> Map.find (Name.fromString "person")

                        { controlled with
                            Value =
                                { controlled.Value with
                                    Types =
                                        Map.ofList [
                                            Name [ "a"; "b" ], person
                                            Name [ "AB" ], person
                                        ]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "collision before source success"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable rule"
            Expect.isEmpty result.Sources "no writable partial output"
        testCase "Private modules are internal in generated source"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (
                    Map.map (fun _ controlled ->
                        { controlled with
                            Access = AccessControlled.Private
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.stringContains
                result.Sources.Head.Text
                "internal static class"
                "module visibility"
        for family, model in [ "classic", TypeCases.classic; "v4", TypeCases.v4 ] do
            testCase (
                "Compiled immutable type construction "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (model ()) 14 TypeCases.consumer)
                    17
                    "independent construction observation"

            testCase (
                "Compiled structural value protocol "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (model ()) 14 TypeCases.equalityConsumer)
                    1
                    "independent boxed/nested numeric observations"

            testCase (
                "Record aliases retain structural transparency "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (model ()) 14 TypeCases.aliasConsumer)
                    15
                    "equivalent records share the represented type"

            testCase (
                "Nested closed records retain typed immutable properties "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (model ()) 14 TypeCases.nestedConsumer)
                    1
                    "nested/function/unit types"
        testCase "Empty v4 libraries preserve their typed family and source declaration"
        <| fun _ ->
            for version in [ "4"; "4.0.0" ] do
                let model = {
                    SourceVersion =
                        SourceFormatVersion.parse version
                        |> Result.defaultWith (fun x -> failwithf "%A" x)
                    Distribution =
                        DistributionModel.V4(
                            Morphir.IR.V4.Library(
                                PackageName.packageNameFromString "example",
                                Morphir.IR.V4.OrderedMap.empty,
                                {
                                    Modules = Morphir.IR.V4.OrderedMap.empty
                                }
                            )
                        )
                }

                let result =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Success "empty library"
                Expect.equal result.Family ModelFamily.V4 "typed family"
                Expect.equal result.SourceVersion version "source declaration"
                Expect.isEmpty result.Sources "empty artifacts"
        testCase "Specs are refused as distributions rather than treated as empty libraries"
        <| fun _ ->
            let model =
                { classicEmpty "3.1.0" with
                    Distribution =
                        DistributionModel.Classic(
                            Distribution.Specs(
                                PackageName.packageNameFromString "example",
                                Map.empty,
                                { Modules = Map.empty }
                            )
                        )
                }

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "whole distribution failure"
            Expect.equal result.Diagnostics.Head.Rule "unsupported-distribution" "rule"
            Expect.isEmpty result.Sources "no artifacts"
        testCase "A later unsupported body discards earlier successfully lowered modules"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (fun modules ->
                    let bad =
                        modules
                        |> Map.toList
                        |> List.head
                        |> snd

                    let values =
                        bad.Value.Values
                        |> Map.map (fun _ controlled ->
                            let value = Documented.value controlled.Value

                            { controlled with
                                Value =
                                    Documented.WithoutDocumentation
                                        { value with
                                            Body = Value.Unit Unchecked.defaultof<JsonElement>
                                        }
                            }
                        )

                    modules
                    |> Map.add
                        (ModulePath.modulePathFromString "zLater")
                        { bad with
                            Value = { bad.Value with Values = values }
                        }
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "whole model"

            Expect.isTrue
                (result.Diagnostics.Head.Location.Contains("values"))
                "located declaration"

            Expect.isEmpty result.Sources "no partial earlier source"
        testCase "Open record declarations produce a located failure"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (
                    Map.map (fun _ controlled ->
                        let alias =
                            Type.TypeAliasDefinition(
                                [],
                                Type.ExtensibleRecord(
                                    Unchecked.defaultof<JsonElement>,
                                    Name.fromString "row",
                                    []
                                )
                            )

                        { controlled with
                            Value =
                                { controlled.Value with
                                    Types =
                                        Map.ofList [
                                            Name.fromString "alias",
                                            AccessControlled.public' (
                                                Documented.WithoutDocumentation alias
                                            )
                                        ]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "foundation refusal"
            Expect.isEmpty result.Sources "no sources"
        testCase "Documented private constants retain their access"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (
                    Map.map (fun _ controlled ->
                        let values =
                            controlled.Value.Values
                            |> Map.map (fun _ value ->
                                { value with
                                    Access = AccessControlled.Private
                                    Value =
                                        Documented.WithDocumentation(
                                            "private value",
                                            Documented.value value.Value
                                        )
                                }
                            )

                        { controlled with
                            Value =
                                { controlled.Value with
                                    Values = values
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Success "admitted constant"

            Expect.stringContains
                result.Sources.Head.Text
                "internal static int @Answer"
                "private access"
        for version in [ "1"; "2"; "3"; "3.0.0"; "3.1.0" ] do
            testCase (
                "classic source version "
                + version
            )
            <| fun _ ->
                let result =
                    CSharpGenerator.Generate(
                        classicEmpty version,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Success "admitted"
                Expect.equal result.SourceVersion version "exact version"
        testCase "Unknown source version fails before emission"
        <| fun _ ->
            let result =
                CSharpGenerator.Generate(
                    classicEmpty "9",
                    GenerationOptions.Default,
                    CancellationToken.None
                )

            Expect.equal result.Status GenerationStatus.Failure "refused"
            Expect.equal result.Diagnostics.Head.Rule "unsupported-version" "stable rule"
            Expect.isEmpty result.Sources "no partial artifacts"
        testCase "Unknown numeric language version fails before emission"
        <| fun _ ->
            let result =
                CSharpGenerator.Generate(
                    constantModel (),
                    { LanguageVersion = 99 },
                    CancellationToken.None
                )

            Expect.equal result.Status GenerationStatus.Failure "refused"

            Expect.equal
                result.Diagnostics.Head.Location
                "$options/languageVersion"
                "located option"

            Expect.isEmpty result.Sources "no partial artifacts"
        testCase "Default profile is 14 independently of compiler"
        <| fun _ ->
            Expect.equal GenerationOptions.Default.LanguageVersion 14 "stable default"

            let source version =
                CSharpGenerator.Generate(
                    constantModel (),
                    { LanguageVersion = version },
                    CancellationToken.None
                )

            Expect.equal (source 14).Sources (source 15).Sources "same public representation"
        testCase "Given an empty library, generation succeeds without sources"
        <| fun _ ->
            let result =
                CSharpGenerator.Generate(
                    classicEmpty "3",
                    GenerationOptions.Default,
                    CancellationToken.None
                )

            Expect.equal result.Status GenerationStatus.Success "success"
            Expect.isEmpty result.Sources "no files"
            Expect.equal result.SourceVersion "3" "exact version"
            Expect.equal result.Family ModelFamily.Classic "original family"
        testCase "Given caller cancellation, no sources escape"
        <| fun _ ->
            use cts = new CancellationTokenSource()
            cts.Cancel()

            let result =
                CSharpGenerator.Generate(classicEmpty "3", GenerationOptions.Default, cts.Token)

            Expect.equal result.Status GenerationStatus.Cancelled "cancelled"
            Expect.isEmpty result.Sources "no partials"
        testCase "Given a typed model, modern generator-driver AddSource compiles and executes"
        <| fun _ -> hostSmoke (constantModel ())
        testCase "Core references only Models and framework support"
        <| fun _ ->
            let names =
                typeof<CSharpGenerator>.Assembly.GetReferencedAssemblies()
                |> Array.map (fun assembly -> assembly.Name)

            Expect.isTrue (Array.contains "Morphir.Models" names) "real typed dependency"

            for forbidden in
                [
                    "Morphir.Tooling"
                    "Morphir.Ion"
                    "Microsoft.CodeAnalysis"
                    "Morphir.IR.Pipeline"
                ] do
                Expect.isFalse
                    (Array.contains forbidden names)
                    ("no "
                     + forbidden)
        testCase "Generator driver honors caller cancellation"
        <| fun _ ->
            use cts = new CancellationTokenSource()
            cts.Cancel()

            let compilation =
                CSharpCompilation.Create("CancelledHost", references = references ())

            let driver =
                CSharpGeneratorDriver.Create(
                    (InMemoryAdapter(constantModel ()) :> IIncrementalGenerator).AsSourceGenerator()
                )

            Expect.throwsT<OperationCanceledException>
                (fun () ->
                    driver.RunGenerators(compilation, cts.Token)
                    |> ignore
                )
                "driver cancellation"
    ]

[<EntryPoint>]
let main args =
    if
        args.Length = 4
        && args[0] = "--export-profile"
    then
        let options = {
            LanguageVersion = Int32.Parse(args[1], Globalization.CultureInfo.InvariantCulture)
        }

        let model =
            if args[2] = "classic" then
                TypeCases.classic ()
            else
                TypeCases.v4 ()

        let result = CSharpGenerator.Generate(model, options, CancellationToken.None)

        if
            result.Status
            <> GenerationStatus.Success
        then
            failwithf "%A" result.Diagnostics

        for source in result.Sources do
            let path = IO.Path.Combine(args[3], source.RelativePath)

            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path))
            |> ignore

            IO.File.WriteAllText(path, source.Text, UTF8Encoding(false))

        let consumers =
            [
                "Construction", TypeCases.consumer
                "Equality", TypeCases.equalityConsumer
                "Aliases", TypeCases.aliasConsumer
                "Nested", TypeCases.nestedConsumer
            ]
            |> List.map (fun (name, body) ->
                "namespace "
                + name
                + " {\n"
                + body
                + "\n}\n"
            )
            |> String.concat ""

        IO.File.WriteAllText(
            IO.Path.Combine(args[3], "Observation.cs"),
            consumers,
            UTF8Encoding(false)
        )

        0
    else
        runTestsWithCLIArgs [] args tests
