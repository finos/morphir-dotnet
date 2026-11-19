// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Every binding case of the unsigned manifest: documents checked against each other, resolution and the plan digest.
module Morphir.Toolchain.Tests.BindingCaseTests

open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.Manifest

let valid (what: string) (result: Result<'T, ReaderRefusal>) =
    match result with
    | Ok value -> value
    | Error refusal -> failtestf "%s does not read: %s at %s" what (Code.readerName refusal.Code) (DocumentPath.render refusal.Path)

let lockAt relative = LockDocument.read (Fixtures.bytes relative) |> valid relative
let recipeAt relative = RecipeDocument.read (Fixtures.bytes relative) |> valid relative

let platformOf (text: string) =
    match Platform.tryParse text with
    | Ok platform -> platform
    | Error _ -> failtestf "the manifest names an unknown platform %s" text

let bindingOutcome (result: Result<'T, BindingRefusal>) =
    match result with
    | Ok _ -> Valid
    | Error refusal -> Refused(Code.name refusal.Code, DocumentPath.render refusal.Path)

/// The members a manifest lists for a selected provider, from the locked provider.
let project (provider: LockedProvider) =
    [ match provider.Operation with
      | Operation.Compile language ->
          "operation", "compile"
          "language", language.Text
      | Operation.Generate target ->
          "operation", "generate"
          "target", target.Text
      "delivery", Delivery.name (LockedProvider.delivery provider)
      "extensionId", provider.ExtensionId.Text
      "version", provider.Version.Text ]
    |> Map.ofList

/// Compares a resolution with a manifest's expectation, using only the members the manifest lists.
let expectResolution (name: string) (actual: Result<Resolved, ResolutionFailure>) (expected: ResolveExpected) =
    match actual, expected with
    | Ok resolved, Providers(hostTarget, providers) ->
        match hostTarget with
        | Some target -> Expect.equal resolved.Closure.Host.Target.Text target $"{name}: host target"
        | None -> ()

        Expect.equal (List.length resolved.Closure.Providers) (List.length providers) $"{name}: provider count"

        for actual, expected in List.zip resolved.Closure.Providers providers do
            let projected = project actual
            let listed = expected |> List.map (fun (member', _) -> member', Map.find member' projected)
            Expect.equal listed expected $"{name}: provider"
    | Error failure, ResolveRefused(code, path) ->
        Expect.equal (Code.name (Code.Artifact failure.Code), DocumentPath.render failure.Path) (code, path) $"{name}: refusal"
    | Ok resolved, ResolveRefused(code, path) ->
        failtestf "%s resolved to %A, expected %s at %s" name (resolved.Closure.Providers |> List.map project) code path
    | Error failure, Providers _ ->
        failtestf "%s was refused with %s at %s" name (Code.name (Code.Artifact failure.Code)) (DocumentPath.render failure.Path)

let run (case: BindingCase) =
    match case with
    | LockRequest(name, lock, request, expected) ->
        RequestDocument.read (Fixtures.bytes request) |> valid request |> ignore
        let actual = Bindings.lockBindsRequest (lockAt lock) (Fixtures.bytes request) |> bindingOutcome
        Expect.equal actual expected name
    | LockPlatform(name, lock, platform, expected) ->
        let actual = Bindings.lockedClosure (lockAt lock) (platformOf platform) |> bindingOutcome
        Expect.equal actual expected name
    | ReceiptLock(name, receipt, lock, expected) ->
        let receipt = ReceiptDocument.read (Fixtures.bytes receipt) |> valid receipt
        let actual = Bindings.receiptBindsLock receipt (lockAt lock) |> bindingOutcome
        Expect.equal actual expected name
    | Resolve(name, recipe, request, platform, expected) ->
        let request = RequestDocument.read (Fixtures.bytes request) |> valid request
        expectResolution name (Resolution.resolve (recipeAt recipe) request (platformOf platform)) expected
    | PlanCanonical(name, result, canonical, expected) ->
        Expect.equal expected Valid $"{name}: only valid plan-canonical cases are defined"
        let read = ResultDocument.read (Fixtures.bytes result) |> valid result

        match read.Plan with
        | None -> failtestf "%s has no plan" name
        | Some digested ->
            Expect.sequenceEqual digested.Canonical (Fixtures.bytes canonical) $"{name}: canonical bytes"
            Expect.equal digested.Sha256 (Sha256.ofBytes (Fixtures.bytes canonical)) $"{name}: planSha256"
    | LockRequestBytes(name, lock, request, expected, cause) ->
        let actual = Bindings.lockBindsRequest (lockAt lock) (Fixtures.bytes request)
        Expect.equal (bindingOutcome actual) expected name

        let actualCause =
            match actual with
            | Error { Failure = BindingFailure.Binding(BindingProblem.RequestChanged(Some cause)) } -> Some(RequestChangeCause.name cause)
            | _ -> None

        Expect.equal actualCause cause $"{name}: cause"
    | PlanLock(name, result, lock, expected) ->
        Expect.equal expected Valid $"{name}: only valid plan-lock cases are defined"
        let read = ResultDocument.read (Fixtures.bytes result) |> valid result

        match read.Plan with
        | None -> failtestf "%s has no plan" name
        | Some digested ->
            // Equal as parsed values: the same members with the same values, in any order.
            let canonical lock =
                match Canonical.bytes (LockDocument.value lock) with
                | Ok bytes -> bytes
                | Error _ -> failtest "not canonical"

            Expect.sequenceEqual (canonical digested.Plan.Lock) (canonical (lockAt lock)) $"{name}: the plan's lock"
    | Discover(name, windows, files, boundary, start, request) ->
        let segments (path: string) = path.Split('/', System.StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
        // A Windows case is compared with every name folded to lower case.
        let fold (path: string) = if windows then path.ToLowerInvariant() else path
        let present = files |> List.map (fold >> segments) |> set
        let names = if windows then Discovery.PathCase.Folded else Discovery.PathCase.Exact
        let found = Discovery.requestIn names (segments boundary) (segments start) (String.concat "/" >> fold >> segments >> present.Contains)
        Expect.equal (found |> Option.map (String.concat "/" >> fold)) (request |> Option.map fold) name
    | MepVersion(name, version, expected) ->
        let actual =
            match ProtocolVersion.tryParse version with
            | Ok parsed ->
                Expect.equal (ProtocolVersion.text parsed) version $"{name}: the text round-trips"
                Valid
            | Error code -> Refused(Code.readerName code, "")

        Expect.equal actual expected name
    | Nfc(name, input, expected) ->
        Expect.equal (Nfc.normalize input) expected $"{name}: normalized"

        match Canonical.bytes (Value.String input) with
        | Ok bytes -> Expect.sequenceEqual bytes (System.Text.Encoding.UTF8.GetBytes("\"" + expected + "\"")) $"{name}: canonical bytes"
        | Error _ -> failtestf "%s: the string has no canonical form" name
    | SetupPlatforms(name, request, platform, platforms) ->
        let request = RequestDocument.read (Fixtures.bytes request) |> valid request
        let locked = Resolution.lockPlatforms request (platformOf platform) |> List.map Platform.name
        Expect.equal locked platforms name
    | PrerequisiteVersions(name, versions, installed, met) ->
        let comparators =
            versions
            |> List.map (fun text ->
                match Comparator.tryParse text with
                | Ok comparator -> comparator
                | _ -> failtestf "%s: %s is not a comparator" name text)

        let installed =
            match SemVer.tryParse installed with
            | Ok version -> version
            | Error _ -> failtestf "%s: %s is not a version" name installed

        Expect.equal (Comparator.allMet comparators installed) met name
    | Version(name, comparator, version, expected) ->
        let actual =
            if comparator then
                match Comparator.tryParse version with
                | Ok parsed ->
                    Expect.equal parsed.Text version $"{name}: the text round-trips"
                    Valid
                | Error code -> Refused(Code.readerName code, "")
            else
                match SemVer.tryParse version with
                | Ok parsed ->
                    Expect.equal parsed.Text version $"{name}: the text round-trips"
                    Valid
                | Error code -> Refused(Code.readerName code, "")

        Expect.equal actual expected name
    | CanonicalJson(name, value, expected, referenceTuf) ->
        let parse (text: string) (what: string) =
            match Syntax.parseJson (System.Text.Encoding.UTF8.GetBytes text) with
            | Ok parsed -> parsed
            | Error _ -> failtestf "%s: %s is not JSON" name what

        let parsed = parse value "the value"

        match Canonical.bytes parsed with
        | Ok bytes -> Expect.equal (System.Text.Encoding.UTF8.GetString bytes) expected $"{name}: the canonical text"
        | Error _ -> failtestf "%s: the value has no canonical form" name

        // The reference TUF encoder's text is not this contract's encoding; it must still encode the same value, as
        // parsed values (strings compared after NFC, as both encoders write them).
        let normalized (value: Value) =
            match Canonical.bytes value with
            | Ok bytes -> bytes
            | Error _ -> failtestf "%s: no canonical form" name

        Expect.sequenceEqual (normalized (parse referenceTuf "referenceTuf")) (normalized parsed) $"{name}: referenceTuf encodes the same value"
    | CliRequirement(name, requirement, cliVersion, expected) ->
        let parsed = CliRequirement.tryParse requirement

        match expected, cliVersion with
        | CliRead outcome, None ->
            let actual = if parsed.IsSome then Valid else Refused("invalid_value", "")
            Expect.equal actual outcome name
        | CliMet met, Some cli ->
            let comparators =
                match parsed with
                | Some comparators -> comparators
                | None -> failtestf "%s: %s is not in the grammar" name requirement

            let installed =
                match SemVer.tryParse cli with
                | Ok version -> version
                | Error _ -> failtestf "%s: %s is not a version" name cli

            Expect.equal (Comparator.allMet comparators installed) met name
        | _ -> failtestf "%s: a cliVersion goes with an expected met, and only then" name

/// The binding kinds of the pinned corpus. A corpus may add kinds this suite runs, but it may not drop one of these.
let pinnedKinds =
    set
        [ "lock-request"
          "lock-platform"
          "receipt-lock"
          "resolve"
          "plan-canonical"
          "lock-request-bytes"
          "plan-lock"
          "discover"
          "mep-version"
          "nfc"
          "setup-platforms"
          "prerequisite-versions"
          "version" ]

[<Tests>]
let tests =
    let cases = bindings ()

    testList "Unsigned binding cases" [
        testCase "the manifest lists binding cases of every pinned kind" (fun () ->
            // A kind this suite does not run is refused while the manifest is read; here no pinned kind is missing.
            let kinds = cases |> List.map _.Kind |> set
            Expect.isEmpty (Set.difference pinnedKinds kinds) "every pinned kind has cases")

        testList "cases" [ for case in cases -> testCase case.Name (fun () -> run case) ]
    ]
    |> testLabel "PlatformSpecific"
