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
using Morphir.Tooling.Features.TaskRecords;
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

    /// <summary>
    /// The resolution that gave <see cref="Effective"/> and <see cref="Origins"/>, which F# reads for C#
    /// (<c>ConfigValueProjections.TryFindConfiguredOrigin</c>). Null when no configuration was loaded, which has no
    /// configured origin.
    /// </summary>
    public DocumentResolution? Resolution { get; init; }
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
            Resolution = resolution,
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
            var key = string.Join('.', diagnostic.KeyPath);
            var text = $"{diagnostic.Source.Path}: {key}: {diagnostic.Message} ({diagnostic.Code})".Replace(": : ", ": ");
            // The resolver checks every value of ir.layout and ir.mode itself, in the order of their precedence (ResolveLayout).
            if (diagnostic.Severity == ConfigSeverity.Error && IsLayoutValidation(diagnostic)) continue;
            if (diagnostic.Severity == ConfigSeverity.Error) Error(CompileCodes.Configuration, text);
            else warnings.Add(Diagnostic.Create(Severity.Warning, CompileCodes.Configuration, text, null));
        }
        var config = new ConfigurationView(loaded);
        var working = CompilePaths.RealPath(context.WorkingDirectory);
        var baseDirectory = loaded.ProjectRoot ?? working;

        foreach (var (key, value, reason) in new[]
                 {
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

        // IR layout: --ir-layout, else ir.layout, else the deprecated ir.mode, else single-file.
        var layout = ResolveLayout(overrides, config, warnings, Error);
        if (layout is { Value: IrLayout.DocumentTree } tree && version is { } requested && !HasDocumentTrees(requested.Value.Version))
            Error(CompileCodes.UnsupportedSetting,
                $"The layout document-tree from {Describe(tree.Origin)} is not supported for IR version {requested.Value.Text} from {Describe(requested.Origin)}. "
                + "Document trees exist only for IR 3 (3.1.0) and IR 4. Use \"single-file\", or IR version 3 or 4.");

        // Exposed modules.
        Setting<ImmutableArray<string>>? exposed = config.Strings("project", "exposed_modules");

        // A frontend extension: --extension, else [frontend.<language>].extension of the selected language.
        if (overrides.ExtensionId is not null && overrides.Extension is not null)
            Error(CompileCodes.InvalidArguments, "Choose --extension or --extension-id, not both.");
        Setting<Morphir.Extensions.ExtensionId>? installedId = null;
        if (overrides.ExtensionId is { } rawId)
        {
            if (Morphir.Extensions.ExtensionId.TryParse(rawId, out var parsedId, out _))
                installedId = new(parsedId, new SettingOrigin.CommandLine("--extension-id"));
            else Error(CompileCodes.InvalidArguments, "--extension-id must be a valid full extension identity.");
        }
        var extension = overrides.ExtensionId is not null ? null : overrides.Extension is { } cliExtension
            ? new Setting<string>(cliExtension, new SettingOrigin.CommandLine("--extension"))
            : language is null ? null : config.String("frontend", language.Value.Value, "extension");
        if (overrides.ExtensionId is null && extension is null && language is not null && config.String("extensions", language.Value.Value, "path") is { } modulePath)
            extension = new(language.Value.Value, modulePath.Origin);
        Features.Extensions.WasmModuleSource? wasmModule = null;
        if (overrides.ExtensionId is null && extension is not null && !Features.Extensions.ConfiguredWasmModule.TryRead(config, extension.Value,
                loaded.ProjectRoot ?? working, out wasmModule, out var moduleError))
            Error(CompileCodes.InvalidSetting, moduleError!);
        if (extension is not null && string.IsNullOrWhiteSpace(extension.Value))
            Error(CompileCodes.InvalidSetting, $"The frontend extension from {Describe(extension.Origin)} is empty.");

        // A backend extension of the requested target, which compile --target does not run (only generate does).
        var ignoredBackend = overrides.Target?.Target is RequestedTarget.Valid requestedTarget
            ? config.String("codegen", requestedTarget.Id.Value, "extension")
            : null;
        // The other keys of that table are configured backend options, as generate reads them.
        var configuredBackendOptions = overrides.Target?.Target is RequestedTarget.Valid optionsTarget
            ? BackendOptions.ConfiguredOf(config.ScalarMembers("codegen", optionsTarget.Id.Value))
            : [];

        // The other keys of each [frontend.<language>] table are extra options of the frontend of that language (OPT-1).
        var frontendOptions = config.FrontendOptionTables();

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

        if (errors.Count > 0 || package is null || version is null || layout is null || sources is null || place is null)
            return Checked<CompileSettings>.Fail(warnings.Concat(errors));

        return Checked<CompileSettings>.Of(new CompileSettings
        {
            Language = language,
            Package = package,
            IrVersion = version,
            IrLayout = layout,
            ExposedModules = exposed,
            Extension = extension,
            InstalledExtensionId = installedId,
            WasmModule = wasmModule,
            Sources = sources,
            UnreadProjectFileKeys = unread,
            IgnoredBackendExtension = ignoredBackend,
            ConfiguredBackendOptions = configuredBackendOptions,
            FrontendOptions = frontendOptions,
            ProjectRoot = loaded.ProjectRoot,
            OutRoot = place.OutRoot,
            Member = place.Member,
            MorphirHome = place.MorphirHome,
            Output = output,
            WorkingDirectory = working,
        }, warnings);
    }

    /// <summary>
    /// The layout of the IR (CLI-1): <c>--ir-layout</c>, else <c>ir.layout</c>, else the deprecated <c>ir.mode</c>
    /// (<c>classic</c> is <c>single-file</c>, <c>vfs</c> is <c>document-tree</c>), else <c>single-file</c>. Each value that
    /// is set is checked once: the selected value that is not valid (not one of the words, or not a string) is one
    /// <see cref="CompileCodes.InvalidSetting"/> error, and an overridden value that is not valid is one warning with the
    /// same code that says it is ignored. A configured <c>ir.mode</c> always gives a warning, as the <c>morphir.toml</c>
    /// specification says. Null when the selected value is not valid.
    /// </summary>
    private static Setting<IrLayout>? ResolveLayout(CompileOverrides overrides, ConfigurationView config, List<Diagnostic> warnings,
        Action<DiagnosticCode, string> error)
    {
        var candidates = new List<LayoutCandidate>();
        if (overrides.IrLayout is { } cli) candidates.Add(new(CliLayout, new(cli, new SettingOrigin.CommandLine(CliLayout)), IsMode: false));
        if (config.Configured("ir", "layout") is { } layout) candidates.Add(new(LayoutKey, layout, IsMode: false));
        if (config.Configured("ir", "mode") is { } mode)
        {
            candidates.Add(new(ModeKey, mode, IsMode: true));
            var spelled = mode.Value is { } text ? $" = \"{text}\"" : "";
            warnings.Add(Diagnostic.Create(Severity.Warning, CompileCodes.DeprecatedSetting,
                $"The setting {ModeKey}{spelled} in {Describe(mode.Origin)} is deprecated. Use {LayoutKey} = \"single-file\" or \"document-tree\"."
                + (candidates[0].IsMode ? "" : $" The layout {candidates[0].Name} is used, and {ModeKey} is ignored."), null));
        }
        if (candidates.Count == 0) return new(IrLayout.SingleFile, SettingOrigin.Default.Instance);

        var selected = candidates[0];
        foreach (var overridden in candidates.Skip(1))
            if (overridden.Parse() is null)
                warnings.Add(Diagnostic.Create(Severity.Warning, CompileCodes.InvalidSetting,
                    $"{overridden.Refusal()} It is ignored: the layout {selected.Name} is used.", null));
        if (selected.Parse() is { } parsed) return new(parsed, selected.Value.Origin);
        error(CompileCodes.InvalidSetting, selected.Refusal());
        return null;
    }

    /// <summary>A layout value that is set: the option or key that sets it, its text (null when it is not a string) and its origin.</summary>
    private sealed record LayoutCandidate(string Name, Setting<string?> Value, bool IsMode)
    {
        /// <summary>The layout of the value, or null when it is not one of the words of its key.</summary>
        public IrLayout? Parse() => IsMode
            ? Value.Value switch { "classic" => IrLayout.SingleFile, "vfs" => IrLayout.DocumentTree, _ => null }
            : IrStorageNames.TryParseLayout(Value.Value, out var layout) ? layout : null;

        /// <summary>Why the value is refused, with the option or key, its file and the accepted values.</summary>
        public string Refusal()
        {
            var place = Value.Origin is SettingOrigin.Configuration ? $" in {Describe(Value.Origin)}" : "";
            var what = Value.Value is { } text ? $"{Name} = '{text}'{place} is not valid" : $"{Name}{place} is not a string";
            var accepted = IsMode ? $"Use {LayoutKey} = \"single-file\" or \"document-tree\"." : "Use \"single-file\" or \"document-tree\".";
            return $"The IR {(IsMode ? "mode" : "layout")} {what}. {accepted}";
        }
    }

    private const string CliLayout = "--ir-layout";

    /// <summary>The code of a schema check of a value; a parse or reader error of a file has another code.</summary>
    private const string SchemaValidation = "config.validation";

    /// <summary>
    /// Whether <paramref name="diagnostic"/> is the schema check of the value of exactly <c>ir.layout</c> or <c>ir.mode</c>,
    /// which the resolver checks itself in the order of their precedence. Any other diagnostic of those keys, such as
    /// a duplicate key that makes the whole file invalid, stays an error of the configuration.
    /// </summary>
    internal static bool IsLayoutValidation(ConfigDiagnostic diagnostic) =>
        diagnostic.Code == SchemaValidation
        && (ConfigValueProjections.IsAt(diagnostic, ["ir", "layout"]) || ConfigValueProjections.IsAt(diagnostic, ["ir", "mode"]));

    private const string LayoutKey = "ir.layout";
    private const string ModeKey = "ir.mode";

    /// <summary>
    /// Whether the document-tree specifications define a tree for a version: not for classic IR 1 or 2. A version that no
    /// writer declares is refused by the writer registry.
    /// </summary>
    private static bool HasDocumentTrees(Morphir.IR.Versioning.FormatVersion version) =>
        !Morphir.Compilation.FormatVersion.SameVersion(version, Morphir.IR.Versioning.FormatVersion.NewClassic(1))
        && !Morphir.Compilation.FormatVersion.SameVersion(version, Morphir.IR.Versioning.FormatVersion.NewClassic(2));

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
        public bool Has(params string[] path) => Find(path) is not null;
        private ConfigValue? Find(string[] path) =>
            ConfigValueProjections.TryFind(loaded.Effective, path, out var value) ? value : null;

        private SettingOrigin Origin(string[] path) =>
            ConfigValueProjections.TryFindOrigin(loaded.Origins, path, out var origin) && !origin.Source.Role.Equals(SourceRole.Defaults)
                ? new SettingOrigin.Configuration(origin.Source.Path, ConfigValueProjections.KeyText(path))
                : SettingOrigin.Default.Instance;

        /// <summary>
        /// The value at the path when a configuration file sets it: its text, or a null text when the value is not a string.
        /// A default is not a configured value. A nonempty table has its origins on the keys below it, so the first of them
        /// that a configuration file sets is the origin of the table.
        /// </summary>
        public Setting<string?>? Configured(params string[] path) =>
            Find(path) is { } value && ConfiguredOrigin(path) is { } origin
                ? new(ConfigValueProjections.TryText(value, out var text) ? text : null, origin)
                : null;

        private SettingOrigin.Configuration? ConfiguredOrigin(string[] path) =>
            loaded.Resolution is { } resolution && ConfigValueProjections.TryFindConfiguredOrigin(resolution, path, out var origin)
                ? new SettingOrigin.Configuration(origin.Source.Path, ConfigValueProjections.KeyText(path))
                : null;

        public Setting<string>? String(params string[] path) =>
            Find(path) is { } value && ConfigValueProjections.TryText(value, out var text) ? new(text, Origin(path)) : null;

        public Setting<string>? Version(params string[] path) =>
            Find(path) is { } value && ConfigValueProjections.TryVersionText(value, out var text) ? new(text, Origin(path)) : null;

        public Setting<ImmutableArray<string>>? Strings(params string[] path) =>
            Find(path) is { } value && ConfigValueProjections.TryTextList(value, out var items)
                ? new([.. items], Origin(path))
                : null;

        /// <summary>The scalar members of the table at the path, as JSON values, in ordinal order. No table gives none.</summary>
        public IReadOnlyList<KeyValuePair<string, System.Text.Json.JsonElement>> ScalarMembers(params string[] path) =>
            Find(path) is { } table ? ConfigValueProjections.ScalarMembers(table) : [];

        /// <summary>
        /// The extra options of each <c>[frontend.&lt;language&gt;]</c> table, by the language: each scalar key except
        /// <c>extension</c> and <c>project_file</c>, which select the frontend and the sources, with <c>-</c> for <c>_</c>
        /// (<c>loop_lowering</c> is <c>loop-lowering</c>).
        /// </summary>
        public ImmutableDictionary<LanguageId, ImmutableArray<KeyValuePair<string, System.Text.Json.JsonElement>>> FrontendOptionTables()
        {
            var tables = ImmutableDictionary.CreateBuilder<LanguageId, ImmutableArray<KeyValuePair<string, System.Text.Json.JsonElement>>>();
            if (Find(["frontend"]) is not { } frontend) return tables.ToImmutable();
            foreach (var language in ConfigValueProjections.Tables(frontend))
            {
                ImmutableArray<KeyValuePair<string, System.Text.Json.JsonElement>> options =
                [
                    .. ScalarMembers("frontend", language)
                        .Where(pair => pair.Key is not ("extension" or "project_file"))
                        .Select(pair => new KeyValuePair<string, System.Text.Json.JsonElement>(pair.Key.Replace('_', '-'), pair.Value))
                ];
                // A table name that is not a language id names no frontend, so no frontend can read its keys.
                if (options.Length > 0 && LanguageId.TryParse(language, out var id, out _)) tables[id] = options;
            }
            return tables.ToImmutable();
        }

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
