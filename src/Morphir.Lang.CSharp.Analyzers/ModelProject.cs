// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Morphir.Lang.CSharp.Analyzers;

/// <summary>Decides whether a compilation is a Morphir model project. The analyzers report only in a model project.</summary>
public static class ModelProject
{
    /// <summary>The MSBuild property that marks a project. The package sets it with its props file.</summary>
    public const string PropertyName = "build_property.MorphirModel";

    /// <summary>True when the options set the property to true, or the assembly has <c>[assembly: Morphir.MorphirModel]</c>.</summary>
    public static bool IsModelProject(Compilation compilation, AnalyzerOptions options)
    {
        if (options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(PropertyName, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return compilation.Assembly.GetAttributes().Any(attribute =>
            attribute.AttributeClass is { Name: "MorphirModelAttribute", ContainingNamespace: { Name: "Morphir", ContainingNamespace.IsGlobalNamespace: true } });
    }
}
