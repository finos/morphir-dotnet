// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Backend

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open Morphir.IR.Model
open System
open System.Globalization
open System.Text

/// <summary>
/// The options of the backend: the explicit numeric language version, which the installed compiler never selects, and two
/// carriers of the handling options (OPT-1). The carrier in the model is between them: the command line wins over it, and
/// it wins over the configured keys. The profile is a handling option (<c>HandlingOptions.Profile</c>).
/// </summary>
type GenerationOptions =
    {
        LanguageVersion: int
        /// <summary>The handling options of the command line (<c>--option</c>). They win over every other carrier.</summary>
        CommandLine: Morphir.Lang.CSharp.HandlingOptions
        /// <summary>The handling options of <c>[codegen.csharp]</c> in <c>morphir.toml</c>. The carrier in the model wins over them.</summary>
        Configured: Morphir.Lang.CSharp.HandlingOptions
    }

    static member Default = {
        LanguageVersion = 15
        CommandLine = Morphir.Lang.CSharp.HandlingOptions.empty
        Configured = Morphir.Lang.CSharp.HandlingOptions.empty
    }

    /// <summary>The language version and the two carriers, with no reflection.</summary>
    override this.ToString() =
        $"C# {this.LanguageVersion}, command line [{this.CommandLine}], configured [{this.Configured}]"

/// The whole-model outcome. Failure and cancellation never contain sources.
[<RequireQualifiedAccess>]
type GenerationStatus =
    | Success
    | Failure
    | Cancelled

/// A stable refusal rule and its structural model path.
type GenerationDiagnostic = {
    Rule: string
    Location: string
    Message: string
}

/// A dotted literal C# namespace in NFC. Source escape sequences are not interpreted.
[<Sealed>]
type NamespaceOverride private (value: string) =
    member _.Value = value

    static member TryCreate(value: string) : Result<NamespaceOverride, GenerationDiagnostic> =
        let reserved =
            Set.ofArray (
                "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while __arglist __makeref __reftype __refvalue"
                    .Split(' ')
            )

        let letter c =
            match Char.GetUnicodeCategory c with
            | UnicodeCategory.UppercaseLetter
            | UnicodeCategory.LowercaseLetter
            | UnicodeCategory.TitlecaseLetter
            | UnicodeCategory.ModifierLetter
            | UnicodeCategory.OtherLetter
            | UnicodeCategory.LetterNumber -> true
            | _ -> false

        let part c =
            letter c
            || match Char.GetUnicodeCategory c with
               | UnicodeCategory.DecimalDigitNumber
               | UnicodeCategory.ConnectorPunctuation
               | UnicodeCategory.NonSpacingMark
               | UnicodeCategory.SpacingCombiningMark
               | UnicodeCategory.Format -> true
               | _ -> false

        let segment (text: string) =
            let escaped = text.StartsWith('@')
            let identifier = if escaped then text.Substring(1) else text

            identifier.Length > 0
            && (identifier[0] = '_'
                || letter identifier[0])
            && identifier
               |> Seq.forall part
            && (escaped
                || not (Set.contains identifier reserved))

        if
            not (String.IsNullOrEmpty value)
            && not (
                value
                |> Seq.exists Char.IsSurrogate
            )
            && value.IsNormalized(NormalizationForm.FormC)
            && value.Split('.')
               |> Array.forall segment
        then
            Ok(NamespaceOverride value)
        else
            Error
                {
                    Rule = "invalid-namespace"
                    Location = "$options/namespace"
                    Message =
                        "Use a dotted literal C# namespace in NFC with valid identifiers; escape reserved words with @. Source Unicode escapes and surrogate characters are not admitted."
                }

exception internal Refusal of GenerationDiagnostic

module internal Admission =
    let fail rule location message =
        raise (
            Refusal
                {
                    Rule = rule
                    Location = location
                    Message = message
                }
        )

/// <summary>
/// The options of the backend for one value, after the precedence order of OPT-1: the command line, the carrier on the
/// output type of the value, the configured keys, the profile default. The plan of a traversal and of a tail call reads
/// them (tasks 6.4 and 6.5).
/// </summary>
module internal BackendHandling =
    /// <summary>The effective options of a value with the given output type.</summary>
    let value (options: GenerationOptions) (output: Morphir.IR.V4.Type) : Morphir.Lang.CSharp.EffectiveHandling =
        Morphir.Lang.CSharp.HandlingOptions.resolve [ options.CommandLine; Morphir.Lang.CSharp.HandlingCarrier.read output; options.Configured ]

    /// <summary>
    /// The handling options of a key and word list, as <c>--option</c> and <c>[codegen.csharp]</c> give them. An unknown
    /// key or word gives the diagnostic <c>invalid-backend-option</c>.
    /// </summary>
    let parse (pairs: (string * string) seq) : Result<Morphir.Lang.CSharp.HandlingOptions, GenerationDiagnostic> =
        let invalid key message = { Rule = "invalid-backend-option"; Location = "$options/" + key; Message = message }

        pairs
        |> Seq.fold
            (fun state (key, word) ->
                state
                |> Result.bind (fun options ->
                    if List.contains key Morphir.Lang.CSharp.HandlingOptions.backendKeys then
                        Morphir.Lang.CSharp.HandlingOptions.trySet key word options |> Result.mapError (invalid key)
                    else
                        let accepted = Morphir.Lang.CSharp.HandlingOptions.describe Morphir.Lang.CSharp.HandlingOptions.backendKeys
                        Error(invalid key $"'{key}' is not a handling option of the C# backend. The handling options are {accepted}.")))
            (Ok Morphir.Lang.CSharp.HandlingOptions.empty)

/// UTF-8 source text, without BOM, with LF newlines and a portable relative path.
type GeneratedSource = { RelativePath: string; Text: string }

/// Runtime support required by the generated library, independent of compiler release channel.
type RuntimeRequirement = {
    TargetFramework: string
    Package: string
}

/// Immutable whole-model generation result; original family and exact source version are retained.
type GenerationResult = {
    Status: GenerationStatus
    Family: ModelFamily
    SourceVersion: string
    LanguageVersion: int
    Sources: GeneratedSource list
    Diagnostics: GenerationDiagnostic list
    Runtime: RuntimeRequirement
}
