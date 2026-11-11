// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGetSemanticVersion = NuGet.Versioning.SemanticVersion;

namespace Morphir.Build.Helpers;

// The named release profiles (approved decision DEP-1, spec dotnet-deployment-profiles). This file is pure apart from
// the archive writer: it names the four deployment offerings, the six first-delivery platforms, the publish properties
// of each profile, the artifact identity of each pair that the build defines and its launch metadata. The Nuke targets
// in Build.ReleaseProfiles.cs publish and pack from it.

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

/// <summary>What an artifact holds once extracted: one file, a folder, or a .NET tool package.</summary>
public enum ArtifactLayout
{
    /// <summary>One executable file and nothing else.</summary>
    SingleFile,

    /// <summary>A folder with the launch file beside the assemblies and native libraries it loads.</summary>
    Folder,

    /// <summary>A NuGet package that <c>dotnet tool install</c> installs.</summary>
    ToolPackage,
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
    private static readonly ImmutableSortedDictionary<string, string> Lean = Properties(
        ("DebugType", "none"),
        ("DebugSymbols", "false"),
        // Native AOT copies its native symbol file whenever this is true, whatever DebugType says.
        ("CopyOutputSymbolsToPublishDirectory", "false"),
        ("PublishDocumentationFile", "false"),
        ("PublishReferencesDocumentationFiles", "false"));

    /// <summary>The Native AOT <c>morphir</c>: one native file. No installed .NET; no in-process F# frontend.</summary>
    public static readonly Deployment NativeAot = new(
        "native-aot", "src/Morphir/Morphir.csproj", ExecutionMode.Native, ArtifactLayout.SingleFile, bundledRuntime: true,
        inProcessFSharpFrontend: false, requiredRuntime: null, requiredSdk: null,
        Lean.SetItems(Properties(("SelfContained", "true"), ("PublishAot", "true"), ("PublishTrimmed", "true"), ("PublishSingleFile", "true"),
            // The WASM engine is linked into the one file (build/wasmtime/WasmtimeStaticLink.targets), not shipped beside it.
            ("MorphirWasmtimeStaticLink", "true"))));

    /// <summary>
    /// The managed <c>morphir</c> with its runtime. It holds FSharp.Compiler.Service, so it is untrimmed and a folder:
    /// the F# frontend reads compiler references from assembly locations, which a single-file bundle does not have.
    /// </summary>
    public static readonly Deployment SelfContained = new(
        "self-contained", "src/Morphir.Managed/Morphir.Managed.csproj", ExecutionMode.Managed, ArtifactLayout.Folder,
        bundledRuntime: true, inProcessFSharpFrontend: true, requiredRuntime: null, requiredSdk: null,
        Lean.SetItems(Properties(("SelfContained", "true"), ("PublishTrimmed", "false"), ("PublishSingleFile", "false"))));

    /// <summary>The managed <c>morphir</c> apphost on an installed runtime. Untrimmed, a folder.</summary>
    public static readonly Deployment FrameworkDependent = new(
        "framework-dependent", "src/Morphir.Managed/Morphir.Managed.csproj", ExecutionMode.Managed, ArtifactLayout.Folder,
        bundledRuntime: false, inProcessFSharpFrontend: true, requiredRuntime: Runtime, requiredSdk: null,
        Lean.SetItems(Properties(("SelfContained", "false"), ("UseAppHost", "true"), ("PublishTrimmed", "false"), ("PublishSingleFile", "false"))));

    /// <summary>The existing <c>Morphir.Tool</c> package, command <c>dotnet-morphir</c>. One portable package.</summary>
    public static readonly Deployment DotnetTool = new(
        "dotnet-tool", "src/Morphir.Tool/Morphir.Tool.csproj", ExecutionMode.Managed, ArtifactLayout.ToolPackage,
        bundledRuntime: false, inProcessFSharpFrontend: true, requiredRuntime: Runtime, requiredSdk: ToolSdk,
        // The PackTool target packs with these properties too.
        Properties(
            ("PackAsTool", "true"),
            ("ToolCommandName", "dotnet-morphir"),
            ("IsPackable", "true"),
            ("DebugType", "none"),
            ("IncludeSymbols", "false")));

    /// <summary>The four offerings, in a fixed order.</summary>
    public static IReadOnlyList<Deployment> All { get; } = [NativeAot, SelfContained, FrameworkDependent, DotnetTool];

    private Deployment(string id, string project, ExecutionMode execution, ArtifactLayout layout, bool bundledRuntime,
        bool inProcessFSharpFrontend, string? requiredRuntime, string? requiredSdk, ImmutableSortedDictionary<string, string> buildProperties)
    {
        Id = id;
        Project = project;
        Execution = execution;
        Layout = layout;
        BundledRuntime = bundledRuntime;
        InProcessFSharpFrontend = inProcessFSharpFrontend;
        RequiredRuntime = requiredRuntime;
        RequiredSdk = requiredSdk;
        // The entry point writes this deployment into its build (src/Morphir.BuildProfile.targets), so the artifact
        // reports the offering it is. A build that no named profile makes states no deployment.
        BuildProperties = buildProperties.SetItem("MorphirDeployment", id);
    }

    /// <summary>The wire value, for example <c>native-aot</c>.</summary>
    public string Id { get; }

    /// <summary>The project that this deployment publishes or packs, relative to the repository root.</summary>
    public string Project { get; }

    public ExecutionMode Execution { get; }

    /// <summary>One file, a folder, or a tool package.</summary>
    public ArtifactLayout Layout { get; }

    /// <summary>True when the artifact carries its own runtime (or is native code).</summary>
    public bool BundledRuntime { get; }

    /// <summary>True when the artifact holds the in-process F# frontend and so FSharp.Compiler.Service.</summary>
    public bool InProcessFSharpFrontend { get; }

    /// <summary>The installed runtime that the artifact needs to run, or null.</summary>
    public string? RequiredRuntime { get; }

    /// <summary>The SDK that installing the artifact needs, or null. A tool install needs it; running does not.</summary>
    public string? RequiredSdk { get; }

    /// <summary>The MSBuild properties of the publish (or, for the tool, the pack). Immutable, in ordinal key order.</summary>
    public ImmutableSortedDictionary<string, string> BuildProperties { get; }

    /// <summary>True for the portable tool package, which is one artifact for every platform.</summary>
    public bool IsPortable => this == DotnetTool;

    /// <summary>The deployment with this wire value. Ordinal and case-sensitive, as wire values are.</summary>
    public static bool TryParse(string? id, out Deployment? deployment)
    {
        deployment = All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return deployment is not null;
    }

    public override string ToString() => Id;

    /// <summary>An immutable property set in ordinal key order.</summary>
    internal static ImmutableSortedDictionary<string, string> Properties(params (string Key, string Value)[] properties) =>
        ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            properties.Select(property => KeyValuePair.Create(property.Key, property.Value)));
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

/// <summary>What an artifact needs before it runs. The operating-system prerequisites are not declared yet.</summary>
public sealed record LaunchPrerequisites
{
    /// <summary>Qualifying the packaged artifact on each platform establishes the operating-system prerequisites.</summary>
    public const string OperatingSystemNotDeclared =
        "not declared yet: established by qualifying the packaged artifact on each platform";

    internal LaunchPrerequisites(string runtime, string installation)
    {
        Runtime = runtime;
        Installation = installation;
    }

    /// <summary>The .NET runtime that must be installed, or why none is needed.</summary>
    public string Runtime { get; }

    /// <summary>What installing the artifact needs.</summary>
    public string Installation { get; }

    public string OperatingSystem => OperatingSystemNotDeclared;
}

/// <summary>
/// The launch metadata of one artifact, written beside it as a build output. It is not a public contract and has no
/// contract version of its own. Only <see cref="ReleaseArtifacts.Resolve"/> makes one.
/// </summary>
/// <remarks>
/// The public, authenticated descriptor of an artifact is the tool release metadata v1 release descriptor, whose
/// offering-specific tool ids finos/morphir task 1.2 defines. morphir-dotnet task 2.5 maps this build output into that
/// descriptor and its catalog. The JSON note leaves the task numbers out because they belong to the plans, not to
/// the build output.
/// </remarks>
public sealed record BuildLaunchInfo
{
    private const string Note =
        "Build output of morphir-dotnet, not a public contract. The public, authenticated descriptor of this artifact is its tool release metadata v1 release descriptor.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal BuildLaunchInfo(string release, Deployment deployment, TargetPlatform? platform, string artifact, string launchPath)
    {
        Implementation = ReleaseArtifacts.Implementation;
        Release = release;
        Deployment = deployment.Id;
        Platform = platform?.Rid ?? "any";
        Artifact = artifact;
        LaunchPath = launchPath;
        Execution = deployment.Execution == ExecutionMode.Native ? "native" : "managed";
        Layout = deployment.Layout switch
        {
            ArtifactLayout.SingleFile => "single-file",
            ArtifactLayout.Folder => "folder",
            ArtifactLayout.ToolPackage => "tool-package",
            _ => throw new ArgumentOutOfRangeException(nameof(deployment)),
        };
        BundledRuntime = deployment.BundledRuntime;
        RequiredRuntime = deployment.RequiredRuntime;
        RequiredSdk = deployment.RequiredSdk;
        InProcessFSharpFrontend = deployment.InProcessFSharpFrontend;
        Prerequisites = new LaunchPrerequisites(
            deployment.RequiredRuntime
                ?? (deployment.Execution == ExecutionMode.Native
                    ? "none: native code that carries everything it runs"
                    : "none: the artifact carries its .NET runtime"),
            deployment.RequiredSdk ?? "none: extract the archive and run the launch path");
    }

    public string Implementation { get; }

    public string Release { get; }

    public string Deployment { get; }

    /// <summary>The RID, or <c>any</c> for the portable tool package.</summary>
    public string Platform { get; }

    public string Artifact { get; }

    public string LaunchPath { get; }

    /// <summary><c>native</c> or <c>managed</c>.</summary>
    public string Execution { get; }

    /// <summary><c>single-file</c>, <c>folder</c> or <c>tool-package</c>.</summary>
    public string Layout { get; }

    public bool BundledRuntime { get; }

    public string? RequiredRuntime { get; }

    public string? RequiredSdk { get; }

    public bool InProcessFSharpFrontend { get; }

    public LaunchPrerequisites Prerequisites { get; }

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
            launch.Layout,
            launch.BundledRuntime,
            launch.RequiredRuntime,
            launch.RequiredSdk,
            launch.InProcessFSharpFrontend,
            Prerequisites = new
            {
                launch.Prerequisites.Runtime,
                launch.Prerequisites.Installation,
                launch.Prerequisites.OperatingSystem,
            },
        }, Options) + "\n";
}

/// <summary>
/// One artifact of one release. <see cref="Platform"/> is null for the portable tool package. Only
/// <see cref="ReleaseArtifacts.Resolve"/> makes one.
/// </summary>
public sealed record ReleaseArtifact
{
    internal ReleaseArtifact(string release, Deployment deployment, TargetPlatform? platform, string fileName, BuildLaunchInfo launch)
    {
        Release = release;
        Deployment = deployment;
        Platform = platform;
        FileName = fileName;
        Launch = launch;
    }

    public string Release { get; }

    public Deployment Deployment { get; }

    public TargetPlatform? Platform { get; }

    public string FileName { get; }

    public BuildLaunchInfo Launch { get; }
}

/// <summary>
/// A (deployment, platform) pair that the build defines resolves to one artifact; any other pair is not defined by
/// the build, with the reason. "Defined" says what this build produces, not what a platform is qualified for.
/// </summary>
public abstract record ArtifactResolution
{
    private ArtifactResolution() { }

    /// <summary>The build defines this artifact.</summary>
    public sealed record Defined : ArtifactResolution
    {
        internal Defined(ReleaseArtifact artifact) => Artifact = artifact;

        public ReleaseArtifact Artifact { get; }
    }

    /// <summary>The build defines no artifact for this pair. <see cref="Rid"/> is null when no platform was given.</summary>
    public sealed record NotDefined : ArtifactResolution
    {
        internal NotDefined(Deployment deployment, string? rid, string reason)
        {
            Deployment = deployment;
            Rid = rid;
            Reason = reason;
        }

        public Deployment Deployment { get; }

        public string? Rid { get; }

        public string Reason { get; }
    }
}

/// <summary>Artifact identities: implementation, exact release, deployment and platform.</summary>
public static class ReleaseArtifacts
{
    public const string Implementation = "dotnet";

    /// <summary>
    /// The artifact of <paramref name="deployment"/> for <paramref name="rid"/> in <paramref name="release"/>. A platform
    /// outside the six first-delivery platforms is not defined; it never maps to another platform or deployment. The
    /// portable tool package needs no platform (<paramref name="rid"/> null); every other deployment needs one.
    /// </summary>
    /// <exception cref="ArgumentException">The release is not an exact SemVer 2.0.0 version, or has build metadata.</exception>
    public static ArtifactResolution Resolve(Deployment deployment, string? rid, string release)
    {
        CheckRelease(release);

        TargetPlatform? platform = null;
        if (rid is null)
        {
            if (!deployment.IsPortable)
                return new ArtifactResolution.NotDefined(deployment, null, $"{deployment.Id} needs a platform: one of {SupportedPlatforms()}.");
        }
        else if (!TargetPlatform.TryParse(rid, out platform))
        {
            var what = rid.Length == 0 ? "no platform was given" : $"'{rid}' is not a supported platform";
            return new ArtifactResolution.NotDefined(deployment, rid, $"{deployment.Id}: {what}. Supported platforms: {SupportedPlatforms()}.");
        }

        // One portable package serves every platform, so the tool artifact has no platform even when one is named.
        var target = deployment.IsPortable ? null : platform;
        var fileName = target is null
            ? $"Morphir.Tool.{release}.nupkg"
            : $"morphir-{Implementation}-{release}-{deployment.Id}-{target.Rid}.{target.ArchiveExtension}";
        var launchPath = target is null ? "dotnet-morphir" : target.Executable("morphir");
        var launch = new BuildLaunchInfo(release, deployment, target, fileName, launchPath);
        return new ArtifactResolution.Defined(new ReleaseArtifact(release, deployment, target, fileName, launch));
    }

    /// <summary>
    /// Refuses a release that is not exact SemVer 2.0.0 in its canonical spelling, and one with build metadata.
    /// NuGet.Versioning (which Nuke already brings) parses it: its <c>SemanticVersion</c> follows SemVer 2.0.0 and
    /// refuses leading zeros in numeric identifiers, empty identifiers and wildcards.
    /// </summary>
    private static void CheckRelease(string release)
    {
        if (!NuGetSemanticVersion.TryParse(release, out var version) || version.ToFullString() != release)
            throw new ArgumentException($"'{release}' is not an exact SemVer release (MAJOR.MINOR.PATCH with an optional pre-release).", nameof(release));
        if (version.HasMetadata)
        {
            throw new ArgumentException(
                $"'{release}' has build metadata ('+{version.Metadata}'). A release identity is an exact SemVer version without build metadata: SemVer ignores build metadata when it orders versions, so two builds would claim one release.",
                nameof(release));
        }
    }

    private static string SupportedPlatforms() => string.Join(", ", TargetPlatform.All.Select(candidate => candidate.Rid));
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
        Deployment.Properties(
            ("SelfContained", "true"),
            ("PublishSingleFile", "true"),
            ("PublishTrimmed", "true"),
            ("TrimMode", "partial"),
            ("TreatWarningsAsErrors", "false")),
        // The RIDs of the build-executables matrix in .github/workflows/deployment.yml.
        [TargetPlatform.WinX64, TargetPlatform.OsxArm64, TargetPlatform.LinuxX64, TargetPlatform.LinuxArm64]);

    /// <summary>The untrimmed baseline that the manual AOT workflow compares with. Never released.</summary>
    public static readonly LegacyAsset UntrimmedSingleFile = new(
        "legacy-untrimmed-single-file", trimmed: false,
        Deployment.Properties(
            ("SelfContained", "true"),
            ("PublishSingleFile", "true"),
            ("PublishTrimmed", "false")),
        []);

    private LegacyAsset(string id, bool trimmed, ImmutableSortedDictionary<string, string> buildProperties, IReadOnlyList<TargetPlatform> releasedPlatforms)
    {
        Id = id;
        Trimmed = trimmed;
        BuildProperties = buildProperties;
        ReleasedPlatforms = releasedPlatforms;
    }

    public string Id { get; }

    /// <summary>The AOT-compatible composition, published managed.</summary>
    public string Project => "src/Morphir/Morphir.csproj";

    public ImmutableSortedDictionary<string, string> BuildProperties { get; }

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

/// <summary>
/// The build's one archive writer, for the release profiles and the extension process folders alike. It writes a
/// publish folder into an archive by the shared archive convention: files at the archive root with no
/// top folder, <c>/</c> separators, ordinal entry order, one fixed time stamp, and explicit modes (0755 for the
/// executables, 0644 for every other file). The same folder therefore gives the same bytes on every run.
/// </summary>
public static class ReleaseArchive
{
    /// <summary>The time stamp of every entry: 2000-01-01T00:00:00Z.</summary>
    public static readonly DateTimeOffset Timestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>0644.</summary>
    public const UnixFileMode RegularMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>0755.</summary>
    public const UnixFileMode ExecutableMode = RegularMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    // The .NET host runs createdump to write a crash dump; it ships beside a self-contained runtime on Linux and macOS.
    private const string CreateDump = "createdump";

    // S_IFREG, the regular-file type bits of a Unix mode in a zip entry's external attributes.
    private const int RegularFileType = 0x8000;

    // Ustar fields: a name of at most 100 bytes, and a prefix of at most 155 bytes split off at a '/'.
    private const int UstarName = 100;
    private const int UstarPrefix = 155;

    /// <summary>The archive format for a runtime identifier: zip for <c>win-*</c>, otherwise tar.gz.</summary>
    public static ArchiveFormat FormatFor(string rid) =>
        rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Zip : ArchiveFormat.TarGz;

    /// <summary>The file extension of an archive format, with its dot: <c>.zip</c> or <c>.tar.gz</c>.</summary>
    public static string ExtensionFor(ArchiveFormat format) => format == ArchiveFormat.Zip ? ".zip" : ".tar.gz";

    /// <summary>The files of a folder as relative paths with <c>/</c> separators, in ordinal order: the archive's entries.</summary>
    public static string[] Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The files that run as programs in a publish folder whose launch file is <paramref name="launchPath"/>.</summary>
    public static IReadOnlySet<string> ExecutablesFor(string launchPath) =>
        ImmutableSortedSet.Create(StringComparer.Ordinal, launchPath, CreateDump);

    /// <summary>
    /// Creates <paramref name="archive"/> from <paramref name="sourceDirectory"/>. A file whose archive path is in
    /// <paramref name="executables"/> gets mode 0755; every other file gets 0644, whatever the build machine's modes. A
    /// path that a ustar tar cannot hold fails with <see cref="InvalidOperationException"/> naming the path, before
    /// anything is written, and no archive is left.
    /// </summary>
    /// <remarks>
    /// Determinism is scoped: archiving the same folder again with the same build machine OS and .NET version gives the
    /// same bytes, whatever the files' times and modes on disk. Archives made on different operating systems or .NET
    /// versions can differ in bytes (the zip "version made by" field, and the deflate output of the runtime's zlib), but
    /// not in entry names, order, times, modes or content.
    /// </remarks>
    public static void Create(string sourceDirectory, string archive, ArchiveFormat format, IReadOnlySet<string> executables)
    {
        if (File.Exists(archive)) File.Delete(archive);
        var files = Files(sourceDirectory)
            .Select(name => (Path: Path.Combine(sourceDirectory, name), Name: name))
            .ToList();
        UnixFileMode ModeOf(string name) => executables.Contains(name) ? ExecutableMode : RegularMode;

        if (format == ArchiveFormat.Zip)
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                entry.LastWriteTime = Timestamp;
                entry.ExternalAttributes = (RegularFileType | (int)ModeOf(file.Name)) << 16;
                using var content = File.OpenRead(file.Path);
                using var target = entry.Open();
                content.CopyTo(target);
            }
            return;
        }

        var unrepresentable = files.Select(file => file.Name).FirstOrDefault(name => !FitsUstar(name));
        if (unrepresentable is not null)
        {
            throw new InvalidOperationException(
                $"The path '{unrepresentable}' cannot be stored in a ustar tar: a ustar name holds at most {UstarName} bytes after a split at '/', with at most {UstarPrefix} bytes before it.");
        }

        using var output = File.Create(archive);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        // Ustar, not PAX: a PAX entry also records access and change times, which differ between runs.
        using var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: false);
        foreach (var file in files)
        {
            using var content = File.OpenRead(file.Path);
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, file.Name)
            {
                DataStream = content,
                Mode = ModeOf(file.Name),
                ModificationTime = Timestamp,
            });
        }
    }

    /// <summary>True when a ustar header can hold the path, in its name field or split into prefix and name at a '/'.</summary>
    private static bool FitsUstar(string path)
    {
        if (Encoding.UTF8.GetByteCount(path) <= UstarName) return true;
        for (var at = path.IndexOf('/'); at >= 0; at = path.IndexOf('/', at + 1))
        {
            if (Encoding.UTF8.GetByteCount(path.AsSpan(0, at)) <= UstarPrefix
                && Encoding.UTF8.GetByteCount(path.AsSpan(at + 1)) <= UstarName)
                return true;
        }

        return false;
    }

    /// <summary>The lowercase hex SHA-256 of a file.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
