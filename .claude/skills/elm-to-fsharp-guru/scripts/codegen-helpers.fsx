#!/usr/bin/env -S dotnet fsi

(*
codegen-helpers.fsx

Purpose: Build-time code generation utilities for JSON codecs, visitors, and lenses.

Usage:
    dotnet fsi codegen-helpers.fsx <command> [args]

Commands:
    json-codec <type-file>  - Generate JSON codec for types in file
    visitor <type-file>     - Generate visitor pattern for recursive types
    lenses <type-file>      - Generate lenses for record types

Example:
    dotnet fsi codegen-helpers.fsx json-codec src/Morphir.Core/IR/Type.fs

Output:
    - Generated F# code
    - Usage instructions
*)

#r "nuget: Spectre.Console, 0.49.1"

open System
open System.IO
open Spectre.Console

type Command =
    | JsonCodec of typeFile: string
    | Visitor of typeFile: string
    | Lenses of typeFile: string
    | Unknown of cmd: string

let parseCommand (args: string[]) : Command =
    if args.Length < 1 then
        Unknown ""
    else
        match args.[0].ToLower() with
        | "json-codec" when args.Length >= 2 -> JsonCodec args.[1]
        | "visitor" when args.Length >= 2 -> Visitor args.[1]
        | "lenses" when args.Length >= 2 -> Lenses args.[1]
        | cmd -> Unknown cmd

let generateJsonCodec (typeFile: string) =
    AnsiConsole.MarkupLine($"[bold]Generating JSON codec for:[/] {typeFile}")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[yellow]Note:[/] This is a placeholder. Full implementation requires:")
    AnsiConsole.MarkupLine("  • F# AST parsing")
    AnsiConsole.MarkupLine("  • Type analysis")
    AnsiConsole.MarkupLine("  • Code generation")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[bold]Recommended approach:[/]")
    AnsiConsole.MarkupLine("  1. Use System.Text.Json source generators for C# interop")
    AnsiConsole.MarkupLine("  2. Use custom Myriad plugin for pure F#")
    AnsiConsole.MarkupLine("  3. Write manual codecs for simple types")

let generateVisitor (typeFile: string) =
    AnsiConsole.MarkupLine($"[bold]Generating visitor for:[/] {typeFile}")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[yellow]Note:[/] This is a placeholder. Full implementation requires:")
    AnsiConsole.MarkupLine("  • Recursive type detection")
    AnsiConsole.MarkupLine("  • Visitor pattern generation")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[bold]Recommended approach:[/]")
    AnsiConsole.MarkupLine("  1. Create custom Myriad plugin for visitor generation")
    AnsiConsole.MarkupLine("  2. Manual implementation for complex cases")

let generateLenses (typeFile: string) =
    AnsiConsole.MarkupLine($"[bold]Generating lenses for:[/] {typeFile}")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[yellow]Note:[/] This is a placeholder. Full implementation requires:")
    AnsiConsole.MarkupLine("  • Record type detection")
    AnsiConsole.MarkupLine("  • Lens generation")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[bold]Recommended approach:[/]")
    AnsiConsole.MarkupLine("  1. Use Myriad.Plugins.Lenses (built-in)")
    AnsiConsole.MarkupLine("  2. Custom Myriad plugin for complex scenarios")

let showUsage () =
    AnsiConsole.MarkupLine("[red]Error:[/] Invalid command or missing arguments")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[bold]Usage:[/]")
    AnsiConsole.MarkupLine("  dotnet fsi codegen-helpers.fsx <command> [args]")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("[bold]Commands:[/]")
    AnsiConsole.MarkupLine("  json-codec <type-file>  - Generate JSON codec")
    AnsiConsole.MarkupLine("  visitor <type-file>     - Generate visitor pattern")
    AnsiConsole.MarkupLine("  lenses <type-file>      - Generate lenses")

// Main
let args = fsi.CommandLineArgs |> Array.skip 1

match parseCommand args with
| JsonCodec typeFile -> generateJsonCodec typeFile
| Visitor typeFile -> generateVisitor typeFile
| Lenses typeFile -> generateLenses typeFile
| Unknown _ ->
    showUsage()
    exit 1
