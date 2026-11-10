// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The cases of the TEST-ONLY signed manifest that need no TUF client: resolution, frozen locks and lock-against-recipe
/// checks over recipe bytes taken from <c>repository.json</c>, which these pure functions treat as already
/// authenticated, and the one recipe check that only reads the document. Every other section is listed with the reason
/// it is not run, and a test fails when the manifest has a section that is neither run nor listed.
/// </summary>
module Morphir.Toolchain.Tests.SignedCaseTests

open System.Text
open System.Text.Json
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest
open Morphir.Toolchain.Tests.BindingCaseTests

/// Sections this suite runs.
let ranSections = [ "selection"; "frozen"; "lockRecipe"; "recipeChecks" ]

/// Sections this suite does not run, each with its reason.
let notRunSections =
    [ "trust",
      "Loads and mutates the signed repository through a TUF client (signatures, snapshot links, expiry, revocation, target lengths and digests). No tool release metadata client exists in morphir-dotnet yet (task 4.2)."
      "channelSelection",
      "Selects a recipe target from the authenticated targets metadata's custom.morphir channels and status, which needs the TUF client to load that metadata."
      "v1Client",
      "Runs the tool release metadata v1 client by tool id, channel and v1 platform; that client does not exist here yet."
      "v1Descriptors",
      "Reads each release target through the TUF client and validates it against tool-release-v1.schema.json, which is not part of this pin." ]

/// Members of the signed manifest that are data, not case sections.
let metadataMembers = [ "contractVersion"; "repository"; "referenceClient" ]

/// The recipe-check rule this suite runs: the others authenticate targets through the TUF client.
let readerOnlyRecipeRule = "recipe-document-reads"

let private manifest () = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "cases.json")

/// The exact bytes of a target in the TEST-ONLY repository: the UTF-8 bytes of its <c>targetFiles</c> text.
let targetBytes (target: string) =
    use repository = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")

    match repository.RootElement.GetProperty("targetFiles").TryGetProperty target with
    | true, text -> Encoding.UTF8.GetBytes(nonNull (text.GetString()))
    | false, _ -> failtestf "repository.json has no target file %s" target

let repositoryId () =
    use repository = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")
    str repository.RootElement "repositoryId"

let signedRecipe (target: string) =
    RecipeDocument.read (targetBytes target) |> valid target

let private cases (section: string) =
    use document = manifest ()
    [ for case in document.RootElement.GetProperty(section).EnumerateArray() -> case.Clone() ]

let private selectionTests () =
    [ for case in cases "selection" ->
          let name = str case "name"
          requireKnown name [ "name"; "rule"; "recipe"; "request"; "platform"; "expected" ] case

          testCase name (fun () ->
              let request = RequestDocument.read (Encoding.UTF8.GetBytes(str case "request")) |> valid name
              let actual = Resolution.resolve (signedRecipe (str case "recipe")) request (platformOf (str case "platform"))
              expectResolution name actual (resolveExpected name case)) ]

let private frozenTests () =
    [ for case in cases "frozen" ->
          let requestFile = str case "request"
          requireKnown requestFile [ "request"; "lock"; "recipe"; "expected" ] case
          let expected = case.GetProperty "expected"

          requireKnown
              $"{requestFile} expected"
              [ "lockEqualsResolution"; "requestSha256"; "recipeSha256"; "platforms" ]
              expected

          testCase $"frozen {requestFile}" (fun () ->
              let requestBytes = Fixtures.bytesIn Fixtures.signed requestFile
              let request = RequestDocument.read requestBytes |> valid requestFile
              let lockFile = str case "lock"
              let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed lockFile) |> valid lockFile
              let recipeTarget = str case "recipe"
              let recipe = signedRecipe recipeTarget

              Expect.equal request.Identity.Text (str expected "requestSha256") "the request digest"
              Expect.equal (Bindings.lockBindsRequest lock requestBytes) (Ok()) "the lock binds the request bytes"
              Expect.equal lock.Recipe.Sha256.Text (str expected "recipeSha256") "the lock binds the recipe digest"
              Expect.equal recipe.Sha256.Text (str expected "recipeSha256") "the recipe bytes have that digest"

              let platforms = [ for p in expected.GetProperty("platforms").EnumerateArray() -> platformOf (nonNull (p.GetString())) ]
              Expect.equal (lock.Platforms |> List.map fst) platforms "the lock records exactly these platforms"

              Expect.isTrue (expected.GetProperty("lockEqualsResolution").GetBoolean()) "the case asks for resolution equality"

              for platform, closure in lock.Platforms do
                  match Resolution.resolve recipe request platform with
                  | Ok resolved -> Expect.equal resolved.Closure closure $"the {Platform.name platform} closure is the resolution"
                  | Error failure -> failtestf "%s does not resolve: %A" (Platform.name platform) failure

              match Resolution.lock recipe lock.Recipe.RepositoryId lock.Recipe.Target request platforms with
              | Ok(written, _) ->
                  Expect.equal { written with Identity = lock.Identity } lock "setup writes the same lock"
              | Error failure -> failtestf "the lock cannot be written: %A" failure) ]

let private lockRecipeTests () =
    [ for case in cases "lockRecipe" ->
          let name = str case "name"
          requireKnown name [ "name"; "rule"; "lock"; "recipe"; "expected" ] case

          testCase name (fun () ->
              let lockFile = str case "lock"
              let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed lockFile) |> valid lockFile
              let repository = match Sha256.tryParse (repositoryId ()) with Ok id -> id | Error _ -> failtest "repositoryId"
              let actual = Bindings.lockMatchesRecipe lock (signedRecipe (str case "recipe")) repository |> bindingOutcome
              Expect.equal actual (Manifest.expected name case) name) ]

let private recipeCheckTests () =
    [ for case in cases "recipeChecks" do
          let name = str case "name"
          requireKnown name [ "name"; "rule"; "recipe"; "expected" ] case

          if optStr case "rule" = Some readerOnlyRecipeRule then
              testCase name (fun () ->
                  let actual = DocumentCaseTests.outcome "recipe" (targetBytes (str case "recipe"))
                  Expect.equal actual (Manifest.expected name case) name) ]

[<Tests>]
let tests =
    testList "Signed cases without a TUF client" [
        testCase "every section of the signed manifest is run or listed with a reason" (fun () ->
            use document = manifest ()
            let sections = document.RootElement.EnumerateObject() |> Seq.map _.Name |> set
            let accounted = set (ranSections @ (notRunSections |> List.map fst) @ metadataMembers)
            Expect.equal sections accounted "no section is silently skipped"
            Expect.equal (str document.RootElement "contractVersion") "0.1.0-draft.1" "the contract version")

        testCase "exactly one recipe check reads the document alone" (fun () ->
            let rules = cases "recipeChecks" |> List.choose (fun case -> optStr case "rule")
            Expect.equal (rules |> List.filter ((=) readerOnlyRecipeRule) |> List.length) 1 "one reader-only recipe check")

        testCase "every other recipe the signed repository publishes reads as a recipe" (fun () ->
            use repository = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")

            let unreadable =
                cases "recipeChecks"
                |> List.filter (fun case -> optStr case "rule" = Some readerOnlyRecipeRule)
                |> List.map (fun case -> str case "recipe")
                |> set

            for target in repository.RootElement.GetProperty("targetFiles").EnumerateObject() do
                if target.Name.StartsWith "toolchains/recipes/" && not (unreadable.Contains target.Name) then
                    signedRecipe target.Name |> ignore)

        testCase "the frozen locks name the TEST-ONLY repository" (fun () ->
            for case in cases "frozen" do
                let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed (str case "lock")) |> valid "lock"
                Expect.equal lock.Recipe.RepositoryId.Text (repositoryId ()) "the recipe's repository")

        testList "selection" (selectionTests ())
        testList "frozen" (frozenTests ())
        testList "lockRecipe" (lockRecipeTests ())
        testList "recipeChecks (reader only)" (recipeCheckTests ())
    ]
