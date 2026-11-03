// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open Morphir.IR.Model

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
