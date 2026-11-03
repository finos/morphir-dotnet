module Morphir.CodeGeneration.CSharp.Tests.CancellationCases

open System
open System.Diagnostics
open System.Threading
open Expecto
open Morphir.IR
open Morphir.IR.Model
open Morphir.CodeGeneration.CSharp

// A compact, acyclic alias DAG expands into many tuple nodes. Cancellation must
// interrupt expansion itself, rather than wait for its next caller-side check.
let private model depth =
    let a = TypeCases.a
    let pkg = PackageName.packageNameFromString "example"
    let path = ModulePath.modulePathFromString "cancellation"
    let root = Name.fromString "aRoot"
    let name index = Name [ "layer"; string index ]
    let fq local = FQName.fqName pkg path local

    let reference local =
        Morphir.IR.Classic.Type.Reference(a, fq local, [])

    let alias body =
        Morphir.IR.Classic.AccessControlled.public' (
            Morphir.IR.Classic.Documented.WithoutDocumentation(
                Morphir.IR.Classic.Type.TypeAliasDefinition([], body)
            )
        )

    let types =
        [
            yield root, alias (reference (name depth))
            for index in 0..depth do
                let body =
                    if index = 0 then
                        Morphir.IR.Classic.Type.Reference(a, TypeCases.sdk "basics" "int", [])
                    else
                        Morphir.IR.Classic.Type.Tuple(
                            a,
                            [ reference (name (index - 1)); reference (name (index - 1)) ]
                        )

                yield name index, alias body
        ]
        |> Map.ofList

    let modul
        : Morphir.IR.Classic.Module.ModuleDefinition<System.Text.Json.JsonElement, System.Text.Json.JsonElement> = {
        Types = types
        Values = Map.empty
        Doc = None
    }

    {
        SourceVersion = TypeCases.version "3"
        Distribution =
            DistributionModel.Classic(
                Morphir.IR.Classic.Distribution.Library(
                    pkg,
                    Map.empty,
                    {
                        Modules =
                            Map.ofList[path, Morphir.IR.Classic.AccessControlled.public' modul]
                    }
                )
            )
    }

let private duringActive label input =
    use cancellation = new CancellationTokenSource()
    use started = new ManualResetEventSlim(false)
    let mutable outcome = Error(Exception "The worker did not run.")

    let worker =
        Thread(
            ThreadStart(fun () ->
                started.Set()

                try
                    outcome <-
                        Ok(
                            CSharpGenerator.Generate(
                                input,
                                GenerationOptions.Default,
                                cancellation.Token
                            )
                        )
                with error ->
                    outcome <- Error error
            )
        )

    worker.IsBackground <- true
    worker.Start()
    Expect.isTrue (started.Wait(TimeSpan.FromSeconds 2.0)) "generation worker started"
    Thread.Sleep 30
    let wasRunning = not (worker.Join 0)
    let latency = Stopwatch.StartNew()
    cancellation.Cancel()
    let stopped = worker.Join(TimeSpan.FromSeconds 30.0)
    latency.Stop()
    Expect.isTrue stopped "the owned generation thread must exit before the assertion"
    Expect.isTrue wasRunning "cancellation occurred during a nonempty active generation"

    match outcome with
    | Error error -> raise error
    | Ok result ->
        Expect.equal result.Status GenerationStatus.Cancelled "caller cancellation"
        Expect.isEmpty result.Sources "whole-model cancellation returns no partial sources"
        Expect.equal result.Family input.Family "typed family survives cancellation"

        Expect.equal
            result.SourceVersion
            input.SourceVersion.Text
            "source version survives cancellation"

    printfn
        "Mid-generation cancellation %s: active=%b, stopLatencyMs=%.3f"
        label
        wasRunning
        latency.Elapsed.TotalMilliseconds

    Expect.isTrue
        (latency.Elapsed < TimeSpan.FromMilliseconds 500.0)
        (sprintf "Cancellation should interrupt %s promptly, observed %A" label latency.Elapsed)

let duringAliasExpansion () =
    let warm =
        CSharpGenerator.Generate(model 3, GenerationOptions.Default, CancellationToken.None)

    Expect.equal warm.Status GenerationStatus.Success "the acyclic source model is admitted"
    duringActive "alias expansion" (model 22)

let duringLargeLiteral (make: FunctionCases.Definition list -> VersionedModel) =
    let definition value : FunctionCases.Definition = {
        Name = "text"
        Inputs = []
        Output = FunctionCases.Text
        Body = FunctionCases.StringValue value
    }

    let warm =
        CSharpGenerator.Generate(
            make [ definition "warm" ],
            GenerationOptions.Default,
            CancellationToken.None
        )

    Expect.equal warm.Status GenerationStatus.Success "the literal input is admitted"
    duringActive "large string generation" (make [ definition (String('x', 1000000)) ])
