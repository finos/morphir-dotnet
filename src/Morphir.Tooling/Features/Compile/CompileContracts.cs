// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics;
using Morphir.Compilation;
using Morphir.IR.Model;
using Morphir.Tooling.Features.TaskRecords;

namespace Morphir.Tooling.Features.Compile;

/// <summary>
/// A stage of the compile pipeline, in the STRM-2 stage order. The report orders diagnostics by this order.
/// </summary>
public enum CompileStage
{
    /// <summary>Settings resolution from the command line, the project configuration and defaults.</summary>
    Settings,
    /// <summary>Source selection: a project file, input files or a directory.</summary>
    Sources,
    /// <summary>Source capture: bounded, UTF-8 reads of the selected documents.</summary>
    Capture,
    /// <summary>Frontend selection and compilation, which builds the requested IR family.</summary>
    Frontend,
    /// <summary>The IR writer of the requested format version.</summary>
    Write,
    /// <summary>Publication of the written IR.</summary>
    Publish,
    /// <summary>With <c>--target</c>: model loading from the written bytes, backend selection, option validation and generation (CLI-2).</summary>
    Generate,
    /// <summary>With <c>--target</c>: owned publication of the generated artifacts.</summary>
    GeneratePublish,
}

/// <summary>Wire values of compile stages.</summary>
public static class CompileStages
{
    /// <summary>The camelCase wire value, for example <c>settings</c>.</summary>
    public static string WireValue(this CompileStage stage) => stage switch
    {
        CompileStage.Settings => "settings",
        CompileStage.Sources => "sources",
        CompileStage.Capture => "capture",
        CompileStage.Frontend => "frontend",
        CompileStage.Write => "write",
        CompileStage.Publish => "publish",
        CompileStage.Generate => "generate",
        CompileStage.GeneratePublish => "generatePublish",
        _ => throw new UnreachableException()
    };
}

/// <summary>Why a compile failed. The kind decides the exit code.</summary>
public enum CompileFailureKind
{
    /// <summary>The frontend, an IR builder or a writer reported a source or model error (exit code 1).</summary>
    Source,
    /// <summary>The invocation is not valid (exit code 2).</summary>
    Invocation,
    /// <summary>The configuration, the project file or the selection refused the request (exit code 2).</summary>
    Configuration,
    /// <summary>An operational, protocol or I/O failure (exit code 2).</summary>
    Operational,
    /// <summary>The run was cancelled before publication (exit code 130).</summary>
    Cancelled,
}

/// <summary>Exit codes and wire values of failure kinds.</summary>
public static class CompileFailureKinds
{
    /// <summary>The exit code of a failure of this kind.</summary>
    public static int ExitCode(this CompileFailureKind kind) => kind switch
    {
        CompileFailureKind.Source => 1,
        CompileFailureKind.Cancelled => 130,
        CompileFailureKind.Invocation or CompileFailureKind.Configuration or CompileFailureKind.Operational => 2,
        _ => throw new UnreachableException()
    };

    /// <summary>The lowercase wire value, for example <c>cancelled</c>.</summary>
    public static string WireValue(this CompileFailureKind kind) => kind switch
    {
        CompileFailureKind.Source => "source",
        CompileFailureKind.Invocation => "invocation",
        CompileFailureKind.Configuration => "configuration",
        CompileFailureKind.Operational => "operational",
        CompileFailureKind.Cancelled => "cancelled",
        _ => throw new UnreachableException()
    };
}

/// <summary>Where a resolved setting came from.</summary>
public abstract record SettingOrigin
{
    private SettingOrigin() { }

    /// <summary>A command-line flag, for example <c>--ir-version</c>.</summary>
    public sealed record CommandLine(string Flag) : SettingOrigin;

    /// <summary>A configuration file, with the key path that supplied the value.</summary>
    public sealed record Configuration(string File, string Key) : SettingOrigin;

    /// <summary>A documented default.</summary>
    public sealed record Default : SettingOrigin
    {
        /// <summary>The one default origin.</summary>
        public static Default Instance { get; } = new();
    }

    /// <summary>Calls the function for this origin.</summary>
    public TResult Match<TResult>(Func<CommandLine, TResult> commandLine, Func<Configuration, TResult> configuration,
        Func<Default, TResult> @default) => this switch
        {
            CommandLine cli => commandLine(cli),
            Configuration config => configuration(config),
            Default value => @default(value),
            _ => throw new UnreachableException()
        };
}

/// <summary>A resolved setting and its origin.</summary>
public sealed record Setting<T>(T Value, SettingOrigin Origin);

/// <summary>The raw values of the command line. The settings resolver parses them.</summary>
public sealed record CompileOverrides
{
    /// <summary><c>--language</c>.</summary>
    public string? Language { get; init; }
    /// <summary><c>--input</c>, in the given order.</summary>
    public IReadOnlyList<string> Inputs { get; init; } = [];
    /// <summary><c>--project-file</c>.</summary>
    public string? ProjectFile { get; init; }
    /// <summary><c>--package-name</c>.</summary>
    public string? PackageName { get; init; }
    /// <summary><c>--ir-version</c>.</summary>
    public string? IrVersion { get; init; }
    /// <summary><c>--project</c>: a project directory, a member path or a project name.</summary>
    public string? Project { get; init; }
    /// <summary><c>--config</c>: an explicitly selected configuration file.</summary>
    public string? Config { get; init; }
    /// <summary><c>--extension</c>.</summary>
    public string? Extension { get; init; }
    /// <summary>Explicit installed full identity; never interpreted as a path or command.</summary>
    public string? ExtensionId { get; init; }
    /// <summary><c>--types-only</c>, which this build does not implement.</summary>
    public bool TypesOnly { get; init; }
    /// <summary><c>-o/--output</c>: the directory that receives an installed copy of the IR.</summary>
    public string? Output { get; init; }
    /// <summary><c>--out-dir</c>: the out root, before <c>MORPHIR_OUT_DIR</c> and <c>workspace.out_dir</c>.</summary>
    public string? OutDir { get; init; }
    /// <summary>
    /// <c>--target</c>, <c>--generate-output</c> and <c>--option</c>, parsed by the command because they need no
    /// configuration (CLI-2), or null when no target is requested.
    /// </summary>
    public CompileTarget? Target { get; init; }
}

/// <summary>The directory and environment of one invocation.</summary>
public sealed record CompileContext(string WorkingDirectory, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>The context of the current process.</summary>
    public static CompileContext Current()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value) environment[key] = value;
        return new(System.Environment.CurrentDirectory, environment);
    }
}

/// <summary>Supplies the invocation context. Tests register one; the default reads the current process.</summary>
public sealed record CompileContextSource(Func<CompileContext> Current);

/// <summary>
/// What the sources come from (SRC-1). Every path is absolute. The settings resolver chooses one, in the order
/// <c>--project-file</c>, <c>--input</c>, the configured project file, the source directory.
/// </summary>
public abstract record SourceRequest
{
    private SourceRequest() { }

    /// <summary>One project file, from <c>--project-file</c> or <c>[frontend.&lt;language&gt;].project_file</c>.</summary>
    public sealed record ProjectFile(string Path, SettingOrigin Origin) : SourceRequest;

    /// <summary><c>--input</c> values in the given order: files, or one directory.</summary>
    public sealed record Inputs(ImmutableArray<string> Paths) : SourceRequest;

    /// <summary>The project source directory, read in lexical path order.</summary>
    public sealed record SourceDirectory(string Path, SettingOrigin Origin) : SourceRequest;
}

/// <summary>The resolved settings of one compile.</summary>
public sealed record CompileSettings
{
    /// <summary>The selected language, or null when the document extensions decide it.</summary>
    public Setting<LanguageId>? Language { get; init; }
    /// <summary>The package name.</summary>
    public required Setting<ParsedPackageName> Package { get; init; }
    /// <summary>The requested IR format version, with its exact spelling.</summary>
    public required Setting<SourceFormatVersion> IrVersion { get; init; }
    /// <summary>The exposed modules, or null to expose every public module.</summary>
    public Setting<ImmutableArray<string>>? ExposedModules { get; init; }
    /// <summary>
    /// The frontend extension that <c>--extension</c> or <c>[frontend.&lt;language&gt;].extension</c> names, or
    /// null. When it is set, the run uses only the frontend that the extension connector gives (FE-2).
    /// </summary>
    public Setting<string>? Extension { get; init; }
    /// <summary>Validated installed identity selected explicitly by --extension-id.</summary>
    public Setting<Morphir.Extensions.ExtensionId>? InstalledExtensionId { get; init; }
    /// <summary>The selected WASM module, parsed from the same effective configuration as these settings.</summary>
    public Features.Extensions.WasmModuleSource? WasmModule { get; init; }
    /// <summary>Where the sources come from.</summary>
    public required SourceRequest Sources { get; init; }
    /// <summary>
    /// The configuration keys that name a project file that was not read because no language is selected.
    /// Source selection names them when it finds no documents.
    /// </summary>
    public ImmutableArray<string> UnreadProjectFileKeys { get; init; } = [];
    /// <summary>
    /// The <c>[codegen.&lt;target&gt;].extension</c> of the requested <c>--target</c>, or null. <c>compile --target</c> does
    /// not use it: it runs the built-in backend, and only <c>morphir generate</c> runs a backend extension.
    /// </summary>
    public Setting<string>? IgnoredBackendExtension { get; init; }
    /// <summary>The project root, or null outside any project.</summary>
    public string? ProjectRoot { get; init; }
    /// <summary>The out root of the workspace (REC-1), for example <c>&lt;workspace&gt;/.morphir/out</c>.</summary>
    public required OutRoot OutRoot { get; init; }
    /// <summary>The member path of the project below the out root. The root member when there is no workspace.</summary>
    public required MemberPath Member { get; init; }
    /// <summary>The Morphir home, which holds the <c>-o</c> install locks, or null when it is not known.</summary>
    public string? MorphirHome { get; init; }
    /// <summary>The absolute <c>-o</c> target, or null when the IR is not installed.</summary>
    public Setting<string>? Output { get; init; }
    /// <summary>The absolute out root.</summary>
    public string OutDirectory => OutRoot.Path;
    /// <summary>The invocation directory.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Every setting and its origin, for the report.</summary>
    public ImmutableArray<(string Name, SettingOrigin Origin)> Origins()
    {
        var origins = ImmutableArray.CreateBuilder<(string, SettingOrigin)>();
        if (Language is { } language) origins.Add(("language", language.Origin));
        origins.Add(("packageName", Package.Origin));
        origins.Add(("irVersion", IrVersion.Origin));
        if (ExposedModules is { } exposed) origins.Add(("exposedModules", exposed.Origin));
        if (Extension is { } extension) origins.Add(("extension", extension.Origin));
        if (Output is { } output) origins.Add(("output", output.Origin));
        switch (Sources)
        {
            case SourceRequest.ProjectFile file: origins.Add(("projectFile", file.Origin)); break;
            case SourceRequest.Inputs: origins.Add(("input", new SettingOrigin.CommandLine("--input"))); break;
            case SourceRequest.SourceDirectory directory: origins.Add(("sourceDirectory", directory.Origin)); break;
        }
        return origins.ToImmutable();
    }
}

/// <summary>A selected source set, with absolute paths for capture.</summary>
public sealed record SelectedSources(SourceSelection Selection, string AbsoluteRoot)
{
    /// <summary>The absolute path of each document, in order.</summary>
    public ImmutableArray<string> AbsoluteDocuments =>
        Selection.Documents.Select(document => Path.GetFullPath(Path.Combine(AbsoluteRoot, document))).ToImmutableArray();
}

/// <summary>Where the written IR went.</summary>
/// <param name="IrPath">The absolute IR file in <c>compile.dest</c>.</param>
/// <param name="Installed">The absolute files that the <c>-o</c> install of this run holds, or none.</param>
public sealed record PublishedCompile(string IrPath, ImmutableArray<string> Installed);

/// <summary>What a successful compile produced. Diagnostics travel on the events and the fold, not here.</summary>
public sealed record CompileSuccess(string Language, string Frontend, SourceFormatVersion IrVersion,
    SelectedSources Sources, ImmutableArray<string> Modules, PublishedCompile Publication)
{
    /// <summary>Actual installed-provider operation observation; absent for legacy and built-in paths.</summary>
    public System.Text.Json.JsonElement? Provider { get; init; }
    /// <summary>The generation of <c>--target</c>, or null when no target was requested.</summary>
    public CompileGeneration? Generation { get; init; }
}

/// <summary>A failed compile. Its diagnostics travel on the events and the fold.</summary>
public sealed record CompileFailure(CompileFailureKind Kind, string Message)
{
    /// <summary>The selected sources, when the run got that far.</summary>
    public SelectedSources? Sources { get; init; }

    /// <summary>The frontend label, when a frontend was selected.</summary>
    public string? Frontend { get; init; }

    /// <summary>The published compile, when the run failed after publication (in the generation stages).</summary>
    public CompileSuccess? Compiled { get; init; }

    /// <summary>The generation of <c>--target</c>, when it started.</summary>
    public CompileGeneration? Generation { get; init; }
}

/// <summary>The conclusion of a run, without diagnostics: a success or a failure.</summary>
public abstract record CompileConclusion
{
    private CompileConclusion() { }

    /// <summary>The compile succeeded.</summary>
    public sealed record Succeeded(CompileSuccess Value) : CompileConclusion;

    /// <summary>The compile failed.</summary>
    public sealed record Failed(CompileFailure Failure) : CompileConclusion;

    /// <summary>Calls the function for this conclusion.</summary>
    public TResult Match<TResult>(Func<CompileSuccess, TResult> succeeded, Func<CompileFailure, TResult> failed) => this switch
    {
        Succeeded success => succeeded(success.Value),
        Failed failure => failed(failure.Failure),
        _ => throw new UnreachableException()
    };
}

/// <summary>
/// One event of the compile stream (STRM-1). Pure stages return <c>Checked</c> values; the runner turns each
/// stage's result into events. <see cref="Completed"/> is always the last event.
/// </summary>
public abstract record CompileEvent
{
    private CompileEvent() { }

    /// <summary>A stage starts.</summary>
    public sealed record StageStarted(CompileStage Stage) : CompileEvent;

    /// <summary>Diagnostics of a stage, for one document or (with a null URI) for no document.</summary>
    public sealed record DiagnosticsReported(CompileStage Stage, string? DocumentUri, ImmutableArray<Diagnostic> Diagnostics) : CompileEvent;

    /// <summary>A stage completes.</summary>
    public sealed record StageCompleted(CompileStage Stage, bool Passed) : CompileEvent;

    /// <summary>The run ends. The settings are null when settings resolution did not complete.</summary>
    public sealed record Completed(CompileConclusion Conclusion, CompileSettings? Settings) : CompileEvent;
}

/// <summary>A compile result: the fold of the event stream.</summary>
public sealed record CompileResult(CompileConclusion Conclusion, CompileSettings? Settings, ImmutableArray<CompileStageDiagnostic> Diagnostics)
{
    /// <summary>True when the compile succeeded.</summary>
    public bool Success => Conclusion is CompileConclusion.Succeeded;

    /// <summary>The exit code: 0, 1, 2 or 130.</summary>
    public int ExitCode => Conclusion.Match(_ => 0, failure => failure.Kind.ExitCode());
}

/// <summary>A diagnostic and the stage that reported it.</summary>
public sealed record CompileStageDiagnostic(CompileStage Stage, Diagnostic Diagnostic);

/// <summary>A message to a user about a frontend that a host does not have. It names where to get one.</summary>
public sealed record FrontendHint(LanguageId Language, string Message);

/// <summary>The diagnostic codes of the compile command.</summary>
public static class CompileCodes
{
    /// <summary>A setting or option that this build does not implement.</summary>
    public static readonly DiagnosticCode UnsupportedSetting = DiagnosticCode.Create("compile.unsupported_setting");
    /// <summary>A setting has an invalid value.</summary>
    public static readonly DiagnosticCode InvalidSetting = DiagnosticCode.Create("compile.invalid_setting");
    /// <summary>No package name is given or configured.</summary>
    public static readonly DiagnosticCode PackageRequired = DiagnosticCode.Create("compile.package_required");
    /// <summary>No language is selected and the sources do not decide one.</summary>
    public static readonly DiagnosticCode LanguageRequired = DiagnosticCode.Create("compile.language_required");
    /// <summary>The configuration could not be loaded.</summary>
    public static readonly DiagnosticCode Configuration = DiagnosticCode.Create("compile.configuration");
    /// <summary>No registered frontend serves the request.</summary>
    public static readonly DiagnosticCode FrontendUnavailable = DiagnosticCode.Create("compile.frontend_unavailable");
    /// <summary>The frontends that read the language do not produce the requested IR target.</summary>
    public static readonly DiagnosticCode IrVersionNotServed = DiagnosticCode.Create("compile.ir_version_not_served");
    /// <summary>More than one registered frontend serves the request.</summary>
    public static readonly DiagnosticCode AmbiguousFrontend = DiagnosticCode.Create("compile.ambiguous_frontend");
    /// <summary>The frontend failed operationally.</summary>
    public static readonly DiagnosticCode FrontendFailed = DiagnosticCode.Create("compile.frontend_failed");
    /// <summary>The run was cancelled.</summary>
    public static readonly DiagnosticCode Cancelled = DiagnosticCode.Create("compile.cancelled");
    /// <summary>The command line is not valid.</summary>
    public static readonly DiagnosticCode InvalidArguments = DiagnosticCode.Create("compile.invalid_arguments");
    /// <summary>An unexpected failure.</summary>
    public static readonly DiagnosticCode Failed = DiagnosticCode.Create("compile.failed");
    /// <summary><c>--target</c> was given, and this build registers no generation services.</summary>
    public static readonly DiagnosticCode GenerationUnavailable = DiagnosticCode.Create("compile.generation_unavailable");
    /// <summary>The written IR could not be published.</summary>
    public static readonly DiagnosticCode PublishFailed = DiagnosticCode.Create("compile.publish_failed");

    /// <summary><c>--project-file</c> and <c>--input</c> are both given.</summary>
    public static readonly DiagnosticCode ConflictingSourceOptions = DiagnosticCode.Create("sources.conflicting_source_options");
    /// <summary>An <c>--input</c> file is a project file.</summary>
    public static readonly DiagnosticCode ProjectFileAsInput = DiagnosticCode.Create("sources.project_file_as_input");
    /// <summary>A source path does not exist.</summary>
    public static readonly DiagnosticCode MissingSource = DiagnosticCode.Create("sources.missing");
    /// <summary>No source document was found.</summary>
    public static readonly DiagnosticCode NoDocuments = DiagnosticCode.Create("sources.no_documents");
    /// <summary>A document is outside the source root.</summary>
    public static readonly DiagnosticCode OutsideRoot = DiagnosticCode.Create("sources.outside_root");
}
