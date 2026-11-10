// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The cases of the TEST-ONLY signed manifest. The TUF client subset (not security reviewed) authenticates each
/// metadata set from the fixture's trusted root at its reference time, and the suite runs the trust cases that load
/// the repository through it, and every check that is a pure function of the authenticated metadata and the target
/// bytes: resolution, frozen locks, lock against recipe, the recipe target rows, the provider repository row, target
/// authentication, channel selection and the locked-generation checks. Every other rule, operation and section is
/// listed with the reason it is not run, and a test fails on anything unlisted.
/// </summary>
module Morphir.Toolchain.Tests.SignedCaseTests

open System
open System.Text
open System.Text.Json
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest
open Morphir.Toolchain.Tests.BindingCaseTests

/// Sections this suite runs, in whole or in part.
let ranSections = [ "selection"; "frozen"; "lockRecipe"; "recipeChecks"; "trust"; "channelSelection" ]

let private noV1Client =
    "needs the tool release metadata v1 resolver, which is the next part of task 4.2 after this verification slice"

/// Sections this suite does not run, each with its reason.
let notRunSections =
    [ "v1Client", $"Selects artifacts by tool id, channel, v1 platform and morphirCli compatibility: {noV1Client}. The TUF client authenticates the metadata it reads, but release selection is the resolver's, not the client's."
      "v1Descriptors", "Validates release targets against tool-release-v1.schema.json, which is not part of this pin." ]

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
        [ "check-recipe-bindings", bindingRows ]

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

/// The TEST-ONLY anchor of the signed repository: its trusted root.
let private anchor () =
    match TrustAnchor.load TrustAnchorPurpose.TestOnly (TufFixture.canonical (TufFixture.metadataSet "metadata").Root) with
    | Ok(trusted, _) -> trusted
    | Error refusal -> failtestf "the fixture root does not load: %A" refusal

/// <summary>The targets of a metadata set (<c>metadata</c>, <c>learnedRevocation</c> or <c>recipeRevocation</c>), as
/// the TUF client authenticates them: a refresh from the trusted root at the fixture's reference time.</summary>
let authenticatedTargets (set: string) : Map<string, AuthenticatedTarget> =
    match TufClient.refresh TufFixture.referenceTime (anchor ()) (TufFixture.sourceOf (TufFixture.metadataSet set) []) with
    | RefreshResult.Refreshed trusted -> (Option.get trusted.Targets).Targets
    | other -> failtestf "the %s metadata set does not refresh: %A" set other

/// A JSON pointer into a role's signed object as member names.
let private pointerPath (pointer: string) =
    pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun segment -> segment.Replace("~1", "/").Replace("~0", "~"))
    |> List.ofArray

/// The metadata set of a trust case after its metadata mutations.
let private mutatedSet (case: JsonElement) =
    let mutations =
        match case.TryGetProperty "mutations" with
        | true, m when m.ValueKind = JsonValueKind.Array -> [ for x in m.EnumerateArray() -> x ]
        | _ -> []

    let withRole (role: string) (value: Value) (set: TufFixture.MetadataSet) =
        match role with
        | "timestamp" -> { set with Timestamp = value }
        | "snapshot" -> { set with Snapshot = value }
        | "targets" -> { set with Targets = value }
        | other -> failtestf "mutation of role %s" other

    let roleOf (role: string) (set: TufFixture.MetadataSet) =
        match role with
        | "timestamp" -> set.Timestamp
        | "snapshot" -> set.Snapshot
        | "targets" -> set.Targets
        | other -> failtestf "role %s" other

    (TufFixture.metadataSet (str case "metadata"), mutations)
    ||> List.fold (fun set m ->
        match str m "op" with
        | "set-signed-member" ->
            let value =
                // Wrapped in an object, since a document is not a bare string.
                match Syntax.parseJson (Encoding.UTF8.GetBytes("{\"value\":" + m.GetProperty("value").GetRawText() + "}")) with
                | Ok wrapped -> TufFixture.memberOf "value" wrapped
                | Error _ -> failtest "the mutation's value does not parse"

            let role = str m "role"
            withRole role (TufFixture.setAt ("signed" :: pointerPath (str m "pointer")) value (roleOf role set)) set
        | "relink" -> TufFixture.relink set
        | "replace-role" -> withRole (str m "role") (TufFixture.at (List.ofArray ((str m "from").Split '.')) (TufFixture.repository ())) set
        | target when target.EndsWith "-target-bytes" || target.EndsWith "-target-text" -> set
        | other -> failtestf "metadata mutation %s" other)
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

/// The served bytes of a target after the case's target mutations.
let private servedBytes (case: JsonElement) (target: string) =
    let mutations =
        match case.TryGetProperty "mutations" with
        | true, m when m.ValueKind = JsonValueKind.Array -> [ for x in m.EnumerateArray() -> x ]
        | true, m when m.ValueKind = JsonValueKind.Object -> [ m ]
        | _ -> []

    let mutable bytes = targetBytes target

    for m in mutations do
        if optStr m "target" = Some target then
            match str m "op" with
            | "replace-target-bytes" -> bytes <- targetBytes (str m "with")
            | "replace-target-text" ->
                let text = Encoding.UTF8.GetString bytes
                let find = str m "find"
                let at = text.IndexOf(find, System.StringComparison.Ordinal)
                bytes <- Encoding.UTF8.GetBytes(text.Substring(0, at) + str m "replace" + text.Substring(at + find.Length))
            | other -> failtestf "mutation %s on a target" other

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
          | "load", _ ->
              yield
                  testCase name (fun () ->
                      let actual =
                          match TufClient.refresh TufFixture.referenceTime (anchor ()) (TufFixture.sourceOf (mutatedSet case) []) with
                          | RefreshResult.Refreshed _ -> Valid
                          | RefreshResult.Failed(failure, _) -> Refused(Code.name (TufFailure.code failure), "")
                          | other -> failtestf "%s did not complete: %A" name other

                      Expect.equal actual (Manifest.expected name case) name)
          | "check-locked-generation", _ ->
              yield
                  testCase name (fun () ->
                      let lockFile = str action "lock"
                      let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed lockFile) |> valid lockFile
                      // The generation's recipe, as setup stored it: the authenticated bytes of the lock's recipe target.
                      let recipe = signedRecipe lock.Recipe.Target.Text

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
    | ("read" | "authenticate" | "check-locked-generation" | "load"), _ -> None
    | "check-recipe", Some rule when ranRecipeRules.Contains rule -> None
    | "check-recipe", Some rule -> skippedRecipeRules.TryFind rule |> Option.orElse (Some "unlisted")
    | _ -> skippedTrust.TryFind op |> Option.orElse (Some "unlisted")

[<Tests>]
let tests =
    testList "Signed cases" [
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
    ]
