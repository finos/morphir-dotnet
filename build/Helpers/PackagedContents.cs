// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Morphir.Build.Helpers;

/// <summary>One file that an artifact contains, with its extracted size.</summary>
public sealed record PackagedFile(string Path, long Bytes);

/// <summary>Where and when an artifact was packed: the commit, whether the working tree was clean, and the time.</summary>
public sealed record PackOrigin(string Commit, bool WorkingTreeClean, DateTimeOffset PackedAt);

/// <summary>
/// What one packaged artifact contains (task 2.4): every file, read from the artifact itself, with the release, the
/// artifact's SHA-256, where it was packed, the archive size and the extracted total. A build output written beside the
/// artifact as <c>&lt;artifact&gt;.build-contents.json</c>. It is not a public contract and sets no size target.
/// </summary>
public sealed record PackagedContents(
    string Artifact,
    string Release,
    string Deployment,
    string Platform,
    string Sha256,
    long ArchiveBytes,
    int FileCount,
    long ExtractedBytes,
    IReadOnlyList<PackagedFile> Files,
    PackOrigin? Origin)
{
    private const string Note = "Build output of morphir-dotnet, not a public contract and not a size target: every file this artifact contains, read from the artifact.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// The folders that hold satellite resource assemblies (<c>*.resources.dll</c>, one folder per culture), in ordinal
    /// order. They hold the translated messages of the F# compiler and FSharp.Core.
    /// </summary>
    public IReadOnlyList<string> SatelliteFolders =>
        Files.Where(file => file.Path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) && file.Path.Contains('/'))
            .Select(file => file.Path[..file.Path.LastIndexOf('/')])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// For a NuGet package, the entries that are package plumbing rather than tool files: the nuspec, <c>_rels/</c>,
    /// <c>[Content_Types].xml</c> and <c>package/</c>. Zero for an archive.
    /// </summary>
    public int PackageMetadataEntries =>
        Artifact.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            ? Files.Count(file => IsPackageMetadata(file.Path))
            : 0;

    /// <summary>The file name of the contents list beside an artifact.</summary>
    public static string FileNameFor(string artifact) => $"{artifact}.build-contents.json";

    /// <summary>
    /// Reads every file entry of <paramref name="archivePath"/>: a zip archive, a tool package or a tar.gz archive.
    /// A tar link or any other entry that is neither a file nor a directory is refused, never dropped.
    /// </summary>
    public static PackagedContents Read(ReleaseArtifact artifact, string archivePath, PackOrigin? origin)
    {
        var files = new List<PackagedFile>();
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var gzip = new GZipStream(File.OpenRead(archivePath), CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                switch (entry.EntryType)
                {
                    case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                        files.Add(new PackagedFile(entry.Name, entry.Length));
                        break;
                    case TarEntryType.Directory:
                        break;
                    default:
                        throw new InvalidDataException(
                            $"{archivePath} has the {entry.EntryType} entry {entry.Name}{(entry.LinkName is { Length: > 0 } link ? $" (link to {link})" : "")}. The size report counts files only, and release archives hold no links.");
                }
            }
        }
        else
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
                if (!entry.FullName.EndsWith('/'))
                    files.Add(new PackagedFile(entry.FullName, entry.Length));
        }

        files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new PackagedContents(artifact.FileName, artifact.Release, artifact.Deployment.Id, artifact.Platform?.Rid ?? "any",
            ReleaseArchive.Sha256(archivePath), new FileInfo(archivePath).Length, files.Count, files.Sum(file => file.Bytes), files, origin);
    }

    public static string ToJson(PackagedContents contents) =>
        JsonSerializer.Serialize(new
        {
            note = Note,
            contents.Artifact,
            contents.Release,
            contents.Deployment,
            contents.Platform,
            contents.Sha256,
            contents.Origin,
            contents.ArchiveBytes,
            contents.FileCount,
            contents.ExtractedBytes,
            contents.Files,
        }, Options) + "\n";

    public static PackagedContents Parse(string json) =>
        JsonSerializer.Deserialize<PackagedContents>(json, Options) ?? throw new InvalidDataException("Empty contents file.");

    /// <summary>
    /// A Markdown report over several artifacts: one row of totals for each artifact of <paramref name="release"/>, in
    /// deployment and platform order, then the artifacts of any other release in their own section, then every file of
    /// each artifact.
    /// </summary>
    public static string SizeReport(IEnumerable<PackagedContents> artifacts, string release)
    {
        var ordered = artifacts
            .OrderBy(contents => Order(Morphir.Build.Helpers.Deployment.All.Select(deployment => deployment.Id), contents.Deployment))
            .ThenBy(contents => Order(TargetPlatform.All.Select(platform => platform.Rid), contents.Platform))
            .ThenBy(contents => contents.Release, StringComparer.Ordinal)
            .ThenBy(contents => contents.Artifact, StringComparer.Ordinal)
            .ToList();
        var current = ordered.Where(contents => contents.Release == release).ToList();
        var others = ordered.Where(contents => contents.Release != release).ToList();

        var report = new StringBuilder();
        report.AppendLine("# Packaged sizes");
        report.AppendLine();
        report.AppendLine($"Build output of morphir-dotnet, not a public contract and not a size target, for release {release}. Each artifact's files are read from the artifact. Extracted bytes are the sum of the files it contains. Satellite resource folders hold the translated resources of one culture each.");
        report.AppendLine();
        Totals(report, current);
        if (current.Any(contents => contents.PackageMetadataEntries > 0) || current.Any(contents => contents.Deployment == "dotnet-tool"))
        {
            report.AppendLine();
            report.AppendLine("For `dotnet-tool (package)` the files are the NuGet package's entries, including its package metadata (the nuspec, `_rels/`, `[Content_Types].xml` and `package/`), and the extracted bytes are the sum of those entries, not the installed size: `dotnet tool install` adds a tool store and an apphost.");
        }

        if (others.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Other releases in this folder");
            report.AppendLine();
            report.AppendLine($"These contents files are of releases other than {release} ({string.Join(", ", others.Select(contents => contents.Release).Distinct(StringComparer.Ordinal))}). They are listed apart and are not part of the totals above.");
            report.AppendLine();
            Totals(report, others);
        }

        foreach (var contents in current.Concat(others))
        {
            report.AppendLine();
            report.AppendLine($"## `{contents.Artifact}`");
            report.AppendLine();
            report.AppendLine("| File | Bytes |");
            report.AppendLine("|---|---|");
            foreach (var file in contents.Files) report.AppendLine($"| `{file.Path}` | {Number(file.Bytes)} |");
        }

        return report.ToString().Replace("\r\n", "\n");
    }

    private static void Totals(StringBuilder report, IReadOnlyList<PackagedContents> rows)
    {
        report.AppendLine("| Deployment | Platform | Release | Artifact | SHA-256 | Archive bytes | Files | Extracted bytes | Satellite resource folders |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var contents in rows)
        {
            var deployment = contents.Deployment == "dotnet-tool" ? "dotnet-tool (package)" : contents.Deployment;
            var files = contents.PackageMetadataEntries > 0
                ? $"{contents.FileCount} ({contents.PackageMetadataEntries} package metadata)"
                : contents.FileCount.ToString(CultureInfo.InvariantCulture);
            report.AppendLine($"| {deployment} | {contents.Platform} | {contents.Release} | `{contents.Artifact}` | `{contents.Sha256}` | {Number(contents.ArchiveBytes)} | {files} | {Number(contents.ExtractedBytes)} | {contents.SatelliteFolders.Count} |");
        }
    }

    private static bool IsPackageMetadata(string path) =>
        (!path.Contains('/') && path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
        || path.StartsWith("_rels/", StringComparison.Ordinal)
        || path == "[Content_Types].xml"
        || path.StartsWith("package/", StringComparison.Ordinal);

    private static int Order(IEnumerable<string> known, string value)
    {
        var index = known.ToList().IndexOf(value);
        return index < 0 ? int.MaxValue : index;
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
