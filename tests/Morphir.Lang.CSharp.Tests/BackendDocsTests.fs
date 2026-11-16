// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendDocsTests

open System.IO
open System.Text.RegularExpressions
open Expecto
open Morphir.IR.Model
open Morphir.Lang.CSharp.Tests.BackendSupport

let private repositoryRoot =
    let rec up (directory: DirectoryInfo) =
        if File.Exists(Path.Combine(directory.FullName, "Morphir.slnx")) then
            directory.FullName
        else
            match directory.Parent with
            | null -> failwith "Morphir.slnx not found above the test source."
            | parent -> up parent

    up (DirectoryInfo __SOURCE_DIRECTORY__)

let private page =
    File.ReadAllText(Path.Combine(repositoryRoot, "docs", "content", "docs", "backends", "csharp", "_index.md")).Replace("\r\n", "\n")

/// The C# blocks of the section "Profiles" of the page: the source, the functional output and the imperative output.
let private profileBlocks () =
    let start = page.IndexOf "## Profiles\n"
    let finish = page.IndexOf("\n## ", start + 1)
    let section = page.Substring(start, finish - start)
    [ for m in Regex.Matches(section, "```csharp\n(?<body>.*?)\n```", RegexOptions.Singleline) -> m.Groups["body"].Value ]

[<Tests>]
let profiles =
    testList "C# backend documentation: profiles" [
        test "the example of the profiles is the output of the frontend and the backend for each profile, as written" {
            match profileBlocks () with
            | [ source; functional; imperative ] ->
                // The package of the example is acme/demo, as its morphir.toml names it.
                let package =
                    match Morphir.IR.CanonicalNames.parsePath "acme/demo" with
                    | Ok path -> Morphir.IR.PackageName path
                    | Error message -> failwith message

                let request =
                    match Morphir.Compilation.CompileRequest.Parse(Morphir.Lang.CSharp.CSharpFrontend.languageId, null, [ FrontendSupport.document "Totals.cs" source ], package, null, FrontendSupport.v4Target, "") with
                    | Ok request -> request
                    | Error failure -> failwithf "%A" failure

                let result =
                    match FrontendSupport.frontend.CompileAsync(request, System.Threading.CancellationToken.None).GetAwaiter().GetResult() with
                    | Morphir.Compilation.FrontendOutcome.Completed result -> result
                    | Morphir.Compilation.FrontendOutcome.Failed failure -> failwith failure.Message

                let distribution = FrontendSupport.model result
                let model: VersionedModel = { SourceVersion = SourceFormatVersion.Create "4"; Distribution = DistributionModel.V4 distribution }
                let path = "p_Acme.Demo/m_TotalsModule.g.cs"
                let functionalFiles = generateWithProfile Morphir.Lang.CSharp.Profile.Functional model
                let imperativeFiles = generateWithProfile Morphir.Lang.CSharp.Profile.Imperative model
                Expect.stringContains functionalFiles[path] functional "the functional profile"
                Expect.stringContains imperativeFiles[path] imperative "the imperative profile"
            | blocks -> failtest $"The section has {List.length blocks} C# blocks, not 3."
        }
    ]

[<Tests>]
let tests =
    testList "C# backend documentation" [
        test "the example of the page is the output of the backend for its model, as written" {
            let model =
                library
                    [ "shapes",
                      moduleOf
                          [ "shape", custom [] [ "circle", [ "radius", floatT ]; "square", [ "edge", floatT ]; "rect", [ "w", floatT; "h", floatT ] ] ]
                          [ "size",
                            definition
                                [ "s", own "shapes" "shape" [] ]
                                floatT
                                (patternMatch
                                    (variable "s")
                                    [ constructorPattern "shapes#circle" [ asPattern "r" ], variable "r"
                                      constructorPattern "shapes#square" [ asPattern "e" ], variable "e"
                                      constructorPattern "shapes#rect" [ asPattern "w"; asPattern "h" ], variable "w" ]) ] ]

            let generated = (generate model)["p_Acme.Geo/m_ShapesModule.g.cs"]
            // The page shows the file without its two header lines, the comment and the nullable directive.
            let body = generated.Substring(generated.IndexOf "namespace ")
            Expect.stringContains page body "the documented example is the generated file"
        }
    ]
