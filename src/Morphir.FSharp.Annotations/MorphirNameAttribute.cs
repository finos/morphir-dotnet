// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;

namespace Morphir;

/// <summary>
/// Sets the Morphir name of a declaration: a value, function, type, union case, record field or module.
/// The F# frontend uses this name instead of the name it derives from the F# identifier.
/// </summary>
/// <remarks>
/// <para>
/// The name uses the canonical Morphir name syntax: lowercase words joined with <c>-</c>, and uppercase
/// segments for initialisms, for example <c>triple-add</c> or <c>value-in-USD</c>.
/// </para>
/// <para>
/// Use it to name an F# operator, which has no Morphir words, or to give a declaration a stable Morphir name.
/// <c>[&lt;CompiledName&gt;]</c> changes only the .NET name and never the Morphir name.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
public sealed class MorphirNameAttribute : Attribute
{
    /// <summary>Creates the attribute with a Morphir name in canonical syntax.</summary>
    /// <param name="name">The Morphir name, for example <c>triple-add</c>.</param>
    public MorphirNameAttribute(string name) => Name = name;

    /// <summary>The Morphir name, in canonical syntax.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => $"MorphirName({Name})";
}
