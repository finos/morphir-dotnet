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
/// Configuration files that no reader accepts, by what is wrong with them. A JSON5 reader names the key of a duplicate,
/// where the TOML parser refuses the whole file first.
/// </summary>
public static class MalformedConfiguration
{
    /// <summary>The file name of <see cref="DuplicateLayoutJson5"/>, which only <c>--config</c> selects.</summary>
    public const string DuplicateLayoutJson5File = "morphir.json5";

    /// <summary>A JSON5 configuration of the car-rental project that sets <c>ir.layout</c> twice.</summary>
    public const string DuplicateLayoutJson5 =
        "{ project: { name: \"acme/car-rental\" }, frontend: { language: \"fsharp\" }, ir: { layout: \"document-tree\", layout: \"single-file\" } }\n";
}

/// <summary>
/// Builds the <c>morphir.toml</c> of a settings test: a project named <c>acme/demo</c>, its frontend language, and the
/// keys of its <c>[ir]</c> table, in the order they are given.
/// </summary>
public sealed record ProjectToml
{
    private ProjectToml(string name, string? language, ImmutableList<(string Key, TomlValue Value)> ir) =>
        (Name, Language, Ir) = (name, language, ir);

    /// <summary>The package name, <c>project.name</c>.</summary>
    public string Name { get; }

    /// <summary>The frontend language, <c>frontend.language</c>, or null.</summary>
    public string? Language { get; }

    /// <summary>The keys of <c>[ir]</c>.</summary>
    public ImmutableList<(string Key, TomlValue Value)> Ir { get; }

    /// <summary>A project with no <c>[ir]</c> key.</summary>
    public static ProjectToml AProject(string name = "acme/demo") => new(name, null, []);

    /// <summary>
    /// The F# Orders project of bounded generation (<c>BoundedOrdersRecordMother.ProjectConfiguration</c>) with
    /// <c>ir.layout = "folder"</c>, which is not a layout: a compile with <c>--ir-layout</c> overrides it.
    /// </summary>
    public static ProjectToml OrdersWithAnInvalidLayout => AProject("acme/orders").WithFrontend("fsharp").WithLayout("folder");

    /// <summary>The project with <c>frontend.language</c> set to <paramref name="language"/>.</summary>
    public ProjectToml WithFrontend(string language) => new(Name, language, Ir);

    /// <summary>The project with <c>ir.&lt;key&gt;</c> set to <paramref name="value"/>.</summary>
    public ProjectToml WithIr(string key, TomlValue value) => new(Name, Language, Ir.Add((key, value)));

    /// <summary>The project with <c>ir.layout</c> set to the string <paramref name="layout"/>.</summary>
    public ProjectToml WithLayout(string layout) => WithIr("layout", TomlValue.String(layout));

    /// <summary>The project with the deprecated <c>ir.mode</c> set to the string <paramref name="mode"/>.</summary>
    public ProjectToml WithMode(string mode) => WithIr("mode", TomlValue.String(mode));

    /// <summary>The text of the file.</summary>
    public string Build() =>
        $"[project]\nname = \"{Name}\"\n"
        + (Language is null ? "" : $"\n[frontend]\nlanguage = \"{Language}\"\n")
        + (Ir.IsEmpty ? "" : "\n[ir]\n" + string.Concat(Ir.Select(entry => $"{entry.Key} = {entry.Value.Text}\n")));

    /// <inheritdoc />
    public override string ToString() => Build();
}
