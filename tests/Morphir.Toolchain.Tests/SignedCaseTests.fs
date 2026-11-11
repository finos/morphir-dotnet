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
let ranSections = [ "selection"; "frozen"; "lockRecipe"; "recipeChecks"; "trust"; "channelSelection"; "v1Descriptors"; "v1Client" ]

/// Parts of a section this suite runs that it does not run, each with its reason.
let notRunClauses =
    [ "v1Descriptors",
      "Validation against tool-release-v1.schema.json, which this pin does not hold: the v1 reader checks the schema's rules, written from it by hand." ]

/// Sections this suite does not run, each with its reason. Every section is run.
let notRunSections: (string * string) list = []

/// Members of the signed manifest that are data, not case sections.
let metadataMembers = [ "contractVersion"; "repository"; "referenceClient" ]

/// Recipe check rules this suite runs: the recipe target rows, the host binding rows and the provider binding rows,
/// as pure functions of the authenticated metadata and the target bytes.
let ranRecipeRules =
    set
        [ "recipe-target-portable"
          "recipe-target-authenticated"
          "recipe-target-state"
          "recipe-target-revoked"
          "recipe-document-reads"
          "recipe-document-identity"
          "host-release-authenticated"
          "host-release-revoked"
          "host-release-read"
          "host-release-v1"
          "host-target-authenticated"
          "host-target-digest"
          "host-target-listed"
          "host-target-platform"
          "host-target-metadata"
          "provider-repository"
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
          "provider-claims-model" ]

/// The rules the pinned corpus names. Every one of them must still have cases, so a corpus cannot drop one unnoticed.
let pinnedRecipeRules = ranRecipeRules

/// The rules the contract added after the pin (finos/morphir 837164580), which RecipeBindings runs.
let laterRecipeRules = set [ "host-release-identity"; "provider-descriptor-critical"; "provider-descriptor-filenames" ]

let ranRules = Set.union ranRecipeRules laterRecipeRules

/// Recipe check rules this suite does not run, each with its reason. Every rule of the three tables is run.
let skippedRecipeRules: Map<string, string> = Map.empty

/// Trust operations, and the check-recipe rules, that this suite does not run, with their reasons.
let skippedTrust: Map<string, string> = Map.empty

let private manifest () = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "cases.json")

let private repositoryDocument () = JsonDocument.Parse(Fixtures.bytesIn Fixtures.signed "repository.json")

/// The exact bytes of a target in the TEST-ONLY repository: the UTF-8 bytes of its <c>targetFiles</c> text.
/// The exact bytes of a target, or None when the repository has no file for it.
let tryTargetBytes (target: string) =
    use repository = repositoryDocument ()

    match repository.RootElement.GetProperty("targetFiles").TryGetProperty target with
    | true, text -> Some(Encoding.UTF8.GetBytes(nonNull (text.GetString())))
    | false, _ -> None

let targetBytes (target: string) =
    match tryTargetBytes target with
    | Some bytes -> bytes
    | None -> failtestf "repository.json has no target file %s" target

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
        | "timestamp" -> { set with Timestamp = value; Served = set.Served.Remove role }
        | "snapshot" -> { set with Snapshot = value; Served = set.Served.Remove role }
        | "targets" -> { set with Targets = value; Served = set.Served.Remove role }
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
        | "replace-role-text" ->
            requireKnown "replace-role-text" [ "op"; "role"; "find"; "replace" ] m
            TufFixture.replaceRoleText (str m "role") (str m "find") (str m "replace") set
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

/// The recipe target rows, then the host and provider binding rows, over the authenticated metadata and the bytes the
/// repository serves for each target.
let private recipeRows (listed: Map<string, AuthenticatedTarget>) (target: string) (bytesOf: string -> byte array option) =
    RecipeTargets.check target (listed.TryFind target) (defaultArg (bytesOf target) [||])
    |> Result.bind (fun (recipe, _) -> RecipeBindings.check (repository ()) listed bytesOf recipe)
    |> targetOutcome

/// A value with the member or element a JSON pointer names replaced (RFC 6901: ~1 is /, ~0 is ~).
let private setAtPointer (pointer: string) (replacement: Value) (value: Value) =
    let tokens =
        pointer.Split '/'
        |> Array.skip 1
        |> Array.map (fun token -> token.Replace("~1", "/").Replace("~0", "~"))
        |> List.ofArray

    let rec set (path: string list) (value: Value) =
        match path, value with
        | [], _ -> replacement
        | head :: rest, Value.Object members when members |> List.exists (fst >> (=) head) ->
            Value.Object(members |> List.map (fun (name, item) -> if name = head then name, set rest item else name, item))
        | head :: rest, Value.Array items ->
            Value.Array(items |> List.mapi (fun index item -> if string index = head then set rest item else item))
        | _ -> failtestf "the pointer %s names nothing" pointer

    set tokens value

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

/// <summary>Runs a case that reads targets: <paramref name="run"/> gets the bytes served for each target after the
/// case's mutations (None when the repository has no file for it), and every mutation must have been applied to some
/// target it read. One case can read the recipe, its host releases and its descriptors.</summary>
let private withServedTargets (case: JsonElement) (run: (string -> byte array option) -> 'T) : 'T =
    let mutations = TargetMutations case
    let outcome = run (fun target -> tryTargetBytes target |> Option.map (fun _ -> mutations.Served target))
    mutations.ExpectAllApplied()
    outcome

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
          if rule.IsNone || ranRules.Contains rule.Value then
              yield
                  testCase name (fun () ->
                      let target = str case "recipe"
                      Expect.equal (recipeRows listed target tryTargetBytes) (Manifest.expected name case) name) ]

let private trustTests () =
    [ for case in cases "trust" do
          let name = str case "name"
          requireKnown name [ "name"; "rule"; "metadata"; "mutations"; "action"; "expected" ] case
          let action = case.GetProperty "action"
          let op = str action "op"
          let rule = optStr case "rule"
          // Authenticated when a case reads it: a load case's set may be one the client refuses.
          let metadata = str case "metadata"
          let listed = lazy (authenticatedTargets metadata)

          match op, rule with
          | "read", _ ->
              yield
                  testCase name (fun () ->
                      let target = str action "target"
                      let actual = RecipeTargets.authenticate (listed.Value.TryFind target) (servedBytes case target) |> targetOutcome
                      Expect.equal actual (Manifest.expected name case) name)
          | "authenticate", _ ->
              yield
                  testCase name (fun () ->
                      let actual =
                          RecipeTargets.authenticate (listed.Value.TryFind(str action "target")) (Encoding.UTF8.GetBytes(str action "text"))
                          |> targetOutcome

                      Expect.equal actual (Manifest.expected name case) name)
          | "check-recipe", Some rule when ranRules.Contains rule ->
              yield
                  testCase name (fun () ->
                      let target = str action "recipe"
                      Expect.equal (withServedTargets case (recipeRows listed.Value target)) (Manifest.expected name case) name)
          | "check-recipe-bindings", _ ->
              yield
                  testCase name (fun () ->
                      // The recipe document edited by JSON pointer, then only its bindings checked: the edited bytes are
                      // no longer the authenticated recipe target.
                      let target = str action "recipe"

                      let original =
                          match Syntax.parseJson (targetBytes target) with
                          | Ok value -> value
                          | Error _ -> failtest "the recipe does not parse"

                      let edited =
                          [ for m in action.GetProperty("documentMutations").EnumerateArray() -> m ]
                          |> List.fold
                              (fun (value: Value) m ->
                                  requireKnown $"{name} mutation" [ "pointer"; "value" ] m
                                  setAtPointer (str m "pointer") (Value.String(str m "value")) value)
                              original

                      let recipe = RecipeDocument.read (JsonText.toBytes edited) |> valid target
                      let actual = withServedTargets case (fun bytesOf -> RecipeBindings.check (repository ()) listed.Value bytesOf recipe) |> targetOutcome
                      Expect.equal actual (Manifest.expected name case) name)
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
                      // A generation whose recipe target the trust state does not list is refused before it is read.
                      let recipe () = Ok(signedRecipe lock.Recipe.Target.Text)

                      let actual = RecipeTargets.checkLockedGeneration (repository ()) listed.Value recipe lock |> targetOutcome
                      Expect.equal actual (Manifest.expected name case) name)
          | _ -> () ]

let private v1DescriptorTests () =
    use document = manifest ()
    let section = document.RootElement.GetProperty "v1Descriptors"
    requireKnown "v1Descriptors" [ "note"; "schema"; "releases" ] section
    let releases = [ for r in section.GetProperty("releases").EnumerateArray() -> nonNull (r.GetString()) ]
    let listed = authenticatedTargets "metadata"

    [ for release in releases do
          yield testCase release (fun () ->
              // Read as the binding check reads it: authenticated bytes, a tool-release target of schemaVersion 1, and
              // an unchanged v1 descriptor.
              let entry =
                  match RecipeTargets.authenticate (listed.TryFind release) (targetBytes release) with
                  | Ok entry -> entry
                  | Error refusal -> failtestf "%s is not authenticated: %A" release refusal

              let morphir =
                  match entry.Custom with
                  | Some(Value.Object members) -> members |> List.tryFind (fst >> (=) "morphir") |> Option.map snd
                  | _ -> None

              match morphir with
              | Some(Value.Object members) ->
                  Expect.contains members ("kind", Value.String "tool-release") "custom.morphir.kind"
                  Expect.contains members ("schemaVersion", Value.Number "1") "custom.morphir.schemaVersion"
              | _ -> failtestf "%s has no custom.morphir" release

              Expect.isOk (ToolReleaseDocument.read (targetBytes release)) "a v1 descriptor")
      yield testCase "every other release target is the deliberate negative" (fun () ->
          // The v1Client controls' own release (finos/morphir 837164580) is not an offering; the control cases read it.
          let controlTools =
              use document = manifest ()

              match document.RootElement.GetProperty("v1Client").TryGetProperty "controls" with
              | true, controls -> [ for control in controls.EnumerateArray() -> str control "toolId" ] |> List.distinct
              | false, _ -> []

          // A recipe-check negative names its release through a host offering of the recipe it checks.
          let checkedReleases =
              [ for case in cases "recipeChecks" do
                    match tryTargetBytes (str case "recipe") |> Option.map RecipeDocument.read with
                    | Some(Ok recipe) ->
                        for host in recipe.Hosts do
                            match host.Source with
                            | HostSource.ToolRelease toolId -> yield $"releases/{toolId.Text}/{host.Version.Text}.json"
                            | HostSource.DotnetTool _ -> ()
                    | _ -> () ]
              |> set

          let others =
              listed
              |> Map.keys
              |> Seq.filter (fun target -> target.StartsWith "releases/" && not (List.contains target releases))
              |> Seq.filter (fun target -> not (controlTools |> List.exists (fun id -> target.StartsWith $"releases/{id}/")))
              |> Seq.filter (fun target -> target = "releases/morphir-dotnet-not-a-release/1.2.3.json" || not (checkedReleases.Contains target))
              |> List.ofSeq

          Expect.equal others [ "releases/morphir-dotnet-not-a-release/1.2.3.json" ] "the one release not listed"
          Expect.isError (ToolReleaseDocument.read (targetBytes others.Head)) "it does not read as a v1 descriptor") ]

let private v1ClientTests () =
    use document = manifest ()
    let section = document.RootElement.GetProperty "v1Client"
    requireKnown "v1Client" [ "note"; "recipe"; "selections"; "controls" ] section
    let cli = str (document.RootElement.GetProperty "referenceClient") "cliVersion"
    let recipe = signedRecipe (str section "recipe")
    let listed = authenticatedTargets "metadata"
    let selections = [ for s in section.GetProperty("selections").EnumerateArray() -> s.Clone() ]

    [ for selection in selections do
          let toolId = str selection "toolId"
          let recipePlatform = str selection "recipePlatform"
          requireKnown toolId [ "toolId"; "channel"; "platform"; "deployment"; "recipePlatform"; "recipeBinding"; "expected" ] selection

          yield
              testCase $"{toolId} on {recipePlatform}" (fun () ->
                  let platform = selection.GetProperty "platform"
                  requireKnown $"{toolId} platform" [ "os"; "arch" ] platform
                  let expected = selection.GetProperty "expected"
                  requireKnown $"{toolId} expected" [ "selected" ] expected

                  let request =
                      match Channel.tryParse (str selection "channel") with
                      | Ok channel -> ToolRequest.OnChannel channel
                      | Error _ -> failtest "channel"

                  let id = match ToolId.tryParse toolId with Ok id -> id | Error _ -> failtest "tool id"
                  let cliVersion = match SemVer.tryParse cli with Ok v -> v | Error _ -> failtest "cli version"

                  let resolved =
                      ToolResolver.resolve listed id request { Os = str platform "os"; Arch = str platform "arch" } cliVersion

                  Expect.equal (Result.isOk resolved) (expected.GetProperty("selected").GetBoolean()) "the v1 client's selection"

                  // Not selected because no CLI version meets the offering's requirement, not because a state is malformed.
                  match resolved with
                  | Error refusal -> Expect.equal (Code.name refusal.Code) "no_compatible_release" "why nothing is selected"
                  | Ok _ -> ()

                  // The recipe still binds the offering's artifact for that platform, with its authenticated digest.
                  let target = str (selection.GetProperty "recipeBinding") "targetPath"

                  let host =
                      recipe.Hosts |> List.find (fun host -> host.Deployment.Text = str selection "deployment")

                  let binding = host.Platforms |> List.find (fun (p, _) -> Platform.name p = recipePlatform) |> snd
                  Expect.equal binding.Target.Text target "the recipe binds the target"
                  Expect.equal (Some binding.Sha256) (listed.TryFind target |> Option.map _.Sha256) "with its authenticated digest")

      yield
          testCase "the 18 bound digests are distinct" (fun () ->
              let digests =
                  [ for selection in selections -> listed[str (selection.GetProperty "recipeBinding") "targetPath"].Sha256.Text ]

              Expect.equal (List.length (List.distinct digests)) 18 "distinct")

      // The controls (finos/morphir 837164580): the same client on a release that is not an offering, selected at some
      // CLI versions and not at others, so a client that never selects fails them.
      match section.TryGetProperty "controls" with
      | false, _ -> ()
      | true, controls ->
          for control in controls.EnumerateArray() do
              let control = control.Clone()
              let toolId = str control "toolId"
              let cli = str control "cliVersion"
              requireKnown $"{toolId} control" [ "toolId"; "channel"; "platform"; "cliVersion"; "expected" ] control

              yield
                  testCase $"control {toolId} at CLI {cli}" (fun () ->
                      let platform = control.GetProperty "platform"
                      requireKnown $"{toolId} control platform" [ "os"; "arch" ] platform
                      let expected = control.GetProperty "expected"
                      requireKnown $"{toolId} control expected" [ "selected"; "version"; "targetPath" ] expected
                      let v1Platform = { Os = str platform "os"; Arch = str platform "arch" }

                      let request =
                          match Channel.tryParse (str control "channel") with
                          | Ok channel -> ToolRequest.OnChannel channel
                          | Error _ -> failtest "channel"

                      let id = match ToolId.tryParse toolId with Ok id -> id | Error _ -> failtest "tool id"
                      let cliVersion = match SemVer.tryParse cli with Ok v -> v | Error _ -> failtest "cli version"

                      match ToolResolver.resolve listed id request v1Platform cliVersion, expected.GetProperty("selected").GetBoolean() with
                      | Ok selection, true ->
                          Expect.equal selection.Version.Text (str expected "version") "the selected version"

                          // The artifact the client then installs: the descriptor's artifact for the platform, read from
                          // its authenticated bytes.
                          let descriptor = ToolReleaseDocument.read (targetBytes selection.Target) |> valid selection.Target
                          let artifact = descriptor.Artifacts |> List.tryFind (fun a -> a.Platform = v1Platform)
                          Expect.equal (artifact |> Option.map _.TargetPath) (Some(str expected "targetPath")) "the selected artifact"
                      | Error refusal, false ->
                          requireKnown $"{toolId} control expected" [ "selected" ] expected
                          Expect.equal (Code.name refusal.Code) "no_compatible_release" "why nothing is selected"
                      | outcome, selected -> failtestf "control at %s: expected selected = %b, got %A" cli selected outcome) ]

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
    | ("read" | "authenticate" | "check-locked-generation" | "check-recipe-bindings" | "load"), _ -> None
    | "check-recipe", Some rule when ranRules.Contains rule -> None
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
            let skipped = skippedRecipeRules |> Map.keys |> set
            let accounted = Set.union ranRules skipped
            Expect.isEmpty (Set.difference rules accounted) "every rule of the manifest is run or skipped with a reason"
            Expect.isEmpty (Set.difference skipped rules) "no skipped rule is stale"
            Expect.isEmpty (Set.difference pinnedRecipeRules rules) "no rule of the pinned corpus has lost its cases")

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
        testList "v1Descriptors" (v1DescriptorTests ())
        testList "v1Client" (v1ClientTests ())

        testCase "every part of a run section that is not run names its section" (fun () ->
            for section, _ in notRunClauses do
                Expect.contains ranSections section $"{section} is run")

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
