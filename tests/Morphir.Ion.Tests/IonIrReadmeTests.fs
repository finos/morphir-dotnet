module Morphir.Ion.Tests.IonIrReadmeTests

open System.IO
open System.Text.RegularExpressions
open Expecto

let private sourceRoot =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Morphir.Ion")

/// Every code the Ion IR reader can return, found by scanning its source where it raises or names one.
let private codesInSource () =
    let files = [
        yield! Directory.GetFiles(Path.Combine(sourceRoot, "Ir"), "*.fs")
        yield Path.Combine(sourceRoot, "IonValue.fs")
    ]

    [
        for file in files do
            for found in
                Regex.Matches(
                    File.ReadAllText file,
                    @"(?:fail\s+|Code = |IonReadError\.\w+ _ -> )""([a-z0-9_]+)"""
                ) do
                yield found.Groups[1].Value
    ]
    |> List.distinct

[<Tests>]
let tests =
    testList "Ion IR error codes" [
        testCase "Every code in the source is listed in the README"
        <| fun () ->
            let readme = File.ReadAllText(Path.Combine(sourceRoot, "README.md"))
            let codes = codesInSource ()
            Expect.isGreaterThan codes.Length 15 "The scan finds the codes"

            for code in codes do
                Expect.stringContains readme $"`{code}`" "The README lists the code"
    ]
