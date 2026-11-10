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

/// A binding check's inputs: the recipe as a value, the authenticated targets, and the bytes served in place of the
/// signed repository's.
type Setup =
    { Recipe: Value
      Listed: Map<string, AuthenticatedTarget>
      Served: Map<string, byte array> }

let signed () =
    { Recipe = valueOf recipeTarget
      Listed = SignedCaseTests.authenticatedTargets "metadata"
      Served = Map.empty }

let rec replaceText (old: string) (replacement: string) (value: Value) =
    match value with
    | Value.String text when text = old -> Value.String replacement
    | Value.Object members -> Value.Object(members |> List.map (fun (name, item) -> name, replaceText old replacement item))
    | Value.Array items -> Value.Array(items |> List.map (replaceText old replacement))
    | other -> other

let private currentValue (setup: Setup) (target: string) =
    match setup.Served.TryFind target with
    | Some bytes ->
        match Syntax.parseJson bytes with
        | Ok value -> value
        | Error _ -> failtestf "%s does not parse" target
    | None -> valueOf target

/// Serves a target's edited value, listed with its new length and digest (its custom object kept), with every recipe
/// digest of its old bytes rebound to the new ones: the publisher bound the edited bytes.
let editTarget (target: string) (edit: Value -> Value) (setup: Setup) =
    let old = setup.Listed[target]
    let bytes = bytesOf (edit (currentValue setup target))

    let entry =
        { old with
            Length = uint64 bytes.Length
            Sha256 = Sha256.ofBytes bytes }

    { Recipe = replaceText old.Sha256.Text entry.Sha256.Text setup.Recipe
      Listed = setup.Listed.Add(target, entry)
      Served = setup.Served.Add(target, bytes) }

/// Serves other bytes for a target without listing them: the served bytes no longer match their entry.
let serve (target: string) (bytes: byte array) (setup: Setup) =
    { setup with Served = setup.Served.Add(target, bytes) }

let editRecipe (edit: Value -> Value) (setup: Setup) = { setup with Recipe = edit setup.Recipe }

let editListed (target: string) (edit: AuthenticatedTarget -> AuthenticatedTarget) (setup: Setup) =
    { setup with Listed = setup.Listed.Add(target, edit setup.Listed[target]) }

let unlist (target: string) (setup: Setup) = { setup with Listed = setup.Listed.Remove target }

/// The descriptor with an edit applied to each of its artifacts.
let eachArtifact (edit: Value -> Value) (descriptor: Value) =
    setAt [ "artifacts" ] (Value.Array(items (memberOf "artifacts" descriptor) |> List.map edit)) descriptor

/// The check's outcome as "valid" or "code @ path".
let outcomeOf (setup: Setup) =
    checkWith setup.Listed setup.Served (readRecipe setup.Recipe)

let expectOutcome (expected: string) (setup: Setup) = Expect.equal (outcomeOf setup) expected "the outcome"

let tuplePath = [ "claims"; "capabilities"; "modelTransport"; "tuples"; "0" ]

/// The value at a path of member names and array indexes.
let rec valueAt (path: string list) (value: Value) =
    match path, value with
    | [], _ -> value
    | head :: rest, Value.Array items -> valueAt rest (List.item (int head) items)
    | head :: rest, _ -> valueAt rest (memberOf head value)

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

[<Tests>]
let tupleTests =
    // Review of #583, A3: tuples were compared by their canonical bytes, after NFC, so KELVIN SIGN equalled K.
    let irVersion (version: string) =
        signed ()
        |> editRecipe (setAt [ "providers"; "0"; "modelTuple"; "irVersion" ] (str "4.0.0-K") >> setAt [ "providers"; "1"; "modelTuple"; "irVersion" ] (str "4.0.0-K"))
        |> editTarget frontendTarget (eachArtifact (setAt (tuplePath @ [ "irVersion" ]) (str version)))

    testList "A model tuple is compared as a parsed value" [
        testCase "the same text binds" (fun () -> irVersion "4.0.0-K" |> expectOutcome "valid")

        testCase "KELVIN SIGN is not K, though NFC maps one to the other" (fun () ->
            irVersion "4.0.0-K" |> expectOutcome "release_descriptor_mismatch @ providers[0].modelTuple")

        testCase "member order does not matter" (fun () ->
            let reversed (tuple: Value) =
                match tuple with
                | Value.Object members -> Value.Object(List.rev members)
                | other -> other

            signed ()
            |> editTarget frontendTarget (eachArtifact (fun artifact -> setAt tuplePath (reversed (valueAt tuplePath artifact)) artifact))
            |> expectOutcome "valid")
    ]