// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp.Analyzers;

/// <summary>The category and the id prefix of every rule of this assembly.</summary>
/// <remarks>
/// A rule has a short Roslyn id such as <c>MORPHIR1001</c>, because <c>#pragma warning disable</c> and
/// <c>.editorconfig</c> use short ids. The absolute IRI of the rule goes in the help link of its descriptor.
/// </remarks>
public static class AnalyzerIds
{
    /// <summary>The category of every Morphir rule.</summary>
    public const string Category = "Morphir";

    /// <summary>The prefix of every Roslyn id of a Morphir rule.</summary>
    public const string Prefix = "MORPHIR";
}
