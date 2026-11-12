// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.InteropServices;
using Morphir.TestSupport.Packaging;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// Where the published and packaged forms of the product are: the <c>artifacts</c> folder of the repository, which
/// Morphir.CliArtifacts.targets writes into the test assembly, the release folder of the packaged offerings, and the
/// platform of this machine.
/// </summary>
/// <param name="ArtifactsDirectory">The <c>artifacts</c> folder that the publish targets write.</param>
/// <param name="ReleaseDirectory"><c>MORPHIR_RELEASE_DIR</c>, or <c>artifacts/release</c>: where PackRelease writes.</param>
/// <param name="Rid">The runtime identifier of this machine, for example <c>osx-arm64</c>.</param>
public sealed record ArtifactLocations(string ArtifactsDirectory, string ReleaseDirectory, string Rid)
{
    /// <summary>The metadata key of the <c>artifacts</c> folder in the test assembly.</summary>
    public const string ArtifactsMetadata = "Morphir.TestSupport.Artifacts";

    /// <summary>The release folder of the packaged offerings. Default: <c>artifacts/release</c>.</summary>
    public const string ReleaseDirectoryVariable = "MORPHIR_RELEASE_DIR";

    /// <summary>The locations from the metadata of a test assembly and the environment.</summary>
    public static ArtifactLocations Of(IReadOnlyDictionary<string, string> metadata, string testAssembly, Func<string, string?> variable)
    {
        if (!metadata.TryGetValue(ArtifactsMetadata, out var artifacts))
        {
            throw new TestSessionException(
                $"{testAssembly} has no path of the artifacts folder. Import tests/Morphir.TestSupport/build/Morphir.CliArtifacts.targets in its project.");
        }

        var release = variable(ReleaseDirectoryVariable) is { Length: > 0 } custom
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(custom))
            : Path.Combine(artifacts, "release");
        return new ArtifactLocations(artifacts, release, RuntimeIdentifiers.Current);
    }

    /// <summary>The metadata of <paramref name="testAssembly"/> that Morphir.CliArtifacts.targets writes.</summary>
    public static IReadOnlyDictionary<string, string> MetadataOf(Assembly testAssembly) =>
        testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith("Morphir.TestSupport.", StringComparison.Ordinal) && attribute.Value is not null)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!);
}

/// <summary>The runtime identifier of this machine.</summary>
public static class RuntimeIdentifiers
{
    /// <summary>For example <c>win-arm64</c>, <c>linux-x64</c> or <c>osx-arm64</c>.</summary>
    public static string Current { get; } = Of(
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux", RuntimeInformation.OSArchitecture);

    public static string Of(string os, Architecture architecture) => architecture switch
    {
        Architecture.X64 => $"{os}-x64",
        Architecture.Arm64 => $"{os}-arm64",
        _ => throw new TestSessionException($"The architecture {architecture} has no runtime identifier of a Morphir release."),
    };
}

/// <summary>
/// The published forms: the trimmed and untrimmed single-file executables and the Native AOT executable. Each is in
/// the folder of its own publish target, and a missing one stops the run with the command that publishes it. A form
/// never resolves to the executable of another form.
/// </summary>
public static class PublishedExecutables
{
    /// <summary>The executable of <paramref name="target"/>, or a <see cref="TestSessionException"/> with the publish command.</summary>
    public static string PathOf(TestTarget target, ArtifactLocations at, Func<string, bool> fileExists)
    {
        var (folder, publish) = target switch
        {
            TestTarget.Trimmed => ("single-file", "PublishSingleFile"),
            TestTarget.Untrimmed => ("single-file-untrimmed", "PublishSingleFileUntrimmed"),
            TestTarget.NativeAot => ("executables", "PublishExecutable"),
            _ => throw new ArgumentException($"The target {target.Name} is not a published executable.", nameof(target)),
        };

        var executable = Path.Combine(at.ArtifactsDirectory, folder, at.Rid, at.Rid.StartsWith("win-", StringComparison.Ordinal) ? "morphir.exe" : "morphir");
        return fileExists(executable)
            ? executable
            : throw new TestSessionException(
                $"The target {target.Name} has no executable at {executable}. Publish it: ./build.sh --target {publish} --rid {at.Rid}");
    }
}

/// <summary>
/// The packaged offering of a run, installed once before the first test into a folder of the run, and removed after
/// the last test.
/// </summary>
public sealed class PackagedTarget : IDisposable
{
    private PackagedTarget(string root, PackagedInstallation installation, ArtifactLocations at)
    {
        Root = root;
        Installation = installation;
        Locations = at;
    }

    /// <summary>The folder of the installations of this run. Its path has a space and no link.</summary>
    public string Root { get; }

    /// <summary>The installation of the offering, which each test of the run uses.</summary>
    public PackagedInstallation Installation { get; }

    public ArtifactLocations Locations { get; }

    /// <summary>The name of the offering in a launch metadata file and in <c>MORPHIR_TEST_TARGET</c>.</summary>
    public static string DeploymentOf(PackagedOffering offering) => new TestTarget.Packaged(offering).Name;

    /// <summary>
    /// Installs the offering of <paramref name="target"/> from the release folder. A missing artifact stops the run
    /// with the pack command; no build folder is used instead.
    /// </summary>
    public static PackagedTarget Install(TestTarget.Packaged target, ArtifactLocations at)
    {
        var root = CanonicalPath.Of(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"morphir packaged {Guid.NewGuid():N}")).FullName);
        try
        {
            var deployment = DeploymentOf(target.Offering);
            return new PackagedTarget(root, PackagedInstallation.Install(at.ReleaseDirectory, deployment, at.Rid, Path.Combine(root, $"{deployment} install")), at);
        }
        catch (InvalidOperationException error)
        {
            TolerantDirectory.TryDelete(root);
            throw new TestSessionException(error.Message);
        }
        catch
        {
            TolerantDirectory.TryDelete(root);
            throw;
        }
    }

    /// <summary>A second installation of the same offering into <paramref name="directory"/>, for a test that changes it.</summary>
    public PackagedInstallation InstallAnother(string directory) =>
        PackagedInstallation.Install(Locations.ReleaseDirectory, Installation.Artifact.Deployment, Locations.Rid, directory);

    public void Dispose() => TolerantDirectory.TryDelete(Root);
}
