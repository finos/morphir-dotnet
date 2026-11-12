/// The pipeline parser for Ion files and Ion trees, run through `MorphirProcessor` beside a JSON parser, and the
/// sample in the README, which is compiled here and compared with the README's text.
module Morphir.Ion.Tests.IonIrParserTests

open System
open System.IO
open System.Text.Json
open Expecto
open Morphir.IR.Pipeline
open Morphir.Json.Classic
open Morphir.Ion
open Morphir.Ion.Tests

module Sample =
    // README-SAMPLE-START
    open Morphir.IR.Pipeline
    open Morphir.Ion

    /// Reads a directory that holds an Ion document tree, and says what it holds.
    let describeTree (directory: string) : string =
        match VFileTree.loadFromDisk directory with
        | Result.Error error -> TreeLoadError.describe error
        | Ok tree ->
            match IonIrTree.read IonReadLimits.defaults tree with
            | Ok(V4Ir(version, _)) -> $"IR {version}"
            | Ok(ClassicIr(version, _)) -> $"classic IR {version}"
            | Result.Error error ->
                let where = Option.defaultValue "the tree" error.Path
                $"{error.Code} in {where}"

    /// Puts the Ion parser first in a pipeline. Input that is not Ion goes on to the other parsers.
    let withIon (others: Parser<IrDocument> list) : MorphirProcessor<IrDocument> =
        let start =
            MorphirProcessor.empty<IrDocument>
            |> MorphirProcessor.parse IonIrParser.defaultParser

        others
        |> List.fold (fun processor parser -> MorphirProcessor.parse parser processor) start
// README-SAMPLE-END

let private limits = IonReadLimits.defaults

/// A parser for classic IR JSON, standing in for the JSON parser that follows the Ion parser in a pipeline.
let private jsonParser: Parser<IrDocument> =
    Parser.forFile (fun file ->
        match VFile.readText file with
        | Result.Error(ContentError.ReadFailed(path, cause)) ->
            Result.Error(ParseError.Failed(path, cause))
        | Ok text when not (text.TrimStart().StartsWith "{") ->
            Result.Error(ParseError.UnsupportedInput "the file is not JSON")
        | Ok text ->
            use document = JsonDocument.Parse text

            match ClassicJson.readDistribution document.RootElement with
            | Ok read -> Ok(ClassicIr("3.0.0", read.Distribution))
            | Result.Error error ->
                Result.Error(
                    ParseError.InvalidInput
                        {
                            Path = None
                            RuleId = Some error.Rule
                            Location = Some error.Location
                            Message = error.Message
                            Position = None
                        }
                )
    )

let private treeOf (files: (string * string) list) : VFileTree =
    files
    |> List.map (fun (location, text) -> location, VFile.ofText location text)
    |> Map.ofList
    |> VFileTree.fromFileMap

let private fixtureTree (name: string) : VFileTree =
    let prefix = $"trees/{name}/"

    Fixtures.names
    |> Array.filter (fun file -> file.StartsWith prefix)
    |> Array.map (fun file ->
        let location = file.Substring prefix.Length
        location, VFile.ofBytes location (ReadOnlyMemory(Fixtures.bytes file))
    )
    |> Map.ofArray
    |> VFileTree.fromFileMap

let private invalid (error: ParseError) =
    match error with
    | ParseError.InvalidInput failure -> failure
    | other -> failtestf "Expected invalid input, got %A" other

let private int = "\"morphir/SDK:basics#int\""

let private smallTree = [
    "manifest.ion",
    "morphir::{ionVersion: \"0.1.0-draft.1\", formatVersion: \"4.0.0\", kind: library, packageName: \"p\", pathBudget: 4000}\n"
    "pkg/p/m/module.ion", "public::def::module::{}\n"
    "pkg/p/m/a.type.ion", $"public::def::alias::type::{{typeExp: {int}}}\n"
]

let private errorsOf (tree: VFileTree) =
    VFileTree.collectMessages tree
    |> List.filter (fun message ->
        message.Severity = MessageSeverity.Error
        || message.Severity = MessageSeverity.Fatal
    )

/// The lines between the sample markers of this file, without the indent of the module that holds them.
let private sampleFromSource () =
    let lines =
        File.ReadAllLines(Path.Combine(__SOURCE_DIRECTORY__, "IonIrParserTests.fs"))

    let start =
        lines
        |> Array.findIndex (fun line -> line.Trim() = "// README-SAMPLE-START")

    let finish =
        lines
        |> Array.findIndex (fun line -> line.Trim() = "// README-SAMPLE-END")

    lines[start + 1 .. finish - 1]
    |> Array.map (fun line ->
        if
            line.Length
            >= 4
        then
            line.Substring 4
        else
            line.TrimStart()
    )
    |> String.concat "\n"

/// The first F# block after the heading in the README.
let private sampleFromReadme () =
    let readme =
        File
            .ReadAllText(
                Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Morphir.Ion", "README.md")
            )
            .Replace("\r\n", "\n")

    let heading = readme.IndexOf "### Reading a tree in a pipeline"
    Expect.isGreaterThan heading 0 "The README has the section"
    let opening = readme.IndexOf("```fsharp\n", heading)
    let closing = readme.IndexOf("\n```", opening)

    readme.Substring(
        opening
        + "```fsharp\n".Length,
        closing
        - opening
        - "```fsharp\n".Length
    )

[<Tests>]
let tests =
    testList "The Ion parser in a pipeline" [
        testList "What it handles" [
            testCase "An Ion file is parsed"
            <| fun () ->
                let file =
                    VFile.ofBytes
                        "x.ion"
                        (ReadOnlyMemory(Fixtures.bytes "pairs/v4-empty-library.ion"))

                match IonIrParser.defaultParser (File file) with
                | Ok(V4Ir("4.0.0", _)) -> ()
                | other -> failtestf "Expected a v4 document, got %A" other

            testCase "A tree with manifest.ion is parsed"
            <| fun () ->
                match IonIrParser.defaultParser (Directory(fixtureTree "v4-application")) with
                | Ok(V4Ir("4.0.0", _)) -> ()
                | other -> failtestf "Expected a v4 document, got %A" other

            testCase "A file is Ion when its name ends in .10n, whatever it holds"
            <| fun () ->
                let failure =
                    match
                        IonIrParser.defaultParser (File(VFile.ofText "model.10N" "morphir::{"))
                    with
                    | Result.Error error -> invalid error
                    | Ok _ -> failtest "The file is malformed"

                Expect.equal failure.RuleId (Some "malformed_ion") "Read as Ion"

            testCase "A file is Ion when its bytes start with the binary marker"
            <| fun () ->
                let marker = ReadOnlyMemory<byte>([| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |])

                let failure =
                    match IonIrParser.defaultParser (File(VFile.ofBytes "payload.bin" marker)) with
                    | Result.Error error -> invalid error
                    | Ok _ -> failtest "An empty datagram has no header"

                Expect.equal failure.RuleId (Some "invalid_document") "Read as Ion"

            testCase "The marker is found in a stream without reading it all"
            <| fun () ->
                let source =
                    StreamSource(fun () ->
                        new MemoryStream([| 0xE0uy; 0x01uy; 0x00uy; 0xEAuy |]) :> Stream
                    )

                match IonIrParser.defaultParser (File(VFile.ofStream "payload.bin" source)) with
                | Result.Error(ParseError.InvalidInput failure) ->
                    Expect.equal failure.RuleId (Some "invalid_document") "Read as Ion"
                | other -> failtestf "Expected invalid input, got %A" other

            testCase "A JSON file is unsupported, so the next parser gets its turn"
            <| fun () ->
                for file in
                    [
                        VFile.ofText "ir.json" "{}"
                        VFile.ofText "notes.txt" "hello"
                        VFile.ofText "empty" ""
                    ] do
                    match IonIrParser.defaultParser (File file) with
                    | Result.Error(ParseError.UnsupportedInput _) -> ()
                    | other -> failtestf "Expected unsupported input for %A, got %A" file.Path other

            testCase "A file with no content that is named .ion is read as an empty document"
            <| fun () ->
                match IonIrParser.defaultParser (File(VFile.fromPath "empty.ion")) with
                | Result.Error(ParseError.InvalidInput failure) ->
                    Expect.equal failure.RuleId (Some "invalid_document") "No header"
                | other -> failtestf "Expected invalid input, got %A" other

            testCase "A tree with only a JSON or YAML manifest is unsupported"
            <| fun () ->
                for manifest in [ "manifest.json"; "manifest.yaml"; "manifest.yml" ] do
                    let tree = treeOf [ manifest, "{}" ]

                    match IonIrParser.defaultParser (Directory tree) with
                    | Result.Error(ParseError.UnsupportedInput _) -> ()
                    | other -> failtestf "Expected unsupported input for %s, got %A" manifest other

                    match IonIrTree.read limits tree with
                    | Result.Error error ->
                        Expect.equal error.Code "missing_manifest" "The reader says why"
                    | Ok _ -> failtest "Not an Ion tree"

            testCase "A manifest under a directory with an empty path is at the root for both"
            <| fun () ->
                // The reader gives such a directory no segment, so its files sit at the root.
                let files =
                    smallTree
                    |> List.map (fun (location, text) -> File(VFile.ofText location text))

                let tree =
                    { VFileTree.empty with
                        Content = [
                            Directory
                                { VFileTree.empty with
                                    Path = ""
                                    Content = files
                                }
                        ]
                    }

                Expect.isOk (IonIrTree.read limits tree) "The reader finds the manifest"

                match IonIrParser.defaultParser (Directory tree) with
                | Ok _ -> ()
                | other -> failtestf "Expected the tree to be parsed, got %A" other

            testCase "A directory with no manifest at all is unsupported"
            <| fun () ->
                match IonIrParser.defaultParser (Directory(treeOf [ "README.md", "hello" ])) with
                | Result.Error(ParseError.UnsupportedInput _) -> ()
                | other -> failtestf "Expected unsupported input, got %A" other

            testCase "Two manifests is invalid input, not unsupported"
            <| fun () ->
                let failure =
                    match
                        IonIrParser.defaultParser (
                            Directory(
                                treeOf (
                                    ("manifest.json", "{}")
                                    :: smallTree
                                )
                            )
                        )
                    with
                    | Result.Error error -> invalid error
                    | Ok _ -> failtest "The root is ambiguous"

                Expect.equal failure.RuleId (Some "ambiguous_manifest") "The rule"

            testCase
                "A file in a tree that cannot be read is a failed parser with the file's path and cause"
            <| fun () ->
                let cause = IOException "the disk went away"
                let location = "pkg/p/m/a.type.ion"

                let tree =
                    smallTree
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, VFile.ofStream l (StreamSource(fun () -> raise cause))
                        else
                            l, VFile.ofText l text
                    )
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                match IonIrParser.defaultParser (Directory tree) with
                | Result.Error(ParseError.Failed(Some path, found)) ->
                    Expect.equal path location "The file"
                    Expect.isTrue (obj.ReferenceEquals(found, cause)) "The cause"
                | other -> failtestf "Expected a failed parser, got %A" other

            testCase "A single file that cannot be read is a failed parser"
            <| fun () ->
                let cause = IOException "gone"
                let file = VFile.ofStream "x.ion" (StreamSource(fun () -> raise cause))

                match IonIrParser.defaultParser (File file) with
                | Result.Error(ParseError.Failed(Some "x.ion", found)) ->
                    Expect.isTrue (obj.ReferenceEquals(found, cause)) "The cause"
                | other -> failtestf "Expected a failed parser, got %A" other

            testCase "The limits of the parser apply to a tree as a whole"
            <| fun () ->
                // The manifest holds six values, the module file one and the type file two: each fits, the total does not.
                let tight = { limits with MaxNodes = 7 }

                match IonIrParser.parser tight (Directory(treeOf smallTree)) with
                | Result.Error(ParseError.InvalidInput failure) ->
                    Expect.equal failure.RuleId (Some "node_limit_exceeded") "The limit"
                | other -> failtestf "Expected the limit, got %A" other
        ]

        testList "Beside a JSON parser" [
            let processor = Sample.withIon [ jsonParser ]

            testCase "JSON input reaches the JSON parser"
            <| fun () ->
                let json = Fixtures.text "pairs/v3-daily-rate.json"
                let model = ref None

                let capture =
                    Plugin.create
                        "capture"
                        (fun (found: IrDocument) file ->
                            model.Value <- Some found
                            Some found, file
                        )

                let result =
                    MorphirProcessor.run
                        (File(VFile.ofText "daily-rate.json" json))
                        (MorphirProcessor.plugin capture processor)

                Expect.isEmpty (errorsOf result) "No error"

                match model.Value with
                | Some(ClassicIr("3.0.0", _)) -> ()
                | other -> failtestf "The JSON parser should have produced the model, got %A" other

                // The Ion parser declined, which the run records as a warning.
                let declined =
                    VFileTree.collectMessages result
                    |> List.filter (fun message -> message.Severity = MessageSeverity.Warning)

                Expect.hasLength declined 1 "The Ion parser's refusal is a warning"

            testCase "Ion input reaches the Ion parser, and the JSON parser is not needed"
            <| fun () ->
                let model = ref None

                let capture =
                    Plugin.create
                        "capture"
                        (fun (found: IrDocument) file ->
                            model.Value <- Some found
                            Some found, file
                        )

                let result =
                    MorphirProcessor.run
                        (Directory(fixtureTree "v3-daily-rate"))
                        (MorphirProcessor.plugin capture processor)

                Expect.isEmpty (errorsOf result) "No error"

                match model.Value with
                | Some(ClassicIr("3.0.0", _)) -> ()
                | other -> failtestf "The Ion parser should have produced the model, got %A" other

            testCase "An error in a tree reaches the tree's messages with its path and rule"
            <| fun () ->
                let location = "pkg/p/m/rate.value.ion"

                let tree =
                    treeOf (
                        smallTree
                        @ [ location, "public::def::value::{" ]
                    )

                let result = MorphirProcessor.run (Directory tree) processor
                let errors = errorsOf result
                Expect.hasLength errors 1 "One error"
                let message = errors.Head
                Expect.equal message.Path (Some location) "The file"
                Expect.equal message.RuleId (Some "malformed_ion") "The rule"
                Expect.equal message.Source (Some "parser") "The phase"
                Expect.equal message.Severity MessageSeverity.Error "The severity"

            testCase "A problem inside a tree file names the place in it"
            <| fun () ->
                let location = "pkg/p/m/a.type.ion"

                let files =
                    smallTree
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l, $"public::def::alias::type::{{typeExp: {int}, bogus: 1}}\n"
                        else
                            l, text
                    )

                let result = MorphirProcessor.run (Directory(treeOf files)) processor
                let message = (errorsOf result).Head
                Expect.equal message.Path (Some location) "The file"
                Expect.equal message.RuleId (Some "unknown_member") "The rule"
                Expect.equal message.Location (Some "$[0].bogus") "The place in the file"

            testCase "An error in an Ion file reaches the file's messages with its rule and place"
            <| fun () ->
                let text =
                    "morphir::{ionVersion: \"0.1.0-draft.1\", formatVersion: \"4.0.0\", kind: library, packageName: \"p\"}\npublic::def::module::{name: \"m\", bogus: 1}\nmorphir_footer::{}\n"

                let result = MorphirProcessor.run (File(VFile.ofText "model.ion" text)) processor
                let message = (errorsOf result).Head
                Expect.equal message.Path (Some "model.ion") "The processor names the input"
                Expect.equal message.RuleId (Some "unknown_member") "The rule"
                Expect.equal message.Location (Some "$[1].bogus") "The place in the file"

            testCase "A failed read reaches the messages with the file's path"
            <| fun () ->
                let location = "pkg/p/m/a.type.ion"

                let tree =
                    smallTree
                    |> List.map (fun (l, text) ->
                        if l = location then
                            l,
                            VFile.ofStream l (StreamSource(fun () -> raise (IOException "gone")))
                        else
                            l, VFile.ofText l text
                    )
                    |> Map.ofList
                    |> VFileTree.fromFileMap

                let result = MorphirProcessor.run (Directory tree) processor
                let message = (errorsOf result).Head
                Expect.equal message.Path (Some location) "The file"
                Expect.stringContains message.Message "gone" "The cause"

            testCase "Input no parser handles is still reported"
            <| fun () ->
                let result = MorphirProcessor.run (File(VFile.ofText "x.txt" "hello")) processor
                Expect.isNonEmpty (errorsOf result) "No parser succeeded"
        ]

        testList "The README sample" [
            testCase "The sample in the README is the sample compiled here"
            <| fun () -> Expect.equal (sampleFromReadme ()) (sampleFromSource ()) "The same text"
            |> testLabel "PlatformSpecific"

            testCase "describeTree reads a tree from disk"
            <| fun () ->
                let directory =
                    Path.Combine(
                        Path.GetTempPath(),
                        "morphir-ion-sample-"
                        + Guid.NewGuid().ToString "N"
                    )

                try
                    let prefix = "trees/v4-escaped-names/"

                    for file in
                        Fixtures.names
                        |> Array.filter (fun name -> name.StartsWith prefix) do
                        let target = Path.Combine(directory, file.Substring prefix.Length)

                        Directory.CreateDirectory(Path.GetDirectoryName target)
                        |> ignore

                        File.WriteAllBytes(target, Fixtures.bytes file)

                    Expect.equal (Sample.describeTree directory) "IR 4.0.0" "A v4 tree"
                    File.Delete(Path.Combine(directory, "manifest.ion"))

                    Expect.equal
                        (Sample.describeTree directory)
                        "missing_manifest in the tree"
                        "No manifest"

                    Expect.stringContains
                        (Sample.describeTree (Path.Combine(directory, "nowhere")))
                        "directory not found"
                        "No directory"
                finally
                    if Directory.Exists directory then
                        Directory.Delete(directory, true)
            |> testLabel "PlatformSpecific"

            testCase "withIon puts the Ion parser first"
            <| fun () ->
                let processor = Sample.withIon [ jsonParser ]
                Expect.hasLength processor.Parsers 2 "Ion, then JSON"

                match processor.Parsers.Head(File(VFile.ofText "x.json" "{}")) with
                | Result.Error(ParseError.UnsupportedInput _) -> ()
                | other -> failtestf "The first parser is the Ion parser, got %A" other
        ]
    ]
