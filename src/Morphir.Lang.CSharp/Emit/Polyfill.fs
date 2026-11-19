// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Emit

/// <summary>
/// The file with the runtime types that the C# 15 output needs and that an older target framework lacks: <c>IsExternalInit</c>
/// below .NET 5, <c>CompilerFeatureRequiredAttribute</c> below .NET 7, and <c>UnionAttribute</c>, <c>IUnion</c> and
/// <c>IsClosedTypeAttribute</c> below .NET 11. The types are internal and each group is behind its guard, so the file is the
/// same for every target framework. The text is <c>Morphir.Polyfill.Source</c>, the same text that the frontend adds to the
/// compilation of <c>morphir compile</c>.
/// </summary>
module internal Polyfill =
    /// <summary>The path of the file.</summary>
    let path = "Morphir.Polyfill.g.cs"

    /// <summary>The text of the file.</summary>
    let text: string = Morphir.Polyfill.Source
