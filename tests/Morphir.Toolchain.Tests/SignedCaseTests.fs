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
let ranSections = [ "selection"; "frozen"; "lockRecipe"; "recipeChecks"; "trust"; "channelSelection"; "v1Descriptors" ]

/// Parts of a section this suite runs that it does not run, each with its reason.
let notRunClauses =
    [ "v1Descriptors",
      "Validation against tool-release-v1.schema.json, which this pin does not hold: the v1 reader checks the schema's rules, written from it by hand." ]

let private noV1Client =
    "needs the tool release metadata v1 client, which does not exist in morphir-dotnet yet (task 4.2)"

/// Sections this suite does not run, each with its reason.
let notRunSections =
    [ "v1Client", $"Selects artifacts by tool id, channel and v1 platform: {noV1Client}." ]

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

/// Recipe check rules this suite does not run, each with its reason. Every rule of the three tables is run.
let skippedRecipeRules: Map<string, string> = Map.empty

/// Trust operations, and the check-recipe rules, that this suite does not run, with their reasons.
let skippedTrust =
    Map.ofList
        [ "load", "loads the mutated repository through a TUF client: signatures, snapshot links and expiry are metadata verification" ]

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

/// The served bytes of a target after the case's target mutations, or None when the repository has no file for it.
let private tryServedBytes (case: JsonElement) (target: string) =
    tryTargetBytes target |> Option.map (fun _ -> servedBytes case target)

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
                      Expect.equal (recipeRows listed target tryTargetBytes) (Manifest.expected name case) name) ]

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
                      Expect.equal (recipeRows listed target (tryServedBytes case)) (Manifest.expected name case) name)
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
                      let actual = RecipeBindings.check (repository ()) listed (tryServedBytes case) recipe |> targetOutcome
                      Expect.equal actual (Manifest.expected name case) name)
          | "check-locked-generation", _ ->
              yield
                  testCase name (fun () ->
                      let lockFile = str action "lock"
                      let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed lockFile) |> valid lockFile
                      // The generation's recipe, as setup stored it: the authenticated bytes of the lock's recipe target.
                      // A generation whose recipe target the trust state does not list is refused before it is read.
                      let recipe = lazy (signedRecipe lock.Recipe.Target.Text)

                      let actual = RecipeTargets.checkLockedGeneration (repository ()) listed recipe lock |> targetOutcome
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
          let others =
              listed
              |> Map.keys
              |> Seq.filter (fun target -> target.StartsWith "releases/" && not (List.contains target releases))
              |> List.ofSeq

          Expect.equal others [ "releases/morphir-dotnet-not-a-release/1.2.3.json" ] "the one release not listed"
          Expect.isError (ToolReleaseDocument.read (targetBytes others.Head)) "it does not read as a v1 descriptor") ]

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
    | ("read" | "authenticate" | "check-locked-generation" | "check-recipe-bindings"), _ -> None
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
        testList "v1Descriptors" (v1DescriptorTests ())

        testCase "every part of a run section that is not run names its section" (fun () ->
            for section, _ in notRunClauses do
                Expect.contains ranSections section $"{section} is run")
    ]
