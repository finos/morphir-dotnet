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
    let generated = assembly.GetType("Example.SampleModule", true)
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
        NamespaceCases.tests
        testCase "Business matrix retains all seven required inputs"
        <| fun _ ->
            Expect.equal
                (BusinessCases.cases
                 |> List.map (fun (id, _, _, _, _, _) -> id)
                 |> Set.ofList)
                (Set.ofList [
                    "car-classic-1"
                    "car-classic-2"
                    "car-classic-3"
                    "car-v4-synthesized"
                    "daily-classic"
                    "daily-v4"
                    "books-v4-types"
                ])
                "no silently missing positive matrix rows"
        for version in [ "1"; "2"; "3" ] do
            testCase (
                "Original car lifecycle classic "
                + version
            )
            <| fun _ ->
                let model =
                    BusinessCases.read (
                        "car-rental-v"
                        + version
                        + ".json"
                    )

                Expect.equal model.SourceVersion.Text version "original source version"

                Expect.equal
                    (executeConsumer model 14 BusinessCases.carConsumer)
                    7
                    "independent lifecycle and seven rejections"
        testCase "SYNTHESIZED TEST INPUT v4 car lifecycle"
        <| fun _ ->
            Expect.equal
                (executeConsumer (BusinessCases.synthesizedV4Car ()) 14 BusinessCases.carConsumer)
                7
                "independent lifecycle"
        for name, version in [ "daily-rate-v3.json", "3"; "daily-rate-v4.json", "4" ] do
            testCase (
                "Original daily rate "
                + name
            )
            <| fun _ ->
                let model = BusinessCases.read name
                Expect.equal model.SourceVersion.Text version "original source spelling"

                Expect.equal
                    (executeConsumer model 14 BusinessCases.dailyConsumer)
                    5000
                    "independent daily rate"
        testCase "COMPATIBLE TYPE SLICE original v4 books and records"
        <| fun _ ->
            Expect.equal
                (executeConsumer (BusinessCases.booksTypeSlice ()) 14 BusinessCases.booksConsumer)
                1
                "nonempty immutable nested type behavior"
        for name, rule in
            [
                "complete-example.json", "unresolved-type"
                "books-and-records-example.json", "unsupported-body"
            ] do
            testCase (
                "Original ecosystem refusal "
                + name
            )
            <| fun _ ->
                let result =
                    CSharpGenerator.Generate(
                        BusinessCases.read name,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "original source limitations"
                Expect.isEmpty result.Sources "no success-shaped partial artifacts"

                Expect.isTrue
                    (result.Diagnostics
                     |> List.exists (fun d ->
                         d.Rule = rule
                         && d.Location
                            <> ""
                     ))
                    "located original limitation"
        testCase "Mid-traversal cancellation interrupts acyclic alias expansion"
        <| fun _ -> CancellationCases.duringAliasExpansion ()
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            testCase (
                "Large nonempty literal cancellation "
                + family
            )
            <| fun _ -> CancellationCases.duringLargeLiteral make
        for family in [ "classic"; "v4" ] do
            testCase (
                "Public functions cannot expose private nominal types "
                + family
            )
            <| fun _ ->
                let choice = FunctionCases.Own("data", "choice", [ FunctionCases.Int ])

                let definitions: FunctionCases.Definition list = [
                    {
                        Name = "identityChoice"
                        Inputs = [ "x", choice ]
                        Output = choice
                        Body = FunctionCases.Variable "x"
                    }
                ]

                let model =
                    if family = "classic" then
                        PatternCases.classicWithDefinitions definitions
                        |> mapClassicModules (
                            Map.map (fun path modul ->
                                if
                                    path
                                    <> TypeCases.path
                                then
                                    modul
                                else
                                    { modul with
                                        Value =
                                            { modul.Value with
                                                Types =
                                                    modul.Value.Types
                                                    |> Map.change
                                                        (Name.fromString "choice")
                                                        (Option.map (fun typ ->
                                                            { typ with
                                                                Access = AccessControlled.Private
                                                            }
                                                        ))
                                            }
                                    }
                            )
                        )
                    else
                        let model = PatternCases.v4WithDefinitions definitions

                        match model.Distribution with
                        | DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, library)) ->
                            let modules =
                                library.Modules
                                |> Morphir.IR.V4.OrderedMap.toList
                                |> List.map (fun (path, modul) ->
                                    if
                                        path
                                        <> TypeCases.path
                                    then
                                        path, modul
                                    else
                                        path,
                                        { modul with
                                            Value =
                                                { modul.Value with
                                                    Types =
                                                        modul.Value.Types
                                                        |> Morphir.IR.V4.OrderedMap.toList
                                                        |> List.map (fun (name, typ) ->
                                                            name,
                                                            if
                                                                name = Name.fromString "choice"
                                                            then
                                                                { typ with
                                                                    Access = Morphir.IR.V4.Private
                                                                }
                                                            else
                                                                typ
                                                        )
                                                        |> TypeCases.ordered
                                                }
                                        }
                                )
                                |> TypeCases.ordered

                            { model with
                                Distribution =
                                    DistributionModel.V4(
                                        Morphir.IR.V4.Library(pkg, deps, { Modules = modules })
                                    )
                            }
                        | _ -> failwith "V4 library required."

                let result =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "no invalid public signature"

                Expect.equal
                    result.Diagnostics.Head.Rule
                    "private-type-exposure"
                    "located visibility refusal"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            testCase (
                "Frozen SDK generated-call observations "
                + family
            )
            <| fun _ ->
                Expect.equal FrozenSdkCases.entries.Length 207 "frozen required IDs"

                Expect.equal
                    (FrozenSdkCases.entries
                     |> List.map (fun entry -> entry.Id)
                     |> List.sort)
                    FrozenSdkCases.requiredIds
                    "exact authority ID set"

                Expect.equal FrozenSdkCases.excludedIds.Length 8 "exclusions are not passes"

                Expect.equal
                    (FrozenSdkCases.entries
                     |> List.map (fun entry -> entry.Operation)
                     |> List.distinct
                     |> List.length)
                    67
                    "all inventory operations"

                Expect.equal
                    (executeConsumer
                        (make FrozenSdkCases.definitions)
                        14
                        (FrozenSdkCases.consumer ()))
                    207
                    "generated calls observe frozen values and errors"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            for rule, definition in SdkCases.refusals do
                testCase (
                    "SDK domain refusal "
                    + family
                    + " "
                    + definition.Name
                )
                <| fun _ ->
                    let result =
                        CSharpGenerator.Generate(
                            make [ definition ],
                            GenerationOptions.Default,
                            CancellationToken.None
                        )

                    Expect.equal result.Status GenerationStatus.Failure "refused domain"
                    Expect.isEmpty result.Sources "no artifacts"
                    Expect.equal result.Diagnostics.Head.Rule rule "located rule"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            testCase (
                "Compiled SDK arithmetic, comparisons, maps and variants "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (make SdkCases.definitions) 14 SdkCases.consumer)
                    1
                    "SDK meaning and persistence"
        for memberName in [ "referenceEquals"; "memberwiseClone"; "finalize" ] do
            testCase (
                "Record payload cannot hide inherited "
                + memberName
            )
            <| fun _ ->
                let shape = FunctionCases.RecordTy [ memberName, FunctionCases.Int ]

                let model =
                    FunctionCases.classicWith [
                        {
                            Name = "identity"
                            Inputs = [ "x", shape ]
                            Output = shape
                            Body = FunctionCases.Variable "x"
                        }
                    ]

                let result =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "inherited protocol collision"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            testCase (
                "Method cannot hide its own generic parameter "
                + family
            )
            <| fun _ ->
                let definition: FunctionCases.Definition = {
                    Name = "tA"
                    Inputs = [ "x", FunctionCases.Generic "a" ]
                    Output = FunctionCases.Generic "a"
                    Body = FunctionCases.Variable "x"
                }

                let result =
                    CSharpGenerator.Generate(
                        make [ definition ],
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal
                    result.Status
                    GenerationStatus.Failure
                    "generic method identity collision"
        testCase "V4 record values preserve source field evaluation order"
        <| fun _ ->
            let consumer =
                PatternCases.consumer
                    .Replace("trace[0]!=1 || trace[1]!=2", "trace[0]!=2 || trace[1]!=1")
                    .Replace("trace[1]!=4 || trace[2]!=3", "trace[1]!=3 || trace[2]!=4")

            Expect.equal
                (executeConsumer (PatternCases.v4Reversed ()) 14 consumer)
                1
                "v4 ordered fields"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            for index, (rule, definition) in
                PatternCases.refusals
                |> List.indexed do
                testCase ($"Pattern and record refusal {family} {index}")
                <| fun _ ->
                    let result =
                        CSharpGenerator.Generate(
                            make [ definition ],
                            GenerationOptions.Default,
                            CancellationToken.None
                        )

                    Expect.equal result.Status GenerationStatus.Failure "semantic refusal"
                    Expect.isEmpty result.Sources "no partial artifacts"
                    Expect.equal result.Diagnostics.Head.Rule rule "located category"
        for family, make in [ "classic", PatternCases.classic; "v4", PatternCases.v4 ] do
            testCase (
                "Compiled ordered patterns and persistent records "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (make ()) 14 PatternCases.consumer)
                    1
                    "first match, aliases, recursion, record persistence and evaluation order"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            testCase (
                "Function generic parameter collisions are located "
                + family
            )
            <| fun _ ->
                let definition: FunctionCases.Definition = {
                    Name = "bad"
                    Inputs = [
                        "x", FunctionCases.RawGeneric(Name [ "a" ])
                        "y", FunctionCases.RawGeneric(Name [ "A" ])
                    ]
                    Output = FunctionCases.Generic "a"
                    Body = FunctionCases.Variable "x"
                }

                let result =
                    CSharpGenerator.Generate(
                        make [ definition ],
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "generic target collision"
                Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"

            testCase (
                "Empty function type parameter names are refused "
                + family
            )
            <| fun _ ->
                let definition: FunctionCases.Definition = {
                    Name = "bad"
                    Inputs = [ "x", FunctionCases.Generic "" ]
                    Output = FunctionCases.Generic ""
                    Body = FunctionCases.Variable "x"
                }

                let result =
                    CSharpGenerator.Generate(
                        make [ definition ],
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "invalid type parameter"
                Expect.equal result.Diagnostics.Head.Rule "invalid-name" "stable category"
        testCase "An empty library needs no package namespace allocation"
        <| fun _ ->
            let model =
                { classicEmpty "3" with
                    Distribution =
                        DistributionModel.Classic(
                            Distribution.Library(
                                PackageName(Path []),
                                Map.empty,
                                { Modules = Map.empty }
                            )
                        )
                }

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Success "empty libraries succeed"
            Expect.isEmpty result.Sources "no source allocation"
        testCase "Inherited CLR members cannot become success-shaped module collisions"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (
                    Map.map (fun _ modul ->
                        let value =
                            modul.Value.Values
                            |> Map.toList
                            |> List.head
                            |> snd

                        { modul with
                            Value =
                                { modul.Value with
                                    Values = Map.ofList[Name.fromString "getHashCode", value]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "inherited member collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "A generated type name cannot collide with its required protocol"
        <| fun _ ->
            let model =
                RefusalCases.classic [] (Type.Record(LiteralCases.a, []))
                |> mapClassicModules (
                    Map.map (fun _ modul ->
                        let declaration =
                            modul.Value.Types
                            |> Map.toList
                            |> List.head
                            |> snd

                        { modul with
                            Value =
                                { modul.Value with
                                    Types = Map.ofList[Name.fromString "getHashCode", declaration]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "type protocol collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "Reordered v4 declarations preserve source paths and bytes"
        <| fun _ ->
            let model = TypeCases.v4 ()

            let reordered =
                match model.Distribution with
                | DistributionModel.V4(Morphir.IR.V4.Library(pkg, deps, definition)) ->
                    let modules =
                        definition.Modules
                        |> Morphir.IR.V4.OrderedMap.toList
                        |> List.rev
                        |> List.map (fun (name, modul) ->
                            name,
                            { modul with
                                Value =
                                    { modul.Value with
                                        Types =
                                            modul.Value.Types
                                            |> Morphir.IR.V4.OrderedMap.toList
                                            |> List.rev
                                            |> TypeCases.ordered
                                    }
                            }
                        )
                        |> TypeCases.ordered

                    { model with
                        Distribution =
                            DistributionModel.V4(
                                Morphir.IR.V4.Library(pkg, deps, { Modules = modules })
                            )
                    }
                | _ -> failwith "v4 fixture"

            let first =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            let second =
                CSharpGenerator.Generate(
                    reordered,
                    GenerationOptions.Default,
                    CancellationToken.None
                )

            Expect.equal second.Sources first.Sources "same artifacts"
        for caseName in [ "morphirEquals"; "tA" ] do
            testCase (
                "Case identities cannot hide protocols or generic parameters "
                + caseName
            )
            <| fun _ ->
                let model =
                    TypeCases.classic ()
                    |> mapClassicModules (
                        Map.map (fun _ modul ->
                            let choice =
                                Type.CustomTypeDefinition(
                                    [ Name.fromString "a" ],
                                    AccessControlled.public' (
                                        Map.ofList[Name.fromString caseName, []]
                                    )
                                )

                            { modul with
                                Value =
                                    { modul.Value with
                                        Types =
                                            Map.ofList[Name.fromString "choice",
                                                       AccessControlled.public' (
                                                           Documented.WithoutDocumentation choice
                                                       )]
                                    }
                            }
                        )
                    )

                let result =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "case target collision"
                Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "A type cannot share its module class identity"
        <| fun _ ->
            let model =
                RefusalCases.classic [] (Type.Record(LiteralCases.a, []))
                |> mapClassicModules (
                    Map.map (fun _ modul ->
                        let declaration =
                            modul.Value.Types
                            |> Map.toList
                            |> List.head
                            |> snd

                        { modul with
                            Value =
                                { modul.Value with
                                    Types = Map.ofList[Name.fromString "badModule", declaration]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "class type collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "Record generic parameters cannot share payload member identities"
        <| fun _ ->
            let model =
                RefusalCases.classic
                    [ Name.fromString "a" ]
                    (Type.Record(
                        LiteralCases.a,
                        [
                            {
                                Name = Name.fromString "tA"
                                Type = Type.Variable(LiteralCases.a, Name.fromString "a")
                            }
                        ]
                    ))

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "generic payload collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "Portable source allocation supports Windows device names"
        <| fun _ ->
            let model = constantModel ()

            let model =
                match model.Distribution with
                | DistributionModel.Classic(Distribution.Library(_, deps, definition)) ->
                    { model with
                        Distribution =
                            DistributionModel.Classic(
                                Distribution.Library(
                                    PackageName.packageNameFromString "con",
                                    deps,
                                    definition
                                )
                            )
                    }
                | _ -> failwith "classic fixture"

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal
                result.Sources.Head.RelativePath
                "p_Con/m_SampleModule.g.cs"
                "safe prefixed components"

            Expect.equal
                (executeConsumer
                    model
                    14
                    "public static class Observation { public static int Run() => Con.SampleModule.Answer(); }")
                42
                "device-named namespace compiles"
        for family, make in [ "classic", FunctionCases.classicWith; "v4", FunctionCases.v4With ] do
            for index, (rule, definition) in
                FunctionCases.refusals
                |> List.indexed do
                testCase ($"Function admission {family} {index}")
                <| fun _ ->
                    let result =
                        CSharpGenerator.Generate(
                            make [ definition ],
                            GenerationOptions.Default,
                            CancellationToken.None
                        )

                    Expect.equal result.Status GenerationStatus.Failure "semantic refusal"
                    Expect.isEmpty result.Sources "whole model"
                    Expect.equal result.Diagnostics.Head.Rule rule "stable category"
        for label, model in [ "classic", FunctionCases.classic; "v4", FunctionCases.v4 ] do
            testCase (
                "Compiled lexical function behavior "
                + label
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (model ()) 14 FunctionCases.consumer)
                    1
                    "capture, shadowing, partial application, generics and evaluation order"
        testCase "Nested record fields cannot hide their generated protocol"
        <| fun _ ->
            let inner =
                Type.Record(
                    LiteralCases.a,
                    [
                        {
                            Name = Name.fromString "morphirEquals"
                            Type = Type.Reference(LiteralCases.a, TypeCases.sdk "basics" "int", [])
                        }
                    ]
                )

            let model =
                RefusalCases.classic
                    []
                    (Type.Record(
                        LiteralCases.a,
                        [
                            {
                                Name = Name.fromString "inner"
                                Type = inner
                            }
                        ]
                    ))

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "nested collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "A value cannot share its module class identity"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (
                    Map.map (fun _ modul ->
                        { modul with
                            Value =
                                { modul.Value with
                                    Values =
                                        Map.ofList[Name.fromString "sampleModule",
                                                   modul.Value.Values
                                                   |> Map.toList
                                                   |> List.head
                                                   |> snd]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "method class collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "A value cannot share an emitted record identity"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (
                    Map.map (fun _ modul ->
                        let alias =
                            Type.TypeAliasDefinition(
                                [],
                                Type.Record(
                                    LiteralCases.a,
                                    [
                                        {
                                            Name = Name.fromString "x"
                                            Type =
                                                Type.Reference(
                                                    LiteralCases.a,
                                                    TypeCases.sdk "basics" "int",
                                                    []
                                                )
                                        }
                                    ]
                                )
                            )

                        { modul with
                            Value =
                                { modul.Value with
                                    Types =
                                        Map.ofList[Name.fromString "answer",
                                                   AccessControlled.public' (
                                                       Documented.WithoutDocumentation alias
                                                   )]
                                }
                        }
                    )
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "method type collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "Portable module paths detect case-insensitive collisions"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (fun modules ->
                    let modul =
                        modules
                        |> Map.toList
                        |> List.head
                        |> snd

                    Map.ofList[ModulePath(Path[Name["ab"]]), modul
                               ModulePath(Path[Name["aB"]]), modul]
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "portable path collision"
            Expect.equal result.Diagnostics.Head.Rule "path-collision" "stable category"
            Expect.stringContains result.Diagnostics.Head.Message "AbModule" "first owner"
            Expect.stringContains result.Diagnostics.Head.Message "ABModule" "second owner"
        testCase "Generated payload members cannot collide with the structural protocol"
        <| fun _ ->
            let model =
                RefusalCases.classic
                    []
                    (Type.Record(
                        LiteralCases.a,
                        [
                            {
                                Name = Name.fromString "morphirEquals"
                                Type =
                                    Type.Reference(LiteralCases.a, TypeCases.sdk "basics" "int", [])
                            }
                        ]
                    ))

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "member collision"
            Expect.equal result.Diagnostics.Head.Rule "name-collision" "stable category"
        testCase "Malformed typed names fail before source success"
        <| fun _ ->
            let model =
                constantModel ()
                |> mapClassicModules (fun modules ->
                    let modul =
                        modules
                        |> Map.toList
                        |> List.head
                        |> snd

                    Map.ofList[ModulePath(Path[Name["../escape"]]), modul]
                )

            let result =
                CSharpGenerator.Generate(model, GenerationOptions.Default, CancellationToken.None)

            Expect.equal result.Status GenerationStatus.Failure "invalid typed name"
            Expect.equal result.Diagnostics.Head.Rule "invalid-name" "stable category"
        testCase "Module and type can retain the same Morphir name"
        <| fun _ ->
            let model =
                TypeCases.classic ()
                |> mapClassicModules (
                    Map.map (fun _ modul ->
                        let controlled = Map.find (Name.fromString "person") modul.Value.Types

                        { modul with
                            Value =
                                { modul.Value with
                                    Types =
                                        Map.ofList[Name.fromString "data", controlled

                                                   Name.fromString "age",
                                                   Map.find
                                                       (Name.fromString "age")
                                                       modul.Value.Types]
                                }
                        }
                    )
                )

            Expect.equal
                (executeConsumer
                    model
                    14
                    "public static class Observation { public static int Run() => new Example.DataModule.Data(2,\"x\").Age; }")
                2
                "separate module class identity"
        for label, model, body in LiteralCases.admitted do
            testCase (
                "Literal fidelity "
                + label
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer
                        model
                        14
                        ("public static class Observation { public static int Run() { "
                         + body
                         + " } }"))
                    1
                    "independent literal observation"
        for label, rule, model in LiteralCases.refused do
            testCase (
                "Located literal refusal "
                + label
            )
            <| fun _ ->
                let result =
                    CSharpGenerator.Generate(
                        model,
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "refused"
                Expect.isEmpty result.Sources "atomic failure"
                Expect.equal result.Diagnostics.Head.Rule rule "semantic category"
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
                "Finite nested aliases compile and execute "
                + family
            )
            <| fun _ ->
                Expect.equal
                    (executeConsumer (model ()) 14 TypeCases.consumer)
                    17
                    "repeated alias use in actual type arguments is finite"

            testCase (
                "Genuinely recursive aliases fail without partial sources "
                + family
            )
            <| fun _ ->
                let result =
                    CSharpGenerator.Generate(
                        TypeCases.withRecursiveAlias (model ()),
                        GenerationOptions.Default,
                        CancellationToken.None
                    )

                Expect.equal result.Status GenerationStatus.Failure "recursive alias refused"
                Expect.equal result.Diagnostics.Head.Rule "recursive-alias" "stable cycle rule"
                Expect.isNotEmpty result.Diagnostics.Head.Location "located cycle diagnostic"
                Expect.isEmpty result.Sources "no successful partial sources"

            testCase (
                "Private constructors remain inaccessible to a separate consumer "
                + family
            )
            <| fun _ ->
                let diagnostics =
                    externalDiagnostics
                        (model ()
                         |> TypeCases.hideConstructors)
                        "public class External { public static object Run() => new Example.DataModule.Choice<int>.Value(3); }"

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
        && args[0] = "--export-business"
    then
        let id, label, names, expected, make, consumer =
            BusinessCases.cases
            |> List.find (fun (id, _, _, _, _, _) -> id = args[2])

        let model = make ()

        let options = {
            LanguageVersion = Int32.Parse(args[1], Globalization.CultureInfo.InvariantCulture)
        }

        let result = CSharpGenerator.Generate(model, options, CancellationToken.None)

        if
            result.Status
            <> GenerationStatus.Success
            || List.isEmpty result.Sources
        then
            failwithf "Nonempty business generation failed: %A" result.Diagnostics

        for source in result.Sources do
            let path = IO.Path.Combine(args[3], source.RelativePath)

            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path))
            |> ignore

            IO.File.WriteAllText(path, source.Text, UTF8Encoding(false))

        IO.File.WriteAllText(
            IO.Path.Combine(args[3], "Observation.cs"),
            consumer,
            UTF8Encoding(false)
        )

        IO.File.WriteAllText(
            IO.Path.Combine(args[3], "Program.cs"),
            "if (Observation.Run() != "
            + string expected
            + ") throw new System.Exception(\"Independent observation mismatch\"); System.Console.WriteLine(\"generated-consumer:business-passed\");",
            UTF8Encoding(false)
        )

        let originalSources =
            BusinessCases.sources
            |> List.filter (fun source -> List.contains source.Name names)

        let excluded =
            if id = "books-v4-types" then
                [ "open-deal: IncompleteBody/Draft"; "close-deal: IncompleteBody/Draft" ]
            else
                []

        let metadata = {|
            Id = id
            Label = label
            OriginalSources = originalSources
            ExcludedValues = excluded
            Family = string model.Family
            SourceVersion = model.SourceVersion.Text
            LanguageVersion = options.LanguageVersion
            Expected = expected
        |}

        IO.File.WriteAllText(
            IO.Path.Combine(args[3], "case.json"),
            JsonSerializer.Serialize(metadata),
            UTF8Encoding(false)
        )

        0
    elif
        args.Length = 4
        && args[0] = "--export-profile"
    then
        let options = {
            LanguageVersion = Int32.Parse(args[1], Globalization.CultureInfo.InvariantCulture)
        }

        let model =
            if args[2] = "classic" then
                PatternCases.classicWithDefinitions (
                    PatternCases.definitions
                    @ SdkCases.definitions
                    @ FrozenSdkCases.definitions
                )
            else
                PatternCases.v4WithDefinitions (
                    PatternCases.definitions
                    @ SdkCases.definitions
                    @ FrozenSdkCases.definitions
                )

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
                "Functions", FunctionCases.consumer
                "Patterns", PatternCases.consumer
                "SDK", SdkCases.consumer
                "FrozenSDK", FrozenSdkCases.consumer ()
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
