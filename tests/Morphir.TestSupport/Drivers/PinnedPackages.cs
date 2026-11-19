// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>The versions that <c>Directory.Packages.props</c> pins, for a check of a reported version.</summary>
public static class PinnedPackages
{
    /// <summary>The version of <paramref name="package"/> in the text of a <c>Directory.Packages.props</c> file.</summary>
    public static string VersionOf(string props, string package) =>
        System.Xml.Linq.XDocument.Parse(props).Descendants("PackageVersion")
            .Single(element => (string?)element.Attribute("Include") == package).Attribute("Version")!.Value;
}
