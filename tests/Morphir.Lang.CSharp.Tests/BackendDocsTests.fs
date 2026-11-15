// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendDocsTests

open System.IO
open Expecto
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
