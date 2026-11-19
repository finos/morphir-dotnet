using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Morphir.Build.Helpers;

/// <summary>
/// Checks that library packages are published dependencies first: a package whose project references the project of
/// another listed package must come after it, or a consumer of the first could restore it before its dependency exists.
/// </summary>
public static class PublishOrder
{
    /// <summary>One message for each package that comes before a listed package it references.</summary>
    /// <param name="packages">The packages in publish order, with their project files.</param>
    /// <param name="referencesOf">The project references of a project file, as written in it.</param>
    public static IReadOnlyList<string> Violations(
        IReadOnlyList<(string PackageId, string Project)> packages, Func<string, IEnumerable<string>> referencesOf)
    {
        string Normal(string path) => Path.GetFullPath(path).Replace('\\', '/');
        var position = packages.Select((package, index) => (Normal(package.Project), index)).ToDictionary(pair => pair.Item1, pair => pair.index);

        return packages
            .SelectMany((package, index) => referencesOf(package.Project)
                .Select(reference => Normal(Path.Combine(Path.GetDirectoryName(package.Project) ?? "", reference)))
                .Where(reference => position.TryGetValue(reference, out var dependency) && dependency > index)
                .Select(reference => $"{package.PackageId} is published before {packages[position[reference]].PackageId}, which it references."))
            .ToList();
    }

    /// <summary>
    /// The <c>ProjectReference</c> paths of a project file that become package dependencies: a reference with all
    /// assets private is bundled or build-only. DTDs are refused and nothing is resolved.
    /// </summary>
    public static IEnumerable<string> ProjectReferences(string project)
    {
        using var reader = XmlReader.Create(project, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

        return XDocument.Load(reader).Descendants("ProjectReference")
            .Where(reference => !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase))
            .Select(reference => (string?)reference.Attribute("Include"))
            .OfType<string>()
            .Select(path => path.Replace('\\', '/'))
            .ToList();
    }
}
