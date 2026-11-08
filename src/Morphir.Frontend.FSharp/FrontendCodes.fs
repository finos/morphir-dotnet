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

    /// <summary>
    /// A module (or namespace-level declarations) is not under the package path. Its Morphir module path
    /// would be outside the package or empty.
    /// </summary>
    [<CompiledName("ModuleOutsidePackage")>]
    let moduleOutsidePackage = DiagnosticCode.Create "fsharp.module_outside_package"

    /// <summary>
    /// Two declarations in one scope have the same Morphir name, for example <c>taxRate</c> and <c>TaxRate</c>.
    /// The related location is the first declaration.
    /// </summary>
    [<CompiledName("NameCollision")>]
    let nameCollision = DiagnosticCode.Create "fsharp.name_collision"

    /// <summary>An F# identifier gives no Morphir name, for example a custom operator.</summary>
    [<CompiledName("UnsupportedName")>]
    let unsupportedName = DiagnosticCode.Create "fsharp.unsupported_name"

/// <summary>Language-neutral compilation codes that the F# frontend reports.</summary>
[<RequireQualifiedAccess>]
module CompileCodes =

    /// <summary>An exposed module name matches no compiled module.</summary>
    [<CompiledName("UnknownExposedModule")>]
    let unknownExposedModule = DiagnosticCode.Create "compile.unknown_exposed_module"
