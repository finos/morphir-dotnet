// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Morphir.Build.Helpers;

/// <summary>One file that an artifact ships, with its extracted size.</summary>
public sealed record ShippedFile(string Path, long Bytes);

/// <summary>
/// What one artifact ships (task 2.4): every file, read from the artifact itself, with the archive size and the
/// extracted total. A build output written beside the artifact as <c>&lt;artifact&gt;.build-contents.json</c>. It is not a
/// public contract and sets no size target.
/// </summary>
public sealed record ShippedContents(
    string Artifact,
    string Deployment,
    string Platform,
    long ArchiveBytes,
    int FileCount,
    long ExtractedBytes,
    IReadOnlyList<ShippedFile> Files)
{
    private const string Note = "Build output of morphir-dotnet, not a public contract and not a size target: every file this artifact ships, read from the artifact.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The file name of the contents list beside an artifact.</summary>
    public static string FileNameFor(string artifact) => $"{artifact}.build-contents.json";

    /// <summary>Reads every file entry of <paramref name="archivePath"/>: a zip archive, a tool package or a tar.gz archive.</summary>
    public static ShippedContents Read(ReleaseArtifact artifact, string archivePath)
    {
        var files = new List<ShippedFile>();
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var gzip = new GZipStream(File.OpenRead(archivePath), CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    files.Add(new ShippedFile(entry.Name, entry.Length));
        }
        else
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
                if (!entry.FullName.EndsWith('/'))
                    files.Add(new ShippedFile(entry.FullName, entry.Length));
        }

        files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new ShippedContents(artifact.FileName, artifact.Deployment.Id, artifact.Platform?.Rid ?? "any",
            new FileInfo(archivePath).Length, files.Count, files.Sum(file => file.Bytes), files);
    }

    public static string ToJson(ShippedContents contents) =>
        JsonSerializer.Serialize(new
        {
            note = Note,
            contents.Artifact,
            contents.Deployment,
            contents.Platform,
            contents.ArchiveBytes,
            contents.FileCount,
            contents.ExtractedBytes,
            contents.Files,
        }, Options) + "\n";

    public static ShippedContents Parse(string json) =>
        JsonSerializer.Deserialize<ShippedContents>(json, Options) ?? throw new InvalidDataException("Empty contents file.");

    /// <summary>
    /// A Markdown report over several artifacts: one row of totals for each, in deployment and platform order, then every
    /// shipped file of each artifact.
    /// </summary>
    public static string SizeReport(IEnumerable<ShippedContents> artifacts)
    {
        var ordered = artifacts
            .OrderBy(contents => Order(Morphir.Build.Helpers.Deployment.All.Select(deployment => deployment.Id), contents.Deployment))
            .ThenBy(contents => Order(TargetPlatform.All.Select(platform => platform.Rid), contents.Platform))
            .ThenBy(contents => contents.Artifact, StringComparer.Ordinal)
            .ToList();

        var report = new StringBuilder();
        report.AppendLine("# Shipped sizes");
        report.AppendLine();
        report.AppendLine("Build output of morphir-dotnet, not a public contract and not a size target. Each artifact's files are read from the artifact. Extracted bytes are the sum of the shipped files.");
        report.AppendLine();
        report.AppendLine("| Deployment | Platform | Artifact | Archive bytes | Files | Extracted bytes |");
        report.AppendLine("|---|---|---|---|---|---|");
        foreach (var contents in ordered)
            report.AppendLine($"| {contents.Deployment} | {contents.Platform} | `{contents.Artifact}` | {Number(contents.ArchiveBytes)} | {contents.FileCount} | {Number(contents.ExtractedBytes)} |");

        foreach (var contents in ordered)
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

    private static int Order(IEnumerable<string> known, string value)
    {
        var index = known.ToList().IndexOf(value);
        return index < 0 ? int.MaxValue : index;
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
