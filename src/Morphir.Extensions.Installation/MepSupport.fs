// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Collections.Generic
open Morphir.Mep
open Semver

/// Whether an offered set can run, or why no exact implemented version matches.
[<RequireQualifiedAccess>]
type MepAvailability =
    internal
    | Runnable of ProtocolVersion
    | NotImplemented of reason: string
    | NotSupported of reason: string

    /// Stable status for reports. Use Match when C# must branch.
    member this.Status =
        match this with
        | Runnable _ -> "runnable"
        | NotImplemented _ -> "not-implemented"
        | NotSupported _ -> "not-supported"

    /// C# callers handle every case through delegates, without inspecting union tags.
    member this.Match<'R>
        (
            runnable: Func<ProtocolVersion, 'R>,
            notImplemented: Func<string, 'R>,
            notSupported: Func<string, 'R>
        ) =
        match this with
        | Runnable version -> runnable.Invoke version
        | NotImplemented reason -> notImplemented.Invoke reason
        | NotSupported reason -> notSupported.Invoke reason

    /// Stable text without reflection; unavailable results retain the offered versions.
    override this.ToString() =
        match this with
        | Runnable version -> $"Runnable over MEP {version.Value}."
        | NotImplemented reason
        | NotSupported reason -> reason

/// Supported MEP policy and the exact implementations that the actual host lifecycle supplies.
[<AbstractClass; Sealed>]
type MepSupport private () =
    static let supported = SemVersionRange.ParseNpm(">=0.2.0-0 <0.3.0")

    /// The supported line uses npm's default prerelease rule. It does not make different drafts interchangeable.
    static member SupportedLine = ">=0.2.0-0 <0.3.0"

    /// The exact versions implemented by Morphir.Mep, returned as a read-only copy.
    static member ImplementedVersions: IReadOnlyList<ProtocolVersion> =
        Array.AsReadOnly(
            Lifecycle.protocolVersions
            |> Seq.toArray
        )

    /// True when a parsed MEP version belongs to the supported line. Legacy 0.1 has no SemVer spelling here.
    static member IsSupported(version: ProtocolVersion) =
        match Morphir.Toolchain.SemVer.tryParse version.Value with
        | Ok parsed -> supported.Contains parsed.SemVersion
        | Error _ -> false

    /// Selects only an exact implementation identity; distinguishes future versions on the line from unsupported ones.
    static member Evaluate(offered: IReadOnlyList<ProtocolVersion>) : MepAvailability =
        let offered =
            offered
            |> Seq.toArray

        match
            Lifecycle.protocolVersions
            |> Seq.tryFind (fun version ->
                offered
                |> Array.contains version
            )
        with
        | Some version -> MepAvailability.Runnable version
        | None ->
            let supported, unsupported = Array.partition MepSupport.IsSupported offered

            let describe versions =
                versions
                |> Array.map (fun (version: ProtocolVersion) -> version.Value)
                |> String.concat ", "

            let unsupportedReason =
                $"MEP [{describe unsupported}] is not supported; this host supports {MepSupport.SupportedLine}."

            if supported.Length > 0 then
                let pending = $"MEP [{describe supported}] is not implemented by this host yet."

                MepAvailability.NotImplemented(
                    if unsupported.Length = 0 then
                        pending
                    else
                        pending
                        + " "
                        + unsupportedReason
                )
            else
                MepAvailability.NotSupported unsupportedReason
