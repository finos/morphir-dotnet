// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Morphir.TestSupport.Drivers;

/// <summary>One dependency of a package, as its manifest states it.</summary>
/// <param name="VersionRange">The version or the version range, for example <c>0.3.0</c> or <c>[0.3.0]</c>.</param>
public sealed record PackageDependency(string Id, string VersionRange)
{
    /// <summary>True when the range is exactly <paramref name="version"/>: <c>v</c>, <c>[v]</c> or <c>[v, v]</c>.</summary>
    public bool IsExactly(string version)
    {
        var bounds = VersionRange.Trim('[', ']', '(', ')').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return bounds.Length > 0 && bounds.All(bound => string.Equals(bound, version, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The manifest (the <c>.nuspec</c> file) of a package.</summary>
public sealed record PackageManifest(
    string Id,
    string Version,
    string Authors,
    string License,
    string ProjectUrl,
    string ReleaseNotes,
    ImmutableArray<string> PackageTypes,
    ImmutableArray<PackageDependency> Dependencies)
{
    /// <summary>Reads a manifest. A field that the manifest does not have is empty.</summary>
    public static PackageManifest Parse(string text)
    {
        var root = XDocument.Parse(text).Root ?? throw new InvalidDataException("The manifest has no root element.");
        var metadata = root.Elements().SingleOrDefault(element => element.Name.LocalName == "metadata")
            ?? throw new InvalidDataException("The manifest has no metadata element.");

        string Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? string.Empty;

        // An old package can have licenseUrl and no license.
        var license = Value("license") is { Length: > 0 } value ? value : Value("licenseUrl");
        var packageTypes = metadata.Descendants().Where(element => element.Name.LocalName == "packageType")
            .Select(element => (string?)element.Attribute("name") ?? string.Empty);
        var dependencies = metadata.Descendants().Where(element => element.Name.LocalName == "dependency")
            .Select(element => new PackageDependency((string?)element.Attribute("id") ?? string.Empty, (string?)element.Attribute("version") ?? string.Empty))
            .Distinct();

        return new PackageManifest(
            Value("id"), Value("version"), Value("authors"), license, Value("projectUrl"), Value("releaseNotes"), [.. packageTypes], [.. dependencies]);
    }
}

/// <summary>The identity of an assembly and its references, from its metadata. No assembly is loaded.</summary>
public sealed record AssemblyIdentity(string Name, Version Version, string Culture, ImmutableArray<byte> PublicKey, ImmutableArray<AssemblyIdentity.Reference> References)
{
    /// <summary>One assembly that the assembly references.</summary>
    public sealed record Reference(string Name, Version Version);

    /// <summary>The identity in the form that the runtime prints, for a message.</summary>
    public override string ToString() => $"{Name}, Version={Version}, Culture={(Culture.Length == 0 ? "neutral" : Culture)}";

    /// <summary>Reads the identity of the assembly in <paramref name="bytes"/>.</summary>
    public static AssemblyIdentity Of(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var references = metadata.AssemblyReferences
            .Select(metadata.GetAssemblyReference)
            .Select(reference => new Reference(metadata.GetString(reference.Name), reference.Version));
        return new AssemblyIdentity(
            metadata.GetString(definition.Name),
            definition.Version,
            metadata.GetString(definition.Culture),
            [.. metadata.GetBlobBytes(definition.PublicKey)],
            [.. references]);
    }
}

/// <summary>One assembly that a package has, with the entry of the package that holds it.</summary>
public sealed record PackedAssembly(string Entry, AssemblyIdentity Identity);

/// <summary>
/// A package file (<c>.nupkg</c>) that a test reads: its manifest, its entries and its assemblies. A test reads a
/// package through this type and needs no path of the machine.
/// </summary>
public sealed class PackedPackage
{
    private PackedPackage(string path, PackageManifest manifest, ImmutableArray<string> entries)
    {
        Path = path;
        Manifest = manifest;
        Entries = entries;
    }

    /// <summary>The full path of the package file, for a command line and for a message.</summary>
    public string Path { get; }

    /// <summary>The name of the package file, for example <c>Morphir.Tool.0.4.0.nupkg</c>.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    public string Id => Manifest.Id;

    public string Version => Manifest.Version;

    public PackageManifest Manifest { get; }

    /// <summary>The full name of each entry of the package, with <c>/</c>.</summary>
    public ImmutableArray<string> Entries { get; }

    public override string ToString() => $"{Id} {Version} ({Path})";

    /// <summary>Reads the package at <paramref name="path"/>. A file with no manifest at its root is an error.</summary>
    public static PackedPackage Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifests = archive.Entries
            .Where(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (manifests.Length != 1)
            throw new InvalidDataException($"The package {path} has {manifests.Length} manifests at its root, not one.");

        using var reader = new StreamReader(manifests[0].Open());
        return new PackedPackage(path, PackageManifest.Parse(reader.ReadToEnd()), [.. archive.Entries.Select(entry => entry.FullName)]);
    }

    /// <summary>The text of one entry. An entry that the package does not have is an error.</summary>
    public string ReadEntry(string entry)
    {
        using var archive = ZipFile.OpenRead(Path);
        var found = archive.GetEntry(entry) ?? throw new InvalidDataException($"The package {FileName} has no entry {entry}.");
        using var reader = new StreamReader(found.Open());
        return reader.ReadToEnd();
    }

    /// <summary>The assemblies (<c>.dll</c> entries) of the package whose entry passes <paramref name="filter"/>.</summary>
    public ImmutableArray<PackedAssembly> Assemblies(Func<string, bool> filter)
    {
        using var archive = ZipFile.OpenRead(Path);
        var assemblies = ImmutableArray.CreateBuilder<PackedAssembly>();
        foreach (var entry in archive.Entries.Where(entry => entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && filter(entry.FullName)))
        {
            using var buffer = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(buffer);
            assemblies.Add(new PackedAssembly(entry.FullName, AssemblyIdentity.Of(buffer.ToArray())));
        }

        return assemblies.ToImmutable();
    }
}

/// <summary>
/// The packages that the build packed (the output folder of <c>PackAll</c>), for a test of the packages lane. The
/// build gives the folder in <c>MORPHIR_TEST_PACKAGES</c>. The driver reads only that folder: it does not pack, and it
/// does not search another folder.
/// </summary>
public sealed class PackedPackages
{
    /// <summary>The variable that names the folder of the packed packages.</summary>
    public const string Variable = "MORPHIR_TEST_PACKAGES";

    private PackedPackages(string folder, ImmutableArray<PackedPackage> all)
    {
        Folder = folder;
        All = all;
    }

    /// <summary>The folder that the build gave.</summary>
    public string Folder { get; }

    /// <summary>Each package of the folder.</summary>
    public ImmutableArray<PackedPackage> All { get; }

    /// <summary>The command that makes the packages, for a message.</summary>
    public static string PackCommand(string? configuration) =>
        $"./build.sh --target PackAll --configuration {configuration ?? "Release"}";

    /// <summary>
    /// Reads the packages of <paramref name="folder"/> (the value of <see cref="Variable"/>). No folder, a folder that
    /// is not there, or a folder with no package stops the run with the pack command.
    /// </summary>
    public static PackedPackages Of(string? folder, string? configuration)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new TestSessionException(
                $"The packages lane needs the folder of the packed packages in {Variable}. Make the packages first: {PackCommand(configuration)}, and give their folder (artifacts/packages).");

        var full = System.IO.Path.GetFullPath(folder);
        var files = Directory.Exists(full) ? Directory.GetFiles(full, "*.nupkg") : [];
        if (files.Length == 0)
            throw new TestSessionException(
                $"The folder {full} of {Variable} has no package. Make the packages first: {PackCommand(configuration)}.");

        return new PackedPackages(full, [.. files.Order(StringComparer.Ordinal).Select(PackedPackage.Read)]);
    }

    /// <summary>The one package with the id <paramref name="id"/>. No package or two packages with that id is an error.</summary>
    public PackedPackage Find(string id)
    {
        var found = All.Where(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        return found.Length switch
        {
            1 => found[0],
            0 => throw new TestSessionException($"The folder {Folder} has no package {id}. Its packages: {string.Join(", ", All.Select(package => package.Id))}."),
            _ => throw new TestSessionException($"The folder {Folder} has {found.Length} packages {id}: {string.Join(", ", found.Select(package => package.FileName))}. The pack step cleans the folder, so it has one."),
        };
    }
}
