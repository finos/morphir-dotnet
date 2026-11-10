// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;

namespace Morphir.Build.Helpers;

/// <summary>
/// The MSBuild properties that each publish and pack target passes, from the profile model. The targets read these
/// and nothing else, so a test of this mapping is a test of what the targets build.
/// </summary>
public static class TargetSettings
{
    /// <summary>
    /// Set on every publish for one runtime identifier. src/Directory.Build.props then writes the NuGet lock files of
    /// that publish under <c>obj/publish-lock/&lt;rid&gt;/</c>, so a RID-specific restore leaves the committed
    /// <c>packages.lock.json</c> files alone. Package versions stay pinned centrally in Directory.Packages.props.
    /// </summary>
    public const string RidSpecificPublishProperty = "MorphirRidSpecificPublish";

    /// <summary>PublishExecutable: the native-aot profile.</summary>
    public static ImmutableSortedDictionary<string, string> PublishExecutable => Deployment.NativeAot.BuildProperties;

    /// <summary>PublishSingleFile: the released legacy asset, with its settings unchanged (no lean properties).</summary>
    public static ImmutableSortedDictionary<string, string> PublishSingleFile => LegacyAsset.TrimmedSingleFile.BuildProperties;

    /// <summary>PublishSingleFileUntrimmed: the manual AOT baseline, with its settings unchanged.</summary>
    public static ImmutableSortedDictionary<string, string> PublishSingleFileUntrimmed => LegacyAsset.UntrimmedSingleFile.BuildProperties;

    /// <summary>PackTool: the dotnet-tool profile.</summary>
    public static ImmutableSortedDictionary<string, string> PackTool => Deployment.DotnetTool.BuildProperties;

    /// <summary>
    /// <paramref name="properties"/> plus <see cref="RidSpecificPublishProperty"/>. The property changes where NuGet
    /// writes the lock file and nothing that is built.
    /// </summary>
    public static ImmutableSortedDictionary<string, string> ForRidSpecificPublish(ImmutableSortedDictionary<string, string> properties) =>
        properties.SetItem(RidSpecificPublishProperty, "true");
}
