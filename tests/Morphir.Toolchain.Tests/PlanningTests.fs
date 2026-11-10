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

let planned request platform =
    match planFor request platform everySize with
    | Ok(digested, lock) -> digested, lock
    | Error failure -> failtestf "no plan: %A" failure

[<Tests>]
let tests =
    testList "Planning" [
        testCase "the approved request plans the Native AOT host, the process frontend and the built-in generator" (fun () ->
            let digested, _ = planned "request/approved.toml" Platform.WinArm64

            match digested.Plan.Components with
            | [ PlannedComponent.Host(implementation, deployment, host)
                PlannedComponent.Provider(Operation.Compile _, frontendId, Delivery.Process, frontend)
                PlannedComponent.Provider(Operation.Generate _, generatorId, Delivery.BuiltIn, generator) ] ->
                Expect.equal (implementation.Text, deployment.Text) ("dotnet", "native-aot") "the host offering"

                Expect.equal
                    host.Source
                    (match Sha256.tryParse "29d3b2d81343ed1dc3c276b57b94602609d645670dc05afc81ea68c4d5c5f088", TargetPath.tryParse "artifacts/morphir-dotnet-native-aot/1.2.3/win-arm64.zip" with
                     | Ok sha, Ok target -> ComponentSource.RepositoryTarget(repository, target, sha)
                     | _ -> failwith "unreachable")
                    "the win-arm64 host archive from the recipe's repository"

                Expect.equal host.Probe Probe.None "the host is not probed by planning"
                Expect.equal frontendId.Text "org.finos.morphir.fsharp-frontend" "the frontend"
                Expect.equal frontend.Probe Probe.ProcessWithUserRights "a process probe runs with the user's rights"

                match frontend.Source with
                | ComponentSource.RecipeDigest(_, target, _) -> Expect.equal target.Text "extensions/fsharp-frontend/0.6.0/win-arm64.zip" "the frontend archive"
                | other -> failtestf "the frontend source is %A" other

                Expect.equal generatorId.Text "morphir.fsharp" "the built-in generator"
                Expect.equal generator.Source ComponentSource.BuiltIn "nothing is downloaded for a built-in"
                Expect.equal (generator.DownloadBytes, generator.InstalledBytes) (0UL, 0UL) "a built-in has no bytes"
                Expect.equal generator.Probe Probe.InHost "a built-in is checked in the host"
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
            | PlannedComponent.Provider(Operation.Generate _, id, Delivery.Process, backend) ->
                Expect.equal id.Text "morphir-fsharp-backend" "the external backend"
                Expect.equal backend.Probe Probe.ProcessWithUserRights "a process backend probe"
            | other -> failtestf "the generator is %A" other

            Expect.equal (lock.Platforms |> List.map fst) [ Platform.WinX64; Platform.WinArm64; Platform.OsxArm64 ] "the request's platforms and this one")

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
                Expect.equal (failure.Abi |> Option.map _.Text) (Some "extism-kernel-v1") "the missing ABI"

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
