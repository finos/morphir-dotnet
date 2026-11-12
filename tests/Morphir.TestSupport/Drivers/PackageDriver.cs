// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security;

namespace Morphir.TestSupport.Drivers;

/// <summary>A project of this repository whose package the package driver packs. The set is closed.</summary>
public enum PackageProject
{
    Myriad,
    MyriadSystemTextJson,
    MyriadSystemTextJsonAnnotations,
    SdkFSharp,
    Sdk,
}

/// <summary>The package ids of the projects, and the projects that the build of a test project builds first.</summary>
public static class PackageProjects
{
    private static readonly ImmutableDictionary<PackageProject, string> Ids = ImmutableDictionary.CreateRange(
    [
        KeyValuePair.Create(PackageProject.Myriad, "Morphir.Myriad"),
        KeyValuePair.Create(PackageProject.MyriadSystemTextJson, "Morphir.Myriad.SystemTextJson"),
        KeyValuePair.Create(PackageProject.MyriadSystemTextJsonAnnotations, "Morphir.Myriad.SystemTextJson.Annotations"),
        KeyValuePair.Create(PackageProject.SdkFSharp, "Morphir.SDK.FSharp"),
        KeyValuePair.Create(PackageProject.Sdk, "Morphir.SDK"),
    ]);

    public static ImmutableArray<PackageProject> All { get; } = [.. Enum.GetValues<PackageProject>()];

    /// <summary>The id of the package, which is also the name of the project file.</summary>
    public static string IdOf(PackageProject project) => Ids[project];

    /// <summary>
    /// True when Morphir.PackageSources.targets makes the build of the test project build the project first, so
    /// that the driver can pack it with no build. The SDK projects are built and packed in the workspace.
    /// </summary>
    public static bool IsBuiltFirst(PackageProject project) => project is not (PackageProject.SdkFSharp or PackageProject.Sdk);

    /// <summary>The project of the package <paramref name="id"/>, or an error that lists the ids.</summary>
    public static PackageProject Parse(string id)
    {
        foreach (var project in All)
        {
            if (string.Equals(IdOf(project), id, StringComparison.OrdinalIgnoreCase)) return project;
        }

        throw new TestSessionException($"The package driver does not pack '{id}'. The packages are: {string.Join(", ", All.Select(IdOf))}.");
    }
}

/// <summary>
/// The project files of the packages, and the built output of the projects that the build of the test project
/// builds first, from the assembly metadata that Morphir.PackageSources.targets writes. No path is computed from the
/// repository root.
/// </summary>
public sealed class PackageSources
{
    private const string MetadataPrefix = "Morphir.TestSupport.Package.";

    private readonly IReadOnlyDictionary<string, string> _metadata;

    private PackageSources(IReadOnlyDictionary<string, string> metadata, string configuration)
    {
        _metadata = metadata;
        Configuration = configuration;
    }

    /// <summary>The build configuration of the test project, and so of the built projects.</summary>
    public string Configuration { get; }

    public string ProjectOf(PackageProject project) => Value(project, "Project");

    /// <summary>The built assembly of a project that is built first. Another project is an error.</summary>
    public string TargetPathOf(PackageProject project) =>
        PackageProjects.IsBuiltFirst(project)
            ? Value(project, "TargetPath")
            : throw new TestSessionException($"{PackageProjects.IdOf(project)} is not built first by the build of the test project, so it has no built output to pack.");

    /// <summary>The sources of <paramref name="testAssembly"/>, or null when its project does not import Morphir.PackageSources.targets.</summary>
    public static PackageSources? OfDevelopmentBuild(Assembly testAssembly) =>
        Of(testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal) && attribute.Value is not null)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!));

    /// <summary>The sources from the metadata of a test assembly, or null when it has none.</summary>
    public static PackageSources? Of(IReadOnlyDictionary<string, string> metadata)
    {
        var own = metadata.Where(entry => entry.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal)).ToDictionary();
        if (own.Count == 0) return null;

        var missing = PackageProjects.All
            .SelectMany(project => PackageProjects.IsBuiltFirst(project) ? new[] { Key(project, "Project"), Key(project, "TargetPath") } : [Key(project, "Project")])
            .Append(MetadataPrefix + "Configuration")
            .Where(key => !own.ContainsKey(key))
            .ToArray();
        if (missing.Length > 0)
            throw new TestSessionException(
                $"The assembly metadata of the package sources has no {string.Join(", ", missing)}. Import tests/Morphir.TestSupport/build/Morphir.PackageSources.targets in the test project.");

        return new PackageSources(own, own[MetadataPrefix + "Configuration"]);
    }

    private static string Key(PackageProject project, string part) => $"{MetadataPrefix}{PackageProjects.IdOf(project)}.{part}";

    private string Value(PackageProject project, string part) => _metadata[Key(project, part)];
}

/// <summary>The options of a pack with no build.</summary>
public sealed record PackOptions
{
    /// <summary>The version of the package. The default is a new <c>0.1.0-test.&lt;id&gt;</c> version for each pack, which no feed of the machine has.</summary>
    public string? PackageVersion { get; init; }
}

/// <summary>The options of a build and a pack in the workspace.</summary>
public sealed record BuildPackOptions
{
    /// <summary>
    /// A version for the build. The driver writes a changelog with this version in the workspace and gives it to the
    /// build (<c>ChangelogFile</c>), so the build takes its version as for a release. Null: the changelog of the repository.
    /// </summary>
    public string? ChangelogVersion { get; init; }
}

/// <summary>The options of one command of the package driver.</summary>
public sealed record PackageCommandOptions
{
    /// <summary>The time limit. Default: <see cref="PackageDriver.DefaultTimeout"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Text for standard input. Standard input is closed after it.</summary>
    public string? StandardInput { get; init; }

    /// <summary>Variables for this command, added after the driver contained the process. A null value removes a variable.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }
}

/// <summary>One package source pattern of a NuGet configuration: the feed of the workspace, or nuget.org.</summary>
public sealed record PackageSourcePattern(bool FromFeed, string Pattern);

/// <summary>
/// Packs, installs and consumes packages in the workspace (design DRV-1). It packs a project of the development
/// build with no build, or builds and packs it in the workspace (<c>--artifacts-path</c>), so that no file of the
/// solution build changes. A consumer restores into the packages folder of the workspace, so no package of a test
/// goes into a NuGet folder of the machine. In the packages lane it reads the packed packages from the folder that
/// the build gives (<see cref="PackedPackages"/>).
/// </summary>
public interface IPackageDriver
{
    /// <summary>The feed of the workspace: the folder of packages that a consumer restores from.</summary>
    WorkspaceDirectory Feed { get; }

    /// <summary>The NuGet packages folder of the commands of the driver (<c>NUGET_PACKAGES</c>).</summary>
    WorkspaceDirectory Packages { get; }

    /// <summary>The packages that the build packed. Only in the packages lane (<c>MORPHIR_TEST_LANE=packages</c>).</summary>
    PackedPackages Packed { get; }

    /// <summary>Packs a project that the build of the test project built first, with no build and no restore, into <see cref="Feed"/>.</summary>
    Task<PackedPackage> PackAsync(PackageProject project, PackOptions? options = null, CancellationToken cancellation = default);

    /// <summary>Builds and packs a project in the workspace (<c>--artifacts-path</c>) into <see cref="Feed"/>.</summary>
    Task<PackedPackage> BuildAndPackAsync(PackageProject project, BuildPackOptions? options = null, CancellationToken cancellation = default);

    /// <summary>The folder of the build of <see cref="BuildAndPackAsync"/> (its <c>--artifacts-path</c>).</summary>
    WorkspaceDirectory Artifacts { get; }

    /// <summary>Copies a package (for example one of <see cref="Packed"/>) into <see cref="Feed"/>.</summary>
    PackedPackage AddToFeed(PackedPackage package);

    /// <summary>
    /// Writes <c>NuGet.config</c> in <paramref name="directory"/>: the feed of the workspace and, if asked,
    /// nuget.org, with no other source. <paramref name="mapping"/> adds a package source mapping.
    /// </summary>
    WorkspaceFile WriteNuGetConfig(WorkspaceDirectory directory, bool includeNuGetOrg, IReadOnlyList<PackageSourcePattern>? mapping = null);

    /// <summary>Runs one <c>dotnet</c> command in <paramref name="directory"/>, contained in the workspace.</summary>
    Task<CommandResult> DotnetAsync(
        WorkspaceDirectory directory, IReadOnlyList<string> arguments, PackageCommandOptions? options = null, CancellationToken cancellation = default);

    /// <summary>Runs an executable of the workspace, for example an installed tool, contained in the workspace.</summary>
    Task<CommandResult> RunAsync(
        WorkspaceFile executable, IReadOnlyList<string> arguments, PackageCommandOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>The package driver of one test.</summary>
public sealed class PackageDriver : IPackageDriver
{
    /// <summary>The time limit of a command with no other limit.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>The time limit of a build and a pack in the workspace.</summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    // Two packs of one project in one test process wait for each other.
    private static readonly ImmutableDictionary<PackageProject, SemaphoreSlim> ProjectLocks =
        PackageProjects.All.ToImmutableDictionary(project => project, _ => new SemaphoreSlim(1, 1));

    private static readonly Lazy<string?> RealHttpCache = new(FindHttpCache);

    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IScenarioWorkspace _workspace;
    private readonly PackageSources? _sources;
    private readonly PackedPackages? _packed;
    private readonly ProcessGate? _gate;

    /// <param name="sources">The project files of the packages, or null when the test project does not import Morphir.PackageSources.targets.</param>
    /// <param name="packed">The packed packages of the packages lane, or null in another lane.</param>
    /// <param name="gate">The process gate of each command. The default is <see cref="ProcessGate.Shared"/>.</param>
    public PackageDriver(IScenarioWorkspace workspace, PackageSources? sources, PackedPackages? packed, ProcessGate? gate = null)
    {
        _workspace = workspace;
        _sources = sources;
        _packed = packed;
        _gate = gate;
        // Beside the work area, not in it: the mark of the files that a command writes reads only the work area. The
        // names have a space, so each command gets paths with a space.
        Feed = new WorkspaceDirectory(Path.Combine(workspace.Root, "package feed"));
        Packages = new WorkspaceDirectory(Path.Combine(workspace.Root, "nuget packages"));
        Artifacts = new WorkspaceDirectory(Path.Combine(workspace.Root, "artifacts"));
    }

    public WorkspaceDirectory Feed { get; }

    public WorkspaceDirectory Packages { get; }

    public WorkspaceDirectory Artifacts { get; }

    public PackedPackages Packed =>
        _packed ?? throw new TestSessionException($"The packed packages are there only in the packages lane (MORPHIR_TEST_LANE=packages, with {PackedPackages.Variable}).");

    private PackageSources Sources =>
        _sources ?? throw new TestSessionException("The test project has no package sources. Import tests/Morphir.TestSupport/build/Morphir.PackageSources.targets in its project.");

    public async Task<PackedPackage> PackAsync(PackageProject project, PackOptions? options = null, CancellationToken cancellation = default)
    {
        var id = PackageProjects.IdOf(project);
        if (!PackageProjects.IsBuiltFirst(project))
            throw new TestSessionException($"{id} is not built first by the build of the test project, so it cannot be packed with no build. Use BuildAndPackAsync.");

        var version = options?.PackageVersion ?? "0.1.0-test." + Guid.NewGuid().ToString("N");
        // --no-build packs the assemblies of the solution build, and --no-restore keeps the restore files of the
        // project. The manifest goes into the workspace, not into the obj folder of the project.
        string[] arguments =
        [
            "pack", Sources.ProjectOf(project), "-c", Sources.Configuration, "--no-build", "--no-restore",
            "-o", Feed.Path, $"-p:NuspecOutputPath={Path.Combine(_workspace.Root, "nuspec", id)}{Path.DirectorySeparatorChar}",
            $"-p:PackageVersion={version}", "-nologo",
        ];

        var gate = ProjectLocks[project];
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        CommandResult result;
        try
        {
            result = await RunDotnetAsync(_workspace.Work, arguments, consumer: false, options: null, cancellation).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }

        return PackageOf(result, id, version);
    }

    public async Task<PackedPackage> BuildAndPackAsync(PackageProject project, BuildPackOptions? options = null, CancellationToken cancellation = default)
    {
        var id = PackageProjects.IdOf(project);
        var arguments = new List<string>
        {
            "pack", Sources.ProjectOf(project), "-c", Sources.Configuration, "-o", Feed.Path,
            // The intermediate and output files of the build go into the workspace, not into the obj and bin folders
            // of the repository, so the assemblies of the solution build stay.
            "--artifacts-path", Artifacts.Path, "-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false", "-nologo",
        };
        if (options?.ChangelogVersion is { } version)
        {
            var changelog = new WorkspaceFile(Path.Combine(_workspace.Root, "changelog", "CHANGELOG.md"));
            _workspace.WriteText(changelog, $"# Changelog\n\n## [{version}] - 2026-11-03\n\n### Added\n- A version for a test.\n");
            arguments.Add($"-p:ChangelogFile={changelog.Path}");
        }

        var result = await RunDotnetAsync(_workspace.Work, arguments, consumer: false, new PackageCommandOptions { Timeout = BuildTimeout }, cancellation)
            .ConfigureAwait(false);
        return PackageOf(result, id, options?.ChangelogVersion);
    }

    public PackedPackage AddToFeed(PackedPackage package)
    {
        var target = Feed.File(package.FileName);
        Directory.CreateDirectory(Feed.Path);
        File.Copy(package.Path, target.Path, overwrite: false);
        return PackedPackage.Read(target.Path);
    }

    public WorkspaceFile WriteNuGetConfig(WorkspaceDirectory directory, bool includeNuGetOrg, IReadOnlyList<PackageSourcePattern>? mapping = null)
    {
        var sources = $"""<add key="feed" value="{SecurityElement.Escape(Feed.Path)}" />"""
            + (includeNuGetOrg ? """<add key="nuget" value="https://api.nuget.org/v3/index.json" />""" : "");
        var patterns = mapping is { Count: > 0 }
            ? "<packageSourceMapping>"
              + string.Concat(mapping.GroupBy(pattern => pattern.FromFeed).Select(group =>
                  $"""<packageSource key="{(group.Key ? "feed" : "nuget")}">"""
                  + string.Concat(group.Select(pattern => $"""<package pattern="{SecurityElement.Escape(pattern.Pattern)}" />"""))
                  + "</packageSource>"))
              + "</packageSourceMapping>"
            : "";
        var file = directory.File("NuGet.config");
        _workspace.WriteText(file, $"""<?xml version="1.0" encoding="utf-8"?>{"\n"}<configuration><packageSources><clear />{sources}</packageSources>{patterns}</configuration>{"\n"}""");
        return file;
    }

    public Task<CommandResult> DotnetAsync(
        WorkspaceDirectory directory, IReadOnlyList<string> arguments, PackageCommandOptions? options = null, CancellationToken cancellation = default) =>
        RunDotnetAsync(directory, arguments, consumer: true, options, cancellation);

    public Task<CommandResult> RunAsync(
        WorkspaceFile executable, IReadOnlyList<string> arguments, PackageCommandOptions? options = null, CancellationToken cancellation = default) =>
        RunProcessAsync(executable.Path, Path.GetDirectoryName(executable.Path) ?? _workspace.Work.Path, arguments, consumer: true, options, cancellation);

    private Task<CommandResult> RunDotnetAsync(
        WorkspaceDirectory directory, IReadOnlyList<string> arguments, bool consumer, PackageCommandOptions? options, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory.Path);
        return RunProcessAsync("dotnet", directory.Path, arguments, consumer, options, cancellation);
    }

    private async Task<CommandResult> RunProcessAsync(
        string fileName, string directory, IReadOnlyList<string> arguments, bool consumer, PackageCommandOptions? options, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(fileName);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        _workspace.Contain(info);
        info.WorkingDirectory = directory;
        // No MSBuild node outlives a command and keeps a file of the workspace open.
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (consumer)
        {
            // A consumer restores into the packages folder of the workspace, so a package of a test never goes into
            // the NuGet folder of the machine. The HTTP cache of the machine keeps the restore from nuget.org fast.
            info.Environment["NUGET_PACKAGES"] = Packages.Path;
            if (RealHttpCache.Value is { } cache) info.Environment["NUGET_HTTP_CACHE_PATH"] = cache;
        }

        if (options?.Environment is { } environment)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null) info.Environment.Remove(name);
                else info.Environment[name] = value;
            }
        }

        // A product tool and the SDK write UTF-8. Without this, Windows reads a redirected stream in the code page of
        // the console, and a framed message gets a byte order mark.
        info.StandardOutputEncoding = Utf8;
        info.StandardErrorEncoding = Utf8;
        if (options?.StandardInput is not null) info.StandardInputEncoding = Utf8;

        var commandLine = string.Join(' ', new[] { info.FileName }.Concat(info.ArgumentList).Select(Quoted));
        var mark = _workspace.Mark();
        var result = await ChildProcess.RunAsync(info, options?.Timeout ?? DefaultTimeout, cancellation, _gate, options?.StandardInput).ConfigureAwait(false);
        return new CommandResult(
            commandLine, result.ExitCode, result.StandardOutput, result.StandardError, result.ExitedAt - result.StartedAt, _workspace.FilesWrittenSince(mark));
    }

    private PackedPackage PackageOf(CommandResult result, string id, string? version)
    {
        if (result.ExitCode != 0) throw new CommandAssertionException($"The pack of {id} failed.", result);

        var files = (Directory.Exists(Feed.Path) ? Directory.GetFiles(Feed.Path, $"{id}.*.nupkg") : [])
            .Where(file => !file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Select(PackedPackage.Read)
            .Where(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase) && (version is null || package.Version == version))
            .ToArray();
        return files.Length == 1
            ? files[0]
            : throw new CommandAssertionException($"The feed {Feed.Path} has {files.Length} packages {id} {version} after the pack, not one.", result);
    }

    // The HTTP cache of NuGet of the machine, from the environment of the test process before a workspace contains
    // a command (the workspace has its own home).
    private static string? FindHttpCache()
    {
        if (System.Environment.GetEnvironmentVariable("NUGET_HTTP_CACHE_PATH") is { Length: > 0 } configured) return configured;
        try
        {
            var info = new ProcessStartInfo("dotnet") { ArgumentList = { "nuget", "locals", "http-cache", "--list" } };
            var result = ChildProcess.RunAsync(info, TimeSpan.FromMinutes(1)).GetAwaiter().GetResult();
            const string prefix = "http-cache:";
            return result.StandardOutput.Split('\n').Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim() is { Length: > 0 } path
                ? path
                : null;
        }
        catch (Exception error) when (error is ChildProcessTimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Quoted(string argument) =>
        argument.Length > 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"') ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
