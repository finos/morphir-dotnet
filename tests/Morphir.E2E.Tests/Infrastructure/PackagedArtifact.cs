// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// One packaged release artifact, read from the <c>&lt;artifact&gt;.build-launch.json</c> that the PackRelease target
/// writes beside it. That file is a build output of morphir-dotnet, not a public contract.
/// </summary>
public sealed record PackagedArtifact(
    string Deployment,
    string Platform,
    string Release,
    string Artifact,
    string Sha256,
    string LaunchPath,
    string Execution,
    bool BundledRuntime,
    string? RequiredRuntime,
    string? RequiredSdk,
    bool InProcessFSharpFrontend,
    string ArchivePath)
{
    /// <summary>
    /// The artifact of <paramref name="deployment"/> for <paramref name="rid"/> (or the portable <c>any</c>) in
    /// <paramref name="releaseDirectory"/>. Fails with the command that builds it when there is none.
    /// </summary>
    public static PackagedArtifact Find(string releaseDirectory, string deployment, string rid)
    {
        var candidates = Directory.Exists(releaseDirectory)
            ? Directory.GetFiles(releaseDirectory, "*.build-launch.json").Select(Read).ToList()
            : [];
        var matches = candidates.Where(artifact => artifact.Deployment == deployment && (artifact.Platform == rid || artifact.Platform == "any")).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No packaged {deployment} artifact for {rid} in {releaseDirectory}. Run the PackRelease target first: " +
                $"build --target PackRelease --deployment {deployment}{(deployment == "dotnet-tool" ? "" : $" --rid {rid}")}"),
            _ => throw new InvalidOperationException(
                $"{matches.Count} packaged {deployment} artifacts for {rid} in {releaseDirectory}: {string.Join(", ", matches.Select(match => match.Artifact))}. Keep one release there."),
        };
    }

    private static PackagedArtifact Read(string launchFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(launchFile));
        var root = document.RootElement;
        string? Optional(string name) => root.GetProperty(name).ValueKind == JsonValueKind.Null ? null : root.GetProperty(name).GetString();
        var artifact = root.GetProperty("artifact").GetString()!;
        return new PackagedArtifact(
            root.GetProperty("deployment").GetString()!,
            root.GetProperty("platform").GetString()!,
            root.GetProperty("release").GetString()!,
            artifact,
            root.GetProperty("sha256").GetString()!,
            root.GetProperty("launchPath").GetString()!,
            root.GetProperty("execution").GetString()!,
            root.GetProperty("bundledRuntime").GetBoolean(),
            Optional("requiredRuntime"),
            Optional("requiredSdk"),
            root.GetProperty("inProcessFSharpFrontend").GetBoolean(),
            Path.Combine(Path.GetDirectoryName(launchFile)!, artifact));
    }
}

/// <summary>File helpers for release artifacts.</summary>
public static class ReleaseFiles
{
    /// <summary>The lowercase hex SHA-256 of a file.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
