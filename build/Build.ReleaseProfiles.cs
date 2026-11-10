// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Morphir.Build.Helpers;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// The named release profiles (task 2.2, approved decision DEP-1): one publish and one pack target for the four
/// deployment offerings, driven by the profile model in build/Helpers/ReleaseProfiles.cs.
/// </summary>
/// <remarks>
/// <code>
/// dotnet run --project build/_build.csproj -- --target PackRelease --deployment native-aot --rid win-x64
/// dotnet run --project build/_build.csproj -- --target PackRelease --deployment self-contained --rid linux-arm64
/// dotnet run --project build/_build.csproj -- --target PackRelease --deployment framework-dependent --rid osx-arm64
/// dotnet run --project build/_build.csproj -- --target PackRelease --deployment dotnet-tool
/// </code>
/// Output: artifacts/release/{archive}, {archive}.build-launch.json and {archive}.build-contents.json, and the size report
/// artifacts/release/size-report.md over every artifact packed there, from the publish folder
/// artifacts/release/publish/{deployment}/{rid}/. The native-aot and managed profiles publish different projects that
/// both build an assembly named morphir, so their publish folders are separate.
/// </remarks>
partial class Build
{
    [Parameter("Deployment offering for PublishRelease and PackRelease: native-aot, self-contained, framework-dependent or dotnet-tool", Name = "deployment")]
    readonly string? DeploymentId;

    [Parameter("Output directory for release profile artifacts")]
    readonly AbsolutePath ReleaseDir = RootDirectory / "artifacts" / "release";

    /// <summary>
    /// Publish one release profile for one platform: <c>--deployment</c> and, except for <c>dotnet-tool</c>, <c>--rid</c>.
    /// The managed profiles stay untrimmed folders; native-aot is published with Native AOT on a build machine of the
    /// same operating system as the RID.
    /// </summary>
    Target PublishRelease => _ => _
        .Description("Publish one release profile (requires --deployment, and --rid except for dotnet-tool)")
        .Executes(() =>
        {
            var artifact = ResolveRequestedArtifact();
            var publishDirectory = ReleasePublishDirectory(artifact);
            publishDirectory.CreateOrCleanDirectory();

            if (artifact.Deployment.IsPortable)
            {
                // The same pack as the PackTool target, so the package id, command and invocation stay the same.
                var pack = new DotNetPackSettings()
                    .SetProject(RootDirectory / artifact.Deployment.Project)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(publishDirectory)
                    .AddProcessAdditionalArguments("-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false");
                foreach (var (key, value) in artifact.Deployment.BuildProperties) pack = pack.SetProperty(key, value);
                Serilog.Log.Information($"Packing {artifact.Deployment.Id} as {artifact.FileName}...");
                DotNetPack(pack);
            }
            else
            {
                var platform = artifact.Platform!;
                if (artifact.Deployment == Morphir.Build.Helpers.Deployment.NativeAot && platform.OperatingSystem != HostOperatingSystem())
                {
                    throw new Exception(
                        $"native-aot for {platform.Rid} needs a {platform.OperatingSystem} build machine: Native AOT does not compile for another operating system.");
                }

                var publish = ApplyBuildProperties(new DotNetPublishSettings()
                        .SetProject(RootDirectory / artifact.Deployment.Project)
                        .SetConfiguration(Configuration)
                        .SetRuntime(platform.Rid), artifact.Deployment.BuildProperties)
                    .SetOutput(publishDirectory)
                    .AddProcessAdditionalArguments("-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false");
                Serilog.Log.Information($"Publishing {artifact.Deployment.Id} for {platform.Rid}...");
                DotNetPublish(publish);
            }

            var files = Directory.GetFiles(publishDirectory, "*", SearchOption.AllDirectories);
            Serilog.Log.Information($"✓ {artifact.Deployment.Id}: {files.Length} files, {files.Sum(file => new FileInfo(file).Length):N0} bytes in {publishDirectory}");
        });

    /// <summary>
    /// Pack one published release profile: the archive (zip on Windows, tar.gz elsewhere, files at its root) or the tool
    /// package, and its build-side launch metadata beside it. The launch metadata is a build output, not a public
    /// contract; task 2.5 maps it into the tool release metadata v1 release descriptor.
    /// </summary>
    Target PackRelease => _ => _
        .DependsOn(PublishRelease)
        .Description("Pack one release profile into its named archive and launch metadata (requires --deployment, and --rid except for dotnet-tool)")
        .Executes(() =>
        {
            var artifact = ResolveRequestedArtifact();
            var publishDirectory = ReleasePublishDirectory(artifact);
            var output = ReleaseDir / artifact.FileName;
            ReleaseDir.CreateDirectory();

            if (artifact.Deployment.IsPortable)
            {
                var package = publishDirectory / artifact.FileName;
                if (!File.Exists(package)) throw new Exception($"{artifact.FileName} not found in {publishDirectory}");
                File.Copy(package, output, overwrite: true);
            }
            else
            {
                var launcher = publishDirectory / artifact.Launch.LaunchPath;
                if (!File.Exists(launcher)) throw new Exception($"The launch path {artifact.Launch.LaunchPath} is not in {publishDirectory}");
                ReleaseArchive.Create(publishDirectory, output, artifact.Platform!.ArchiveFormat, artifact.Launch.LaunchPath);
            }

            var sha256 = ReleaseArchive.Sha256(output);
            File.WriteAllText(ReleaseDir / BuildLaunchInfo.FileNameFor(artifact.FileName), BuildLaunchInfo.ToJson(artifact.Launch, sha256));
            Serilog.Log.Information($"✓ {artifact.FileName}: {new FileInfo(output).Length:N0} bytes, sha256 {sha256}");

            // Every shipped file, read from the artifact, and the size report over every artifact packed so far (task 2.4).
            var contents = ShippedContents.Read(artifact, output);
            File.WriteAllText(ReleaseDir / ShippedContents.FileNameFor(artifact.FileName), ShippedContents.ToJson(contents));
            var all = Directory.GetFiles(ReleaseDir, "*.build-contents.json").Select(path => ShippedContents.Parse(File.ReadAllText(path)));
            File.WriteAllText(ReleaseDir / "size-report.md", ShippedContents.SizeReport(all));
            Serilog.Log.Information($"✓ {artifact.FileName}: {contents.FileCount} files, {contents.ExtractedBytes:N0} bytes extracted; see {ReleaseDir / "size-report.md"}");
        });

    /// <summary>The artifact that <c>--deployment</c> and <c>--rid</c> name, or a build failure that says why there is none.</summary>
    ReleaseArtifact ResolveRequestedArtifact()
    {
        if (!Morphir.Build.Helpers.Deployment.TryParse(DeploymentId, out var deployment))
        {
            throw new Exception(
                $"--deployment must be one of {string.Join(", ", Morphir.Build.Helpers.Deployment.All.Select(candidate => candidate.Id))}; got '{DeploymentId}'.");
        }

        // The tool package is portable: every first-delivery platform resolves to the same package.
        var rid = deployment!.IsPortable && string.IsNullOrEmpty(Rid) ? TargetPlatform.All[0].Rid : Rid ?? "";
        return ReleaseArtifacts.Resolve(deployment, rid, Version.ToString()) switch
        {
            ArtifactResolution.Supported supported => supported.Artifact,
            ArtifactResolution.Unsupported unsupported => throw new Exception(unsupported.Reason),
            _ => throw new InvalidOperationException("Unknown artifact resolution."),
        };
    }

    AbsolutePath ReleasePublishDirectory(ReleaseArtifact artifact) =>
        ReleaseDir / "publish" / artifact.Deployment.Id / (artifact.Platform?.Rid ?? "any");

    static string HostOperatingSystem() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
        : "linux";
}
