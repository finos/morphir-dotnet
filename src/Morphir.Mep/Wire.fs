// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text.RegularExpressions
open Morphir.Myriad.SystemTextJson

/// A refused protocol version, before it can enter a protocol message.
[<RequireQualifiedAccess; JsonIgnoreType>]
type ParseFailure =
    | InvalidProtocolVersion

    /// Stable diagnostic text, without reflection.
    override _.ToString() =
        "Expected a full SemVer protocol version or the frozen legacy version 0.1."

/// A full SemVer version or the frozen legacy spelling. Negotiation accepts only the pinned draft.
[<JsonParsedValue(TextMember = "Value"); CustomEquality; CustomComparison>]
type ProtocolVersion =
    private
    | ProtocolVersion of string

    /// The original wire spelling, used for exact draft negotiation.
    member this.Value = let (ProtocolVersion value) = this in value

    /// Parses untrusted protocol metadata. Only the frozen legacy spelling 0.1 is a major.minor version.
    static member Parse(text: string | null) : Result<ProtocolVersion, ParseFailure> =
        match text with
        | null -> Error ParseFailure.InvalidProtocolVersion
        | "0.1" -> Ok(ProtocolVersion "0.1")
        | value ->
            let number = "(0|[1-9][0-9]*)"
            let identifier = "(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"

            let pattern =
                $"\\A{number}\\.{number}\\.{number}(-{identifier}(\\.{identifier})*)?(\\+[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?\\z"

            if
                Regex.IsMatch(
                    value,
                    pattern,
                    RegexOptions.CultureInvariant ||| RegexOptions.NonBacktracking,
                    TimeSpan.FromSeconds 1.
                )
            then
                Ok(ProtocolVersion value)
            else
                Error ParseFailure.InvalidProtocolVersion

    /// Parses untrusted text for C# callers, with flow-aware result and failure outputs.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<ProtocolVersion>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) =
        match ProtocolVersion.Parse text with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Builds a version from a source literal and throws if it is invalid.
    static member Create(literal: string) =
        match ProtocolVersion.Parse literal with
        | Ok value -> value
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// Exact wire spelling.
    override this.ToString() = this.Value

    /// Protocol identities compare ordinally.
    override this.Equals(other: obj | null) =
        match other with
        | :? ProtocolVersion as value -> String.Equals(this.Value, value.Value, StringComparison.Ordinal)
        | _ -> false

    /// Ordinal identity hash.
    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? ProtocolVersion as value -> String.CompareOrdinal(this.Value, value.Value)
            | _ -> invalidArg (nameof other) "Not a protocol version."

/// The complete describe params object, including when it has only this one member.
type DescribeParams =
    { ProtocolVersions: IReadOnlyList<ProtocolVersion> }

    /// Stable text for diagnostics.
    override this.ToString() =
        $"Describe ({this.ProtocolVersions.Count} protocol versions)"
