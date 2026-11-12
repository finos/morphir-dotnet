// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Morphir.Build.Helpers;

// The WASM engine each packaged offering carries (tasks 5.2 and 5.3 of the OpenSpec change add-wasm-extension-host).
// PackRelease reads the engine libraries from the artifact it has just packed, checks them against the engine the build
// pins, and writes what it found into the artifact's build-contents.json. This file is pure apart from reading files.

/// <summary>How an artifact carries the WASM engine.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EngineDelivery>))]
public enum EngineDelivery
{
    /// <summary>The Wasmtime package's native library, a file of its own in the artifact.</summary>
    [JsonStringEnumMemberName("separate-library")]
    SeparateLibrary,

    /// <summary>Linked statically into the executable from the pinned Wasmtime C API archive.</summary>
    [JsonStringEnumMemberName("linked")]
    Linked,
}

/// <summary>The WASM engine that one artifact carries, for one platform. A build output, not a public contract.</summary>
/// <param name="Path">For a separate library, its path in the artifact; for a linked engine, the executable it is linked into.</param>
/// <param name="Platform">The runtime identifier the engine is built for.</param>
/// <param name="Sha256">For a separate library, the file's SHA-256; for a linked engine, the pinned C API archive's.</param>
/// <param name="Delivery">A separate library or linked in.</param>
/// <param name="Version">The Wasmtime version: the package's for a separate library, the C API archive's for a linked engine.</param>
/// <param name="Source">What the digest was checked against: the package file, or the C API archive's URL.</param>
public sealed record PackagedEngine(string Path, string Platform, string Sha256, EngineDelivery Delivery, string Version, string Source);

/// <summary>One engine library file in an artifact: its path in the artifact and its SHA-256.</summary>
public sealed record EngineLibraryEntry(string Path, string Sha256);

/// <summary>The Wasmtime package's native library for one platform: <c>runtimes/&lt;rid&gt;/native/&lt;file&gt;</c>.</summary>
public sealed record PinnedLibrary(string Rid, string FileName, string Sha256);

/// <summary>The pinned Wasmtime C API archive for one platform, from <c>build/wasmtime/WasmtimeCApi.props</c>.</summary>
public sealed record PinnedArchive(string Rid, string Url, string Sha256);

/// <summary>What the engine check found in an artifact, and what contradicts the pinned engine; no problems when it agrees.</summary>
public sealed record EngineCheck(IReadOnlyList<PackagedEngine> Engines, IReadOnlyList<string> Problems);

/// <summary>
/// The engine the build pins: the Wasmtime package that the restore resolved, with the SHA-256 of its native library for
/// each platform, and the C API archives that a static link uses. Nothing here is a hard-coded digest: the libraries are
/// hashed from the restored package and the archives' digests are the committed pins.
/// </summary>
public sealed record EnginePin(
    string PackageVersion,
    IReadOnlyDictionary<string, PinnedLibrary> PackageLibraries,
    string CApiVersion,
    IReadOnlyDictionary<string, PinnedArchive> CApiArchives)
{
    private const string Package = "Wasmtime";

    /// <summary>
    /// The pin from a restore's <c>project.assets.json</c> (the Wasmtime package it resolved, found in one of its package
    /// folders) and the text of <c>WasmtimeCApi.props</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The restore has no Wasmtime package, or its folder is not on disk.</exception>
    public static EnginePin Read(string assetsJson, string cApiProps)
    {
        using var assets = JsonDocument.Parse(assetsJson);
        var library = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .FirstOrDefault(entry => entry.Name.StartsWith($"{Package}/", StringComparison.OrdinalIgnoreCase));
        if (library.Value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"The restore resolved no {Package} package, so the engine it ships cannot be checked.");
        var version = library.Name[(Package.Length + 1)..];
        var relative = library.Value.GetProperty("path").GetString()!;
        var folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();
        var folder = folders.Select(root => Path.Combine(root, relative)).FirstOrDefault(Directory.Exists)
            ?? throw new InvalidOperationException($"The {Package} {version} package is in none of the restore's package folders: {string.Join(", ", folders)}.");

        var libraries = new Dictionary<string, PinnedLibrary>(StringComparer.Ordinal);
        var runtimes = Path.Combine(folder, "runtimes");
        if (Directory.Exists(runtimes))
        {
            foreach (var platform in Directory.GetDirectories(runtimes).Order(StringComparer.Ordinal))
            {
                var rid = Path.GetFileName(platform);
                var file = Path.Combine(platform, "native", PackagedEngines.LibraryFileName(rid));
                if (File.Exists(file))
                    libraries[rid] = new PinnedLibrary(rid, Path.GetFileName(file), ReleaseArchive.Sha256(file));
            }
        }

        var props = XDocument.Parse(cApiProps);
        var cApiVersion = props.Descendants("MorphirWasmtimeCApiVersion").Single().Value.Trim();
        var archives = props.Descendants("MorphirWasmtimeCApiArchive").ToDictionary(
            element => (string)element.Attribute("Include")!,
            element => new PinnedArchive((string)element.Attribute("Include")!, element.Element("Url")!.Value.Trim(), element.Element("Sha256")!.Value.Trim()),
            StringComparer.Ordinal);
        return new EnginePin(version, libraries, cApiVersion, archives);
    }

    /// <summary>The pin for <paramref name="project"/>, from its last restore and the repository's C API pins.</summary>
    public static EnginePin Load(string repositoryRoot, string project)
    {
        var assets = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(repositoryRoot, project)))!, "obj", "project.assets.json");
        if (!File.Exists(assets)) throw new InvalidOperationException($"{assets} is missing: restore {project} first.");
        return Read(File.ReadAllText(assets), File.ReadAllText(Path.Combine(repositoryRoot, "build", "wasmtime", "WasmtimeCApi.props")));
    }
}

/// <summary>Reads and checks the WASM engine of a packaged artifact.</summary>
public static class PackagedEngines
{
    /// <summary>The MSBuild switch that links the engine into a Native AOT executable (build/wasmtime/WasmtimeStaticLink.targets).</summary>
    public const string StaticLinkProperty = "MorphirWasmtimeStaticLink";

    private static readonly string[] LibraryFileNames = ["wasmtime.dll", "libwasmtime.so", "libwasmtime.dylib"];

    /// <summary>The engine library's file name on a platform, as the Wasmtime package names it.</summary>
    public static string LibraryFileName(string rid) =>
        rid.StartsWith("win-", StringComparison.Ordinal) ? "wasmtime.dll"
        : rid.StartsWith("osx-", StringComparison.Ordinal) ? "libwasmtime.dylib"
        : "libwasmtime.so";

    /// <summary>How <paramref name="deployment"/> carries the engine: linked when its build turns the static link on.</summary>
    public static EngineDelivery DeliveryFor(Deployment deployment) =>
        deployment.BuildProperties.TryGetValue(StaticLinkProperty, out var value) && value == "true"
            ? EngineDelivery.Linked
            : EngineDelivery.SeparateLibrary;

    /// <summary>The engine library files in an archive (zip, tool package or tar.gz), with their SHA-256, in ordinal path order.</summary>
    public static IReadOnlyList<EngineLibraryEntry> Read(string archivePath)
    {
        var libraries = new List<EngineLibraryEntry>();
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var gzip = new GZipStream(File.OpenRead(archivePath), CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && IsLibrary(entry.Name))
                    libraries.Add(new EngineLibraryEntry(entry.Name, Hash(entry.DataStream ?? Stream.Null)));
            }
        }
        else
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/') && IsLibrary(entry.FullName)))
            {
                using var stream = entry.Open();
                libraries.Add(new EngineLibraryEntry(entry.FullName, Hash(stream)));
            }
        }

        libraries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return libraries;
    }

    /// <summary>
    /// The engine of <paramref name="artifact"/>, given the engine library files it holds, checked against
    /// <paramref name="pin"/>:
    /// <list type="bullet">
    /// <item>each separate library is the package's file for its platform, byte for byte (by SHA-256);</item>
    /// <item>an artifact for one platform holds exactly that platform's library and no other platform's; the portable
    /// tool package holds one for each of the six platforms;</item>
    /// <item>a deployment that links the engine in holds no engine library, and its platform has a pinned C API archive;</item>
    /// <item>an artifact that carries a separate library is not declared single-file (task 5.3).</item>
    /// </list>
    /// </summary>
    public static EngineCheck Check(ReleaseArtifact artifact, IReadOnlyList<EngineLibraryEntry> libraries, EnginePin pin)
    {
        var engines = new List<PackagedEngine>();
        var problems = new List<string>();
        var rid = artifact.Platform?.Rid;
        var delivery = DeliveryFor(artifact.Deployment);

        foreach (var library in libraries)
        {
            // A library below runtimes/<rid>/native/ is for that platform. One elsewhere is for the artifact's platform
            // when it has that platform's file name, and otherwise for the platform whose package file it is.
            var fileName = Path.GetFileName(library.Path);
            var platform = PlatformOf(library.Path)
                ?? (rid is not null && fileName == LibraryFileName(rid) ? rid : null)
                ?? pin.PackageLibraries.Values.FirstOrDefault(candidate => string.Equals(candidate.Sha256, library.Sha256, StringComparison.OrdinalIgnoreCase))?.Rid;
            if (platform is null)
            {
                problems.Add(rid is null
                    ? $"{library.Path} is an engine library outside runtimes/<rid>/native/ and is no platform's package file, so its platform is unknown"
                    : $"{library.Path} is not the engine library of {rid}, which is {LibraryFileName(rid)}, nor any platform's package file");
                continue;
            }

            pin.PackageLibraries.TryGetValue(platform, out var expected);
            engines.Add(new PackagedEngine(library.Path, platform, library.Sha256, EngineDelivery.SeparateLibrary, pin.PackageVersion,
                $"Wasmtime {pin.PackageVersion} package, runtimes/{platform}/native/{expected?.FileName ?? LibraryFileName(platform)}"));

            if (rid is not null && platform != rid)
                problems.Add($"{library.Path} is the engine library of {platform}, but the artifact is for {rid}");
            else if (fileName != LibraryFileName(platform))
                problems.Add($"{library.Path} is not the engine library of {platform}, which is {LibraryFileName(platform)}");
            else if (expected is null)
                problems.Add($"{library.Path}: the Wasmtime {pin.PackageVersion} package has no engine library for {platform}");
            else if (!string.Equals(library.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                var other = pin.PackageLibraries.Values.FirstOrDefault(candidate => string.Equals(candidate.Sha256, library.Sha256, StringComparison.OrdinalIgnoreCase));
                problems.Add($"{library.Path} has SHA-256 {library.Sha256}, but the Wasmtime {pin.PackageVersion} package's runtimes/{platform}/native/{expected.FileName} has {expected.Sha256}"
                    + (other is null ? "" : $" (the artifact's file is the package's {other.Rid} library)"));
            }
        }

        if (delivery == EngineDelivery.Linked)
        {
            // With a library beside it, the executable cannot be shown to carry the engine, so no linked engine is recorded.
            foreach (var library in libraries)
                problems.Add($"{artifact.Deployment.Id} links the engine into {artifact.Launch.LaunchPath}, but the artifact also carries {library.Path} as a separate library");
            if (libraries.Count == 0)
            {
                if (rid is null || !pin.CApiArchives.TryGetValue(rid, out var archive))
                    problems.Add($"{artifact.Deployment.Id} links the engine in, but no Wasmtime C API archive is pinned for {rid ?? "any"}");
                else
                    engines.Add(new PackagedEngine(artifact.Launch.LaunchPath, rid, archive.Sha256, EngineDelivery.Linked, pin.CApiVersion, archive.Url));
            }
        }
        else
        {
            var platforms = rid is null ? TargetPlatform.All.Select(platform => platform.Rid).ToList() : [rid];
            foreach (var platform in platforms)
            {
                var count = engines.Count(engine => engine.Platform == platform);
                if (count == 0)
                    problems.Add($"the artifact carries no engine library for {platform}: expected {LibraryFileName(platform)}, the Wasmtime {pin.PackageVersion} package's runtimes/{platform}/native/{LibraryFileName(platform)}");
                else if (count > 1)
                    problems.Add($"the artifact carries {count} engine libraries for {platform}: {string.Join(", ", engines.Where(engine => engine.Platform == platform).Select(engine => engine.Path))}");
            }
        }

        // A platform that ships a separate library does not claim single-file delivery.
        if (artifact.Launch.Layout == "single-file")
        {
            foreach (var engine in engines.Where(engine => engine.Delivery == EngineDelivery.SeparateLibrary))
                problems.Add($"the artifact is declared single-file, but it carries the engine as the separate library {engine.Path}; an artifact with a separate engine library is not single-file");
        }

        return new EngineCheck(engines, problems);
    }

    /// <summary>The runtime identifier of a path below <c>runtimes/&lt;rid&gt;/native/</c>, or null for any other path.</summary>
    private static string? PlatformOf(string path)
    {
        var segments = path.Split('/');
        for (var index = 0; index + 2 < segments.Length; index++)
        {
            if (segments[index] == "runtimes" && segments[index + 2] == "native")
                return segments[index + 1];
        }

        return null;
    }

    private static bool IsLibrary(string path) => LibraryFileNames.Contains(Path.GetFileName(path), StringComparer.Ordinal);

    private static string Hash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));
}
