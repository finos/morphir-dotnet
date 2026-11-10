// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Morphir.Build.Helpers;

/// <summary>A restored NuGet package that holds a file the Native AOT compiler linked.</summary>
/// <param name="Id">The package id, as its nuspec spells it.</param>
/// <param name="Version">The package version.</param>
/// <param name="Directory">The restored package folder, which holds the nuspec.</param>
public sealed record LinkedPackage(string Id, string Version, string Directory);

/// <summary>
/// The third-party notices of a Native AOT executable, generated from what was linked into it so that they cannot go
/// stale: the files of the ILC response file, each traced to the restored package that holds it, and each package's
/// license and notice files. Where a package ships no license text, a committed fallback in <c>build/notices/</c> gives
/// it, and its first line must name the license expression the package declares. A linked package with neither fails.
/// </summary>
public static class ThirdPartyNotices
{
    private const string Rule = "--------------------------------------------------------------------------------";

    /// <summary>
    /// The files the ILC response file gives the compiler: its inputs (lines without an option), references
    /// (<c>-r:</c>) and satellite assemblies (<c>--satellite:</c>). Relative paths are relative to the project folder.
    /// </summary>
    public static IReadOnlyList<string> LinkedFiles(string responseFile, string projectDirectory)
    {
        var files = new List<string>();
        foreach (var raw in responseFile.Split('\n'))
        {
            var line = raw.Trim().Trim('"');
            string? path = line.StartsWith("-r:", StringComparison.Ordinal) ? line[3..]
                : line.StartsWith("--satellite:", StringComparison.Ordinal) ? line["--satellite:".Length..]
                : line.StartsWith('-') || line.Length == 0 ? null
                : line;
            if (path is null || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            files.Add(Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(projectDirectory, path)));
        }

        return files;
    }

    /// <summary>
    /// The packages that hold <paramref name="files"/> (the nearest parent folder with a <c>.nuspec</c>), once each and
    /// sorted by id, and the files that are in neither a package nor <paramref name="repository"/>.
    /// </summary>
    public static (IReadOnlyList<LinkedPackage> Packages, IReadOnlyList<string> Unowned) Packages(IEnumerable<string> files, string repository)
    {
        var packages = new Dictionary<string, LinkedPackage>(StringComparer.OrdinalIgnoreCase);
        var unowned = new List<string>();
        var root = Path.GetFullPath(repository).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var file in files)
        {
            var package = PackageOf(file);
            if (package is not null)
                packages.TryAdd(package.Directory, package);
            else if (!Path.GetFullPath(file).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                unowned.Add(file);
        }

        return ([.. packages.Values.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)], unowned);
    }

    /// <summary>
    /// The notices text: a list of every package, then each package's license (its own file, or the committed
    /// fallback) and notice files, then the Unicode License of the data the NFC tables come from. The runtime identifier
    /// in a package id is written <c>&lt;rid&gt;</c>, so every platform's text is the same. Throws when a package has no
    /// license text, or when its fallback is for another license than the one it declares.
    /// </summary>
    public static string Write(string version, string rid, IReadOnlyList<LinkedPackage> packages, string fallbacks, string unicodeLicense)
    {
        var entries = packages
            .Select(package => (Package: package, Metadata: Metadata(package), Name: $"{DisplayId(package.Id, rid)} {package.Version}"))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var text = new StringBuilder();
        text.Append($"morphirup {version}: third-party notices\n\n");
        text.Append("morphirup is distributed under the Apache License, Version 2.0, in LICENSE, with the attributions in NOTICE.\n");
        text.Append("Native AOT compiles the components below into the executable. The list is generated from the assemblies\n");
        text.Append("that the Native AOT compiler linked into this build; each component's license and notices follow it.\n\n");
        foreach (var entry in entries)
            text.Append($"- {entry.Name}, {entry.Metadata.License}, {entry.Metadata.Project}\n");
        text.Append("- Unicode Character Database 17.0.0, Unicode-3.0, https://www.unicode.org/ucd/ (the NFC tables of Morphir.Toolchain are generated from it)\n");

        // Packages built from one repository ship the same notices file; it is written out once.
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var (license, source) = LicenseText(entry.Package, entry.Metadata, fallbacks);
            text.Append($"\n{Rule}\n{entry.Name}\nLicense: {entry.Metadata.License}\nCopyright: {entry.Metadata.Copyright}\nProject: {entry.Metadata.Project}\n");
            text.Append($"License text: {source}\n{Rule}\n\n{license}");
            foreach (var notice in NoticeFiles(entry.Package.Directory))
            {
                var content = Normal(File.ReadAllText(notice));
                var label = $"{Path.GetFileName(notice)} of {entry.Name}";
                if (written.TryGetValue(content, out var first))
                    text.Append($"\n{label} is the same text as {first}, above.\n");
                else
                {
                    written[content] = label;
                    text.Append($"\n{label}:\n\n{content}");
                }
            }
        }

        text.Append($"\n{Rule}\nUnicode Character Database 17.0.0\nLicense: Unicode-3.0\n{Rule}\n\n{Normal(unicodeLicense)}");
        return text.ToString();
    }

    /// <summary>The packages (as <c>Id Version</c>) that <paramref name="notices"/> does not list.</summary>
    public static IReadOnlyList<string> Missing(string notices, IEnumerable<LinkedPackage> packages, string rid) =>
        [.. packages
            .Where(package => !notices.Contains($"\n- {DisplayId(package.Id, rid)} {package.Version},", StringComparison.Ordinal))
            .Select(package => $"{package.Id} {package.Version}")];

    /// <summary>A package id with the runtime identifier written <c>&lt;rid&gt;</c>.</summary>
    public static string DisplayId(string id, string rid) =>
        Regex.Replace(id, $@"(?<=^|\.){Regex.Escape(rid)}(?=\.|$)", "<rid>", RegexOptions.IgnoreCase);

    private sealed record NuspecMetadata(string Id, string Version, string License, string? LicenseFile, string Copyright, string Project);

    private static LinkedPackage? PackageOf(string file)
    {
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(file)); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            if (!Directory.Exists(directory)) continue;
            var nuspec = Directory.GetFiles(directory, "*.nuspec").SingleOrDefault();
            if (nuspec is null) continue;
            var metadata = Read(nuspec);
            return new LinkedPackage(metadata.Id, metadata.Version, directory);
        }

        return null;
    }

    private static NuspecMetadata Metadata(LinkedPackage package) =>
        Read(Directory.GetFiles(package.Directory, "*.nuspec").Single());

    private static NuspecMetadata Read(string nuspec)
    {
        var metadata = XDocument.Load(nuspec).Root!.Elements().Single(element => element.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();
        var license = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "license");
        var type = license?.Attribute("type")?.Value;
        return new NuspecMetadata(
            Value("id") ?? throw new InvalidOperationException($"{nuspec} has no id."),
            Value("version") ?? throw new InvalidOperationException($"{nuspec} has no version."),
            type == "expression" ? license!.Value.Trim() : type == "file" ? $"see {license!.Value.Trim()}" : Value("licenseUrl") ?? "not declared",
            type == "file" ? license!.Value.Trim() : null,
            Value("copyright") ?? Value("authors") ?? "not declared",
            Value("projectUrl") ?? "not declared");
    }

    private static (string Text, string Source) LicenseText(LinkedPackage package, NuspecMetadata metadata, string fallbacks)
    {
        if (metadata.LicenseFile is not null && File.Exists(Path.Combine(package.Directory, metadata.LicenseFile)))
            return (Normal(File.ReadAllText(Path.Combine(package.Directory, metadata.LicenseFile))), $"{metadata.LicenseFile} in the package");

        var shipped = Directory.GetFiles(package.Directory)
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^licen[cs]e(\.(txt|md))?$", RegexOptions.IgnoreCase))
            .Order(StringComparer.Ordinal).FirstOrDefault();
        if (shipped is not null)
            return (Normal(File.ReadAllText(shipped)), $"{Path.GetFileName(shipped)} in the package");

        var fallback = Path.Combine(fallbacks, $"{package.Id}.txt");
        if (!File.Exists(fallback))
            throw new InvalidOperationException($"{package.Id} {package.Version} is linked into the executable, but the package ships no license text and build/notices/{package.Id}.txt does not exist.");

        var lines = Normal(File.ReadAllText(fallback)).Split('\n');
        var declared = lines[0].StartsWith("License: ", StringComparison.Ordinal) ? lines[0]["License: ".Length..] : "";
        if (declared != metadata.License)
            throw new InvalidOperationException($"build/notices/{package.Id}.txt is the {(declared.Length > 0 ? declared : "unnamed")} text, but {package.Id} {package.Version} declares {metadata.License}.");
        var source = lines.Length > 1 && lines[1].StartsWith("Source: ", StringComparison.Ordinal) ? lines[1]["Source: ".Length..] : "not recorded";
        var body = string.Join('\n', lines.SkipWhile(line => line.Length > 0).Skip(1));
        return (body, $"build/notices/{package.Id}.txt in morphir-dotnet, from {source}; the package ships none");
    }

    private static IEnumerable<string> NoticeFiles(string directory) =>
        Directory.GetFiles(directory)
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^(third-?party-?notices|notice)(\.(txt|md))?$", RegexOptions.IgnoreCase))
            .Order(StringComparer.Ordinal);

    private static string Normal(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n') + "\n";
}
