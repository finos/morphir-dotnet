// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open System
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4

/// <summary>Why a text is not an absolute IRI for a rule.</summary>
[<RequireQualifiedAccess>]
type RuleIdFailure =
    | Empty
    | NotAbsolute of text: string

    override this.ToString() =
        match this with
        | Empty -> "A rule id is empty."
        | NotAbsolute text -> $"The rule id '{text}' is not an absolute IRI: it has no scheme."

/// <summary>The id of a rule or of an upgrade step: an absolute IRI, compared as a plain string.</summary>
[<CustomEquality; NoComparison>]
type RuleId =
    private
    | RuleId of string

    /// <summary>The IRI text.</summary>
    member this.Value = let (RuleId text) = this in text

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? RuleId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Value

    /// <summary>Parses an absolute IRI: a scheme of letters, digits, plus, minus and dot, a colon, and a rest.</summary>
    static member Parse(text: string) : Result<RuleId, RuleIdFailure> =
        if String.IsNullOrEmpty text then
            Error RuleIdFailure.Empty
        else
            let colon = text.IndexOf ':'

            let validScheme =
                colon > 0
                && colon < text.Length - 1
                && Char.IsLetter text[0]
                && text.Substring(0, colon)
                   |> Seq.forall (fun c -> Char.IsLetterOrDigit c || c = '+' || c = '-' || c = '.')

            if validScheme then Ok(RuleId text) else Error(RuleIdFailure.NotAbsolute text)

    /// <summary>Builds an id from a literal in source code. Throws <c>ArgumentException</c> on invalid text.</summary>
    static member Create(literal: string) : RuleId =
        match RuleId.Parse literal with
        | Ok id -> id
        | Error failure -> invalidArg (nameof literal) (string failure)

/// <summary>
/// One application of a rule or of an upgrade step: the node that it matched, in the input model of the pipeline, and
/// the node that replaced it, in the output. A node that no record names keeps its path, and only the format of its
/// address changes.
/// </summary>
type RewriteApplication = {
    /// <summary>The rule or the upgrade step.</summary>
    Rule: RuleId
    /// <summary>The address in the input model of the pipeline, pinned to the snapshot of that input.</summary>
    Before: NodeAddress
    /// <summary>The address in the output model.</summary>
    After: NodeAddress
    /// <summary>The source location of the node that was matched, when it has one.</summary>
    Origin: SourceLocation option
} with
    /// <summary>For C# callers: the source location of the node that was matched, or null.</summary>
    member this.OriginOrNull: SourceLocation | null = Option.toObj this.Origin

    override this.ToString() = $"{this.Rule}: {NodeAddress.toUri this.Before} to {NodeAddress.toUri this.After}"

/// <summary>The snapshots of a model: the SHA-256 digest of its canonical bytes, which pins an address.</summary>
[<RequireQualifiedAccess>]
module Snapshot =

    /// <summary>The digest of a current-IR model, from its V4 JSON document, as the writer of the models gives it.</summary>
    let ofDistribution (distribution: Distribution) : Result<Sha256Digest, string> =
        match Morphir.Json.V4.V4JsonWriter.writeDistribution false distribution with
        | Ok bytes -> Ok(Sha256Digest.OfBytes bytes)
        | Error error -> Error(string error)
