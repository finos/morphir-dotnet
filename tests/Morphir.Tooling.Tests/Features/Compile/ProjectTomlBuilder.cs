// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Globalization;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>A TOML value of a settings test, as the configuration file spells it.</summary>
public sealed record TomlValue
{
    private TomlValue(string text) => Text = text;

    /// <summary>The TOML spelling of the value.</summary>
    public string Text { get; }

    /// <summary>A string, for example <c>"document-tree"</c>.</summary>
    public static TomlValue String(string value) => new($"\"{value}\"");

    /// <summary>A boolean.</summary>
    public static TomlValue Boolean(bool value) => new(value ? "true" : "false");

    /// <summary>An integer.</summary>
    public static TomlValue Integer(int value) => new(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A nonempty inline table, <c>{ unexpected = true }</c>, whose origin the configuration records on its child.</summary>
    public static TomlValue NonemptyTable() => new("{ unexpected = true }");

    /// <summary>A value of a type that is not a string: <c>boolean</c>, <c>integer</c> or <c>table</c>.</summary>
    public static TomlValue NotAString(string type) => type switch
    {
        "boolean" => Boolean(true),
        "integer" => Integer(42),
        "table" => NonemptyTable(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a TOML type of these tests."),
    };

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// Builds the <c>morphir.toml</c> of a settings test: a project named <c>acme/demo</c> and the keys of its <c>[ir]</c>
/// table, in the order they are given.
/// </summary>
public sealed record ProjectToml
{
    private ProjectToml(string name, ImmutableList<(string Key, TomlValue Value)> ir) => (Name, Ir) = (name, ir);

    /// <summary>The package name, <c>project.name</c>.</summary>
    public string Name { get; }

    /// <summary>The keys of <c>[ir]</c>.</summary>
    public ImmutableList<(string Key, TomlValue Value)> Ir { get; }

    /// <summary>A project with no <c>[ir]</c> key.</summary>
    public static ProjectToml AProject(string name = "acme/demo") => new(name, []);

    /// <summary>The project with <c>ir.&lt;key&gt;</c> set to <paramref name="value"/>.</summary>
    public ProjectToml WithIr(string key, TomlValue value) => new(Name, Ir.Add((key, value)));

    /// <summary>The project with <c>ir.layout</c> set to the string <paramref name="layout"/>.</summary>
    public ProjectToml WithLayout(string layout) => WithIr("layout", TomlValue.String(layout));

    /// <summary>The project with the deprecated <c>ir.mode</c> set to the string <paramref name="mode"/>.</summary>
    public ProjectToml WithMode(string mode) => WithIr("mode", TomlValue.String(mode));

    /// <summary>The text of the file.</summary>
    public string Build() => Ir.IsEmpty
        ? $"[project]\nname = \"{Name}\"\n"
        : $"[project]\nname = \"{Name}\"\n\n[ir]\n" + string.Concat(Ir.Select(entry => $"{entry.Key} = {entry.Value.Text}\n"));

    /// <inheritdoc />
    public override string ToString() => Build();
}
