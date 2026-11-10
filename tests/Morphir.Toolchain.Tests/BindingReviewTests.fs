// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The second review of #583: the binding check's cost, the order rules of the host and provider tables, the row
/// contents, the descriptor readers' rules and the resolver's rules, each pinned by a test that a mutation of the rule
/// fails.
/// </summary>
module Morphir.Toolchain.Tests.BindingReviewTests

open System.Text
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.ReleaseDescriptorTests

let recipeTarget = "toolchains/recipes/fsharp/1.0.0.json"
let frontendTarget = "extensions/fsharp-frontend/0.6.0/release.json"

let items (value: Value) =
    match value with
    | Value.Array items -> items
    | _ -> failwith "not an array"

/// An authenticated entry for served bytes, with an optional custom object.
let entryFor (bytes: byte array) (custom: Value option) =
    { Length = uint64 bytes.Length
      Sha256 = Sha256.ofBytes bytes
      Custom = custom }

/// Runs a read under a time budget on its own task, so a quadratic check fails within the budget instead of hanging.
let withinBudget (seconds: float) (what: string) (read: unit -> 'T) : 'T =
    let task = System.Threading.Tasks.Task.Run(fun () -> read ())

    if task.Wait(System.TimeSpan.FromSeconds seconds) then
        task.Result
    else
        failtestf "%s took longer than %.0f s" what seconds

let readRecipe (value: Value) =
    match RecipeDocument.read (bytesOf value) with
    | Ok recipe -> recipe
    | Error refusal -> failtestf "the recipe does not read: %A" refusal

/// The outcome of the binding check, with served bytes from the signed repository unless overridden.
let checkWith (listed: Map<string, AuthenticatedTarget>) (served: Map<string, byte array>) (recipe: Recipe) =
    let bytesOf (target: string) =
        match served.TryFind target with
        | Some bytes -> Some bytes
        | None -> SignedCaseTests.tryTargetBytes target

    match RecipeBindings.check (SignedCaseTests.repository ()) listed bytesOf recipe with
    | Ok _ -> "valid"
    | Error refusal -> $"{Code.name refusal.Code} @ {DocumentPath.render refusal.Path}"

let private textOf (value: Value) =
    match value with
    | Value.String text -> text
    | _ -> failwith "not a string"

/// The reviewer's shape (amplify.fsx): n compile operations, one dotnet-tool provider for each, all bound to one
/// descriptor whose artifacts' claims list all n languages, so every provider passes and the check runs to the end.
let private sharedDescriptor (n: int) (artifactCount: int) =
        testCase $"{n} providers sharing one descriptor of {n} languages and {artifactCount} artifacts" (fun () ->
            let recipe = valueOf recipeTarget
            let descriptor = valueOf frontendTarget
            let provider = items (memberOf "providers" recipe) |> List.item 1
            let languages = Value.Array [ for i in 1..n -> Value.Object [ "id", str $"l{i}" ] ]
            let artifact = (items (memberOf "artifacts" descriptor)).Head |> setAt [ "claims"; "capabilities"; "frontend"; "languages" ] languages
            let filename = textOf (memberOf "filename" artifact)
            let copies = [ for i in 1..artifactCount -> artifact |> setAt [ "filename" ] (Value.String $"{i}-{filename}") ]
            let descriptorBytes = bytesOf (setAt [ "artifacts" ] (Value.Array copies) descriptor)
            let entry = entryFor descriptorBytes None

            let edited =
                recipe
                |> setAt [ "providers" ] (Value.Array [ for i in 1..n -> provider |> setAt [ "language" ] (str $"l{i}") |> setAt [ "descriptorSha256" ] (str entry.Sha256.Text) ])
                |> setAt [ "operations" ] (Value.Array [ for i in 1..n -> Value.Object [ "operation", str "compile"; "language", str $"l{i}" ] ])

            let recipe = readRecipe edited
            let listed = SignedCaseTests.authenticatedTargets "metadata" |> Map.add frontendTarget entry
            let mutable fetches = 0

            let bytesOf (target: string) =
                if target = frontendTarget then
                    fetches <- fetches + 1
                    Some descriptorBytes
                else
                    SignedCaseTests.tryTargetBytes target

            let outcome =
                withinBudget 5.0 $"checking {n} providers" (fun () -> RecipeBindings.check (SignedCaseTests.repository ()) listed bytesOf recipe)

            Expect.isOk outcome "every provider binds"
            Expect.equal fetches 1 "the shared descriptor is fetched once")

[<Tests>]
let costTests =
    // Review of #583, A1: each provider fetched, authenticated and parsed its descriptor again, and scanned its claim
    // lists with List.contains.
    testList "The binding check reads each shared target once" [
        sharedDescriptor 4_000 1
        sharedDescriptor 2_000 6
        testCase "two offerings of one tool release read its descriptor once" (fun () ->
            let recipe = valueOf recipeTarget
            let host0 = (items (memberOf "hosts" recipe)).Head
            let twin = host0 |> setAt [ "implementation" ] (str "twin")
            let edited = recipe |> setAt [ "hosts" ] (Value.Array [ host0; twin ])
            let recipe = readRecipe edited
            let release = "releases/morphir-dotnet-native-aot/1.2.3.json"
            let mutable fetches = 0

            let bytesOf (target: string) =
                if target = release then fetches <- fetches + 1
                SignedCaseTests.tryTargetBytes target

            let outcome = RecipeBindings.check (SignedCaseTests.repository ()) (SignedCaseTests.authenticatedTargets "metadata") bytesOf recipe
            Expect.isOk outcome "both offerings bind"
            Expect.equal fetches 1 "the shared release descriptor is fetched once")
    ]
