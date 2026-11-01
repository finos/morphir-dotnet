namespace Morphir.CodeGeneration.FSharp

open System.IO
open System.Text.Json
open Morphir.IR.Classic
open Morphir.IR.Pipeline
open Morphir.IR.Pipeline.Plugins

/// Converts a classic Morphir model to a tree of F# module files through a pipeline.
[<RequireQualifiedAccess>]
module Transpiler =
    /// The generated modules in dependency order, stored as tree metadata.
    let manifest = Property.create<ManifestEntry list> "morphir.fsharp.manifest"

    /// The rule, location and message for an expected generation failure.
    let private (|ExpectedFailure|_|) (error: exn) =
        match error with
        | GenerationFailure(category, location, message) -> Some(category, location, message)
        | :? JsonException as ex -> Some("parse", (if isNull ex.Path then "$" else ex.Path), ex.Message)
        | :? System.InvalidOperationException as ex -> Some("decode", "$", ex.Message)
        | :? System.FormatException as ex -> Some("decode", "$", ex.Message)
        | _ -> None

    /// Whether content starts with a byte order mark that JSON's UTF-8 reader cannot skip.
    let private hasUtf16Mark (content: VFileContent) =
        match VFileContent.openRead content with
        | Result.Error _ -> false
        | Result.Ok stream ->
            use stream = stream
            let first = stream.ReadByte()
            let second = stream.ReadByte()
            (first = 0xFF && second = 0xFE) || (first = 0xFE && second = 0xFF)

    /// Parses the file's JSON, from its stream where that is possible.
    let private parseDocument (file: VFile) (content: VFileContent) : Result<JsonDocument, ContentError> =
        match content with
        | VFileContent.Text text -> Result.Ok(JsonDocument.Parse text)
        | _ when hasUtf16Mark content -> VFile.readText file |> Result.map (fun (text: string) -> JsonDocument.Parse text)
        | _ ->
            match VFileContent.openRead content with
            | Result.Error(ContentError.ReadFailed(_, cause)) -> Result.Error(ContentError.ReadFailed(file.Path, cause))
            | Result.Ok stream ->
                use stream = stream
                Result.Ok(JsonDocument.Parse stream)

    /// Reads a classic model from a file. The model keeps JSON attributes that later stages read,
    /// so it decodes from a clone that does not depend on the document staying open.
    let private parseModel (file: VFile) : Result<Decode.Model, ParseError> =
        let invalid rule location message =
            Result.Error(
                ParseError.InvalidInput
                    {
                        Path = None
                        RuleId = Some rule
                        Location = Some location
                        Message = message
                        Position = None
                    }
            )

        match file.Content with
        | None -> invalid "parse" "$" "The model file has no content."
        | Some content ->
            try
                match parseDocument file content with
                | Result.Error(ContentError.ReadFailed(path, cause)) -> Result.Error(ParseError.Failed(path, cause))
                | Result.Ok document ->
                    use document = document
                    Result.Ok(Decode.model (document.RootElement.Clone()))
            with ExpectedFailure(rule, location, message) ->
                invalid rule location message

    /// Emits one file per module, declaring dependency order separately from directory structure.
    let private compile (model: Decode.Model) (_: VFile) : VFileTree =
        try
            let modules = Emit.generate model

            let tree =
                modules
                |> List.map (fun generated -> generated.RelativePath, VFile.ofText generated.RelativePath generated.Source)
                |> Map.ofList
                |> VFileTree.fromFileMap

            tree
            |> VFileTree.set VFileTree.declaredOrder (modules |> List.map (fun generated -> generated.RelativePath))
            |> VFileTree.set
                manifest
                (modules
                 |> List.map (fun generated ->
                     {
                         ManifestEntry.ModulePath = generated.ModulePath
                         Location = generated.RelativePath
                     }))
        with ExpectedFailure(rule, location, message) ->
            VFileTree.empty
            |> VFileTree.report
                { VMessage.create MessageSeverity.Error message with
                    Location = Some location
                    RuleId = Some rule
                    Source = Some "fsharp-compiler"
                }

    /// Reports the first variable that is not bound in its lexical scope. Later plugins would be
    /// typing a broken model, so this stops the run.
    let private scopeValidator: Plugin<Decode.Model> =
        Plugin.create "scope-validator" (fun model file ->
            try
                for path, controlled in Map.toList model.Definition.Modules do
                    for name, definition in Map.toList controlled.Value.Values do
                        Validate.scopes (Emit.modulePath path + "." + Emit.camel name) (Decode.unwrap definition.Value)

                Some model, file
            with ExpectedFailure(rule, location, message) ->
                None,
                file
                |> VFile.report
                    { VMessage.create MessageSeverity.Error message with
                        Location = Some location
                        RuleId = Some rule
                        Source = Some "scope-validator"
                    })

    /// Runs a package plugin on the model's package.
    let private onPackage (plugin: Plugin<Package.PackageDefinition<JsonElement, JsonElement>>) : Plugin<Decode.Model> =
        Plugin.focus (fun (model: Decode.Model) -> model.Definition) (fun definition model -> { model with Definition = definition }) plugin

    let private withoutOptimizer: MorphirProcessor<Decode.Model> =
        pipeline {
            parse (Parser.forFile parseModel)
            uses scopeValidator
            uses (onPackage (TypeValidator.create ()))
            stringify compile
        }

    // Folding only: other rewrites could delete code that emission would reject, which would change
    // which models are accepted.
    let private processor: MorphirProcessor<Decode.Model> =
        pipeline {
            parse (Parser.forFile parseModel)
            uses scopeValidator
            uses (onPackage (TypeValidator.create ()))
            uses (onPackage (Optimizer.create Optimizer.foldingOnly))
            stringify compile
        }

    let private run (selected: MorphirProcessor<Decode.Model>) (input: VFile) : VFileTree =
        let tree = MorphirProcessor.processFile input selected

        // A compiler reports without knowing the input file, so name it here.
        { tree with
            Messages =
                tree.Messages
                |> List.map (fun message ->
                    match message.Path with
                    | Some _ -> message
                    | None -> { message with Path = input.Path })
        }

    /// <summary>
    /// Generates F# from a classic model file. The result has one file per module and declares
    /// their dependency order through <c>manifest</c> and <c>VFileTree.declaredOrder</c>. A failure
    /// returns a tree with no files and error messages naming the rule and location.
    /// </summary>
    /// <param name="input">The model file</param>
    let generate (input: VFile) : VFileTree = run processor input

    /// Generates with the optimizer on or off, so tests can compare behaviour.
    let internal generateWith (optimize: bool) (input: VFile) : VFileTree =
        if optimize then
            generate input
        else
            run withoutOptimizer input
