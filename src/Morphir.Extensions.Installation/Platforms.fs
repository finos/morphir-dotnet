// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.Toolchain

/// Maps an existing toolchain platform to descriptor and v1 index spellings. No platform is inferred from a filename.
[<Sealed>]
type InstallationPlatform private (platform: Platform) =
    static let all =
        Array.AsReadOnly [| for platform, _ in Platform.all -> InstallationPlatform platform |]

    member private _.Mapping =
        match platform with
        | Platform.WinX64 -> "x86_64-pc-windows-msvc", Some { Os = "windows"; Arch = "x86_64" }
        | Platform.WinArm64 -> "aarch64-pc-windows-msvc", Some { Os = "windows"; Arch = "aarch64" }
        | Platform.OsxX64 -> "x86_64-apple-darwin", Some { Os = "macos"; Arch = "x86_64" }
        | Platform.OsxArm64 -> "aarch64-apple-darwin", Some { Os = "macos"; Arch = "aarch64" }
        | Platform.LinuxX64 -> "x86_64-unknown-linux-gnu", Some { Os = "linux"; Arch = "x86_64" }
        | Platform.LinuxArm64 ->
            "aarch64-unknown-linux-gnu", Some { Os = "linux"; Arch = "aarch64" }
        | Platform.LinuxMuslX64 -> "x86_64-unknown-linux-musl", None
        | Platform.LinuxMuslArm64 -> "aarch64-unknown-linux-musl", None

    /// The existing toolchain RID spelling.
    member _.RuntimeIdentifier = Platform.name platform
    /// The descriptor target triple, including musl where applicable.
    member this.DescriptorTriple = fst this.Mapping
    /// Stable text without reflection.
    override this.ToString() = this.RuntimeIdentifier

    /// Gets the v1 index platform. V1 cannot distinguish musl and therefore cannot select a musl process artifact.
    member this.GetIndexPlatform() : Result<ToolPlatform, InstallationFailure> =
        match snd this.Mapping with
        | Some value -> Ok value
        | None ->
            Parsing.refuse
                "extension.no_matching_artifact"
                $"No v1 index platform for '{this.RuntimeIdentifier}'."

    /// Gets the v1 index platform for C# callers, without an F# option.
    member this.TryGetIndexPlatform
        (
            [<Out; MaybeNullWhen(false)>] value: byref<ToolPlatform>,
            [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>
        ) =
        Parsing.complete (this.GetIndexPlatform()) &value &failure

    /// All eight toolchain platforms, in the toolchain contract's order. The collection cannot be changed.
    static member All: IReadOnlyList<InstallationPlatform> = all

    /// Parses the toolchain RID spelling for F# callers.
    static member Parse(text: string | null) : Result<InstallationPlatform, InstallationFailure> =
        match
            all
            |> Seq.tryFind (fun row ->
                String.Equals(row.RuntimeIdentifier, text, StringComparison.Ordinal)
            )
        with
        | Some row -> Ok row
        | None ->
            Parsing.refuse "extension.no_matching_artifact" $"Unknown runtime identifier '{text}'."

    /// Parses a RID for C# callers.
    static member TryParse
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] value: byref<InstallationPlatform>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>)
        =
        Parsing.complete (InstallationPlatform.Parse text) &value &failure

    /// Builds a platform from a source literal; throws when it is not a toolchain RID.
    static member Create(literal: string) =
        match InstallationPlatform.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// Looks up the descriptor triple exactly, without conversion of aliases.
    static member FromDescriptorTriple
        (text: string | null)
        : Result<InstallationPlatform, InstallationFailure> =
        match
            all
            |> Seq.tryFind (fun row ->
                String.Equals(row.DescriptorTriple, text, StringComparison.Ordinal)
            )
        with
        | Some row -> Ok row
        | None ->
            Parsing.refuse "extension.no_matching_artifact" $"Unknown descriptor platform '{text}'."

    /// Looks up the v1 index spelling exactly. Linux rows always mean glibc.
    static member FromIndexPlatform
        (os: string | null, arch: string | null)
        : Result<InstallationPlatform, InstallationFailure> =
        match
            all
            |> Seq.tryFind (fun row ->
                snd row.Mapping
                |> Option.exists (fun index ->
                    String.Equals(index.Os, os, StringComparison.Ordinal)
                    && String.Equals(index.Arch, arch, StringComparison.Ordinal)
                )
            )
        with
        | Some row -> Ok row
        | None ->
            Parsing.refuse
                "extension.no_matching_artifact"
                $"Unknown v1 index platform '{os}/{arch}'."

// Activation follows the executable process architecture, including x64 emulation.
// Keep Toolchain.Platform.current's historical OS-architecture behavior unchanged.
module internal ActivationPlatforms =
    let forProcess windows macos linux architecture (runtimeIdentifier: string) =
        let suffix =
            match architecture with
            | Architecture.X64 -> "x64"
            | Architecture.Arm64 -> "arm64"
            | _ -> "unsupported"

        let prefix =
            if windows then
                "win-"
            elif macos then
                "osx-"
            elif
                linux
                && runtimeIdentifier.StartsWith("linux-musl", StringComparison.Ordinal)
            then
                "linux-musl-"
            elif linux then
                "linux-"
            else
                "unsupported-"

        InstallationPlatform.Parse(
            prefix
            + suffix
        )

    let current () =
        forProcess
            (OperatingSystem.IsWindows())
            (OperatingSystem.IsMacOS())
            (OperatingSystem.IsLinux())
            RuntimeInformation.ProcessArchitecture
            RuntimeInformation.RuntimeIdentifier

    let ensure descriptorTriple =
        current ()
        |> Result.bind (fun expected ->
            if descriptorTriple = Some expected.DescriptorTriple then
                Ok()
            else
                Parsing.refuse
                    "extension.no_matching_artifact"
                    $"Activation requires {expected.DescriptorTriple}; installed artifact targets a different process platform."
        )
