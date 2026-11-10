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
]

let private hosts = [ MorphirHost.Native; MorphirHost.DotnetTool ]

[<Tests>]
let tests =
    testList "WASM delivery" [
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
                [ "platform_unsupported"; "engine_missing"; "engine_load_failed" ]
                "wire values"

            Expect.equal (string WasmRuntimeUnavailableReason.EngineMissing) "engine_missing" "text"
        }

        test "the host can report exactly the three reasons the requirement lists" {
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
                (set [ "platform_unsupported"; "engine_missing"; "engine_load_failed" ])
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
                        $"wasm entry in {host}, {reason}"

                    Expect.isTrue
                        (csharp.IsAvailableIn(host, runtime))
                        $"built-in entry in {host}, {reason}"
        }

        test "host with the engine: a WASM entry for both hosts is available" {
            let entry = wasm "acme.python"

            for host in hosts do
                Expect.isTrue
                    (entry.IsAvailableIn(host, WasmRuntimeAvailability.Available))
                    $"wasm entry in {host}"

                Expect.isTrue
                    ((builtIn "morphir.csharp")
                        .IsAvailableIn(host, WasmRuntimeAvailability.Available))
                    $"built-in in {host}"
        }

        test "a WASM entry for the other host stays unavailable with the engine" {
            let entry =
                (wasm "acme.python").WithHosts(HostAvailability.OnlyIn MorphirHost.DotnetTool)

            Expect.isFalse
                (entry.IsAvailableIn(MorphirHost.Native, WasmRuntimeAvailability.Available))
                "native"

            Expect.isTrue
                (entry.IsAvailableIn(MorphirHost.DotnetTool, WasmRuntimeAvailability.Available))
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
                    $"built-in in {host}"

            Expect.equal asked.Value 0 "a built-in entry never asks the runtime"

            Expect.isTrue
                ((wasm "acme.python").IsAvailableIn(MorphirHost.Native, runtime))
                "wasm entry with the engine"

            Expect.equal asked.Value 1 "a wasm entry asks once"

            for reason in reasons do
                let unavailable =
                    Func<WasmRuntimeAvailability>(fun () ->
                        WasmRuntimeAvailability.Unavailable reason
                    )

                Expect.isFalse
                    ((wasm "acme.python").IsAvailableIn(MorphirHost.Native, unavailable))
                    $"wasm entry without the engine, {reason}"
        }

        test "a WASM entry for the other host does not ask the runtime" {
            let entry =
                (wasm "acme.python").WithHosts(HostAvailability.OnlyIn MorphirHost.DotnetTool)

            let runtime =
                Func<WasmRuntimeAvailability>(fun () -> failtest "the runtime was asked")

            Expect.isFalse (entry.IsAvailableIn(MorphirHost.Native, runtime)) "native"
        }

        test "each unavailable reason has a description for the runtime report" {
            Expect.equal
                (reasons
                 |> List.map (fun reason -> reason.Description))
                [
                    "the engine has no build for this platform"
                    "the engine's native library is not in this installation"
                    "the engine's native library is present but did not load"
                ]
                "descriptions"
        }

        test "a catalog keeps a WASM entry beside the built-ins" {
            let host = {
                Host = MorphirHost.Native
                Version = hostVersion
            }

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
