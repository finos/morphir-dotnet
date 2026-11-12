// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.Extensions
open Morphir.Toolchain

/// A refusal before any installation effect. The CLI chooses its output format and exit code.
[<Sealed>]
type InstallationFailure internal (code: string, message: string) =
    /// Stable diagnostic code, independent of the output format.
    member _.Code = code
    /// Plain diagnostic text, with the refused value or rule.
    member _.Message = message
    /// Stable text without reflection.
    override _.ToString() = $"{code}: {message}"

module internal Parsing =
    let refuse code message =
        Error(InstallationFailure(code, message))

    let complete
        (parsed: Result<'T, InstallationFailure>)
        (value: byref<'T>)
        (failure: byref<InstallationFailure>)
        =
        match parsed with
        | Ok result ->
            value <- result
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            value <- Unchecked.defaultof<_>
            failure <- error
            false

/// Distribution boundary parsers over the existing extension, toolchain and MEP domain values.
[<AbstractClass; Sealed>]
type DistributionIdentifiers private () =
    /// Parses an external id. Built-ins use ExtensionId.Parse directly and may keep the reserved prefix.
    static member ParseExtensionId
        (text: string | null)
        : Result<Morphir.Extensions.ExtensionId, InstallationFailure> =
        match Morphir.Extensions.ExtensionId.Parse text with
        | Error error -> Parsing.refuse "extension.id_not_portable" (error.ToString())
        | Ok id when id.ReservedForBuiltIns ->
            Parsing.refuse "extension.reserved_id" $"'{id.Value}' is reserved for built-ins."
        | Ok id -> Ok id

    /// Parses an external id for C# callers, with flow-aware outputs.
    static member TryParseExtensionId
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] value: byref<Morphir.Extensions.ExtensionId>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>)
        =
        Parsing.complete (DistributionIdentifiers.ParseExtensionId text) &value &failure

    /// Parses a declared relative path with the toolchain's shared portable path rules and length bounds.
    static member ParseDeclaredPath(text: string | null) : Result<TargetPath, InstallationFailure> =
        match text with
        | null ->
            Parsing.refuse "extension.path_not_portable" "A declared relative path is required."
        | value ->
            TargetPath.tryParse value
            |> Result.mapError (fun _ ->
                InstallationFailure(
                    "extension.path_not_portable",
                    $"Not a portable declared relative path: '{value}'."
                )
            )

    /// Parses a declared relative path for C# callers.
    static member TryParseDeclaredPath
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] value: byref<TargetPath>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>)
        =
        Parsing.complete (DistributionIdentifiers.ParseDeclaredPath text) &value &failure

    /// Parses a single portable filename. The returned TargetPath has no directory component.
    static member ParseArtifactFileName
        (text: string | null)
        : Result<TargetPath, InstallationFailure> =
        match DistributionIdentifiers.ParseDeclaredPath text with
        | Ok path when not (path.Text.Contains '/') -> Ok path
        | Ok _ ->
            Parsing.refuse
                "extension.path_not_portable"
                "An artifact filename must be one path segment."
        | Error error -> Error error

    /// Parses a single portable filename for C# callers.
    static member TryParseArtifactFileName
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] value: byref<TargetPath>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>)
        =
        Parsing.complete (DistributionIdentifiers.ParseArtifactFileName text) &value &failure

    /// Parses a digest using the MEP SHA-256 domain value. A checksum does not authenticate a publisher.
    static member ParseDigest
        (text: string | null)
        : Result<Morphir.Mep.Sha256Digest, InstallationFailure> =
        Morphir.Mep.Sha256Digest.Parse text
        |> Result.mapError (fun error ->
            InstallationFailure("extension.invalid_digest", error.ToString())
        )

    /// Parses a digest for C# callers.
    static member TryParseDigest
        (text: string | null,
                        [<Out; MaybeNullWhen(false)>] value: byref<Morphir.Mep.Sha256Digest>,
                        [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>)
        =
        Parsing.complete (DistributionIdentifiers.ParseDigest text) &value &failure

/// One parsed channel or exact release version. Its private representation prevents conflicting selections.
[<RequireQualifiedAccess; CustomEquality; NoComparison>]
type DistributionSelection =
    private
    | Channel of Morphir.Toolchain.Channel
    | Version of SemVer

    /// Canonical requested channel or exact version, including build metadata.
    member this.Text =
        match this with
        | Channel value -> Morphir.Toolchain.Channel.text value
        | Version value -> value.Text

    /// Stable text without reflection.
    override this.ToString() = this.Text

    /// Selections compare by canonical identity, including version build metadata.
    override this.Equals(other: obj | null) =
        match other with
        | :? DistributionSelection as value ->
            String.Equals(this.Text, value.Text, StringComparison.Ordinal)
        | _ -> false

    /// Ordinal identity hash.
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Text

    /// Branches for C# callers without exposing an F# union or function type.
    member this.Match<'R>(channel: Func<string, 'R>, version: Func<SemVer, 'R>) =
        match this with
        | Channel value -> channel.Invoke(Morphir.Toolchain.Channel.text value)
        | Version value -> version.Invoke value

    /// Parses mutually exclusive options, defaulting to stable when both are absent. Null denotes absence.
    static member Parse
        (channel: string | null, version: string | null)
        : Result<DistributionSelection, InstallationFailure> =
        let refused () =
            Parsing.refuse
                "extension.invalid_selection"
                "Choose one valid channel or one canonical SemVer version."

        match channel, version with
        | null, null -> Ok(Channel Morphir.Toolchain.Channel.Stable)
        | NonNull text, null ->
            match Morphir.Toolchain.Channel.tryParse text with
            | Ok value -> Ok(Channel value)
            | Error _ -> refused ()
        | null, NonNull text ->
            match SemVer.tryParse text with
            | Ok value -> Ok(Version value)
            | Error _ -> refused ()
        | _ -> refused ()

    /// Parses options for C# callers, with flow-aware outputs.
    static member TryParse
        (channel: string | null, version: string | null,
                                                   [<Out; MaybeNullWhen(false)>] value:
                                                       byref<DistributionSelection>,
                                                   [<Out; MaybeNullWhen(true)>] failure:
                                                       byref<InstallationFailure>)
        =
        Parsing.complete (DistributionSelection.Parse(channel, version)) &value &failure

    /// Builds a channel selection from a source literal; throws when invalid.
    static member CreateChannel(literal: string) =
        match DistributionSelection.Parse(literal, null) with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// Builds an exact-version selection from a source literal; throws when invalid.
    static member CreateVersion(literal: string) =
        match DistributionSelection.Parse(null, literal) with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())
