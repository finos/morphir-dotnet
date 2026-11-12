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
    // Canonical, so the paths a host trace reports compare equal to it: on macOS the temporary folder is reached
    // through the /var link, and a Windows TEMP can be a short name.
    private static readonly string RunRoot = CanonicalPath.Of(Path.Combine(Path.GetTempPath(), $"morphir packaged e2e {Guid.NewGuid():N}"));

    private PackagedInstallation(PackagedArtifact artifact, string directory, string launchPath, string artifactRoot)
    {
        Artifact = artifact;
        Directory = directory;
        LaunchPath = launchPath;
        ArtifactRoot = artifactRoot;
    }

    public PackagedArtifact Artifact { get; }

    /// <summary>The installation directory: the extracted archive, or the tool path.</summary>
    public string Directory { get; }

    /// <summary>The absolute path of the executable to launch.</summary>
    public string LaunchPath { get; }

    /// <summary>
    /// The folder whose paths, relative to it, are the artifact's entry paths: the installation directory for an
    /// archive, the tool store's package folder for the tool package.
    /// </summary>
    public string ArtifactRoot { get; }

    /// <summary>The installation of the packaged executable type <paramref name="type"/>, installed on first use.</summary>
    public static PackagedInstallation For(string type)
    {
        if (!ExecutableLocator.IsPackaged(type))
            throw new InvalidOperationException(
                $"'{type}' is not a packaged executable type. Set MORPHIR_EXECUTABLE_TYPE to one of {string.Join(", ", ExecutableLocator.PackagedTypes)}.");
        return Installed.GetOrAdd(type, key => new Lazy<PackagedInstallation>(() => Install(key, $"{key} install"))).Value;
    }

    /// <summary>
    /// A second installation of the packaged executable type <paramref name="type"/>, made the same way into its own
    /// directory <c>&lt;type&gt; &lt;name&gt;</c>, for a scenario that changes it. It is never shared, and it is removed
    /// with the others after the test run.
    /// </summary>
    public static PackagedInstallation InstallAnother(string type, string name)
    {
        For(type);
        return Install(type, $"{type} {name}");
    }

    /// <summary>Removes every installation of this test run.</summary>
    public static void RemoveAll() => TolerantDirectory.TryDelete(RunRoot);

    private static PackagedInstallation Install(string deployment, string directoryName)
    {
        var locator = new ExecutableLocator();
        var artifact = PackagedArtifact.Find(locator.ReleaseDirectory, deployment, locator.CurrentRid);
        var sha256 = ReleaseFiles.Sha256(artifact.ArchivePath);
        if (sha256 != artifact.Sha256)
            throw new InvalidOperationException($"{artifact.ArchivePath} has SHA-256 {sha256}, not the {artifact.Sha256} of its launch metadata.");

        var directory = Path.Combine(RunRoot, directoryName);
        if (System.IO.Directory.Exists(directory))
            throw new InvalidOperationException($"{directory} is already installed in this test run.");
        System.IO.Directory.CreateDirectory(directory);

        if (deployment == "dotnet-tool")
            return InstallTool(artifact, directory, directoryName);

        if (artifact.ArchivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var gzip = new GZipStream(File.OpenRead(artifact.ArchivePath), CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, directory, overwriteFiles: false);
        }
        else
        {
            ZipFile.ExtractToDirectory(artifact.ArchivePath, directory);
        }

        return new PackagedInstallation(artifact, directory, Path.Combine(directory, artifact.LaunchPath.Replace('/', Path.DirectorySeparatorChar)), directory);
    }

    /// <summary>
    /// Installs the tool package with the SDK, from a feed that holds only that package, into a tool path. This is the
    /// documented installation of the dotnet-tool offering; the SDK is its installation prerequisite. The install runs
    /// in the same kind of sandbox as the runs, with its own NuGet packages folder, and the package that the tool store
    /// keeps must be the packed file.
    /// </summary>
    private static PackagedInstallation InstallTool(PackagedArtifact artifact, string directory, string directoryName)
    {
        using var sandbox = TestSandbox.Create("morphir-packaged-install-");
        var feed = System.IO.Directory.CreateDirectory(Path.Combine(RunRoot, $"{directoryName} feed")).FullName;
        File.Copy(artifact.ArchivePath, Path.Combine(feed, artifact.Artifact), overwrite: true);
        var config = Path.Combine(RunRoot, $"{directoryName}.nuget.config");
        File.WriteAllText(config,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />" +
            $"<add key=\"morphir-release\" value=\"{System.Security.SecurityElement.Escape(feed)}\" /></packageSources></configuration>");

        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = ChildProcess.StartInfo(dotnet, "tool", "install", "Morphir.Tool", "--version", artifact.Release, "--tool-path", directory, "--configfile", config);
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start, new SandboxOptions { Boundary = sandbox.Root });
        // A contained packages folder: the install neither reads nor writes the machine's NuGet cache.
        start.Environment["NUGET_PACKAGES"] = System.IO.Directory.CreateDirectory(Path.Combine(sandbox.Root, "nuget packages")).FullName;
        var result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes(3)).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dotnet tool install failed with exit code {result.ExitCode}:\n{result.CombinedOutput}");
        if (StoredPackageProblem(directory, "Morphir.Tool", artifact.Release, artifact.ArchivePath) is { } problem)
            throw new InvalidOperationException(problem);

        var launch = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet-morphir.exe" : "dotnet-morphir");
        return new PackagedInstallation(artifact, directory, launch, StorePackageFolder(directory, "Morphir.Tool", artifact.Release));
    }

    /// <summary>The tool store's folder of the extracted package: <c>.store/&lt;id&gt;/&lt;version&gt;/&lt;id&gt;/&lt;version&gt;</c>, in lowercase.</summary>
    public static string StorePackageFolder(string toolPath, string packageId, string version)
    {
        var id = packageId.ToLowerInvariant();
        var lower = version.ToLowerInvariant();
        return Path.Combine(toolPath, ".store", id, lower, id, lower);
    }

    /// <summary>
    /// Null when the tool store of <paramref name="toolPath"/> keeps exactly <paramref name="packed"/> for
    /// <paramref name="packageId"/> <paramref name="version"/>; otherwise what differs. The store path is
    /// <c>.store/&lt;id&gt;/&lt;version&gt;/&lt;id&gt;/&lt;version&gt;/&lt;id&gt;.&lt;version&gt;.nupkg</c>, in lowercase.
    /// </summary>
    public static string? StoredPackageProblem(string toolPath, string packageId, string version, string packed)
    {
        var id = packageId.ToLowerInvariant();
        var lower = version.ToLowerInvariant();
        var stored = Path.Combine(toolPath, ".store", id, lower, id, lower, $"{id}.{lower}.nupkg");
        if (!File.Exists(stored)) return $"The tool path {toolPath} has no stored package {stored}.";
        var storedHash = ReleaseFiles.Sha256(stored);
        var packedHash = ReleaseFiles.Sha256(packed);
        return storedHash == packedHash
            ? null
            : $"The installed {stored} has SHA-256 {storedHash}, not the packed {packed} ({packedHash}).";
    }
}

/// <summary>Removes the packaged installations after the test run.</summary>
[Binding]
public static class PackagedInstallationCleanup
{
    [AfterTestRun]
    public static void RemoveInstallations() => PackagedInstallation.RemoveAll();
}
