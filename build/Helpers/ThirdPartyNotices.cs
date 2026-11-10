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
/// A native library that the link includes and that is not from a NuGet package, such as <c>libwasmtime.a</c> from a
/// pinned C API archive. Declared in <c>build/notices/native/&lt;name&gt;.json</c>, with its license text beside it; the
/// build fails when the link includes such a library without a declaration, or with another SHA-256.
/// </summary>
/// <param name="Name">The file name as linked, for example <c>libwasmtime.a</c>.</param>
/// <param name="Version">The upstream version.</param>
/// <param name="License">The SPDX license expression, for example <c>Apache-2.0 WITH LLVM-exception</c>.</param>
/// <param name="LicenseFile">The license text file, in the declarations folder.</param>
/// <param name="Source">Where it was obtained: the URL of the pinned archive.</param>
/// <param name="Sha256">The SHA-256 of the linked file, lowercase hex.</param>
public sealed record DeclaredNativeLibrary(string Name, string Version, string License, string LicenseFile, string Source, string Sha256)
{
    /// <summary>What is declared: a native library of the link (the default), or a file a linked assembly embeds.</summary>
    public DeclaredKind Kind { get; init; } = DeclaredKind.NativeLibrary;

    /// <summary>A NOTICE text file in the declarations folder, written after the license text; none by default.</summary>
    public string? NoticeFile { get; init; }
}

/// <summary>The kind of a declared third-party component: the <c>kind</c> member of its declaration.</summary>
public enum DeclaredKind
{
    /// <summary><c>native-library</c>, the default: a <c>NativeLibrary</c> item of the Native AOT link.</summary>
    NativeLibrary,

    /// <summary><c>embedded-resource</c>: a file that a linked assembly embeds as a manifest resource.</summary>
    EmbeddedResource,
}

/// <summary>A manifest resource that a linked assembly embeds, with the SHA-256 of its content.</summary>
public sealed record EmbeddedResource(string Assembly, string LogicalName, string Sha256);

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
    /// license text, or when its fallback is for another license than the one it declares. <paramref name="natives"/> are
    /// the declared native libraries the link includes that no package provides (see <see cref="DeclaredNativeLibrary"/>),
    /// with their license text files in <paramref name="declarations"/>; none by default.
    /// </summary>
    public static string Write(string version, string rid, IReadOnlyList<LinkedPackage> packages, string fallbacks, string unicodeLicense,
        IReadOnlyList<DeclaredNativeLibrary>? natives = null, string? declarations = null)
    {
        var declared = (natives ?? []).OrderBy(native => native.Name, StringComparer.Ordinal).ToList();
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
        foreach (var native in declared)
            text.Append($"- {native.Name} {native.Version}, {native.License}, {native.Source}\n");
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

        foreach (var native in declared)
        {
            var folder = declarations ?? throw new ArgumentNullException(nameof(declarations), "Declared components need their declarations folder.");
            var how = native.Kind == DeclaredKind.EmbeddedResource ? "Embedded in a linked assembly as a resource" : "A native library of the link";
            text.Append($"\n{Rule}\n{native.Name} {native.Version}\n{how}\nLicense: {native.License}\nSource: {native.Source}\nSHA-256: {native.Sha256}\n");
            // The license and notice texts are written as the files hold them, each after its own heading.
            text.Append($"License text: build/notices/native/{native.LicenseFile} in morphir-dotnet\n{Rule}\n\n{Verbatim(Path.Combine(folder, native.LicenseFile))}");
            if (native.NoticeFile is { } noticeFile)
                text.Append($"\nNotice: build/notices/native/{noticeFile} in morphir-dotnet\n\n{Verbatim(Path.Combine(folder, noticeFile))}");
        }

        text.Append($"\n{Rule}\nUnicode Character Database 17.0.0\nLicense: Unicode-3.0\n{Rule}\n\n{Normal(unicodeLicense)}");
        return text.ToString();
    }

    /// <summary>
    /// The declared third-party components in <paramref name="declarations"/>, one JSON file each:
    /// <c>{ "kind", "name", "version", "license", "licenseFile", "noticeFile", "source", "sha256" }</c>. <c>kind</c> is
    /// <c>native-library</c> (the default) or <c>embedded-resource</c>; <c>noticeFile</c> is optional. Both text files are
    /// in the same folder, and a declaration that names a missing one fails the build. A missing folder declares none.
    /// </summary>
    public static IReadOnlyList<DeclaredNativeLibrary> ReadDeclaredNativeLibraries(string declarations)
    {
        if (!Directory.Exists(declarations)) return [];
        var declared = new List<DeclaredNativeLibrary>();
        foreach (var file in Directory.GetFiles(declarations, "*.json").Order(StringComparer.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var label = Path.GetFileName(file);
            string? Optional(string member) =>
                !document.RootElement.TryGetProperty(member, out var value) ? null
                : value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString()!
                : throw new InvalidOperationException($"{label}: the member '{member}' is not a string.");
            string Text(string member) =>
                Optional(member) ?? throw new InvalidOperationException($"{label} declares a component without the string member '{member}'.");
            var kind = Optional("kind") switch
            {
                null or "native-library" => DeclaredKind.NativeLibrary,
                "embedded-resource" => DeclaredKind.EmbeddedResource,
                var other => throw new InvalidOperationException($"{label}: the kind '{other}' is not native-library or embedded-resource."),
            };
            var entry = new DeclaredNativeLibrary(Text("name"), Text("version"), Text("license"), Text("licenseFile"), Text("source"), Text("sha256"))
            {
                Kind = kind,
                NoticeFile = Optional("noticeFile"),
            };
            foreach (var text in new[] { entry.LicenseFile, entry.NoticeFile }.OfType<string>())
                if (!File.Exists(Path.Combine(declarations, text)))
                    throw new InvalidOperationException($"{label} names {text}, which is not in {declarations}.");
            declared.Add(entry);
        }

        return declared;
    }

    /// <summary>
    /// The declarations of the native libraries that the link includes, and why the others are not covered. A library in a
    /// restored package is that package's; every other one needs a <see cref="DeclaredKind.NativeLibrary"/> declaration
    /// with its file name and SHA-256 together, because one name can be declared once per platform with another digest.
    /// Only the declarations that matched are returned, for the notices.
    /// </summary>
    public static (IReadOnlyList<DeclaredNativeLibrary> Matched, IReadOnlyList<string> Problems) MatchNativeLibraries(
        IEnumerable<string> linkedNativeLibraries, string repository, IReadOnlyList<DeclaredNativeLibrary> declared)
    {
        var matched = new List<DeclaredNativeLibrary>();
        var problems = new List<string>();
        foreach (var library in linkedNativeLibraries)
        {
            if (PackageOf(library) is not null) continue;
            var name = Path.GetFileName(library);
            var shown = Path.GetRelativePath(repository, library).Replace('\\', '/');
            var named = declared.Where(native => native.Kind == DeclaredKind.NativeLibrary && string.Equals(native.Name, name, StringComparison.Ordinal)).ToList();
            if (named.Count == 0)
            {
                problems.Add($"{shown} is linked into the executable, is in no restored package, and has no declared entry in build/notices/native/ (name, version, license, licenseFile, source and sha256).");
                continue;
            }

            if (!File.Exists(library))
            {
                problems.Add($"{shown} is declared but does not exist, so its SHA-256 cannot be checked.");
                continue;
            }

            var digest = ReleaseArchive.Sha256(library);
            var entry = named.FirstOrDefault(native => string.Equals(native.Sha256, digest, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                problems.Add($"{shown} has SHA-256 {digest}, and no declaration of {name} has it; the declared SHA-256 values are {string.Join(", ", named.Select(native => native.Sha256))}.");
            else if (!matched.Contains(entry))
                matched.Add(entry);
        }

        return (matched, problems);
    }

    /// <summary>Why the native libraries that the link includes are not covered by notices; see <see cref="MatchNativeLibraries"/>.</summary>
    public static IReadOnlyList<string> NativeLibraryProblems(IEnumerable<string> linkedNativeLibraries, string repository, IReadOnlyList<DeclaredNativeLibrary> declared) =>
        MatchNativeLibraries(linkedNativeLibraries, repository, declared).Problems;

    /// <summary>
    /// The manifest resources embedded in <paramref name="assemblies"/> (the assemblies of the ILC response file), with the
    /// SHA-256 of each one's content. A file that is not a .NET assembly is skipped.
    /// </summary>
    public static IReadOnlyList<EmbeddedResource> EmbeddedResources(IEnumerable<string> assemblies)
    {
        var found = new List<EmbeddedResource>();
        foreach (var assembly in assemblies.Where(File.Exists))
        {
            using var stream = File.OpenRead(assembly);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!pe.HasMetadata) continue;
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory;
            foreach (var handle in reader.ManifestResources)
            {
                var resource = reader.GetManifestResource(handle);
                if (!resource.Implementation.IsNil || directory is null || directory.Value.Size == 0) continue;
                var data = pe.GetSectionData(directory.Value.RelativeVirtualAddress).GetReader((int)resource.Offset, directory.Value.Size - (int)resource.Offset);
                var length = data.ReadInt32();
                var content = data.ReadBytes(length);
                found.Add(new EmbeddedResource(assembly, reader.GetString(resource.Name), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content))));
            }
        }

        return found;
    }

    /// <summary>
    /// The <see cref="DeclaredKind.EmbeddedResource"/> declarations that a linked assembly embeds: a resource whose logical
    /// name is the declared file name, or ends with it after a dot or slash, and whose content has the declared SHA-256. The
    /// check runs one way: not every embedded resource is third-party, so an undeclared resource needs nothing, and a
    /// declaration that matches nothing is not listed. A resource of a declared name with another SHA-256 is a stale
    /// declaration and a problem.
    /// </summary>
    public static (IReadOnlyList<DeclaredNativeLibrary> Matched, IReadOnlyList<string> Problems) MatchEmbeddedResources(
        IReadOnlyList<EmbeddedResource> resources, IReadOnlyList<DeclaredNativeLibrary> declared)
    {
        static bool Named(EmbeddedResource resource, string name) =>
            resource.LogicalName == name
            || resource.LogicalName.EndsWith("." + name, StringComparison.Ordinal)
            || resource.LogicalName.EndsWith("/" + name, StringComparison.Ordinal);
        var matched = new List<DeclaredNativeLibrary>();
        var problems = new List<string>();
        foreach (var entry in declared.Where(native => native.Kind == DeclaredKind.EmbeddedResource))
        {
            var named = resources.Where(resource => Named(resource, entry.Name)).ToList();
            if (named.Any(resource => string.Equals(resource.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                matched.Add(entry);
            else if (named.Count > 0)
                problems.AddRange(named.Select(resource =>
                    $"{Path.GetFileName(resource.Assembly)} embeds {resource.LogicalName} with SHA-256 {resource.Sha256}, not the {entry.Sha256} that the declaration of {entry.Name} states."));
        }

        return (matched, problems);
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

    private static string Verbatim(string path)
    {
        var text = File.ReadAllText(path);
        return text.EndsWith('\n') ? text : text + "\n";
    }

    private static string Normal(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n') + "\n";
}
