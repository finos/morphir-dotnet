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
    | Discover(name, files, boundary, start, request) ->
        let segments (path: string) = path.Split('/', System.StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
        let present = files |> List.map segments |> set
        let found = Discovery.request (segments boundary) (segments start) present.Contains
        Expect.equal (found |> Option.map (String.concat "/")) request name
    | MepVersion(name, version, expected) ->
        let actual =
            match ProtocolVersion.tryParse version with
            | Ok parsed ->
                Expect.equal (ProtocolVersion.text parsed) version $"{name}: the text round-trips"
                Valid
            | Error code -> Refused(Code.readerName code, "")

        Expect.equal actual expected name

[<Tests>]
let tests =
    let cases = bindings ()

    testList "Unsigned binding cases" [
        testCase "the manifest lists binding cases of every kind" (fun () ->
            let kinds = cases |> List.map _.Kind |> set

            Expect.equal
                kinds
                (set [ "lock-request"; "lock-platform"; "receipt-lock"; "resolve"; "plan-canonical"; "lock-request-bytes"; "plan-lock"; "discover"; "mep-version" ])
                "every kind")

        testList "cases" [ for case in cases -> testCase case.Name (fun () -> run case) ]
    ]
