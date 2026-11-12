module Morphir.Extensions.Tests.WasmDeliveryTests

open System
open Expecto
open Morphir.IR.Model
open Morphir.Extensions

let private version text =
    match SourceFormatVersion.parse text with
    | Ok parsed -> parsed
    | Error error -> failwith $"bad version {text}: {error}"

let private hostVersion = ReleaseVersion.Create "0.4.0"

let private builtIn (id: string) =
    ExtensionEntry.BuiltInBackend(
        ExtensionIdentity.Create(
            ExtensionId.Create id,
            ExtensionName.Create $"{id} generator",
            hostVersion,
            null
        ),
        {
            Targets = NonEmptySet.create (TargetId.Create "csharp") []
            IrVersions = NonEmptySet.create (version "4.0.0") []
        }
    )

/// An entry that a WASM module delivers. Nothing builds one from configuration yet (#565), so the tests build it.
let private wasm (id: string) =
    { builtIn id with
        Delivery = ExtensionDelivery.Wasm
    }

let private reasons = [
    WasmRuntimeUnavailableReason.PlatformUnsupported
    WasmRuntimeUnavailableReason.EngineMissing
    WasmRuntimeUnavailableReason.EngineLoadFailed
    WasmRuntimeUnavailableReason.WinX64TrapUnsupported
]

/// A development build of the managed entry point, which every built-in's deployments provide.
let private hostAs (command: MorphirHost) =
    HostIdentity.Create(
        command,
        hostVersion,
        NonEmptySet.create Deployment.SelfContained [
            Deployment.FrameworkDependent
            Deployment.DotnetTool
        ],
        BuildProfile.NoProfile,
        ExecutionMode.Jit
    )

let private hosts = [ hostAs MorphirHost.Native; hostAs MorphirHost.DotnetTool ]

[<Tests>]
let tests =
    testList "WASM delivery" [
        for reason in reasons do
            testCase $"runtime refusal {reason.WireValue} has a common unavailable reason"
            <| fun () ->
                let entry = wasm "acme.python"
                let host = hostAs MorphirHost.Native
                let runtime = WasmRuntimeAvailability.Unavailable reason
                Expect.isFalse (entry.IsAvailableIn(host, runtime)) "the runtime refuses the entry"
                let availability = entry.AvailabilityIn(host, runtime)

                let expected =
                    UnavailableReason.Create
                        $"The WASM runtime is unavailable: {reason.Description}."

                Expect.equal
                    availability
                    (ExtensionAvailability.Unavailable expected)
                    "one combined outcome"

                Expect.equal
                    (entry.ReasonOrNullIn(host, runtime))
                    expected
                    "the common query supplies the same reason"

                Expect.equal
                    availability.ReasonOrNull
                    expected
                    "listing reads the same reason from the outcome"

                Expect.sequenceEqual
                    entry.Deployments.Deployments
                    Deployment.All
                    "runtime failure preserves the declared offerings"

        test "available entries have no common reason" {
            for entry in [ builtIn "morphir.csharp"; wasm "acme.python" ] do
                for host in hosts do
                    let availability = entry.AvailabilityIn(host, WasmRuntimeAvailability.Available)
                    Expect.isTrue availability.CanRun "available"
                    Expect.isNone availability.Reason "no F# reason"
                    Expect.isNull availability.ReasonOrNull "no C# reason"

                    Expect.isNull
                        (entry.ReasonOrNullIn(host, WasmRuntimeAvailability.Available))
                        "query agrees"
        }

        test "static host and deployment refusals take precedence without probing" {
            let host = hostAs MorphirHost.Native

            let hostOnly =
                (wasm "acme.python").WithHosts(HostAvailability.OnlyIn MorphirHost.DotnetTool)

            let deploymentReason =
                UnavailableReason.Create "This delivery requires the native-aot offering."

            let deploymentOnly =
                (wasm "acme.python")
                    .WithDeployments(
                        EntryDeployments.Only(
                            NonEmptySet.create Deployment.NativeAot [],
                            deploymentReason
                        )
                    )

            let runtime =
                Func<WasmRuntimeAvailability>(fun () ->
                    failtest "a static refusal must not ask the runtime"
                )

            for entry in [ hostOnly; deploymentOnly ] do
                let availability = entry.AvailabilityIn(host, runtime)
                Expect.isFalse availability.CanRun "statically unavailable"

                Expect.equal
                    availability.ReasonOrNull
                    (entry.ReasonOrNullIn host)
                    "the static reason wins"

                Expect.equal
                    (entry.ReasonOrNullIn(host, runtime))
                    availability.ReasonOrNull
                    "the common query agrees"

                for reason in reasons do
                    Expect.equal
                        (entry.AvailabilityIn(host, WasmRuntimeAvailability.Unavailable reason))
                        availability
                        "runtime failure does not replace a static refusal"
        }

        test "a built-in combined outcome and common query do not ask the runtime" {
            let entry = builtIn "morphir.csharp"

            let runtime =
                Func<WasmRuntimeAvailability>(fun () ->
                    failtest "a built-in must not ask the runtime"
                )

            let host = hostAs MorphirHost.Native

            Expect.equal
                (entry.AvailabilityIn(host, runtime))
                ExtensionAvailability.Available
                "available"

            Expect.isNull (entry.ReasonOrNullIn(host, runtime)) "no reason"
        }

        test "combined outcomes share the caller's lazy runtime probe" {
            let asked = ref 0

            let runtime =
                lazy
                    (asked.Value <-
                        asked.Value
                        + 1

                     WasmRuntimeAvailability.Unavailable WasmRuntimeUnavailableReason.EngineMissing)

            let probe = Func<WasmRuntimeAvailability>(fun () -> runtime.Value)
            let host = hostAs MorphirHost.Native

            for entry in [ wasm "acme.python"; wasm "acme.avro" ] do
                let availability = entry.AvailabilityIn(host, probe)
                Expect.isFalse availability.CanRun "unavailable"

                Expect.equal
                    (entry.ReasonOrNullIn(host, probe))
                    availability.ReasonOrNull
                    "query agrees"

            Expect.equal asked.Value 1 "one shared engine probe"
        }

        test "C# can match the combined outcome without inspecting union cases" {
            let render (availability: ExtensionAvailability) =
                availability.Match(
                    Func<string>(fun () -> "yes"),
                    Func<UnavailableReason, string>(fun reason -> reason.Value)
                )

            Expect.equal (render ExtensionAvailability.Available) "yes" "available"
            let reason = UnavailableReason.Create "Use a process extension."

            Expect.equal
                (render (ExtensionAvailability.Unavailable reason))
                reason.Value
                "common unavailable reason"
        }

        test "a WASM delivery has the wire value wasm and built-ins keep built-in" {
            Expect.equal ExtensionDelivery.Wasm.WireValue "wasm" "wasm"
            Expect.equal (string ExtensionDelivery.Wasm) "wasm" "text"
            Expect.equal ExtensionDelivery.BuiltIn.WireValue "built-in" "built-in"
            Expect.equal (wasm "acme.python").Delivery.WireValue "wasm" "the entry's delivery"

            Expect.equal
                (builtIn "morphir.csharp").Delivery
                ExtensionDelivery.BuiltIn
                "a built-in backend"
        }

        test "Match calls the function of the delivery's case" {
            let name (delivery: ExtensionDelivery) =
                delivery.Match(
                    Func<string>(fun () -> "built-in case"),
                    Func<string>(fun () -> "wasm case")
                )

            Expect.equal (name ExtensionDelivery.BuiltIn) "built-in case" "built-in"
            Expect.equal (name ExtensionDelivery.Wasm) "wasm case" "wasm"
        }

        test "an unavailable runtime has the reasons of the runtime report" {
            Expect.equal
                (reasons
                 |> List.map (fun reason -> reason.WireValue))
                [
                    "platform_unsupported"
                    "engine_missing"
                    "engine_load_failed"
                    "win_x64_trap_unsupported"
                ]
                "wire values"

            Expect.equal (string WasmRuntimeUnavailableReason.EngineMissing) "engine_missing" "text"
        }

        test "the host reports the load reasons and the guarded Windows x64 reason" {
            // Requirement "Unavailable runtime fails by name" (finos/morphir 938f798): platform_unsupported,
            // engine_missing or engine_load_failed. The parent dropped disabled with the switch that would give it.
            let cases =
                Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(
                    typeof<WasmRuntimeUnavailableReason>
                )
                |> Array.map (fun case ->
                    nonNull (Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||]))
                    :?> WasmRuntimeUnavailableReason
                )
                |> Array.map (fun reason -> reason.WireValue)
                |> Set.ofArray

            Expect.equal
                cases
                (set [
                    "platform_unsupported"
                    "engine_missing"
                    "engine_load_failed"
                    "win_x64_trap_unsupported"
                ])
                "every case of the union is a reason the requirement lists, and every listed reason is a case"

            Expect.equal
                (reasons
                 |> List.map (fun reason -> reason.WireValue)
                 |> Set.ofList)
                cases
                "the test's list holds every reason"
        }

        test "a runtime availability says whether it is available and why not" {
            Expect.isTrue WasmRuntimeAvailability.Available.CanRunModules "available"

            let missing =
                WasmRuntimeAvailability.UnavailableBecause
                    WasmRuntimeUnavailableReason.EngineMissing

            Expect.isFalse missing.CanRunModules "unavailable"

            Expect.equal
                missing
                (WasmRuntimeAvailability.Unavailable WasmRuntimeUnavailableReason.EngineMissing)
                "factory"

            let describe (runtime: WasmRuntimeAvailability) =
                runtime.Match(
                    Func<string>(fun () -> "yes"),
                    Func<WasmRuntimeUnavailableReason, string>(fun reason ->
                        $"no: {reason.WireValue}"
                    )
                )

            Expect.equal (describe WasmRuntimeAvailability.Available) "yes" "available case"
            Expect.equal (describe missing) "no: engine_missing" "unavailable case with its reason"
            Expect.equal (string WasmRuntimeAvailability.Available) "available" "available text"
            Expect.equal (string missing) "unavailable (engine_missing)" "unavailable text"
        }

        test
            "host without the engine: a WASM entry for both hosts is unavailable and built-ins stay available" {
            let entry = wasm "acme.python"
            let csharp = builtIn "morphir.csharp"

            for host in hosts do
                for reason in reasons do
                    let runtime = WasmRuntimeAvailability.Unavailable reason

                    Expect.isFalse
                        (entry.IsAvailableIn(host, runtime))
                        $"wasm entry in {host.Host}, {reason}"

                    Expect.isTrue
                        (csharp.IsAvailableIn(host, runtime))
                        $"built-in entry in {host.Host}, {reason}"
        }

        test "host with the engine: a WASM entry for both hosts is available" {
            let entry = wasm "acme.python"

            for host in hosts do
                Expect.isTrue
                    (entry.IsAvailableIn(host, WasmRuntimeAvailability.Available))
                    $"wasm entry in {host.Host}"

                Expect.isTrue
                    ((builtIn "morphir.csharp")
                        .IsAvailableIn(host, WasmRuntimeAvailability.Available))
                    $"built-in in {host.Host}"
        }

        test "a WASM entry for the other host stays unavailable with the engine" {
            let entry =
                (wasm "acme.python").WithHosts(HostAvailability.OnlyIn MorphirHost.DotnetTool)

            Expect.isFalse
                (entry.IsAvailableIn(hostAs MorphirHost.Native, WasmRuntimeAvailability.Available))
                "native"

            Expect.isTrue
                (entry.IsAvailableIn(
                    hostAs MorphirHost.DotnetTool,
                    WasmRuntimeAvailability.Available
                ))
                "dotnet tool"
        }

        test "a WASM entry asks a lazy runtime, and a built-in entry never asks it" {
            let asked = ref 0

            let runtime =
                Func<WasmRuntimeAvailability>(fun () ->
                    asked.Value <-
                        asked.Value
                        + 1

                    WasmRuntimeAvailability.Available
                )

            for host in hosts do
                Expect.isTrue
                    ((builtIn "morphir.csharp").IsAvailableIn(host, runtime))
                    $"built-in in {host.Host}"

            Expect.equal asked.Value 0 "a built-in entry never asks the runtime"

            Expect.isTrue
                ((wasm "acme.python").IsAvailableIn(hostAs MorphirHost.Native, runtime))
                "wasm entry with the engine"

            Expect.equal asked.Value 1 "a wasm entry asks once"

            for reason in reasons do
                let unavailable =
                    Func<WasmRuntimeAvailability>(fun () ->
                        WasmRuntimeAvailability.Unavailable reason
                    )

                Expect.isFalse
                    ((wasm "acme.python").IsAvailableIn(hostAs MorphirHost.Native, unavailable))
                    $"wasm entry without the engine, {reason}"
        }

        test "a WASM entry for the other host does not ask the runtime" {
            let entry =
                (wasm "acme.python").WithHosts(HostAvailability.OnlyIn MorphirHost.DotnetTool)

            let runtime =
                Func<WasmRuntimeAvailability>(fun () -> failtest "the runtime was asked")

            Expect.isFalse (entry.IsAvailableIn(hostAs MorphirHost.Native, runtime)) "native"
        }

        test "each unavailable reason has a description for the runtime report" {
            Expect.equal
                (reasons
                 |> List.map (fun reason -> reason.Description))
                [
                    "the engine has no build for this platform"
                    "the engine's native library is not in this installation"
                    "the engine's native library is present but did not load"
                    "Windows x64 trap handling is unavailable with the required process protections (issue #609)"
                ]
                "descriptions"
        }

        test "a catalog keeps a WASM entry beside the built-ins" {
            let host = hostAs MorphirHost.Native

            match ExtensionCatalog.create host [ builtIn "morphir.csharp"; wasm "acme.python" ] with
            | Error error -> failtest $"refused: {error}"
            | Ok catalog ->
                Expect.equal
                    (catalog.Entries
                     |> Seq.map (fun entry -> entry.Delivery.WireValue)
                     |> List.ofSeq)
                    [ "wasm"; "built-in" ]
                    "both entries, by id"
        }

        test "WithDelivery gives C# the same entry with another delivery" {
            let entry = (builtIn "acme.python").WithDelivery ExtensionDelivery.Wasm
            Expect.equal entry (wasm "acme.python") "same entry, wasm delivery"
        }
    ]
