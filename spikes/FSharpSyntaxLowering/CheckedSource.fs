// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Spikes.CheckedSource

open System
open System.IO
open Expecto
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Morphir.TestSupport

type Program = {
    Source: string
    Syntax: FSharp.Compiler.Syntax.ParsedInput
    Declarations: FSharpImplementationFileDeclaration list
    Symbols: FSharpSymbolUse array
}

let private checker = FSharpChecker.Create(keepAssemblyContents = true)

let check (source: string) =
    use sandbox = TestSandbox.Create("morphir-syntax-lowering-")
    let file = Path.Combine(sandbox.Work, "Pricing.fs")
    File.WriteAllText(file, source)

    let references =
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") :?> string)
            .Split(Path.PathSeparator)
        |> Array.map (fun path ->
            "-r:"
            + path
        )

    let options =
        checker.GetProjectOptionsFromCommandLineArgs(
            Path.Combine(sandbox.Work, "Spike.fsproj"),
            Array.concat [
                [| "--target:library"; "--noframework"; "--simpleresolution"; "--optimize-" |]
                references
                [| file |]
            ]
        )

    let parsing, _ = checker.GetParsingOptionsFromProjectOptions options

    let parsed =
        checker.ParseFile(file, SourceText.ofString source, parsing)
        |> Async.RunSynchronously

    let project =
        checker.ParseAndCheckProject options
        |> Async.RunSynchronously

    let errors =
        project.Diagnostics
        |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

    Expect.isEmpty errors (sprintf "The probe source must type-check: %A" errors)

    {
        Source = source
        Syntax = parsed.ParseTree
        Declarations =
            project.AssemblyContents.ImplementationFiles
            |> Seq.collect (fun file -> file.Declarations)
            |> Seq.toList
        Symbols = project.GetAllUsesOfAllSymbols()
    }
