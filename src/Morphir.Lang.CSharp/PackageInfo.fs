// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

/// <summary>The identity of the C# language package.</summary>
[<RequireQualifiedAccess>]
module PackageInfo =

    /// <summary>The language id that the frontend claims and the backend uses as its target id.</summary>
    [<Literal>]
    let Language = "csharp"

    /// <summary>The extension of a C# source document, with the dot.</summary>
    [<Literal>]
    let SourceExtension = ".cs"
