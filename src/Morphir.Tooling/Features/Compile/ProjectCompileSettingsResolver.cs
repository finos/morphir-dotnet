// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Compilation;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;
using ConfigSeverity = Morphir.Configuration.DiagnosticSeverity;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Resolves the settings of one compile.</summary>
public interface ICompileSettingsResolver
{
    /// <summary>Resolves <paramref name="overrides"/> against the selected project configuration and the defaults.</summary>
    Checked<CompileSettings> Resolve(CompileOverrides overrides, CompileContext context);
}

/// <summary>The effective configuration of the selected project, as the settings resolver reads it.</summary>
public sealed record LoadedCompileConfiguration(ConfigValue Effective, FSharpMap<FSharpList<string>, ValueOrigin> Origins,
    string? ProjectRoot, string? WorkspaceOutDirectory, ImmutableArray<ConfigDiagnostic> Diagnostics)
{
    /// <summary>The root of the workspace that owns the project, or null when the project has no workspace.</summary>
    public string? WorkspaceRoot { get; init; }

    /// <summary>The Morphir home (<c>MORPHIR_HOME</c>, else <c>~/.morphir</c>), or null when it is not known.</summary>
    public string? MorphirHome { get; init; }
}

/// <summary>Loads the configuration of the project that <c>--project</c> and <c>--config</c> select.</summary>
public interface ICompileConfigurationLoader
{
    /// <summary>Discovers and loads the project configuration. Outside any project, only the defaults apply.</summary>
    LoadedCompileConfiguration Load(CompileOverrides overrides, CompileContext context);
}

/// <summary>
/// Loads the project configuration through the existing project discovery, from the native file system.
/// <c>--project</c> names a project directory, or else a member path or project name for discovery from the
/// working directory. <c>--config</c> overlays one explicitly selected file.
/// </summary>
public sealed class FileSystemCompileConfigurationLoader : ICompileConfigurationLoader
{
    /// <inheritdoc />
    public LoadedCompileConfiguration Load(CompileOverrides overrides, CompileContext context)
    {
        var working = CompilePaths.RealPath(context.WorkingDirectory);
        string? selector = null;
        var start = working;
        if (overrides.Project is { } project)
        {
            var directory = Path.GetFullPath(project, working);
            if (Directory.Exists(directory)) start = CompilePaths.RealPath(directory);
            else selector = project;
        }
        else if (overrides.Config is { } config && Path.GetDirectoryName(Path.GetFullPath(config, working)) is { } parent)
        {
            start = CompilePaths.RealPath(parent);
        }

        var containment = Containment.Resolve(null, null, context.Environment, working);
        if (containment.Failure is { } failure)
            return new(DocumentDefaults.emptyValue, MapModule.Empty<FSharpList<string>, ValueOrigin>(), null, null,
                [ConfigFailure(failure.Code, failure.Message)]);
        var boundary = CompilePaths.RealPath(containment.Value!.Boundary);
        var normalizedStart = ProjectPath.normalize(start);
        // An explicitly named project directory is authorized even outside the ambient discovery boundary.
        if (!ProjectPath.contains(ProjectPath.normalize(boundary), normalizedStart)) boundary = start;
        var machine = ProjectSystem.CaptureMachineLocations(context.Environment);
        var home = ProjectSystem.MorphirHomeDirectory(machine, normalizedStart);
        var tree = new FileSystemProjectTree(ProjectPath.normalize(boundary), home);
        var discovered = ProjectSystem.Discover(tree, normalizedStart, selector);
        var selected = discovered.Selection.Projects.Length == 1 ? discovered.Selection.Projects.Head : null;
        var defaults = DocumentDefaults.resolutionInput;
        var input = new ResolutionInput(FSharpList<SourceInput>.Empty,
            MapModule.OfSeq(context.Environment.Select(pair => Tuple.Create(pair.Key, pair.Value))),
            DocumentDefaults.emptyValue, defaults.Profiles, ProjectScope.Root, null, FSharpList<string>.Empty);
        var isolated = containment.Value.Isolated;
        var options = new ProjectLoadOptions
        {
            Input = input,
            Isolated = isolated,
            SelectedPath = overrides.Config is { } file ? ProjectPath.normalize(Path.GetFullPath(file, working)) : null,
            Machine = isolated ? null : machine,
            MachineTrees = isolated ? [] : ProjectSystem.MachineBoundaries(machine, normalizedStart)
                .Select(root => (IProjectTree)new FileSystemProjectTree(root)).ToArray()
        };
        var details = ProjectSystem.LoadDetailed(tree, discovered.Discovery, selected, options);
        var resolution = details.Resolution;
        var diagnostics = resolution.Diagnostics.Concat(discovered.Selection.Diagnostics).ToImmutableArray();
        var hasProject = discovered.Discovery.Root.Sources.Any(source => source.Content.IsAvailable)
            || selected is not null;
        var root = selected?.Path ?? (hasProject ? discovered.Discovery.Location : null);
        var workspaceOut = resolution.OwningWorkspace is { } workspace && workspace.Value.OutputDirectory is { } output
            ? output.Value : null;
        var workspaceRoot = resolution.OwningWorkspace is { } owner ? Path.GetFullPath(owner.Value.Root) : null;
        return new(resolution.Effective, resolution.Origins, root is null ? null : Path.GetFullPath(root), workspaceOut, diagnostics)
        {
            WorkspaceRoot = workspaceRoot,
            MorphirHome = Path.GetFullPath(home, working),
        };
    }

    private static ConfigDiagnostic ConfigFailure(string code, string message) =>
        new(code, message, ConfigSeverity.Error,
            new SourceIdentity("<environment>", DocumentFormat.Memory, SourceRole.Environment, 0, null),
            FSharpList<string>.Empty, null);
}

/// <summary>
/// Resolves compile settings (CLI-1). Each setting comes from the first source that supplies it: a CLI flag, the
/// selected project configuration, or a documented default. It records where each setting came from, and it
/// parses format versions with <c>SourceFormatVersion.parse</c>, so the requested spelling is kept (VER-1).
/// </summary>
public sealed class ProjectCompileSettingsResolver(ICompileConfigurationLoader loader) : ICompileSettingsResolver
{
    /// <summary>The default IR format version.</summary>
    public const string DefaultIrVersion = "4";

    /// <summary>A resolver over the native file system.</summary>
    public ProjectCompileSettingsResolver() : this(new FileSystemCompileConfigurationLoader()) { }

    /// <inheritdoc />
    public Checked<CompileSettings> Resolve(CompileOverrides overrides, CompileContext context)
    {
        var errors = new List<Diagnostic>();
        var warnings = new List<Diagnostic>();
        void Error(DiagnosticCode code, string message) => errors.Add(Diagnostic.Create(Severity.Error, code, message, null));

        if (overrides.TypesOnly)
            Error(CompileCodes.UnsupportedSetting, "The option --types-only is not implemented. This build compiles complete modules only.");

        var loaded = loader.Load(overrides, context);
        foreach (var diagnostic in loaded.Diagnostics)
        {
            var text = $"{diagnostic.Source.Path}: {string.Join('.', diagnostic.KeyPath)}: {diagnostic.Message} ({diagnostic.Code})".Replace(": : ", ": ");
            if (diagnostic.Severity == ConfigSeverity.Error) Error(CompileCodes.Configuration, text);
            else warnings.Add(Diagnostic.Create(Severity.Warning, CompileCodes.Configuration, text, null));
        }
        var config = new ConfigurationView(loaded);
        var working = CompilePaths.RealPath(context.WorkingDirectory);
        var baseDirectory = loaded.ProjectRoot ?? working;

        foreach (var (key, value, reason) in new[]
                 {
                     (new[] { "ir", "layout" }, "document-tree", "The document-tree layout is not implemented. Use \"single-file\"."),
                     (new[] { "ir", "format" }, "yaml", "YAML IR is not implemented. Use \"json\"."),
                     (new[] { "ir", "format" }, "ion", "Ion IR output is not implemented. Use \"json\"."),
                 })
            if (config.String(key) is { } setting && setting.Value == value)
                Error(CompileCodes.UnsupportedSetting, $"The setting {string.Join('.', key)} = \"{value}\" in {Describe(setting.Origin)} is not supported. {reason}");

        // Language: CLI, else frontend.language from a configuration file. The upstream default is not a selection.
        Setting<LanguageId>? language = null;
        var languageText = overrides.Language is { } cliLanguage
            ? new Setting<string>(cliLanguage, new SettingOrigin.CommandLine("--language"))
            : config.String("frontend", "language") is { Origin: SettingOrigin.Configuration } configured ? configured : null;
        if (languageText is not null)
        {
            if (LanguageId.TryParse(languageText.Value, out var id, out var failure)) language = new(id, languageText.Origin);
            else Error(CompileCodes.InvalidSetting, $"The language '{languageText.Value}' from {Describe(languageText.Origin)} is not valid. {failure}");
        }

        // Package name.
        var packageText = overrides.PackageName is { } cliPackage
            ? new Setting<string>(cliPackage, new SettingOrigin.CommandLine("--package-name"))
            : config.String("project", "name");
        Setting<ParsedPackageName>? package = null;
        if (packageText is null)
            Error(CompileCodes.PackageRequired, "No package name is given. Use --package-name, or set project.name in morphir.toml.");
        else if (ParsedPackageName.TryParse(packageText.Value, out var name, out var nameFailure))
            package = new(name, packageText.Origin);
        else
            Error(CompileCodes.InvalidSetting, $"The package name '{packageText.Value}' from {Describe(packageText.Origin)} is not valid: {nameFailure}");

        // IR version, with its exact spelling.
        var versionText = overrides.IrVersion is { } cliVersion
            ? new Setting<string>(cliVersion, new SettingOrigin.CommandLine("--ir-version"))
            : config.Version("ir", "format_version") ?? new Setting<string>(DefaultIrVersion, SettingOrigin.Default.Instance);
        Setting<SourceFormatVersion>? version = null;
        if (SourceFormatVersions.TryParse(versionText.Value, out var parsedVersion, out var versionFailure))
            version = new(parsedVersion, versionText.Origin);
        else
            Error(CompileCodes.InvalidSetting, $"The IR version from {Describe(versionText.Origin)} is not valid. {versionFailure}");

        // Exposed modules.
        Setting<ImmutableArray<string>>? exposed = config.Strings("project", "exposed_modules");

        // A frontend extension: --extension, else [frontend.<language>].extension of the selected language.
        var extension = overrides.Extension is { } cliExtension
            ? new Setting<string>(cliExtension, new SettingOrigin.CommandLine("--extension"))
            : language is null ? null : config.String("frontend", language.Value.Value, "extension");
        if (extension is not null && string.IsNullOrWhiteSpace(extension.Value))
            Error(CompileCodes.InvalidSetting, $"The frontend extension from {Describe(extension.Origin)} is empty.");

        // A backend extension of the requested target, which compile --target does not run (only generate does).
        var ignoredBackend = overrides.Target?.Target is RequestedTarget.Valid requestedTarget
            ? config.String("codegen", requestedTarget.Id.Value, "extension")
            : null;

        // Sources (SRC-1): --project-file, --input, the configured project file, the source directory.
        var unread = ImmutableArray<string>.Empty;
        SourceRequest? sources = null;
        if (overrides.ProjectFile is not null && overrides.Inputs.Count > 0)
            errors.Add(Diagnostic.Create(Severity.Error, CompileCodes.ConflictingSourceOptions,
                "The options --project-file and --input cannot be combined. Use --project-file for a project file, or --input for source files or one directory.", null));
        else if (overrides.ProjectFile is { } projectFile)
            sources = new SourceRequest.ProjectFile(Path.GetFullPath(projectFile, working), new SettingOrigin.CommandLine("--project-file"));
        else if (overrides.Inputs.Count > 0)
            sources = new SourceRequest.Inputs(overrides.Inputs.Select(input => Path.GetFullPath(input, working)).ToImmutableArray());
        else if (language is not null && config.String("frontend", language.Value.Value, "project_file") is { } configuredFile)
            sources = new SourceRequest.ProjectFile(config.Resolve(configuredFile, baseDirectory), configuredFile.Origin);
        else
        {
            if (language is null) unread = config.ProjectFileKeys();
            var directory = config.String("project", "source_directory") is { Origin: SettingOrigin.Configuration } configuredDirectory
                ? configuredDirectory
                : new Setting<string>("src", SettingOrigin.Default.Instance);
            sources = new SourceRequest.SourceDirectory(config.Resolve(directory, baseDirectory), directory.Origin);
        }

        // The task place (REC-1), the same for compile and for generate without --input.
        CompileTaskPlace? place = null;
        switch (CompileTaskLocator.Locate(loaded, context, overrides.OutDir))
        {
            case Outcome<CompileTaskPlace>.Success located:
                place = located.Value;
                break;
            case Outcome<CompileTaskPlace>.Failure failed:
                foreach (var diagnostic in failed.Error.Diagnostics)
                    Error(DiagnosticCode.TryParse(diagnostic.Code, out var code, out _) ? code : CompileCodes.InvalidSetting, diagnostic.Message);
                break;
        }

        // -o/--output: an installed copy of the IR, relative to the invocation directory.
        Setting<string>? output = null;
        if (overrides.Output is { } outputText)
        {
            if (string.IsNullOrWhiteSpace(outputText)) Error(CompileCodes.InvalidSetting, "The option -o/--output needs a directory.");
            else output = new(Path.GetFullPath(outputText, working), new SettingOrigin.CommandLine("--output"));
        }

        if (errors.Count > 0 || package is null || version is null || sources is null || place is null)
            return Checked<CompileSettings>.Fail(warnings.Concat(errors));

        return Checked<CompileSettings>.Of(new CompileSettings
        {
            Language = language,
            Package = package,
            IrVersion = version,
            ExposedModules = exposed,
            Extension = extension,
            Sources = sources,
            UnreadProjectFileKeys = unread,
            IgnoredBackendExtension = ignoredBackend,
            ProjectRoot = loaded.ProjectRoot,
            OutRoot = place.OutRoot,
            Member = place.Member,
            MorphirHome = place.MorphirHome,
            Output = output,
            WorkingDirectory = working,
        }, warnings);
    }

    /// <summary>
    /// The key <c>[codegen.&lt;target&gt;].extension</c> of a target, for messages, as TOML writes it: a dotted target id
    /// is one quoted key, <c>[codegen."acme.codegen"].extension</c>.
    /// </summary>
    public static string CodegenExtensionKey(string targetId) => $"[codegen.{ConfigValueProjections.KeyText([targetId])}].extension";

    /// <summary>A short description of an origin for messages.</summary>
    public static string Describe(SettingOrigin origin) => origin.Match(
        cli => cli.Flag,
        config => config.File,
        _ => "the default");

    /// <summary>
    /// Typed reads of the effective configuration, with origins. Each read takes the key path as segments, one for each
    /// key, so an id with a dot stays one key: <c>("codegen", "acme.codegen", "extension")</c> is the TOML key
    /// <c>codegen."acme.codegen".extension</c>. The lookups and the union decisions are in F# (ConfigValueProjections).
    /// </summary>
    internal sealed class ConfigurationView(LoadedCompileConfiguration loaded)
    {
        private ConfigValue? Find(string[] path) =>
            ConfigValueProjections.TryFind(loaded.Effective, path, out var value) ? value : null;

        private SettingOrigin Origin(string[] path) =>
            ConfigValueProjections.TryFindOrigin(loaded.Origins, path, out var origin) && !origin.Source.Role.Equals(SourceRole.Defaults)
                ? new SettingOrigin.Configuration(origin.Source.Path, ConfigValueProjections.KeyText(path))
                : SettingOrigin.Default.Instance;

        public Setting<string>? String(params string[] path) =>
            Find(path) is { } value && ConfigValueProjections.TryText(value, out var text) ? new(text, Origin(path)) : null;

        public Setting<string>? Version(params string[] path) =>
            Find(path) is { } value && ConfigValueProjections.TryVersionText(value, out var text) ? new(text, Origin(path)) : null;

        public Setting<ImmutableArray<string>>? Strings(params string[] path) =>
            Find(path) is { } value && ConfigValueProjections.TryTextList(value, out var items)
                ? new([.. items], Origin(path))
                : null;

        /// <summary>Every <c>frontend.&lt;language&gt;.project_file</c> key that is set, as TOML writes it (a dotted language is quoted).</summary>
        public ImmutableArray<string> ProjectFileKeys() =>
            Find(["frontend"]) is { } tables
                ? [.. ConfigValueProjections.TablesWithKey(tables, "project_file").Select(language => ConfigValueProjections.KeyText(["frontend", language, "project_file"]))]
                : [];

        /// <summary>A path setting, relative to the directory of the file that set it, else to the base directory.</summary>
        public string Resolve(Setting<string> setting, string baseDirectory) => setting.Origin switch
        {
            SettingOrigin.Configuration { File: var file } when Path.GetDirectoryName(file) is { Length: > 0 } directory
                => Path.GetFullPath(setting.Value, directory),
            _ => Path.GetFullPath(setting.Value, baseDirectory)
        };
    }
}
