// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Morphir.Build.Helpers;

// The named release profiles of task 2.2 (approved decision DEP-1, spec dotnet-deployment-profiles). This file is
// pure: it names the four deployment offerings, the six first-delivery platforms, the publish properties of each
// profile, the artifact identity of each supported pair and its launch metadata. The Nuke targets in
// Build.ReleaseProfiles.cs publish and pack from it.

/// <summary>How a deployment runs: native code, or managed code on the .NET runtime.</summary>
public enum ExecutionMode
{
    Native,
    Managed,
}

/// <summary>The archive format of a platform's artifacts.</summary>
public enum ArchiveFormat
{
    Zip,
    TarGz,
}

/// <summary>
/// One of the four .NET deployment offerings. The set is closed: the constructor is private and <see cref="All"/>
/// holds the only instances.
/// </summary>
public sealed class Deployment
{
    /// <summary>The runtime that a managed deployment runs on, from the projects' target framework <c>net10.0</c>.</summary>
    public const string Runtime = "Microsoft.NETCore.App 10.0";

    /// <summary>The SDK that installing a .NET tool needs.</summary>
    public const string ToolSdk = ".NET SDK 10.0";

    // Archives hold what the program needs to run: no debug symbols and no XML documentation files.
    private static readonly IReadOnlyDictionary<string, string> Lean = new Dictionary<string, string>
    {
        ["DebugType"] = "none",
        ["DebugSymbols"] = "false",
        // Native AOT copies its native symbol file whenever this is true, whatever DebugType says.
        ["CopyOutputSymbolsToPublishDirectory"] = "false",
        ["PublishDocumentationFile"] = "false",
        ["PublishReferencesDocumentationFiles"] = "false",
    };

    /// <summary>The Native AOT <c>morphir</c>. No installed .NET; no in-process F# frontend.</summary>
    public static readonly Deployment NativeAot = new(
        "native-aot", "src/Morphir/Morphir.csproj", ExecutionMode.Native, bundledRuntime: true, inProcessFSharpFrontend: false,
        requiredRuntime: null, requiredSdk: null,
        With(Lean, ("SelfContained", "true"), ("PublishAot", "true"), ("PublishTrimmed", "true"), ("PublishSingleFile", "true")));

    /// <summary>
    /// The managed <c>morphir</c> with its runtime. It holds FSharp.Compiler.Service, so it is untrimmed and a folder:
    /// the F# frontend reads compiler references from assembly locations, which a single-file bundle does not have.
    /// </summary>
    public static readonly Deployment SelfContained = new(
        "self-contained", "src/Morphir.Managed/Morphir.Managed.csproj", ExecutionMode.Managed, bundledRuntime: true,
        inProcessFSharpFrontend: true, requiredRuntime: null, requiredSdk: null,
        With(Lean, ("SelfContained", "true"), ("PublishTrimmed", "false"), ("PublishSingleFile", "false")));

    /// <summary>The managed <c>morphir</c> apphost on an installed runtime. Untrimmed, a folder.</summary>
    public static readonly Deployment FrameworkDependent = new(
        "framework-dependent", "src/Morphir.Managed/Morphir.Managed.csproj", ExecutionMode.Managed, bundledRuntime: false,
        inProcessFSharpFrontend: true, requiredRuntime: Runtime, requiredSdk: null,
        With(Lean, ("SelfContained", "false"), ("UseAppHost", "true"), ("PublishTrimmed", "false"), ("PublishSingleFile", "false")));

    /// <summary>The existing <c>Morphir.Tool</c> package, command <c>dotnet-morphir</c>. One portable package.</summary>
    public static readonly Deployment DotnetTool = new(
        "dotnet-tool", "src/Morphir.Tool/Morphir.Tool.csproj", ExecutionMode.Managed, bundledRuntime: false,
        inProcessFSharpFrontend: true, requiredRuntime: Runtime, requiredSdk: ToolSdk,
        new Dictionary<string, string>
        {
            // The same settings as the PackTool target.
            ["PackAsTool"] = "true",
            ["ToolCommandName"] = "dotnet-morphir",
            ["IsPackable"] = "true",
            ["DebugType"] = "none",
            ["IncludeSymbols"] = "false",
        });

    /// <summary>The four offerings, in a fixed order.</summary>
    public static IReadOnlyList<Deployment> All { get; } = [NativeAot, SelfContained, FrameworkDependent, DotnetTool];

    private Deployment(string id, string project, ExecutionMode execution, bool bundledRuntime, bool inProcessFSharpFrontend,
        string? requiredRuntime, string? requiredSdk, IReadOnlyDictionary<string, string> buildProperties)
    {
        Id = id;
        Project = project;
        Execution = execution;
        BundledRuntime = bundledRuntime;
        InProcessFSharpFrontend = inProcessFSharpFrontend;
        RequiredRuntime = requiredRuntime;
        RequiredSdk = requiredSdk;
        BuildProperties = buildProperties;
    }

    /// <summary>The wire value, for example <c>native-aot</c>.</summary>
    public string Id { get; }

    /// <summary>The project that this deployment publishes or packs, relative to the repository root.</summary>
    public string Project { get; }

    public ExecutionMode Execution { get; }

    /// <summary>True when the artifact carries its own runtime (or is native code).</summary>
    public bool BundledRuntime { get; }

    /// <summary>True when the artifact holds the in-process F# frontend and so FSharp.Compiler.Service.</summary>
    public bool InProcessFSharpFrontend { get; }

    /// <summary>The installed runtime that the artifact needs to run, or null.</summary>
    public string? RequiredRuntime { get; }

    /// <summary>The SDK that installing the artifact needs, or null. A tool install needs it; running does not.</summary>
    public string? RequiredSdk { get; }

    /// <summary>The MSBuild properties of the publish (or, for the tool, the pack).</summary>
    public IReadOnlyDictionary<string, string> BuildProperties { get; }

    /// <summary>True for the portable tool package, which is one artifact for every platform.</summary>
    public bool IsPortable => this == DotnetTool;

    /// <summary>The deployment with this wire value. Ordinal and case-sensitive, as wire values are.</summary>
    public static bool TryParse(string? id, out Deployment? deployment)
    {
        deployment = All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return deployment is not null;
    }

    public override string ToString() => Id;

    internal static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> basis, params (string Key, string Value)[] properties)
    {
        var result = new Dictionary<string, string>(basis);
        foreach (var (key, value) in properties) result[key] = value;
        return result;
    }
}

/// <summary>One of the six first-delivery platforms, by its .NET runtime identifier. The set is closed.</summary>
public sealed class TargetPlatform
{
    public static readonly TargetPlatform WinX64 = new("win-x64", "windows", "x64");
    public static readonly TargetPlatform WinArm64 = new("win-arm64", "windows", "arm64");
    public static readonly TargetPlatform OsxX64 = new("osx-x64", "macos", "x64");
    public static readonly TargetPlatform OsxArm64 = new("osx-arm64", "macos", "arm64");
    public static readonly TargetPlatform LinuxX64 = new("linux-x64", "linux", "x64");
    public static readonly TargetPlatform LinuxArm64 = new("linux-arm64", "linux", "arm64");

    /// <summary>Windows x64/ARM64, macOS x64/ARM64 and Linux glibc x64/ARM64, in a fixed order.</summary>
    public static IReadOnlyList<TargetPlatform> All { get; } = [WinX64, WinArm64, OsxX64, OsxArm64, LinuxX64, LinuxArm64];

    private TargetPlatform(string rid, string os, string architecture)
    {
        Rid = rid;
        OperatingSystem = os;
        Architecture = architecture;
    }

    public string Rid { get; }

    /// <summary><c>windows</c>, <c>macos</c> or <c>linux</c>.</summary>
    public string OperatingSystem { get; }

    /// <summary><c>x64</c> or <c>arm64</c>.</summary>
    public string Architecture { get; }

    public ArchiveFormat ArchiveFormat => OperatingSystem == "windows" ? ArchiveFormat.Zip : ArchiveFormat.TarGz;

    public string ArchiveExtension => ArchiveFormat == ArchiveFormat.Zip ? "zip" : "tar.gz";

    /// <summary>An executable's file name on this platform.</summary>
    public string Executable(string name) => OperatingSystem == "windows" ? $"{name}.exe" : name;

    public static bool TryParse(string? rid, out TargetPlatform? platform)
    {
        platform = All.FirstOrDefault(candidate => string.Equals(candidate.Rid, rid, StringComparison.Ordinal));
        return platform is not null;
    }

    public override string ToString() => Rid;
}

/// <summary>
/// The launch metadata of one artifact, written beside it as a build output. It is not a public contract: the public,
/// authenticated descriptor is the tool release metadata v1 release descriptor, whose offering-specific tool ids
/// finos/morphir task 1.2 defines. Task 2.5 maps this build output into that descriptor and its catalog. This record
/// has no contract version of its own.
/// </summary>
public sealed record BuildLaunchInfo(
    string Implementation,
    string Release,
    string Deployment,
    string Platform,
    string Artifact,
    string LaunchPath,
    string Execution,
    bool BundledRuntime,
    string? RequiredRuntime,
    string? RequiredSdk,
    bool InProcessFSharpFrontend)
{
    private const string Note =
        "Build output of morphir-dotnet, not a public contract. The public descriptor of this artifact is the tool release metadata v1 release descriptor (finos/morphir task 1.2), mapped in morphir-dotnet task 2.5.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The file name of the launch metadata beside an artifact.</summary>
    public static string FileNameFor(string artifact) => $"{artifact}.build-launch.json";

    /// <summary>The launch metadata as JSON, with the artifact's SHA-256 and the note that it is not a contract.</summary>
    public static string ToJson(BuildLaunchInfo launch, string sha256) =>
        JsonSerializer.Serialize(new
        {
            note = Note,
            launch.Implementation,
            launch.Release,
            launch.Deployment,
            launch.Platform,
            launch.Artifact,
            Sha256 = sha256,
            launch.LaunchPath,
            launch.Execution,
            launch.BundledRuntime,
            launch.RequiredRuntime,
            launch.RequiredSdk,
            launch.InProcessFSharpFrontend,
        }, Options) + "\n";
}

/// <summary>One artifact of one release. <see cref="Platform"/> is null for the portable tool package.</summary>
public sealed record ReleaseArtifact(string Release, Deployment Deployment, TargetPlatform? Platform, string FileName, BuildLaunchInfo Launch);

/// <summary>A (deployment, platform) pair resolves to one artifact, or is unsupported with the reason.</summary>
public abstract record ArtifactResolution
{
    private ArtifactResolution() { }

    public sealed record Supported(ReleaseArtifact Artifact) : ArtifactResolution;

    public sealed record Unsupported(Deployment Deployment, string Rid, string Reason) : ArtifactResolution;
}

/// <summary>Artifact identities: implementation, exact release, deployment and platform.</summary>
public static class ReleaseArtifacts
{
    public const string Implementation = "dotnet";

    // SemVer 2.0.0, the pattern that .github/workflows/deployment.yml checks release versions with.
    private static readonly Regex ExactRelease = new(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The artifact of <paramref name="deployment"/> for <paramref name="rid"/> in <paramref name="release"/>. A platform
    /// outside the six first-delivery platforms is unsupported; it never maps to another platform or deployment.
    /// </summary>
    public static ArtifactResolution Resolve(Deployment deployment, string rid, string release)
    {
        if (!ExactRelease.IsMatch(release))
            throw new ArgumentException($"'{release}' is not an exact SemVer release.", nameof(release));

        if (!TargetPlatform.TryParse(rid, out var platform))
        {
            var what = rid.Length == 0 ? "no platform was given" : $"'{rid}' is not a supported platform";
            return new ArtifactResolution.Unsupported(deployment, rid,
                $"{deployment.Id}: {what}. Supported platforms: {string.Join(", ", TargetPlatform.All.Select(candidate => candidate.Rid))}.");
        }

        var fileName = deployment.IsPortable
            ? $"Morphir.Tool.{release}.nupkg"
            : $"morphir-{Implementation}-{release}-{deployment.Id}-{platform!.Rid}.{platform.ArchiveExtension}";
        var target = deployment.IsPortable ? null : platform;
        var launch = new BuildLaunchInfo(
            Implementation,
            release,
            deployment.Id,
            target?.Rid ?? "any",
            fileName,
            deployment.IsPortable ? "dotnet-morphir" : platform!.Executable("morphir"),
            deployment.Execution == ExecutionMode.Native ? "native" : "managed",
            deployment.BundledRuntime,
            deployment.RequiredRuntime,
            deployment.RequiredSdk,
            deployment.InProcessFSharpFrontend);
        return new ArtifactResolution.Supported(new ReleaseArtifact(release, deployment, target, fileName, launch));
    }
}

/// <summary>
/// The release assets that releases have shipped since before the four offerings: a managed, trimmed, self-contained
/// single-file build of the AOT composition, without the F# frontend. They keep their names so that old links, the
/// install scripts and the proto plugin keep working (design REL-1). They are an alias, not one of the offerings.
/// </summary>
public sealed class LegacyAsset
{
    /// <summary>The released legacy asset, built by the PublishSingleFile target.</summary>
    public static readonly LegacyAsset TrimmedSingleFile = new(
        "legacy-trimmed-single-file", trimmed: true,
        new Dictionary<string, string>
        {
            ["SelfContained"] = "true",
            ["PublishSingleFile"] = "true",
            ["PublishTrimmed"] = "true",
            ["TrimMode"] = "partial",
            ["TreatWarningsAsErrors"] = "false",
        },
        // The RIDs of the build-executables matrix in .github/workflows/deployment.yml.
        [TargetPlatform.WinX64, TargetPlatform.OsxArm64, TargetPlatform.LinuxX64, TargetPlatform.LinuxArm64]);

    /// <summary>The untrimmed baseline that the manual AOT workflow compares with. Never released.</summary>
    public static readonly LegacyAsset UntrimmedSingleFile = new(
        "legacy-untrimmed-single-file", trimmed: false,
        new Dictionary<string, string>
        {
            ["SelfContained"] = "true",
            ["PublishSingleFile"] = "true",
            ["PublishTrimmed"] = "false",
        },
        []);

    private LegacyAsset(string id, bool trimmed, IReadOnlyDictionary<string, string> buildProperties, IReadOnlyList<TargetPlatform> releasedPlatforms)
    {
        Id = id;
        Trimmed = trimmed;
        BuildProperties = buildProperties;
        ReleasedPlatforms = releasedPlatforms;
    }

    public string Id { get; }

    /// <summary>The AOT-compatible composition, published managed.</summary>
    public string Project => "src/Morphir/Morphir.csproj";

    public IReadOnlyDictionary<string, string> BuildProperties { get; }

    /// <summary>The platforms that releases publish this asset for.</summary>
    public IReadOnlyList<TargetPlatform> ReleasedPlatforms { get; }

    public string Execution => "managed";

    public bool BundledRuntime => true;

    public bool Trimmed { get; }

    public bool SingleFile => true;

    public bool InProcessFSharpFrontend => false;

    /// <summary>The bare executable release asset, for example <c>morphir-win-x64.exe</c>.</summary>
    public string ExecutableName(string rid) => rid.StartsWith("win-", StringComparison.Ordinal) ? $"morphir-{rid}.exe" : $"morphir-{rid}";

    /// <summary>The proto tarball release asset, for example <c>morphir-osx-arm64-v1.2.3.tar.gz</c>.</summary>
    public string TarballName(string rid, string version) => $"morphir-{rid}-v{version}.tar.gz";
}

/// <summary>Writes a publish folder into a release archive, with its files at the archive root.</summary>
public static class ReleaseArchive
{
    /// <summary>
    /// Creates <paramref name="archive"/> from <paramref name="sourceDirectory"/>. Entry names use <c>/</c>. In a tar
    /// archive the launch path is executable (0755) and other files are 0644, whatever the build machine's file modes.
    /// </summary>
    public static void Create(string sourceDirectory, string archive, ArchiveFormat format, string launchPath)
    {
        if (File.Exists(archive)) File.Delete(archive);
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Name: Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/')))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();

        if (format == ArchiveFormat.Zip)
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var file in files) zip.CreateEntryFromFile(file.Path, file.Name, CompressionLevel.Optimal);
            return;
        }

        const UnixFileMode regular = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        const UnixFileMode executable = regular | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        using var output = File.Create(archive);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        foreach (var file in files)
        {
            using var content = File.OpenRead(file.Path);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Name)
            {
                DataStream = content,
                Mode = file.Name == launchPath ? executable : regular,
                ModificationTime = File.GetLastWriteTimeUtc(file.Path),
            });
        }
    }

    /// <summary>The lowercase hex SHA-256 of a file.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
