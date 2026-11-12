// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Morphir.Build.Helpers;

/// <summary>
/// The target frameworks of the repository, from the properties in Directory.Build.props that the projects use.
/// The build finds outputs below <c>bin/&lt;configuration&gt;/&lt;tfm&gt;/</c> with these values, so a change of
/// target framework is one edit of Directory.Build.props.
/// </summary>
/// <param name="Current">Applications, .NET tools, tests and internal projects (<c>MorphirCurrentTargetFramework</c>).</param>
/// <param name="Library">Packable libraries, newest first (<c>MorphirLibraryTargetFrameworks</c>).</param>
/// <param name="Hosted">MSBuild tasks and Myriad plugins (<c>MorphirHostedTargetFramework</c>).</param>
public sealed record RepositoryTargetFrameworks(string Current, ImmutableArray<string> Library, string Hosted)
{
    /// <summary>Reads the three properties from <paramref name="directoryBuildProps"/>.</summary>
    /// <exception cref="InvalidOperationException">A property is missing or empty.</exception>
    public static RepositoryTargetFrameworks Load(string directoryBuildProps)
    {
        var properties = XDocument.Load(directoryBuildProps).Descendants("PropertyGroup").Elements()
            .GroupBy(element => element.Name.LocalName)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim());

        string Read(string name) =>
            properties.TryGetValue(name, out var value) && value.Length > 0
                ? value
                : throw new InvalidOperationException($"{directoryBuildProps} does not set {name}.");

        return new(
            Read("MorphirCurrentTargetFramework"),
            [.. Read("MorphirLibraryTargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Read("MorphirHostedTargetFramework"));
    }
}
