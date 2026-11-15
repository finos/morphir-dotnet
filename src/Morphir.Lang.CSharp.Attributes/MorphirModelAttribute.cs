// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;

namespace Morphir;

/// <summary>
/// Marks an assembly as a Morphir model project. The Morphir analyzers report only in a model project, so a project
/// that references the package for another reason sees no diagnostic. An MSBuild property of the package marks a
/// project in the same way.
/// </summary>
/// <example><code>[assembly: Morphir.MorphirModel]</code></example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MorphirModelAttribute : Attribute
{
}
