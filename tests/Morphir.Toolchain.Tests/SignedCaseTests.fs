// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The cases of the TEST-ONLY signed manifest that need no metadata verification. The suite treats the targets
/// metadata in <c>repository.json</c> as already authenticated, as a TUF client would hand it over, and runs every
/// check that is a pure function of that metadata and the target bytes: resolution, frozen locks, lock against
/// recipe, the recipe target rows, the provider repository row, target authentication and channel selection. Every
/// other rule, operation and section is listed with the reason it is not run, and a test fails on anything unlisted.
/// </summary>
module Morphir.Toolchain.Tests.SignedCaseTests

open System.Text
open System.Text.Json
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest
open Morphir.Toolchain.Tests.BindingCaseTests

/// Sections this suite runs, in whole or in part.
let ranSections = [ "selection"; "frozen"; "lockRecipe"; "recipeChecks"; "trust"; "channelSelection" ]

let private noV1Client =
    "needs the tool release metadata v1 client, which does not exist in morphir-dotnet yet (task 4.2)"

/// Sections this suite does not run, each with its reason.
let notRunSections =
    [ "v1Client", $"Selects artifacts by tool id, channel and v1 platform: {noV1Client}."
      "v1Descriptors", "Validates release targets against tool-release-v1.schema.json, which is not part of this pin, read through the TUF client." ]

/// Members of the signed manifest that are data, not case sections.
let metadataMembers = [ "contractVersion"; "repository"; "referenceClient" ]

/// Recipe check rules this suite runs: the recipe target rows that are pure functions of the authenticated
/// metadata, and the first provider row.
let ranRecipeRules =
    set
        [ "recipe-target-portable"
          "recipe-target-authenticated"
          "recipe-target-state"
          "recipe-target-revoked"
          "recipe-document-reads"
          "recipe-document-identity"
          "provider-repository" ]

let private bindingRows =
    "a host or provider binding row after the provider repository row; the host release and extension descriptor readers and the claims checks are task 4.3 and not implemented in this slice"

/// Recipe check rules this suite does not run, each with its reason.
let skippedRecipeRules =
    [ for rule in
          [ "host-release-authenticated"
            "host-release-revoked"
            "host-release-read"
            "host-release-v1"
            "host-target-authenticated"
            "host-target-digest"
            "host-target-listed"
            "host-target-platform"
            "host-target-metadata"
            "provider-descriptor-bound"
            "provider-descriptor-read"
            "provider-descriptor-parses"
            "provider-descriptor-identity"
            "provider-descriptor-version"
            "provider-claims-identity"
            "provider-artifact-bound"
            "provider-artifact-listed"
            "provider-artifact-runtime"
            "provider-artifact-digest-listed"
            "provider-artifact-platform"
            "provider-claims-kind"
            "provider-claims-protocol"
            "provider-claims-operand"
            "provider-claims-model" ] ->
          rule, bindingRows ]
    |> Map.ofList

/// Trust operations, and the check-recipe rules, that this suite does not run, with their reasons.
let skippedTrust =
    Map.ofList
        [ "load", "loads the mutated repository through a TUF client: signatures, snapshot links and expiry are metadata verification"
          "check-recipe-bindings", bindingRows ]

let private manifest () = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "cases.json")

let private repositoryDocument () = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")

/// The exact bytes of a target in the TEST-ONLY repository: the UTF-8 bytes of its <c>targetFiles</c> text.
let targetBytes (target: string) =
    use repository = repositoryDocument ()

    match repository.RootElement.GetProperty("targetFiles").TryGetProperty target with
    | true, text -> Encoding.UTF8.GetBytes(nonNull (text.GetString()))
    | false, _ -> failtestf "repository.json has no target file %s" target

let repositoryId () =
    use repository = repositoryDocument ()
    str repository.RootElement "repositoryId"

let repository () =
    match Sha256.tryParse (repositoryId ()) with
    | Ok id -> id
    | Error _ -> failtest "repository.json has no valid repositoryId"

/// The targets of a metadata set (<c>metadata</c> or <c>learnedRevocation</c>), as the authenticated entries a TUF
/// client would hand over.
let authenticatedTargets (set: string) : Map<string, AuthenticatedTarget> =
    use repository = repositoryDocument ()
    let targets = repository.RootElement.GetProperty(set).GetProperty("targets").GetProperty("signed").GetProperty("targets")

    [ for target in targets.EnumerateObject() ->
          let custom =
              match target.Value.TryGetProperty "custom" with
              | true, custom ->
                  match Syntax.parseJson (Encoding.UTF8.GetBytes(custom.GetRawText())) with
                  | Ok value -> Some value
                  | Error _ -> failtest "custom does not parse"
              | false, _ -> None

          let sha =
              match Sha256.tryParse (nonNull (target.Value.GetProperty("hashes").GetProperty("sha256").GetString())) with
              | Ok sha -> sha
              | Error _ -> failtest "not a digest"

          target.Name,
          { Length = target.Value.GetProperty("length").GetUInt64()
            Sha256 = sha
            Custom = custom } ]
    |> Map.ofList

let signedRecipe (target: string) =
    RecipeDocument.read (targetBytes target) |> valid target

let private cases (section: string) =
    use document = manifest ()
    [ for case in document.RootElement.GetProperty(section).EnumerateArray() -> case.Clone() ]

let private targetOutcome (result: Result<'T, TargetRefusal>) =
    match result with
    | Ok _ -> Valid
    | Error refusal -> Refused(Code.name refusal.Code, DocumentPath.render refusal.Path)

/// The recipe target rows, then the provider repository row, over the recipe's authenticated metadata.
let private recipeRows (listed: Map<string, AuthenticatedTarget>) (target: string) (bytes: byte array) =
    RecipeTargets.check target (listed.TryFind target) bytes
    |> Result.bind (fun (recipe, _) ->
        Bindings.recipeProvidersInRepository recipe (repository ())
        |> Result.mapError (fun refusal -> { Code = refusal.Code; Path = refusal.Path }))
    |> targetOutcome

/// <summary>A case's target mutations, applied to the bytes served for each target. A mutation of another op, or one
/// that is never applied, fails the case, so no case passes with a mutation the runner ignored (review of #582,
/// R4).</summary>
type TargetMutations(case: JsonElement) =
    let name = optStr case "name" |> Option.defaultValue "a case"

    let mutations =
        match case.TryGetProperty "mutations" with
        | true, m when m.ValueKind = JsonValueKind.Array -> [ for x in m.EnumerateArray() -> x ]
        | true, m when m.ValueKind = JsonValueKind.Object -> [ m ]
        | true, _ -> failtestf "%s: mutations is not an array" name
        | false, _ -> []

    do
        for m in mutations do
            match str m "op" with
            | "replace-target-bytes" -> requireKnown $"{name} mutation" [ "op"; "target"; "with" ] m
            | "replace-target-text" -> requireKnown $"{name} mutation" [ "op"; "target"; "find"; "replace" ] m
            | other -> failtestf "%s: the mutation %s is not one this runner applies to a target" name other

    let applied = System.Collections.Generic.HashSet<int>()

    /// The bytes served for a target after the mutations of that target.
    member _.Served(target: string) =
        let mutable bytes = targetBytes target

        for index, m in List.indexed mutations do
            if str m "target" = target then
                match str m "op" with
                | "replace-target-bytes" -> bytes <- targetBytes (str m "with")
                | _ ->
                    let text = Encoding.UTF8.GetString bytes
                    let find = str m "find"
                    let at = text.IndexOf(find, System.StringComparison.Ordinal)

                    if at < 0 then
                        failtestf "%s: the mutation's text %s is not in %s" name find target

                    bytes <- Encoding.UTF8.GetBytes(text.Substring(0, at) + str m "replace" + text.Substring(at + find.Length))

                applied.Add index |> ignore

        bytes

    /// Fails unless every mutation was applied to a target the case read.
    member _.ExpectAllApplied() =
        for index, m in List.indexed mutations do
            if not (applied.Contains index) then
                failtestf "%s: the mutation %s of %s was never applied" name (str m "op") (str m "target")

/// The served bytes of a target after the case's target mutations; every mutation must be applied.
let private servedBytes (case: JsonElement) (target: string) =
    let mutations = TargetMutations case
    let bytes = mutations.Served target
    mutations.ExpectAllApplied()
    bytes

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
              Expect.equal (lock.Platforms.Items |> List.map fst) platforms "the lock records exactly these platforms"

              Expect.isTrue (expected.GetProperty("lockEqualsResolution").GetBoolean()) "the case asks for resolution equality"

              for platform, closure in lock.Platforms.Items do
                  match Resolution.resolve recipe request platform with
                  | Ok resolved -> Expect.equal resolved.Closure closure $"the {Platform.name platform} closure is the resolution"
                  | Error failure -> failtestf "%s does not resolve: %A" (Platform.name platform) failure

              match Resolution.lock recipe lock.Recipe.RepositoryId lock.Recipe.Target request (List.head platforms) with
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
              let actual = Bindings.lockMatchesRecipe lock (signedRecipe (str case "recipe")) (repository ()) |> bindingOutcome
              Expect.equal actual (Manifest.expected name case) name) ]

let private recipeCheckTests () =
    let listed = authenticatedTargets "metadata"

    [ for case in cases "recipeChecks" do
          let name = str case "name"
          requireKnown name [ "name"; "rule"; "recipe"; "expected" ] case
          let rule = optStr case "rule"

          // Valid cases have no rule: they must pass the rows this suite runs.
          if rule.IsNone || ranRecipeRules.Contains rule.Value then
              yield
                  testCase name (fun () ->
                      let target = str case "recipe"
                      let bytes = if listed.ContainsKey target then targetBytes target else [||]
                      Expect.equal (recipeRows listed target bytes) (Manifest.expected name case) name) ]

let private trustTests () =
    [ for case in cases "trust" do
          let name = str case "name"
          requireKnown name [ "name"; "rule"; "metadata"; "mutations"; "action"; "expected" ] case
          let action = case.GetProperty "action"
          let op = str action "op"
          let rule = optStr case "rule"
          let listed = authenticatedTargets (str case "metadata")

          match op, rule with
          | "read", _ ->
              yield
                  testCase name (fun () ->
                      let target = str action "target"
                      let actual = RecipeTargets.authenticate (listed.TryFind target) (servedBytes case target) |> targetOutcome
                      Expect.equal actual (Manifest.expected name case) name)
          | "authenticate", _ ->
              yield
                  testCase name (fun () ->
                      let actual =
                          RecipeTargets.authenticate (listed.TryFind(str action "target")) (Encoding.UTF8.GetBytes(str action "text"))
                          |> targetOutcome

                      Expect.equal actual (Manifest.expected name case) name)
          | "check-recipe", Some rule when ranRecipeRules.Contains rule ->
              yield
                  testCase name (fun () ->
                      let target = str action "recipe"
                      Expect.equal (recipeRows listed target (servedBytes case target)) (Manifest.expected name case) name)
          | "check-locked-generation", _ ->
              yield
                  testCase name (fun () ->
                      let lockFile = str action "lock"
                      let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed lockFile) |> valid lockFile
                      // The generation's recipe, as setup stored it: the authenticated bytes of the lock's recipe target.
                      // A generation whose recipe target the trust state does not list is refused before it is read.
                      let recipe () = Ok(signedRecipe lock.Recipe.Target.Text)

                      let actual = RecipeTargets.checkLockedGeneration (repository ()) listed recipe lock |> targetOutcome
                      Expect.equal actual (Manifest.expected name case) name)
          | _ -> () ]

let private channelTests () =
    let listed = authenticatedTargets "metadata" |> Map.toList

    [ for case in cases "channelSelection" ->
          let name = str case "name"
          requireKnown name [ "name"; "preset"; "channel"; "expected" ] case
          let expected = case.GetProperty "expected"

          testCase name (fun () ->
              let preset =
                  match Token.tryParse (str case "preset") with
                  | Ok token -> token
                  | Error _ -> failtest "preset"

              let channel =
                  match Channel.tryParse (str case "channel") with
                  | Ok channel -> channel
                  | Error _ -> failtest "channel"

              let actual = RecipeTargets.select preset channel listed

              match optStr expected "recipe" with
              | Some recipe -> Expect.equal actual (Ok recipe) name
              | None ->
                  requireKnown name [ "code"; "path" ] expected
                  Expect.equal (targetOutcome actual) (Refused(str expected "code", str expected "path")) name) ]

/// Why a trust case is not run, or None when it is.
let trustSkipReason (op: string) (rule: string option) =
    match op, rule with
    | ("read" | "authenticate" | "check-locked-generation"), _ -> None
    | "check-recipe", Some rule when ranRecipeRules.Contains rule -> None
    | "check-recipe", Some rule -> skippedRecipeRules.TryFind rule |> Option.orElse (Some "unlisted")
    | _ -> skippedTrust.TryFind op |> Option.orElse (Some "unlisted")

[<Tests>]
let tests =
    testList "Signed cases without metadata verification" [
        testCase "every section of the signed manifest is run or listed with a reason" (fun () ->
            use document = manifest ()
            let sections = document.RootElement.EnumerateObject() |> Seq.map _.Name |> set
            let accounted = set (ranSections @ (notRunSections |> List.map fst) @ metadataMembers)
            Expect.equal sections accounted "no section is silently skipped"
            Expect.equal (str document.RootElement "contractVersion") "0.1.0-draft.1" "the contract version")

        testCase "every recipe check rule is run or listed with a reason, and no listed rule is stale" (fun () ->
            let checkRecipe =
                cases "trust"
                |> List.filter (fun case -> (str (case.GetProperty "action") "op").StartsWith "check-recipe")
                |> List.choose (fun case -> optStr case "rule")

            let rules = (cases "recipeChecks" |> List.choose (fun case -> optStr case "rule")) @ checkRecipe |> set
            let accounted = Set.union ranRecipeRules (skippedRecipeRules |> Map.keys |> set)
            Expect.equal rules accounted "the manifest's rules are exactly the run and the skipped rules")

        testCase "every trust case is run or listed with a reason" (fun () ->
            let unlisted =
                cases "trust"
                |> List.filter (fun case -> trustSkipReason (str (case.GetProperty "action") "op") (optStr case "rule") = Some "unlisted")
                |> List.map (fun case -> str case "name")

            Expect.isEmpty unlisted "no trust case is silently skipped")

        testCase "every other recipe the signed repository publishes reads as a recipe" (fun () ->
            use repository = repositoryDocument ()

            let unreadable =
                cases "recipeChecks"
                |> List.filter (fun case -> optStr case "rule" = Some "recipe-document-reads")
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
        testList "recipeChecks" (recipeCheckTests ())
        testList "trust" (trustTests ())
        testList "channelSelection" (channelTests ())

        testList "the launch check reads the generation's recipe without letting an exception out" [
            // Review of #582, R7: a Lazy caches the exception its factory throws.
            let listed () = authenticatedTargets "metadata"
            let lockOf (file: string) = LockDocument.read (Fixtures.bytesIn Fixtures.signed file) |> valid file
            let launch lock recipe = RecipeTargets.checkLockedGeneration (repository ()) (listed ()) recipe lock |> targetOutcome

            yield testCase "a recipe source that throws is a refusal" (fun () ->
                let lock = lockOf "fsharp-native-aot.lock.json"
                let actual = launch lock (fun () -> raise (System.IO.IOException "the stored recipe is gone"))
                Expect.equal actual (Refused("invalid_document", "recipe.target")) "refused, not thrown")

            yield testCase "a recipe source that is refused is that refusal" (fun () ->
                let lock = lockOf "fsharp-native-aot.lock.json"
                let refusal: TargetRefusal = { Code = Code.Reader ReaderCode.UnsupportedContractVersion; Path = DocumentPath.root.Child "schemaVersion" }
                Expect.equal (launch lock (fun () -> Error refusal)) (Refused("unsupported_contract_version", "schemaVersion")) "the source's refusal")

            yield testCase "an unlisted recipe target is refused before the recipe is asked for" (fun () ->
                let lock = lockOf "negative/lock-recipe-unlisted.lock.json"
                let mutable asked = false
                let actual = launch lock (fun () -> asked <- true; Error({ Code = Code.Reader ReaderCode.InvalidDocument; Path = DocumentPath.root }: TargetRefusal))
                Expect.equal actual (Refused("target_not_authenticated", "recipe.target")) "unlisted"
                Expect.isFalse asked "the recipe was not asked for")
        ]

        testList "a target mutation the runner does not apply fails its case" [
            let caseOf (json: string) = (JsonDocument.Parse json).RootElement.Clone()

            yield testCase "the reviewer's made-up mutation" (fun () ->
                let case = caseOf """{"name":"made up","mutations":[{"op":"made-up-mutation","target":"nothing"}]}"""
                Expect.throwsT<AssertException> (fun () -> TargetMutations case |> ignore) "an unknown op fails")

            yield testCase "a mutation of a target the case never reads" (fun () ->
                let case =
                    caseOf
                        """{"name":"other target","mutations":[{"op":"replace-target-bytes","target":"releases/morphir-dotnet-native-aot/1.2.3.json","with":"releases/morphir-dotnet-self-contained/1.2.3.json"}]}"""

                Expect.throwsT<AssertException> (fun () -> servedBytes case "toolchains/recipes/fsharp/1.0.0.json" |> ignore) "never applied")

            yield testCase "a text mutation whose text is not in the target" (fun () ->
                let case =
                    caseOf """{"name":"absent text","mutations":[{"op":"replace-target-text","target":"toolchains/recipes/fsharp/1.0.0.json","find":"no such text","replace":"x"}]}"""

                Expect.throwsT<AssertException> (fun () -> servedBytes case "toolchains/recipes/fsharp/1.0.0.json" |> ignore) "not applied")
        ]
    ]
