#!/usr/bin/env -S dotnet fsi

(*
codegen-helpers.fsx

Purpose: Build-time code generation utilities for visitors and lenses.
For JSON, use the Morphir.Myriad.SystemTextJson plugin (src/Morphir.Myriad.SystemTextJson), which generates
System.Text.Json converters and contexts at build time.

Usage:
    dotnet fsi codegen-helpers.fsx <command> [args]

Commands:
    visitor <type-file>     - Generate visitor pattern for recursive types
    lenses <type-file>      - Generate lenses for record types

Example:
    dotnet fsi codegen-helpers.fsx visitor src/Morphir.Core/IR/Type.fs

Output:
    - Generated F# code
    - Usage instructions
*)

#r "nuget: Spectre.Console, 0.49.1"

open System
open System.IO
open Spectre.Console

type Command =
    | Visitor of typeFile: string
    | Lenses of typeFile: string
    | Unknown of cmd: string

let parseCommand (args: string[]) : Command =
    if args.Length < 1 then
        Unknown ""
    else
        match args.[0].ToLower() with
        | "visitor" when args.Length >= 2 -> Visitor args.[1]
        | "lenses" when args.Length >= 2 -> Lenses args.[1]
        | cmd -> Unknown cmd

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
    AnsiConsole.MarkupLine("  visitor <type-file>     - Generate visitor pattern")
    AnsiConsole.MarkupLine("  lenses <type-file>      - Generate lenses")
    AnsiConsole.WriteLine()
    AnsiConsole.MarkupLine("For JSON, add an FSharpJsonContext item with the Morphir.Myriad.SystemTextJson package.")

// Main
let args = fsi.CommandLineArgs |> Array.skip 1

match parseCommand args with
| Visitor typeFile -> generateVisitor typeFile
| Lenses typeFile -> generateLenses typeFile
| Unknown _ ->
    showUsage()
    exit 1
