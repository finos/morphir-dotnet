module Morphir.CodeGeneration.FSharp.Tests.GenerationOptionsTests

open System.Text.Json
open Expecto
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.Testing
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

let private ok =
    function
    | Ok value -> value
    | Result.Error error -> failtestf "%A" error

let private typed version json =
    use document = JsonDocument.Parse(json: string)

    let distribution =
        (Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
         |> ok)
            .Distribution

    {
        SourceVersion =
            SourceFormatVersion.parse version
            |> ok
        Distribution = DistributionModel.Classic distribution
    }

let private generate (row: Representation) =
    let options = {
        GenerationOptions.Default with
            Optimize = row.Optimize
    }

    let json = ModelResources.read $"car-rental-v{row.Version}.json"

    let tree =
        if row.Layout = "memory" then
            Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
                options
                (typed row.Version json)
                (VFile.fromPath "model.json")
        else
            Morphir.CodeGeneration.FSharp.Transpiler.generateWithOptions
                options
                (VFile.ofText "model.json" json)

    if VFileTree.hasErrors tree then
        Result.Error(
            diagnosticsOf tree
            |> List.map (fun diagnostic -> diagnostic.Category)
        )
    else
        Ok(
            modulesOf tree
            |> List.map (fun file -> file.RelativePath, file.Source)
        )

/// The IR v4 model of a MorphirIR fixture, as generation reads it.
let private typedV4 name =
    use document = JsonDocument.Parse(ModelResources.read name)

    {
        SourceVersion = SourceFormatVersion.parse "4" |> ok
        Distribution = DistributionModel.V4((Morphir.Json.V4.V4Json.readDistribution document.RootElement |> ok).Distribution)
    }

/// The files of a tree, as path and text, failing on errors.
let private filesOf (tree: VFileTree) =
    if VFileTree.hasErrors tree then
        failtestf "generation failed: %A" (diagnosticsOf tree)

    modulesOf tree |> List.map (fun file -> file.RelativePath, file.Source)

/// The unformatted rule: LF line endings only, and exactly one final LF.
let private expectUnformattedRule (files: (string * string) list) =
    Expect.isNonEmpty files "files"

    for path, text in files do
        Expect.isFalse (text.Contains '\r') $"{path} has LF line endings only"
        Expect.isTrue (text.EndsWith "\n") $"{path} ends with LF"
        Expect.isFalse (text.EndsWith "\n\n") $"{path} ends with exactly one LF"

let private formattingOf tree =
    VFileTree.tryGet Morphir.CodeGeneration.FSharp.Transpiler.formatting tree

let private off = { GenerationOptions.Default with Format = false }

[<Tests>]
let tests =    testList "Generation options" [
        let required = [
            for version in [ "1"; "2"; "3" ] do
                for layout in [ "memory"; "file" ] do
                    for optimize in [ true; false ] ->
                        {
                            Version = version
                            Format = "json"
                            Layout = layout
                            Optimize = optimize
                        }
        ]

        let variants = [
            for row in required ->
                {
                    Representation = row
                    Input = Supported(fun () -> row)
                }
        ]

        let case = GenerationCases.rentalTotal required variants

        for row in variants do
            testCase
                $"{case.Id}/{row.Representation.Version}/{row.Representation.Layout}/optimize={row.Representation.Optimize}"
            <| fun () ->
                GenerationConformance.run generate case row
                |> ignore

        testCase "defaults retain constant folding"
        <| fun () -> Expect.isTrue GenerationOptions.Default.Optimize "existing behavior"

        testCase "typed false omits the folding stage while retaining source behavior"
        <| fun () ->
            let json =
                Morphir.CodeGeneration.FSharp.Tests.Fixtures.model [
                    "math",
                    [],
                    [
                        Morphir.CodeGeneration.FSharp.Tests.Fixtures.binding
                            "sum"
                            []
                            Morphir.CodeGeneration.FSharp.Tests.Fixtures.intType
                            (Morphir.CodeGeneration.FSharp.Tests.Fixtures.op
                                "add"
                                (Morphir.CodeGeneration.FSharp.Tests.Fixtures.integer 20)
                                (Morphir.CodeGeneration.FSharp.Tests.Fixtures.integer 22))
                    ]
                ]

            let plain =
                Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
                    { GenerationOptions.Default with Optimize = false }
                    (typed "3" json)
                    (VFile.fromPath "model.json")

            let folded =
                Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
                    GenerationOptions.Default
                    (typed "3" json)
                    (VFile.fromPath "model.json")

            Expect.stringContains (modulesOf plain).Head.Source "+" "explicit false keeps addition"
            Expect.stringContains (modulesOf folded).Head.Source "42" "default folds exact addition"

        testCase "the default formats when the runtime can, and the tree says whether it did"
        <| fun () ->
            Expect.isTrue GenerationOptions.Default.Format "formatting is the default"
            let tree = Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith GenerationOptions.Default (typedV4 "daily-rate-v4.json") (VFile.fromPath "model.json")
            filesOf tree |> ignore
            // This test host runs JIT, which can run Fantomas.
            Expect.equal (formattingOf tree) (Some "applied") "formatting was applied"

        testCase "Format false writes the emitter's own text for an IR v4 model: LF only, one final LF, recorded as off"
        <| fun () ->
            let tree = Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith off (typedV4 "daily-rate-v4.json") (VFile.fromPath "model.json")
            let files = filesOf tree
            expectUnformattedRule files
            Expect.equal (formattingOf tree) (Some "off") "formatting is off"

            let formatted =
                Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith GenerationOptions.Default (typedV4 "daily-rate-v4.json") (VFile.fromPath "model.json")
                |> filesOf

            Expect.equal (List.map fst files) (List.map fst formatted) "the same files in the same order"
            Expect.notEqual (List.map snd files) (List.map snd formatted) "the text is not the formatter's"

        testCase "Format false writes the emitter's own text for classic models too, from memory and from a file"
        <| fun () ->
            for version in [ "1"; "2"; "3" ] do
                let json = ModelResources.read $"car-rental-v{version}.json"

                for tree in
                    [ Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith off (typed version json) (VFile.fromPath "model.json")
                      Morphir.CodeGeneration.FSharp.Transpiler.generateWithOptions off (VFile.ofText "model.json" json) ] do
                    expectUnformattedRule (filesOf tree)
                    Expect.equal (formattingOf tree) (Some "off") $"classic {version}: formatting is off"

        testCase "Format false gives the same bytes under any current culture"
        <| fun () ->
            let generateIn (culture: string) =
                let previous = System.Globalization.CultureInfo.CurrentCulture
                System.Globalization.CultureInfo.CurrentCulture <- System.Globalization.CultureInfo.GetCultureInfo culture

                try
                    Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith off (typedV4 "daily-rate-v4.json") (VFile.fromPath "model.json")
                    |> filesOf
                finally
                    System.Globalization.CultureInfo.CurrentCulture <- previous

            let invariant = generateIn "en-US"

            for culture in [ "de-DE"; "tr-TR"; "ar-SA"; "fr-CH" ] do
                Expect.equal (generateIn culture) invariant $"{culture} gives the same text"
    ]
