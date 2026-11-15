// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open Morphir.IR.Model
open System
open System.Globalization
open System.Text

/// Explicit numeric language version. The installed compiler never selects this value.
type GenerationOptions =
    {
        LanguageVersion: int
    }

    static member Default = { LanguageVersion = 14 }

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
