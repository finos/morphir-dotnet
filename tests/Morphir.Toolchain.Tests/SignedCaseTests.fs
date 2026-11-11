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
    match Fixtures.external with
    | Ok(Some _) -> []
    | _ ->
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

/// <summary>A value with the member or element a JSON pointer names replaced (RFC 6901: ~1 is /, ~0 is ~). As
/// serde_json's <c>pointer_mut</c>, the pointer must name a member or element that exists: an array index is decimal
/// without a leading zero, and a pointer that is not empty starts with /.</summary>
let setAtPointer (pointer: string) (replacement: Value) (value: Value) =
    if pointer <> "" && not (pointer.StartsWith '/') then
        failtestf "the pointer %s does not start with /" pointer

    let tokens =
        pointer.Split '/'
        |> Array.skip 1
        |> Array.map (fun token -> token.Replace("~1", "/").Replace("~0", "~"))
        |> List.ofArray

    let index (token: string) (count: int) =
        let digits = token <> "" && token |> Seq.forall Char.IsAsciiDigit && (token = "0" || not (token.StartsWith '0'))

        match digits, Int32.TryParse(token, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
        | true, (true, at) when at < count -> at
        | _ -> failtestf "the pointer %s names no element %s" pointer token

    let rec set (path: string list) (value: Value) =
        match path, value with
        | [], _ -> replacement
        | head :: rest, Value.Object members when members |> List.exists (fst >> (=) head) ->
            Value.Object(members |> List.map (fun (name, item) -> if name = head then name, set rest item else name, item))
        | head :: rest, Value.Array items ->
            let at = index head items.Length
            Value.Array(items |> List.mapi (fun position item -> if position = at then set rest item else item))
        | _ -> failtestf "the pointer %s names nothing" pointer

    set tokens value

/// A JSON value written in a manifest, read as this suite's values.
let private rawValue (what: string) (element: JsonElement) =
    // Wrapped in an object, since a document is not a bare string.
    match Syntax.parseJson (Encoding.UTF8.GetBytes("{\"value\":" + element.GetRawText() + "}")) with
    | Ok wrapped -> TufFixture.memberOf "value" wrapped
    | Error _ -> failtestf "%s does not parse" what

/// Fails unless an element is an object with exactly these members: none missing and none this runner does not apply.
let requireExactly (where: string) (expected: string list) (element: JsonElement) =
    if element.ValueKind <> JsonValueKind.Object then
        failtestf "%s is not an object" where

    let actual = [ for p in element.EnumerateObject() -> p.Name ]
    let unknown = Set.difference (Set.ofList actual) (Set.ofList expected)
    let missing = Set.difference (Set.ofList expected) (Set.ofList actual)

    if not unknown.IsEmpty then
        failtestf "%s has members this runner does not apply: %A" where (Set.toList unknown)

    if not missing.IsEmpty then
        failtestf "%s lacks %A" where (Set.toList missing)

    if actual.Length <> (Set.ofList actual).Count then
        failtestf "%s repeats a member" where

let private textMember (where: string) (element: JsonElement) (name: string) =
    match element.GetProperty name with
    | value when value.ValueKind = JsonValueKind.String -> nonNull (value.GetString())
    | _ -> failtestf "%s: %s is not a string" where name

/// <summary>One declared mutation of a trust case, with exactly the members its op defines (finos/morphir's
/// <c>mutate</c> in <c>tests/signed_repository.rs</c> at <c>dde74f94</c>). The metadata mutations change the case's
/// metadata set and the target mutations change the bytes served for targets, each in their declared order.</summary>
type TrustMutation =
    | SetSignedMember of role: string * pointer: string * value: Value
    | Relink
    | ReplaceRoleText of role: string * find: string * replace: string
    | ReplaceRole of role: string * from: string
    | ReplaceTargetBytes of target: string * source: string
    | ReplaceTargetText of target: string * find: string * replace: string

/// A trust case's mutations: an array that every case states, even when empty. An unknown op or member fails.
let trustMutations (name: string) (case: JsonElement) : TrustMutation list =
    let declared =
        match case.TryGetProperty "mutations" with
        | true, m when m.ValueKind = JsonValueKind.Array -> [ for x in m.EnumerateArray() -> x ]
        | true, _ -> failtestf "%s: mutations is not an array" name
        | false, _ -> failtestf "%s: a trust case states its mutations, even none" name

    declared
    |> List.mapi (fun position m ->
        let where = $"{name}: mutation {position}"

        let op =
            match m.ValueKind, m.TryGetProperty "op" with
            | JsonValueKind.Object, (true, op) when op.ValueKind = JsonValueKind.String -> nonNull (op.GetString())
            | _ -> failtestf "%s has no op" where

        let exactly members = requireExactly $"{where} ({op})" ("op" :: members) m
        let text = textMember $"{where} ({op})" m

        match op with
        | "set-signed-member" ->
            exactly [ "role"; "pointer"; "value" ]
            SetSignedMember(text "role", text "pointer", rawValue where (m.GetProperty "value"))
        | "relink" ->
            exactly []
            Relink
        | "replace-role-text" ->
            exactly [ "role"; "find"; "replace" ]
            ReplaceRoleText(text "role", text "find", text "replace")
        | "replace-role" ->
            exactly [ "role"; "from" ]
            ReplaceRole(text "role", text "from")
        | "replace-target-bytes" ->
            exactly [ "target"; "with" ]
            ReplaceTargetBytes(text "target", text "with")
        | "replace-target-text" ->
            exactly [ "target"; "find"; "replace" ]
            ReplaceTargetText(text "target", text "find", text "replace")
        | other -> failtestf "%s: the op %s is not one this runner applies" where other)

let private roleEnvelope (role: string) (set: TufFixture.MetadataSet) =
    match role with
    | "timestamp" -> set.Timestamp
    | "snapshot" -> set.Snapshot
    | "targets" -> set.Targets
    | other -> failtestf "a mutation of role %s, which a metadata set does not serve" other

let private withRole (role: string) (envelope: Value) (set: TufFixture.MetadataSet) =
    match role with
    | "timestamp" -> { set with Timestamp = envelope }
    | "snapshot" -> { set with Snapshot = envelope }
    | "targets" -> { set with Targets = envelope }
    | other -> failtestf "a mutation of role %s, which a metadata set does not serve" other

/// <summary>A metadata set after the metadata mutations, in their declared order. A replace-role-text replacement
/// stays applied when the role is later relinked, replaced or edited, as in finos/morphir's runner; if the edit removes
/// or repeats its text, serving the role fails.</summary>
let applyMetadataMutations (set: TufFixture.MetadataSet) (mutations: TrustMutation list) =
    (set, mutations)
    ||> List.fold (fun set mutation ->
        match mutation with
        | SetSignedMember(role, pointer, value) ->
            let envelope = roleEnvelope role set
            let signed = setAtPointer pointer value (TufFixture.memberOf "signed" envelope)
            withRole role (TufFixture.setAt [ "signed" ] signed envelope) set
        | Relink -> TufFixture.relink set
        | ReplaceRoleText(role, find, replace) ->
            roleEnvelope role set |> ignore
            TufFixture.replaceRoleText role find replace set
        | ReplaceRole(role, from) -> withRole role (TufFixture.at (List.ofArray (from.Split '.')) (TufFixture.repository ())) set
        | ReplaceTargetBytes _
        | ReplaceTargetText _ -> set)

/// <summary>The bytes served for each target after a case's target mutations, applied in their declared order to the
/// repository's target files, as finos/morphir's runner applied them: a replace-target-bytes takes the source's bytes
/// as the mutations before it left them. Every target mutation must change bytes the action reads, so no case passes
/// with a mutation the runner ignored (review of #582, R4).</summary>
type ServedTargets(name: string, mutations: TrustMutation list) =
    let changed = System.Collections.Generic.Dictionary<string, byte array * Set<int>>(StringComparer.Ordinal)

    let current (target: string) =
        match changed.TryGetValue target with
        | true, entry -> Some entry
        | false, _ -> tryTargetBytes target |> Option.map (fun bytes -> bytes, Set.empty)

    let targetMutations =
        mutations
        |> List.indexed
        |> List.filter (fun (_, m) ->
            match m with
            | ReplaceTargetBytes _
            | ReplaceTargetText _ -> true
            | _ -> false)

    do
        for position, mutation in targetMutations do
            match mutation with
            | ReplaceTargetBytes(target, source) ->
                match current source with
                | Some(bytes, from) -> changed[target] <- (bytes, Set.add position from)
                | None -> failtestf "%s: replace-target-bytes of %s takes %s, which the repository does not serve" name target source
            | ReplaceTargetText(target, find, replace) ->
                match current target with
                | None -> failtestf "%s: replace-target-text of %s, which the repository does not serve" name target
                | Some(bytes, from) ->
                    let text = Encoding.UTF8.GetString bytes
                    let at = text.IndexOf(find, StringComparison.Ordinal)

                    if at < 0 then
                        failtestf "%s: the mutation's text %s is not in %s" name find target

                    changed[target] <- (Encoding.UTF8.GetBytes(text.Substring(0, at) + replace + text.Substring(at + find.Length)), Set.add position from)
            | _ -> ()

    let read = System.Collections.Generic.HashSet<int>()

    /// The bytes served for a target, or None when the repository has no file for it.
    member _.TryServed(target: string) =
        current target
        |> Option.map (fun (bytes, from) ->
            read.UnionWith from
            bytes)

    member this.Served(target: string) =
        match this.TryServed target with
        | Some bytes -> bytes
        | None -> failtestf "%s: the repository serves no target %s" name target

    /// Fails unless every target mutation changed bytes the action read.
    member _.ExpectAllApplied(action: string) =
        for position, mutation in targetMutations do
            if not (read.Contains position) then
                failtestf "%s: mutation %d (%A) changes bytes the %s action never reads, so it would be ignored" name position mutation action

/// The action of a trust case, with exactly the members its op defines.
type TrustAction =
    | Load
    | Read of target: string
    | Authenticate of target: string * text: string
    | CheckRecipe of recipe: string
    | CheckRecipeBindings of recipe: string * changes: (string * Value) list
    | CheckLockedGeneration of lock: string

let trustAction (name: string) (action: JsonElement) =
    let op =
        match action.ValueKind, action.TryGetProperty "op" with
        | JsonValueKind.Object, (true, op) when op.ValueKind = JsonValueKind.String -> nonNull (op.GetString())
        | _ -> failtestf "%s: the action has no op" name

    let where = $"{name}: action {op}"
    let exactly members = requireExactly where ("op" :: members) action
    let text = textMember where action

    match op with
    | "load" ->
        exactly []
        Load
    | "read" ->
        exactly [ "target" ]
        Read(text "target")
    | "authenticate" ->
        exactly [ "target"; "text" ]
        Authenticate(text "target", text "text")
    | "check-recipe" ->
        exactly [ "recipe" ]
        CheckRecipe(text "recipe")
    | "check-recipe-bindings" ->
        exactly [ "recipe"; "documentMutations" ]

        let changes =
            match action.GetProperty "documentMutations" with
            | list when list.ValueKind = JsonValueKind.Array ->
                [ for position, change in Seq.indexed (list.EnumerateArray()) ->
                      let at = $"{where} document mutation {position}"
                      requireExactly at [ "pointer"; "value" ] change
                      textMember at change "pointer", rawValue at (change.GetProperty "value") ]
            | _ -> failtestf "%s: documentMutations is not an array" where

        CheckRecipeBindings(text "recipe", changes)
    | "check-locked-generation" ->
        exactly [ "lock" ]
        CheckLockedGeneration(text "lock")
    | other -> failtestf "%s: the action %s is not one this runner runs" name other

let private actionName (action: TrustAction) =
    match action with
    | Load -> "load"
    | Read _ -> "read"
    | Authenticate _ -> "authenticate"
    | CheckRecipe _ -> "check-recipe"
    | CheckRecipeBindings _ -> "check-recipe-bindings"
    | CheckLockedGeneration _ -> "check-locked-generation"

/// <summary>Runs a trust case as finos/morphir's <c>trust_cases</c> did, for every action: the metadata set after the
/// metadata mutations is refreshed from the trusted root at <paramref name="start"/>; a load case's outcome is that
/// refresh's, and any other action is refused with the refresh's refusal, or runs over the targets it authenticated and
/// the bytes served after the target mutations, every one of which must be read.</summary>
let runTrustCase (start: DateTimeOffset) (name: string) (metadata: string) (mutations: TrustMutation list) (action: TrustAction) =
    let set = applyMetadataMutations (TufFixture.metadataSet metadata) mutations
    let served = ServedTargets(name, mutations)

    let outcome =
        match action, TufClient.refresh start (anchor ()) (TufFixture.sourceOf set []) with
        | _, RefreshResult.Failed(failure, _) -> Refused(Code.name (TufFailure.code failure), "")
        | Load, RefreshResult.Refreshed _ -> Valid
        | _, RefreshResult.Refreshed trusted ->
            let listed =
                match trusted.Targets with
                | Some targets -> targets.Targets
                | None -> failtestf "%s: the refresh authenticated no targets" name

            match action with
            | Load -> Valid
            | Read target -> RecipeTargets.authenticate (listed.TryFind target) (served.Served target) |> targetOutcome
            | Authenticate(target, text) -> RecipeTargets.authenticate (listed.TryFind target) (Encoding.UTF8.GetBytes text) |> targetOutcome
            | CheckRecipe recipe -> recipeRows listed recipe served.TryServed
            | CheckRecipeBindings(recipe, changes) ->
                // The served recipe, which must authenticate, edited by JSON pointer; then only its bindings are
                // checked, since the edited bytes are no longer the authenticated recipe target.
                let bytes = served.Served recipe

                match RecipeTargets.authenticate (listed.TryFind recipe) bytes with
                | Ok _ -> ()
                | Error refusal -> failtestf "%s: the recipe %s does not authenticate: %A" name recipe refusal

                let original =
                    match Syntax.parseJson bytes with
                    | Ok value -> value
                    | Error _ -> failtestf "%s: the recipe does not parse" name

                let edited = (original, changes) ||> List.fold (fun value (pointer, change) -> setAtPointer pointer change value)
                let document = RecipeDocument.read (JsonText.toBytes edited) |> valid recipe
                RecipeBindings.check (repository ()) listed served.TryServed document |> targetOutcome
            | CheckLockedGeneration lockFile ->
                let lock = LockDocument.read (Fixtures.bytesIn Fixtures.signed lockFile) |> valid lockFile
                // The generation's recipe: the bytes served for the lock's recipe target. The check turns an exception
                // from this source into a refusal, so a target the repository does not serve is recorded and fails
                // the case after the check, instead of passing as that refusal.
                let unserved = ref None

                let recipe () =
                    let target = lock.Recipe.Target.Text

                    match served.TryServed target with
                    | Some bytes -> RecipeDocument.read bytes |> Result.mapError (fun r -> ({ Code = Code.Reader r.Code; Path = r.Path }: TargetRefusal))
                    | None ->
                        unserved.Value <- Some target
                        Error({ Code = Code.Reader ReaderCode.InvalidDocument; Path = DocumentPath.root }: TargetRefusal)

                let outcome = RecipeTargets.checkLockedGeneration (repository ()) listed recipe lock |> targetOutcome

                match unserved.Value with
                | Some target -> failtestf "%s: the repository serves no recipe target %s" name target
                | None -> outcome
        | _, other -> failtestf "%s did not complete: %A" name other

    served.ExpectAllApplied(actionName action)
    outcome

/// A trust case of the manifest, run at a start time.
let private runManifestTrustCase (start: DateTimeOffset) (case: JsonElement) =
    let name = str case "name"
    requireKnown name [ "name"; "rule"; "metadata"; "mutations"; "action"; "expected" ] case
    let mutations = trustMutations name case
    runTrustCase start name (str case "metadata") mutations (trustAction name (case.GetProperty "action"))

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

/// Why a trust case is not run, or None when it is.
let trustSkipReason (op: string) (rule: string option) =
    match op, rule with
    | ("read" | "authenticate" | "check-locked-generation" | "check-recipe-bindings" | "load"), _ -> None
    | "check-recipe", Some rule when ranRules.Contains rule -> None
    | "check-recipe", Some rule -> skippedRecipeRules.TryFind rule |> Option.orElse (Some "unlisted")
    | _ -> skippedTrust.TryFind op |> Option.orElse (Some "unlisted")

let private trustTests () =
    [ for case in cases "trust" do
          let name = str case "name"
          let op = str (case.GetProperty "action") "op"

          // A case is run unless it is skipped with a stated reason; an unlisted one runs and fails on what it lacks.
          match trustSkipReason op (optStr case "rule") with
          | Some reason when reason <> "unlisted" -> ()
          | _ ->
              yield
                  testCase name (fun () ->
                      Expect.equal (runManifestTrustCase TufFixture.referenceTime case) (Manifest.expected name case) name) ]

let private v1DescriptorTests () =
    use document = manifest ()
    let section = document.RootElement.GetProperty "v1Descriptors"
    requireKnown "v1Descriptors" [ "note"; "schema"; "releases" ] section
    Expect.equal (str section "schema") "tool-release-v1.schema.json" "the manifest names the v1 schema"
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

              let bytes = targetBytes release
              Expect.isOk (ToolReleaseDocument.read bytes) "a v1 descriptor"

              match Fixtures.external with
              | Ok(Some _) ->
                  match Syntax.parseJson bytes with
                  | Ok value -> SchemaTests.expectValid "tool-release" release value
                  | Error _ -> failtestf "%s is not JSON" release
              | _ -> ())
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

/// The outcome of a load trust case: its metadata set, after its mutations, refreshed from the trusted root at a start.
let private loadOutcome (start: DateTimeOffset) (case: JsonElement) = runManifestTrustCase start case


/// <summary>The reference window (finos/morphir's <c>every_case_has_one_outcome_across_the_reference_window</c>): the
/// fixtures are built so every case has the same outcome at any instant after the expired timestamp's expiry and
/// before every other expiry. The Rust client could only compare expiry with the wall clock; this client takes the
/// start time, so every load case is run at both ends of the window, at the reference time and now.</summary>
let private referenceWindowTests () =
    use repository = repositoryDocument ()
    let root = repository.RootElement
    let text (e: JsonElement) = nonNull (e.GetString())

    let instant (value: string) =
        // RFC 3339 UTC timestamps of one fixed shape compare as strings.
        Expect.isTrue (value.Length = 20 && value.EndsWith 'Z') $"{value} is a fixed-shape UTC timestamp"
        DateTimeOffset.Parse(value, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AssumeUniversal)

    let reference = text (root.GetProperty "referenceTime")
    let expired = text (root.GetProperty("rejectionMetadata").GetProperty("expiredTimestamp").GetProperty("signed").GetProperty "expires")

    let expiries =
        [ yield text (root.GetProperty("trustedRoot").GetProperty("signed").GetProperty "expires")
          for set in [ "metadata"; "learnedRevocation" ] do
              for role in [ "timestamp"; "snapshot"; "targets" ] do
                  yield text (root.GetProperty(set).GetProperty(role).GetProperty("signed").GetProperty "expires") ]

    let earliest = expiries |> List.min

    [ testCase "the reference time and now lie after the expired timestamp and before every other expiry" (fun () ->
          Expect.isLessThan (instant expired) (instant reference) "after the expired timestamp"
          Expect.isLessThan (instant reference) (instant earliest) "before every other expiry"
          Expect.equal expired "2020-01-01T00:00:00Z" "the expired timestamp's expiry"
          Expect.equal earliest "2999-01-01T00:00:00Z" "the earliest other expiry"
          Expect.isTrue (instant expired < DateTimeOffset.UtcNow && DateTimeOffset.UtcNow < instant earliest) "now is inside the window"
          Expect.equal TufFixture.referenceTime (instant reference) "the client runs at the fixture's reference time")

      testCase "every load case has one outcome across the whole window" (fun () ->
          let starts =
              [ (instant expired).AddSeconds 1.0
                instant reference
                DateTimeOffset.UtcNow
                (instant earliest).AddSeconds -1.0 ]

          let loads = cases "trust" |> List.filter (fun case -> str (case.GetProperty "action") "op" = "load")
          Expect.isNonEmpty loads "load cases"

          for case in loads do
              let outcomes = starts |> List.map (fun start -> loadOutcome start case)
              Expect.equal (List.distinct outcomes) [ Manifest.expected (str case "name") case ] $"""{str case "name"}: one outcome at {starts}""") ]

/// <summary>A recipe whose preset is a device name (finos/morphir's
/// <c>a_non_portable_recipe_target_is_refused_without_panicking</c>): its target
/// <c>toolchains/recipes/con/1.0.0.json</c> is refused as <c>non_portable_path</c>, by the TUF client when targets
/// metadata lists it, and by selection and the recipe target check over authenticated targets. Nothing here touches
/// the file system, so no directory named <c>con</c> is ever created.</summary>
let private devicePresetTests () =
    let target = "toolchains/recipes/con/1.0.0.json"
    let recipeBytes = Encoding.UTF8.GetBytes """{"preset":"con","version":"1.0.0"}"""

    let state =
        Value.Object
            [ "morphir",
              Value.Object
                  [ "kind", Value.String "toolset-recipe"
                    "schemaVersion", Value.String "0.1.0-draft.1"
                    "preset", Value.String "con"
                    "version", Value.String "1.0.0"
                    "channels", Value.Array [ Value.String "stable" ]
                    "status", Value.String "active" ] ]

    let entry =
        { Length = uint64 recipeBytes.Length
          Sha256 = Sha256.ofBytes recipeBytes
          Custom = Some state }

    let con = match Token.tryParse "con" with Ok t -> t | Error _ -> failtest "con is a token"

    [ testCase "the TUF client refuses targets metadata that lists the device-name recipe" (fun () ->
          let set = TufFixture.metadataSet "metadata"

          let listing =
              Value.Object
                  [ "length", TufFixture.number entry.Length
                    "hashes", Value.Object [ "sha256", Value.String entry.Sha256.Text ]
                    "custom", state ]

          let targets = TufFixture.resign "targets" (TufFixture.setAt [ "signed"; "targets"; target ] listing set.Targets)
          let relinked = TufFixture.relink { set with Targets = targets }

          match TufClient.refresh TufFixture.referenceTime (anchor ()) (TufFixture.sourceOf relinked []) with
          | RefreshResult.Failed(failure, _) -> Expect.equal (Code.name (TufFailure.code failure)) "non_portable_path" "refused"
          | other -> failtestf "expected a refusal, got %A" other)

      testCase "a selected device-name recipe is refused by the target check before it is read" (fun () ->
          // Selection names the greatest active recipe of the preset on the channel; the recipe target checks then
          // start with the target path rules, before anything is read (toolset recipe page). The Rust reference
          // refused at selection; here the refusal is the check's first row, with the same code and empty path.
          let listed = (target, entry) :: (authenticatedTargets "metadata" |> Map.toList)
          Expect.equal (RecipeTargets.select con Channel.Stable listed) (Ok target) "selection names the device-name target"
          Expect.equal (targetOutcome (RecipeTargets.check target (Some entry) recipeBytes)) (Refused("non_portable_path", "")) "the check refuses it"
          // The check refuses the path before the bytes: even bytes that are not a recipe give the same refusal.
          Expect.equal (targetOutcome (RecipeTargets.check target (Some entry) [||])) (Refused("non_portable_path", "")) "before anything is read") ]

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

        testList "reference window" (referenceWindowTests ())
        testList "device-name preset" (devicePresetTests ())
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

        testList "every trust action runs over every declared mutation, or fails" [
            let caseOf (json: string) = (JsonDocument.Parse json).RootElement.Clone()
            let release = "releases/morphir-dotnet-native-aot/1.2.3.json"
            let other = "releases/morphir-dotnet-self-contained/1.2.3.json"
            let recipe = "toolchains/recipes/fsharp/1.0.0.json"
            let run metadata mutations action = runTrustCase TufFixture.referenceTime "synthetic" metadata mutations action
            let quoted (a: string) (b: string) = "\"" + a + "\":\"" + b + "\""

            yield testCase "an unknown mutation op fails" (fun () ->
                let case = caseOf """{"name":"made up","mutations":[{"op":"made-up-mutation","target":"nothing"}]}"""
                Expect.throws (fun () -> trustMutations "made up" case |> ignore) "an unknown op fails")

            yield testCase "a mutation member its op does not define fails" (fun () ->
                let case = caseOf """{"name":"extra","mutations":[{"op":"relink","role":"targets"}]}"""
                Expect.throws (fun () -> trustMutations "extra" case |> ignore) "an unknown member fails")

            yield testCase "a mutation without a member its op defines fails" (fun () ->
                let case = caseOf """{"name":"missing","mutations":[{"op":"replace-role","role":"timestamp"}]}"""
                Expect.throws (fun () -> trustMutations "missing" case |> ignore) "a missing member fails")

            yield testCase "a trust case without a mutations array fails" (fun () ->
                Expect.throws (fun () -> trustMutations "none" (caseOf """{"name":"none"}""") |> ignore) "missing"
                Expect.throws (fun () -> trustMutations "object" (caseOf """{"name":"object","mutations":{"op":"relink"}}""") |> ignore) "an object")

            yield testCase "an unknown action, or an action member its op does not define, fails" (fun () ->
                Expect.throws (fun () -> trustAction "unknown" (caseOf """{"op":"made-up-action"}""") |> ignore) "unknown op"
                Expect.throws (fun () -> trustAction "extra" (caseOf """{"op":"load","target":"x"}""") |> ignore) "extra member"
                Expect.throws (fun () -> trustAction "missing" (caseOf """{"op":"read"}""") |> ignore) "missing member"

                Expect.throws
                    (fun () -> trustAction "change" (caseOf """{"op":"check-recipe-bindings","recipe":"r","documentMutations":[{"pointer":"/a","value":1,"x":2}]}""") |> ignore)
                    "an unknown document mutation member")

            yield testCase "every refused load case refuses each other action the same way" (fun () ->
                // Review of #591: the metadata mutations applied to load actions only. The same set is now refreshed for
                // every action, so a set the client refuses refuses a read, an authentication and a launch check too.
                let refused =
                    [ for case in cases "trust" do
                          let name = str case "name"

                          if str (case.GetProperty "action") "op" = "load" then
                              match Manifest.expected name case with
                              | Refused _ as expected -> yield name, str case "metadata", trustMutations name case, expected
                              | _ -> () ]

                Expect.isGreaterThanOrEqual refused.Length 3 "refused load cases"

                for name, metadata, mutations, expected in refused do
                    for action in [ Read release; Authenticate(release, Encoding.UTF8.GetString(targetBytes release)); CheckRecipe recipe; CheckLockedGeneration "fsharp-native-aot.lock.json" ] do
                        Expect.equal (run metadata mutations action) expected $"{name} as {action}")

            yield testCase "a target mutation of a load, an authentication or a launch check fails, since none reads served bytes" (fun () ->
                let mutation = [ ReplaceTargetBytes(recipe, other) ]
                Expect.throws (fun () -> run "metadata" mutation Load |> ignore) "load"
                Expect.throws (fun () -> run "metadata" mutation (Authenticate(release, "x")) |> ignore) "authenticate"
                Expect.throws (fun () -> run "metadata" [ ReplaceTargetBytes(release, other) ] (CheckLockedGeneration "fsharp-native-aot.lock.json") |> ignore) "a launch check reads only the recipe")

            yield testCase "a mutation of a target the action never reads fails" (fun () ->
                Expect.throws (fun () -> run "metadata" [ ReplaceTargetBytes("releases/morphir-v1-control/1.2.3.json", other) ] (Read release) |> ignore) "never read")

            yield testCase "a text mutation whose text is not in the target fails" (fun () ->
                Expect.throws (fun () -> ServedTargets("absent", [ ReplaceTargetText(recipe, "no such text", "x") ]) |> ignore) "not applied")

            yield testCase "target mutations apply in order: a later one takes bytes an earlier one changed" (fun () ->
                let original = Encoding.UTF8.GetString(targetBytes other)
                let find = quoted "id" "morphir-dotnet-self-contained"
                Expect.stringContains original find "the text to change"
                let replace = quoted "id" "morphir-dotnet-changed"
                let mutations = [ ReplaceTargetText(other, find, replace); ReplaceTargetBytes(release, other) ]
                let served = ServedTargets("ordered", mutations)
                Expect.stringContains (Encoding.UTF8.GetString(served.Served release)) replace "the changed bytes"
                served.ExpectAllApplied "read"
                Expect.throws (fun () -> ServedTargets("unread", mutations).ExpectAllApplied "read") "nothing read")

            yield testCase "a relink keeps a replace-role-text replacement of the snapshot" (fun () ->
                // Review of #591: relink dropped the snapshot's and timestamp's served overrides.
                let replace = quoted "_type" "snapshoT"
                let set = applyMetadataMutations (TufFixture.metadataSet "metadata") [ ReplaceRoleText("snapshot", quoted "_type" "snapshot", replace); Relink ]
                let served = TufFixture.served "snapshot" set
                Expect.stringContains (Encoding.UTF8.GetString served) replace "still replaced"

                Expect.equal
                    (TufFixture.at [ "signed"; "meta"; "snapshot.json"; "hashes"; "sha256" ] set.Timestamp)
                    (Value.String(Digest.ofBytes served))
                    "the timestamp links the served snapshot")

            yield testCase "an edit after a replace-role-text keeps it, or fails when it removes its text" (fun () ->
                let replace = quoted "_type" "targetS"
                let kept =
                    applyMetadataMutations
                        (TufFixture.metadataSet "metadata")
                        [ ReplaceRoleText("targets", quoted "_type" "targets", replace); SetSignedMember("targets", "/expires", Value.String "2998-01-01T00:00:00Z") ]

                Expect.stringContains (Encoding.UTF8.GetString(TufFixture.served "targets" kept)) replace "still replaced"

                let removed =
                    applyMetadataMutations
                        (TufFixture.metadataSet "metadata")
                        [ ReplaceRoleText("timestamp", quoted "_type" "timestamp", quoted "_type" "x"); SetSignedMember("timestamp", "/_type", Value.String "other") ]

                Expect.throws (fun () -> TufFixture.served "timestamp" removed |> ignore) "the text is gone")

            yield testCase "a second replace-role-text of a role fails" (fun () ->
                let twice = [ ReplaceRoleText("targets", quoted "_type" "targets", quoted "_type" "a"); ReplaceRoleText("targets", quoted "_type" "a", quoted "_type" "b") ]
                Expect.throws (fun () -> applyMetadataMutations (TufFixture.metadataSet "metadata") twice |> ignore) "not composed")

            yield testCase "a pointer must name a member or element that exists" (fun () ->
                let set mutation = applyMetadataMutations (TufFixture.metadataSet "metadata") [ mutation ] |> ignore
                Expect.throws (fun () -> set (SetSignedMember("timestamp", "/no-such-member", Value.String "x"))) "a missing member"
                Expect.throws (fun () -> set (SetSignedMember("timestamp", "version", Value.String "x"))) "no leading slash"
                Expect.throws (fun () -> set (SetSignedMember("root", "/version", Value.String "x"))) "a role a set does not serve"
                let array = Value.Object [ "a", Value.Array [ Value.Number "1" ] ]
                Expect.equal (setAtPointer "/a/0" (Value.Number "2") array) (Value.Object [ "a", Value.Array [ Value.Number "2" ] ]) "an element"
                Expect.throws (fun () -> setAtPointer "/a/1" Value.Null array |> ignore) "past the end"
                Expect.throws (fun () -> setAtPointer "/a/00" Value.Null array |> ignore) "a leading zero")
        ]
    ]