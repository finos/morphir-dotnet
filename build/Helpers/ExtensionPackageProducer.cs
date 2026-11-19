// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Extensions.Installation;
using Morphir.Mep;
using Morphir.Toolchain;

namespace Morphir.Build.Helpers;

/// <summary>Existing-format package metadata, without installation or readiness authority.</summary>
public sealed record PreparedExtensionPackage(byte[] Release, byte[] Manifest);

/// <summary>Feeds actual publish-file observations to the installer's canonical package writer.</summary>
public static class ExtensionPackageProducer
{
    public static PreparedExtensionPackage Prepare(byte[] releaseTemplate, string folder, string archive, string rid, string entryPoint)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        RejectReparsePoints(folder);
        if (!InstallationPlatform.TryParse(rid, out var platform, out var platformFailure))
            throw new InvalidOperationException(platformFailure.ToString());
        if (!DistributionIdentifiers.TryParseDeclaredPath(entryPoint, out var entry, out var entryFailure))
            throw new InvalidOperationException(entryFailure.ToString());
        if (!DistributionIdentifiers.TryParseArtifactFileName(Path.GetFileName(archive), out var archiveName, out var nameFailure))
            throw new InvalidOperationException(nameFailure.ToString());
        var executables = ReleaseArchive.ExecutablesFor(entryPoint);
        var files = ReleaseArchive.Files(folder).Select(relative =>
        {
            var path = Path.Combine(folder, relative);
            var current = path;
            while (!StringComparer.OrdinalIgnoreCase.Equals(current, Path.GetDirectoryName(Path.GetFullPath(folder))))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Package producer input contains a reparse point.");
                current = Path.GetDirectoryName(current)!;
            }
            if (!DistributionIdentifiers.TryParseDeclaredPath(relative, out var declared, out var failure))
                throw new InvalidOperationException(failure.ToString());
            return PackageFile.Create(declared, new FileInfo(path).Length, Sha256Digest.Create(ReleaseArchive.Sha256(path)), executables.Contains(relative))
                .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
        }).ToArray();
        var manifest = PackageManifest.FromFiles(Array.AsReadOnly(files))
            .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
        var release = PackageRelease.PrepareWholeArchive(releaseTemplate, platform, archiveName,
            Sha256Digest.Create(ReleaseArchive.Sha256(archive)), manifest, entry)
            .Match(value => value, error => throw new InvalidOperationException(error.ToString()));
        return new(release, manifest.ToBytes());
    }

    private static void RejectReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Package producer input contains a reparse point.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Package producer input contains a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
            }
        }
    }
}
