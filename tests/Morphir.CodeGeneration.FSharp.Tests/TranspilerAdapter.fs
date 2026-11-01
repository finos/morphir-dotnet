/// Reads a generation tree back as ordered modules and diagnostics, so behaviour tests can stay
/// focused on what they check.
module Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

open Morphir.IR.Pipeline

type GeneratedModule = {
    ModulePath: string
    RelativePath: string
    Source: string
}

type Diagnostic = {
    Category: string
    InputPath: string
    Location: string
    Message: string
}

/// Modules in dependency order, as the manifest declares them.
let modulesOf (tree: VFileTree) : GeneratedModule list =
    let entries =
        VFileTree.tryGet Morphir.CodeGeneration.FSharp.Transpiler.manifest tree
        |> Option.defaultValue []

    entries
    |> List.map (fun entry ->
        let file = VFileTree.findFile entry.Location tree |> Option.get

        match VFile.readText file with
        | Ok source ->
            {
                ModulePath = entry.ModulePath
                RelativePath = entry.Location
                Source = source
            }
        | Result.Error error -> failwithf "could not read %s: %A" entry.Location error)

/// Error diagnostics on a tree.
let diagnosticsOf (tree: VFileTree) : Diagnostic list =
    VFileTree.collectMessages tree
    |> List.filter (fun message -> message.Severity = MessageSeverity.Error || message.Severity = MessageSeverity.Fatal)
    |> List.map (fun message ->
        {
            Category = message.RuleId |> Option.defaultValue ""
            InputPath = message.Path |> Option.defaultValue ""
            Location = message.Location |> Option.defaultValue ""
            Message = message.Message
        })

let private asResult (tree: VFileTree) =
    if VFileTree.hasErrors tree then
        Result.Error (diagnosticsOf tree)
    else
        Result.Ok (modulesOf tree)

module Transpiler =
    /// Generates from JSON text, as a result of modules or diagnostics.
    let Generate (path: string, json: string) : Result<GeneratedModule list, Diagnostic list> =
        Morphir.CodeGeneration.FSharp.Transpiler.generate (VFile.ofText path json) |> asResult

    /// Generates with the optimizer stage on or off, to compare behaviour.
    let GenerateWith (optimize: bool) (path: string, json: string) : Result<GeneratedModule list, Diagnostic list> =
        Morphir.CodeGeneration.FSharp.Transpiler.generateWith optimize (VFile.ofText path json) |> asResult
