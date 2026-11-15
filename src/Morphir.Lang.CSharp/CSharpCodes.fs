// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open Morphir.Compilation

/// <summary>The diagnostic codes of the C# frontend. Codes are stable once released.</summary>
[<RequireQualifiedAccess>]
module CSharpCodes =

    /// <summary>A C# parse or bind diagnostic. Its data carries the C# diagnostic id, for example CS0029.</summary>
    [<CompiledName("Compiler")>]
    let compiler = DiagnosticCode.Create "csharp.compiler"

    /// <summary>The frontend cannot load its fixed reference assemblies. The diagnostic has no location.</summary>
    [<CompiledName("ReferencesUnavailable")>]
    let referencesUnavailable = DiagnosticCode.Create "csharp.references_unavailable"

    /// <summary>
    /// A namespace is not below the package path, or is the package path itself. Its Morphir module path would be
    /// outside the package or empty.
    /// </summary>
    [<CompiledName("ModuleOutsidePackage")>]
    let moduleOutsidePackage = DiagnosticCode.Create "csharp.module_outside_package"

    /// <summary>Two declarations in one scope have the same Morphir name, for example <c>taxRate</c> and <c>TaxRate</c>.</summary>
    [<CompiledName("NameCollision")>]
    let nameCollision = DiagnosticCode.Create "csharp.name_collision"

    /// <summary>A C# identifier gives no Morphir name.</summary>
    [<CompiledName("UnsupportedName")>]
    let unsupportedName = DiagnosticCode.Create "csharp.unsupported_name"

    /// <summary>A C# type, method or member outside the package has no Morphir SDK mapping. The message names the symbol.</summary>
    [<CompiledName("UnsupportedReference")>]
    let unsupportedReference = DiagnosticCode.Create "csharp.unsupported_reference"

    /// <summary>
    /// A C# construct outside the subset. The code is <c>csharp.unsupported_</c> and the slug of the construct, the same
    /// slug that the admission rule of the analyzer uses when the analyzer reports the construct.
    /// </summary>
    [<CompiledName("Unsupported")>]
    let unsupported (slug: string) = DiagnosticCode.Create("csharp.unsupported_" + slug)
