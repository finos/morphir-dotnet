#!/usr/bin/env -S dotnet fsi

(*
analyze-elm-module.fsx

Purpose: Analyze Elm module structure, dependencies, types, functions, and identify code generation opportunities.

Usage:
    dotnet fsi analyze-elm-module.fsx <elm-file-path>

Example:
    dotnet fsi analyze-elm-module.fsx src/Morphir/IR/Type.elm

Output:
    - Module name and package
    - Type definitions (custom types, aliases, records)
    - Function signatures
    - Dependencies on other modules
    - Code generation opportunities (repetitive patterns)
    - Complexity metrics
*)

#r "nuget: Spectre.Console, 0.49.1"

open System
open System.IO
open System.Text.RegularExpressions
open Spectre.Console

type ElmTypeDefinition =
    | CustomType of name: string * cases: (string * string list) list
    | TypeAlias of name: string * definition: string
    | OpaqueType of name: string

type ElmFunction = {
    Name: string
    Signature: string
    DocComment: string option
}

type ElmModule = {
    PackagePath: string list
    ModuleName: string
    Imports: string list
    Types: ElmTypeDefinition list
    Functions: ElmFunction list
    ExposedItems: string list
}

type CodeGenOpportunity =
    | JsonCodecs of typeNames: string list
    | Visitors of typeNames: string list
    | Lenses of typeNames: string list
    | Boilerplate of pattern: string * typeCount: int

let extractModuleName (content: string) : (string list * string) option =
    let moduleRegex = Regex(@"module\s+([\w\.]+)\s+exposing", RegexOptions.Multiline)
    match moduleRegex.Match(content) with
    | m when m.Success ->
        let fullName = m.Groups.[1].Value
        let parts = fullName.Split('.')
        if parts.Length > 1 then
            Some (parts.[0..parts.Length-2] |> Array.toList, parts.[parts.Length-1])
        else
            Some ([], fullName)
    | _ -> None

let extractExposedItems (content: string) : string list =
    let exposingRegex = Regex(@"exposing\s*\((.*?)\)", RegexOptions.Singleline)
    match exposingRegex.Match(content) with
    | m when m.Success ->
        let exposedText = m.Groups.[1].Value
        if exposedText.Contains("..") then
            ["(..)"] // All items exposed
        else
            exposedText.Split(',')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace(s)))
            |> Array.toList
    | _ -> []

let extractImports (content: string) : string list =
    let importRegex = Regex(@"^import\s+([\w\.]+)", RegexOptions.Multiline)
    importRegex.Matches(content)
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Seq.toList

let extractCustomTypes (content: string) : ElmTypeDefinition list =
    let typeRegex = Regex(@"type\s+(\w+)(?:\s+\w+)*\s*=\s*([^=]+?)(?=\n\n|\ntype\s|\n{-|\z)", RegexOptions.Singleline)
    typeRegex.Matches(content)
    |> Seq.cast<Match>
    |> Seq.map (fun m ->
        let name = m.Groups.[1].Value
        let definition = m.Groups.[2].Value
        
        // Parse cases
        let cases =
            definition.Split('|')
            |> Array.map (fun case ->
                let trimmed = case.Trim()
                let parts = trimmed.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                if parts.Length > 0 then
                    let caseName = parts.[0]
                    let caseArgs = parts.[1..] |> Array.toList
                    Some (caseName, caseArgs)
                else
                    None
            )
            |> Array.choose id
            |> Array.toList
        
        CustomType (name, cases)
    )
    |> Seq.toList

let extractTypeAliases (content: string) : ElmTypeDefinition list =
    let aliasRegex = Regex(@"type alias\s+(\w+)(?:\s+\w+)*\s*=\s*([^=]+?)(?=\n\n|\ntype\s|\n{-|\z)", RegexOptions.Singleline)
    aliasRegex.Matches(content)
    |> Seq.cast<Match>
    |> Seq.map (fun m ->
        let name = m.Groups.[1].Value
        let definition = m.Groups.[2].Value.Trim()
        TypeAlias (name, definition)
    )
    |> Seq.toList

let extractFunctions (content: string) : ElmFunction list =
    // Extract doc comments and function signatures
    let funcRegex = Regex(@"(?:({-\|([^}]+?)-})\s+)?(\w+)\s*:\s*(.+?)(?=\n(?:\w+\s*:|\w+\s*=|{-\||\z))", RegexOptions.Singleline)
    funcRegex.Matches(content)
    |> Seq.cast<Match>
    |> Seq.map (fun m ->
        let docComment =
            if m.Groups.[2].Success then
                Some (m.Groups.[2].Value.Trim())
            else
                None
        
        let name = m.Groups.[3].Value
        let signature = m.Groups.[4].Value.Trim().Replace("\n", " ")
        
        {
            Name = name
            Signature = signature
            DocComment = docComment
        }
    )
    |> Seq.toList

let identifyCodeGenOpportunities (types: ElmTypeDefinition list) (functions: ElmFunction list) : CodeGenOpportunity list =
    let mutable opportunities = []
    
    // Check for JSON codec opportunities
    let codecCandidates =
        types
        |> List.choose (fun t ->
            match t with
            | CustomType (name, cases) when cases.Length >= 3 -> Some name
            | TypeAlias (name, def) when def.Contains("{") -> Some name
            | _ -> None
        )
    
    if codecCandidates.Length >= 3 then
        opportunities <- JsonCodecs codecCandidates :: opportunities
    
    // Check for visitor opportunities (recursive types)
    let visitorCandidates =
        types
        |> List.choose (fun t ->
            match t with
            | CustomType (name, cases) ->
                let hasRecursion = cases |> List.exists (fun (_, args) -> args |> List.exists (fun a -> a.Contains(name)))
                if hasRecursion then Some name else None
            | _ -> None
        )
    
    if visitorCandidates.Length >= 1 then
        opportunities <- Visitors visitorCandidates :: opportunities
    
    // Check for lens opportunities (nested records)
    let lensCandidates =
        types
        |> List.choose (fun t ->
            match t with
            | TypeAlias (name, def) when def.Contains("{") && def.Contains(":") -> Some name
            | _ -> None
        )
    
    if lensCandidates.Length >= 3 then
        opportunities <- Lenses lensCandidates :: opportunities
    
    opportunities

let analyzeModule (filePath: string) : Result<ElmModule, string> =
    if not (File.Exists(filePath)) then
        Error $"File not found: {filePath}"
    else
        try
            let content = File.ReadAllText(filePath)
            
            match extractModuleName content with
            | None -> Error "Could not parse module name"
            | Some (packagePath, moduleName) ->
                let types = extractCustomTypes content @ extractTypeAliases content
                let functions = extractFunctions content
                
                Ok {
                    PackagePath = packagePath
                    ModuleName = moduleName
                    Imports = extractImports content
                    Types = types
                    Functions = functions
                    ExposedItems = extractExposedItems content
                }
        with
        | ex -> Error $"Error reading file: {ex.Message}"

let renderModule (elmModule: ElmModule) =
    let panel = Panel($"[bold yellow]Module:[/] [cyan]{String.Join(".", elmModule.PackagePath @ [elmModule.ModuleName])}[/]")
    panel.Border <- BoxBorder.Rounded
    AnsiConsole.Write(panel)
    
    AnsiConsole.WriteLine()
    
    // Exposed items
    AnsiConsole.MarkupLine("[bold]Exposed Items:[/]")
    if elmModule.ExposedItems.IsEmpty then
        AnsiConsole.MarkupLine("  [dim]None explicitly exposed[/]")
    else
        for item in elmModule.ExposedItems do
            AnsiConsole.MarkupLine($"  - {item}")
    
    AnsiConsole.WriteLine()
    
    // Imports
    AnsiConsole.MarkupLine("[bold]Imports:[/]")
    if elmModule.Imports.IsEmpty then
        AnsiConsole.MarkupLine("  [dim]No imports[/]")
    else
        for imp in elmModule.Imports do
            AnsiConsole.MarkupLine($"  - {imp}")
    
    AnsiConsole.WriteLine()
    
    // Types
    AnsiConsole.MarkupLine($"[bold]Type Definitions:[/] [green]{elmModule.Types.Length}[/]")
    for typedef in elmModule.Types do
        match typedef with
        | CustomType (name, cases) ->
            AnsiConsole.MarkupLine($"  [cyan]type {name}[/] = [dim]{cases.Length} cases[/]")
            for (caseName, args) in cases do
                let argsStr = if args.IsEmpty then "" else $" ({String.Join(", ", args)})"
                AnsiConsole.MarkupLine($"    | {caseName}{argsStr}")
        | TypeAlias (name, def) ->
            let defPreview = if def.Length > 50 then def.Substring(0, 50) + "..." else def
            AnsiConsole.MarkupLine($"  [cyan]type alias {name}[/] = [dim]{defPreview}[/]")
        | OpaqueType name ->
            AnsiConsole.MarkupLine($"  [cyan]type {name}[/] [dim](opaque)[/]")
    
    AnsiConsole.WriteLine()
    
    // Functions
    AnsiConsole.MarkupLine($"[bold]Functions:[/] [green]{elmModule.Functions.Length}[/]")
    for func in elmModule.Functions |> List.take (min 10 elmModule.Functions.Length) do
        let sigPreview = if func.Signature.Length > 60 then func.Signature.Substring(0, 60) + "..." else func.Signature
        let docMarker = if func.DocComment.IsSome then "[yellow]📝[/] " else ""
        AnsiConsole.MarkupLine($"  {docMarker}[cyan]{func.Name}[/] : [dim]{sigPreview}[/]")
    
    if elmModule.Functions.Length > 10 then
        AnsiConsole.MarkupLine($"  [dim]... and {elmModule.Functions.Length - 10} more[/]")

let renderCodeGenOpportunities (opportunities: CodeGenOpportunity list) =
    if opportunities.IsEmpty then
        AnsiConsole.MarkupLine("[dim]No code generation opportunities identified[/]")
    else
        AnsiConsole.MarkupLine("[bold yellow]Code Generation Opportunities:[/]")
        
        for opp in opportunities do
            match opp with
            | JsonCodecs typeNames ->
                AnsiConsole.MarkupLine($"  [green]✓[/] [bold]JSON Codecs[/] - {typeNames.Length} types: {String.Join(", ", typeNames)}")
                AnsiConsole.MarkupLine("    → Consider: Myriad plugin or System.Text.Json source generators")
            | Visitors typeNames ->
                AnsiConsole.MarkupLine($"  [green]✓[/] [bold]Visitor Pattern[/] - {typeNames.Length} recursive types: {String.Join(", ", typeNames)}")
                AnsiConsole.MarkupLine("    → Consider: Myriad visitor generator")
            | Lenses typeNames ->
                AnsiConsole.MarkupLine($"  [green]✓[/] [bold]Lenses[/] - {typeNames.Length} record types: {String.Join(", ", typeNames)}")
                AnsiConsole.MarkupLine("    → Consider: Myriad lens generator")
            | Boilerplate (pattern, count) ->
                AnsiConsole.MarkupLine($"  [green]✓[/] [bold]Boilerplate[/] - {pattern} ({count} instances)")
                AnsiConsole.MarkupLine("    → Consider: Custom Myriad plugin or build script")

let renderComplexityMetrics (elmModule: ElmModule) =
    let totalTypes = elmModule.Types.Length
    let totalFunctions = elmModule.Functions.Length
    let functionsWithDocs = elmModule.Functions |> List.filter (fun f -> f.DocComment.IsSome) |> List.length
    let docCoverage = if totalFunctions > 0 then (float functionsWithDocs / float totalFunctions) * 100.0 else 0.0
    
    AnsiConsole.MarkupLine("[bold]Complexity Metrics:[/]")
    AnsiConsole.MarkupLine($"  Types: [green]{totalTypes}[/]")
    AnsiConsole.MarkupLine($"  Functions: [green]{totalFunctions}[/]")
    AnsiConsole.MarkupLine($"  Doc Coverage: [yellow]{docCoverage:F1}%%[/] ({functionsWithDocs}/{totalFunctions})")
    AnsiConsole.MarkupLine($"  Imports: [cyan]{elmModule.Imports.Length}[/]")

// Main
let args = fsi.CommandLineArgs |> Array.skip 1

if args.Length < 1 then
    AnsiConsole.MarkupLine("[red]Error:[/] Missing Elm file path")
    AnsiConsole.MarkupLine("Usage: dotnet fsi analyze-elm-module.fsx <elm-file-path>")
    exit 1

let filePath = args.[0]

AnsiConsole.Status()
    .Start("Analyzing Elm module...", fun ctx ->
        ctx.Spinner <- Spinner.Known.Dots
        ctx.SpinnerStyle <- Style.Parse("green")
        
        match analyzeModule filePath with
        | Error err ->
            AnsiConsole.MarkupLine($"[red]Error:[/] {err}")
            exit 1
        | Ok elmModule ->
            AnsiConsole.Clear()
            renderModule elmModule
            AnsiConsole.WriteLine()
            
            let opportunities = identifyCodeGenOpportunities elmModule.Types elmModule.Functions
            renderCodeGenOpportunities opportunities
            AnsiConsole.WriteLine()
            
            renderComplexityMetrics elmModule
            
            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine("[dim]Analysis complete![/]")
    )
