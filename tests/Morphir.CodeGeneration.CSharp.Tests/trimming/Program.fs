module TrimConsumer

open System
open System.Security.Cryptography
open System.Text
open System.Threading
open Morphir.IR.Model
open Morphir.CodeGeneration.CSharp
open Morphir.CodeGeneration.CSharp.Tests

let require condition message =
    if not condition then
        failwith message

[<EntryPoint>]
let main _ =
    let definitions =
        PatternCases.definitions
        @ SdkCases.definitions

    let models = [
        PatternCases.classicWithDefinitions definitions
        PatternCases.v4WithDefinitions definitions
    ]

    for model in models do
        for language in [ 14; 15 ] do
            let options = { LanguageVersion = language }
            let first = CSharpGenerator.Generate(model, options, CancellationToken.None)
            let second = CSharpGenerator.Generate(model, options, CancellationToken.None)

            require
                (first.Status = GenerationStatus.Success
                 && first.Sources.Length = 2)
                "Nonempty typed generation"

            require (first = second) "Deterministic in-memory generation"

            require
                (first.SourceVersion = model.SourceVersion.Text
                 && first.Family = model.Family)
                "Source fidelity"

            require
                (first.Runtime.TargetFramework = "net10.0"
                 && first.Runtime.Package = "Morphir.SDK.CSharp")
                "Output runtime contract"

            for source in first.Sources do
                let hash =
                    SHA256.HashData(Encoding.UTF8.GetBytes source.Text)
                    |> Convert.ToHexString

                printfn
                    "%A/%s/%d/%s=%s"
                    first.Family
                    first.SourceVersion
                    language
                    source.RelativePath
                    hash

            use cancellation = new CancellationTokenSource()
            cancellation.Cancel()
            let cancelled = CSharpGenerator.Generate(model, options, cancellation.Token)

            require
                (cancelled.Status = GenerationStatus.Cancelled
                 && List.isEmpty cancelled.Sources)
                "Cancellation boundary"

            let refused =
                CSharpGenerator.Generate(model, { LanguageVersion = 13 }, CancellationToken.None)

            require
                (refused.Status = GenerationStatus.Failure
                 && List.isEmpty refused.Sources)
                "Refusal boundary"

    printfn "fresh-in-memory-consumer:passed"
    0
