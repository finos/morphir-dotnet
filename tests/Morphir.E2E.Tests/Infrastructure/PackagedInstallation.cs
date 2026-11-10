// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Infrastructure;

/// <summary>
/// A packaged artifact installed the way a user installs it: an archive extracted into a new directory whose path
/// contains a space, or the tool package installed with <c>dotnet tool install --tool-path</c>. Each executable type is
/// installed once per test run, after its SHA-256 is checked against its launch metadata.
/// </summary>
public sealed class PackagedInstallation
{
    private static readonly ConcurrentDictionary<string, Lazy<PackagedInstallation>> Installed = new(StringComparer.Ordinal);
    private static readonly string RunRoot = Path.Combine(Path.GetTempPath(), $"morphir packaged e2e {Guid.NewGuid():N}");

    private PackagedInstallation(PackagedArtifact artifact, string directory, string launchPath)
    {
        Artifact = artifact;
        Directory = directory;
        LaunchPath = launchPath;
    }

    public PackagedArtifact Artifact { get; }

    /// <summary>The installation directory: the extracted archive, or the tool path.</summary>
    public string Directory { get; }

    /// <summary>The absolute path of the executable to launch.</summary>
    public string LaunchPath { get; }

    /// <summary>The installation of the packaged executable type <paramref name="type"/>, installed on first use.</summary>
    public static PackagedInstallation For(string type)
    {
        if (!ExecutableLocator.IsPackaged(type))
            throw new InvalidOperationException(
                $"'{type}' is not a packaged executable type. Set MORPHIR_EXECUTABLE_TYPE to one of {string.Join(", ", ExecutableLocator.PackagedTypes)}.");
        return Installed.GetOrAdd(type, key => new Lazy<PackagedInstallation>(() => Install(key))).Value;
    }

    /// <summary>Removes every installation of this test run.</summary>
    public static void RemoveAll() => TolerantDirectory.TryDelete(RunRoot);

    private static PackagedInstallation Install(string deployment)
    {
        var locator = new ExecutableLocator();
        var artifact = PackagedArtifact.Find(locator.ReleaseDirectory, deployment, locator.CurrentRid);
        var sha256 = ReleaseFiles.Sha256(artifact.ArchivePath);
        if (sha256 != artifact.Sha256)
            throw new InvalidOperationException($"{artifact.ArchivePath} has SHA-256 {sha256}, not the {artifact.Sha256} of its launch metadata.");

        var directory = Path.Combine(RunRoot, $"{deployment} install");
        System.IO.Directory.CreateDirectory(directory);

        if (deployment == "dotnet-tool")
            return InstallTool(artifact, directory);

        if (artifact.ArchivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var gzip = new GZipStream(File.OpenRead(artifact.ArchivePath), CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, directory, overwriteFiles: false);
        }
        else
        {
            ZipFile.ExtractToDirectory(artifact.ArchivePath, directory);
        }

        return new PackagedInstallation(artifact, directory, Path.Combine(directory, artifact.LaunchPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Installs the tool package with the SDK, from a feed that holds only that package, into a tool path. This is the
    /// documented installation of the dotnet-tool offering; the SDK is its installation prerequisite.
    /// </summary>
    private static PackagedInstallation InstallTool(PackagedArtifact artifact, string directory)
    {
        var feed = System.IO.Directory.CreateDirectory(Path.Combine(RunRoot, "tool feed")).FullName;
        File.Copy(artifact.ArchivePath, Path.Combine(feed, artifact.Artifact), overwrite: true);
        var config = Path.Combine(RunRoot, "nuget.tool.config");
        File.WriteAllText(config,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />" +
            $"<add key=\"morphir-release\" value=\"{System.Security.SecurityElement.Escape(feed)}\" /></packageSources></configuration>");

        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = ChildProcess.StartInfo(dotnet, "tool", "install", "Morphir.Tool", "--version", artifact.Release, "--tool-path", directory, "--configfile", config);
        var result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dotnet tool install failed with exit code {result.ExitCode}:\n{result.CombinedOutput}");

        var launch = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet-morphir.exe" : "dotnet-morphir");
        return new PackagedInstallation(artifact, directory, launch);
    }
}

/// <summary>Removes the packaged installations after the test run.</summary>
[Binding]
public static class PackagedInstallationCleanup
{
    [AfterTestRun]
    public static void RemoveInstallations() => PackagedInstallation.RemoveAll();
}
