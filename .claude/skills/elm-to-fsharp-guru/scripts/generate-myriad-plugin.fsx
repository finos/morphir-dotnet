#!/usr/bin/env -S dotnet fsi

(*
generate-myriad-plugin.fsx

Purpose: Scaffold custom Myriad plugin projects with templates and MSBuild integration.

Usage:
    dotnet fsi generate-myriad-plugin.fsx <plugin-name>

Example:
    dotnet fsi generate-myriad-plugin.fsx MorphirJsonCodec

Output:
    - Myriad plugin project structure
    - Template implementation
    - MSBuild integration files
    - Usage examples
*)

#r "nuget: Spectre.Console, 0.49.1"

open System
open System.IO
open Spectre.Console

let createPluginProject (pluginName: string) (outputDir: string) =
    let projectDir = Path.Combine(outputDir, pluginName)
    Directory.CreateDirectory(projectDir) |> ignore
    
    // Create project file
    let projectFile = Path.Combine(projectDir, $"{pluginName}.fsproj")
    let projectContent = $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Plugin.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Myriad.Core" Version="0.8.3" />
    <PackageReference Include="FSharp.Compiler.Service" Version="43.8.400" />
  </ItemGroup>
</Project>"""
    File.WriteAllText(projectFile, projectContent)
    
    // Create plugin source file
    let pluginFile = Path.Combine(projectDir, "Plugin.fs")
    let pluginContent = $"""module {pluginName}

open Myriad.Core
open FSharp.Compiler.SyntaxTree

[<MyriadGenerator("{pluginName.ToLower()}")>]
type {pluginName}Generator() =
    interface IMyriadGenerator with
        member _.Generate(context: GeneratorContext) : Output =
            // TODO: Implement your code generation logic here
            
            // 1. Parse input AST
            // let inputTypes = parseInputTypes context
            
            // 2. Generate code
            // let generatedCode = generateCode inputTypes
            
            // 3. Return generated AST
            Output.Ast []

// Helper functions
// let parseInputTypes (context: GeneratorContext) = ...
// let generateCode (types: ...) = ...
"""
    File.WriteAllText(pluginFile, pluginContent)
    
    // Create README
    let readmeFile = Path.Combine(projectDir, "README.md")
    let readmeContent = $"""# {pluginName}

Custom Myriad plugin for morphir-dotnet.

## Purpose

[Describe what this plugin generates]

## Usage

```fsharp
[<{pluginName}>]
type MyType = ...
```

## Generated Code

[Describe what gets generated]

## Development

Build: `dotnet build`
Test: `dotnet test`

## References

- [Myriad Docs](https://moiraesoftware.github.io/myriad/)
- [Custom Plugin Guide](https://moiraesoftware.github.io/myriad/how-to/Create-Plugins.html)
"""
    File.WriteAllText(readmeFile, readmeContent)
    
    projectDir

// Main
let args = fsi.CommandLineArgs |> Array.skip 1

if args.Length < 1 then
    AnsiConsole.MarkupLine("[red]Error:[/] Missing plugin name")
    AnsiConsole.MarkupLine("Usage: dotnet fsi generate-myriad-plugin.fsx <plugin-name>")
    exit 1

let pluginName = args.[0]
let outputDir = Environment.CurrentDirectory

AnsiConsole.Status()
    .Start($"Generating Myriad plugin: {pluginName}...", fun ctx ->
        ctx.Spinner <- Spinner.Known.Dots
        ctx.SpinnerStyle <- Style.Parse("green")
        
        let projectDir = createPluginProject pluginName outputDir
        
        AnsiConsole.Clear()
        let panel = Panel($"[bold green]✓[/] Myriad Plugin Scaffolded")
        panel.Border <- BoxBorder.Rounded
        AnsiConsole.Write(panel)
        
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine($"[bold]Plugin Name:[/] {pluginName}")
        AnsiConsole.MarkupLine($"[bold]Location:[/] [cyan]{projectDir}[/]")
        AnsiConsole.WriteLine()
        
        AnsiConsole.MarkupLine("[bold]Files Created:[/]")
        AnsiConsole.MarkupLine($"  • {pluginName}.fsproj")
        AnsiConsole.MarkupLine("  • Plugin.fs")
        AnsiConsole.MarkupLine("  • README.md")
        
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[bold]Next Steps:[/]")
        AnsiConsole.MarkupLine($"  1. cd {pluginName}")
        AnsiConsole.MarkupLine("  2. Implement code generation in Plugin.fs")
        AnsiConsole.MarkupLine("  3. dotnet build")
        AnsiConsole.MarkupLine("  4. Reference in your project")
    )
