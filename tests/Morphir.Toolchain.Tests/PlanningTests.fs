// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Plan-only setup over the synthetic F# recipe: what each component plans and that the plan digest is reproducible.
module Morphir.Toolchain.Tests.PlanningTests

open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.Tests.BindingCaseTests

let repository =
    match Sha256.tryParse (String.replicate 64 "b") with
    | Ok id -> id
    | Error _ -> failwith "unreachable"

let recipeTarget =
    match TargetPath.tryParse "toolchains/recipes/fsharp/1.0.0.json" with
    | Ok target -> target
    | Error _ -> failwith "unreachable"

/// Every artifact has the same synthetic sizes.
let everySize (_: TargetPath) =
    Some { DownloadBytes = 1000UL; InstalledBytes = 3000UL }

let planFor (request: string) (platform: Platform) sizes =
    Planning.plan
        { Recipe = recipeAt "recipe/fsharp-synthetic.json"
          RecipeRepository = repository
          RecipeTarget = recipeTarget
          Request = RequestDocument.read (Fixtures.bytes request) |> valid request
          Platform = platform
          Sizes = sizes
          MetadataRefresh = MetadataRefresh.Required }

/// <summary>A plan with the synthetic members masked: every SHA-256 (the fixture's are synthetic), the sizes, which the
/// recipe does not carry, and the lock's request digest. The fixture's lock is <c>lock/two-platforms-bound.json</c>,
/// which records another platform too, so only the plan's own platform's closure is compared.</summary>
let rec masked (value: Value) =
    match value with
    | Value.Object members ->
        Value.Object
            [ for name, item in members ->
                  match name, item with
                  | ("sha256" | "requestSha256"), _ -> name, Value.String "<sha256>"
                  | name, Value.String _ when name.EndsWith "Sha256" -> name, Value.String "<sha256>"
                  | ("downloadBytes" | "installedBytes"), _ -> name, Value.Number "0"
                  | _ -> name, masked item ]
    | Value.Array items -> Value.Array(items |> List.map masked)
    | other -> other

/// The plan with its lock reduced to the closure of the plan's own platform.
let ownClosure (plan: Value) =
    let platform =
        match plan with
        | Value.Object members -> members |> List.tryPick (function "platform", Value.String p -> Some p | _ -> None)
        | _ -> None

    match plan with
    | Value.Object members ->
        Value.Object
            [ for name, item in members ->
                  match name, item with
                  | "lock", Value.Object lock ->
                      name,
                      Value.Object
                          [ for key, member' in lock ->
                                match key, member' with
                                | "platforms", Value.Object closures ->
                                    key, Value.Object(closures |> List.filter (fun (p, _) -> Some p = platform))
                                | _ -> key, member' ]
                  | _ -> name, item ]
    | other -> other

let canonical value =
    match Canonical.bytes value with
    | Ok bytes -> System.Text.Encoding.UTF8.GetString bytes
    | Error _ -> failtest "not canonical"

/// The plan member of a result fixture, as a value.
let fixturePlan (relative: string) =
    match Syntax.parseJson (Fixtures.bytes relative) with
    | Ok(Value.Object members) -> members |> List.find (fst >> (=) "plan") |> snd
    | _ -> failtest "the fixture does not parse"

let prerequisiteNames (plan: Plan) =
    [ for planned in plan.Components ->
          match planned with
          | PlannedComponent.Host(_, _, p)
          | PlannedComponent.Provider(_, _, _, p) -> p.Prerequisites |> List.map _.Name.Text ]

let planned request platform =
    match planFor request platform everySize with
    | Ok(digested, lock) -> digested, lock
    | Error failure -> failtestf "no plan: %A" failure

[<Tests>]
let tests =
    testList "Planning" [
        testCase "the approved request on win-x64 plans exactly the approved setup-plan fixture, digests and sizes aside" (fun () ->
            let digested, _ = planned "request/approved.toml" Platform.WinX64

            Expect.equal
                (canonical (masked (ownClosure (ResultDocument.planValue digested.Plan))))
                (canonical (masked (ownClosure (fixturePlan "result/setup-plan.json"))))
                "every member the plan digest covers")

        testCase "a prerequisite limited to other platforms is not in the plan, and one for this platform is" (fun () ->
            let windows, _ = planned "request/approved.toml" Platform.WinX64
            Expect.equal (prerequisiteNames windows.Plan) [ []; []; [] ] "glibc applies only to linux-x64 and linux-arm64"

            let linux, _ = planned "request/approved.toml" Platform.LinuxX64
            Expect.equal (prerequisiteNames linux.Plan) [ [ "glibc" ]; [ "glibc" ]; [] ] "the host and the frontend need glibc on linux-x64")

        testCase "the approved request plans the Native AOT host, the process frontend and the built-in generator" (fun () ->
            let digested, _ = planned "request/approved.toml" Platform.WinArm64

            let probes = digested.Plan.Components |> List.map PlannedComponent.probe
            Expect.equal probes [ Probe.ProcessWithUserRights; Probe.ProcessWithUserRights; Probe.InHost ] "readiness runs the staged host and the process guest with the user's rights; a built-in runs in the host"

            match digested.Plan.Components with
            | [ PlannedComponent.Host(implementation, deployment, host)
                PlannedComponent.Provider(Operation.Compile _, frontendId, PlannedDelivery.Process, frontend)
                PlannedComponent.Provider(Operation.Generate _, generatorId, PlannedDelivery.BuiltIn, generator) ] ->
                Expect.equal (implementation.Text, deployment.Text) ("dotnet", "native-aot") "the host offering"

                Expect.equal
                    host.Source
                    (match Sha256.tryParse "29d3b2d81343ed1dc3c276b57b94602609d645670dc05afc81ea68c4d5c5f088", TargetPath.tryParse "artifacts/morphir-dotnet-native-aot/1.2.3/win-arm64.zip" with
                     | Ok sha, Ok target -> ComponentSource.RepositoryTarget(repository, target, sha)
                     | _ -> failwith "unreachable")
                    "the win-arm64 host archive from the recipe's repository"

                Expect.equal frontendId.Text "org.finos.morphir.fsharp-frontend" "the frontend"

                match frontend.Source with
                | ComponentSource.RecipeDigest(_, target, _) -> Expect.equal target.Text "extensions/fsharp-frontend/0.6.0/win-arm64.zip" "the frontend archive"
                | other -> failtestf "the frontend source is %A" other

                Expect.equal generatorId.Text "morphir.fsharp" "the built-in generator"
                Expect.equal generator.Source ComponentSource.BuiltIn "nothing is downloaded for a built-in"
                Expect.equal (generator.DownloadBytes, generator.InstalledBytes) (0UL, 0UL) "a built-in has no bytes"
            | components -> failtestf "unexpected components %A" components)

        testCase "the plan digest is the SHA-256 of the plan's canonical bytes and reads back" (fun () ->
            let digested, _ = planned "request/approved.toml" Platform.WinX64
            let again, _ = planned "request/approved.toml" Platform.WinX64
            Expect.equal digested.Sha256 again.Sha256 "planning is deterministic"
            Expect.equal digested.Sha256 (Sha256.ofBytes digested.Canonical) "planSha256"

            let result =
                match ResultDocument.create Command.Setup Outcome.Planned (Some digested) None [] with
                | Ok result -> result
                | Error reason -> failtest reason

            match ResultDocument.read (ResultDocument.write result) with
            | Ok read -> Expect.equal (read.Plan |> Option.map _.Sha256) (Some digested.Sha256) "the written result reads with the same digest"
            | Error refusal -> failtestf "the written result is refused: %s at %s" (Code.readerName refusal.Code) (DocumentPath.render refusal.Path))

        testCase "an explicit external backend replaces the built-in and the lock extends to the current platform" (fun () ->
            let digested, lock = planned "request/team-platforms-external-backend.toml" Platform.WinArm64

            match digested.Plan.Components |> List.last with
            | PlannedComponent.Provider(Operation.Generate _, id, PlannedDelivery.Process, _) as backend ->
                Expect.equal id.Text "morphir-fsharp-backend" "the external backend"
                Expect.equal (PlannedComponent.probe backend) Probe.ProcessWithUserRights "a process backend probe"
            | other -> failtestf "the generator is %A" other

            Expect.equal (lock.Platforms.Items |> List.map fst) [ Platform.WinX64; Platform.WinArm64; Platform.OsxArm64 ] "the request's platforms and this one")

        testCase "a missing WASM ABI is reported and never substituted" (fun () ->
            let recipe = recipeAt "recipe/wasm-abi-alternatives.json"
            let request = RequestDocument.read (Fixtures.bytes "request/wasm-example-default.toml") |> valid "request"

            match
                Planning.plan
                    { Recipe = recipe
                      RecipeRepository = repository
                      RecipeTarget = recipeTarget
                      Request = request
                      Platform = Platform.WinX64
                      Sizes = everySize
                      MetadataRefresh = MetadataRefresh.Required }
            with
            | Error(PlanFailure.Unresolved failure) ->
                Expect.equal failure.Code ArtifactCode.AbiUnavailable "abi_unavailable"

                match failure.Problem with
                | ArtifactProblem.AbiUnavailable abi -> Expect.equal abi.Text "extism-kernel-v1" "the missing ABI"
                | other -> failtestf "the problem is %A" other

                let diagnostic = Diagnostics.ofResolutionFailure failure

                match diagnostic.Detail with
                | DiagnosticDetail.UnsupportedArtifact(ArtifactProblem.AbiUnavailable abi, Component.Provider _, Platform.WinX64, _) ->
                    Expect.equal abi.Text "extism-kernel-v1" "the diagnostic names the ABI"
                | other -> failtestf "unexpected diagnostic %A" other
            | other -> failtestf "expected abi_unavailable, got %A" other)

        testCase "an unsupported platform is reported with the offering's other platforms as alternatives" (fun () ->
            match planFor "request/approved.toml" Platform.LinuxMuslX64 everySize with
            | Error(PlanFailure.Unresolved failure) ->
                Expect.equal (failure.Code, DocumentPath.render failure.Path) (ArtifactCode.UnsupportedArtifact, "platforms") "the platform"

                let alternatives = failure.Alternatives |> List.choose _.Platform
                Expect.contains alternatives Platform.LinuxX64 "the glibc closure is an alternative, never a substitute"
                Expect.isFalse (List.contains Platform.LinuxMuslX64 alternatives) "the requested platform is not an alternative"
            | other -> failtestf "expected unsupported_artifact, got %A" other)

        testCase "a downloaded artifact without a known size gives no plan" (fun () ->
            match planFor "request/approved.toml" Platform.WinX64 (fun _ -> None) with
            | Error(PlanFailure.SizeUnknown target) ->
                Expect.equal target.Text "artifacts/morphir-dotnet-native-aot/1.2.3/win-x64.zip" "the host archive"
            | other -> failtestf "expected SizeUnknown, got %A" other)
    ]
