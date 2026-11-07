// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp

open Morphir.Compilation

/// <summary>The diagnostic codes of the F# frontend. Codes are stable once released.</summary>
[<RequireQualifiedAccess>]
module FSharpCodes =

    /// <summary>An F# parse or type-check diagnostic. Its data carries the F# error number.</summary>
    [<CompiledName("Compiler")>]
    let compiler = DiagnosticCode.Create "fsharp.compiler"

    /// <summary>
    /// The frontend could not get complete, unambiguous and unchanged compiler evidence for a <c>match</c>.
    /// The diagnostic is at the original match, and its data names the reason.
    /// </summary>
    [<CompiledName("UnsupportedMatchShape")>]
    let unsupportedMatchShape = DiagnosticCode.Create "fsharp.unsupported_match_shape"
