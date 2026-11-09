using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Morphir.Compilation;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Compile.Sources;

/// <summary>The diagnostic codes of project-file expansion.</summary>
public static class ProjectFileSourceCodes
{
    /// <summary>A document of the project file is outside the project file's directory.</summary>
    public static readonly DiagnosticCode OutsideRoot = DiagnosticCode.Create("sources.outside_root");
    /// <summary>The project file declares no source document.</summary>
    public static readonly DiagnosticCode NoDocuments = DiagnosticCode.Create("sources.no_documents");
    /// <summary>The project file declares a document more than once.</summary>
    public static readonly DiagnosticCode RepeatedDocument = DiagnosticCode.Create("sources.repeated_document");
    /// <summary>A document path cannot be written as a portable root-relative path.</summary>
    public static readonly DiagnosticCode NotPortable = DiagnosticCode.Create("sources.not_portable");
}

/// <summary>The diagnostic codes of the MSBuild project-file provider.</summary>
public static class MsBuildCodes
{
    /// <summary>No .NET SDK 8.0 or later could evaluate the project file.</summary>
    public static readonly DiagnosticCode Unavailable = DiagnosticCode.Create("msbuild.unavailable");
    /// <summary>MSBuild could not evaluate the project file. The message keeps the MSBuild errors.</summary>
    public static readonly DiagnosticCode EvaluationFailed = DiagnosticCode.Create("msbuild.evaluation_failed");
    /// <summary>The project file has a <c>ProjectReference</c>, which compilation does not follow.</summary>
    public static readonly DiagnosticCode UnsupportedProjectReference = DiagnosticCode.Create("msbuild.unsupported_project_reference");
    /// <summary>The project file has a <c>PackageReference</c>, which compilation ignores. A warning.</summary>
    public static readonly DiagnosticCode IgnoredPackageReference = DiagnosticCode.Create("msbuild.ignored_package_reference");
    /// <summary>Evaluation did not end before the deadline.</summary>
    public static readonly DiagnosticCode DeadlineExceeded = DiagnosticCode.Create("msbuild.deadline_exceeded");
    /// <summary>Evaluation wrote more output than the budget.</summary>
    public static readonly DiagnosticCode OutputLimitExceeded = DiagnosticCode.Create("msbuild.output_limit_exceeded");
}

/// <summary>The settings of the MSBuild provider.</summary>
/// <param name="DotnetPath">The <c>dotnet</c> executable. A bare name is found on <c>PATH</c>.</param>
/// <param name="Deadline">How long evaluation can take before its process tree is stopped.</param>
/// <param name="MaxOutputBytes">The byte budget of each MSBuild output stream.</param>
public sealed record MsBuildProviderOptions(string DotnetPath, TimeSpan Deadline, int MaxOutputBytes)
{
    /// <summary>
    /// <c>DOTNET_HOST_PATH</c> when it is set (the SDK sets it for the processes it starts), else <c>dotnet</c>
    /// from <c>PATH</c>; a two-minute deadline; and 16 MiB for each output stream.
    /// </summary>
    public static MsBuildProviderOptions Default { get; } = new(
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet",
        TimeSpan.FromMinutes(2),
        16 * 1024 * 1024);
}

/// <summary>
/// Expands an MSBuild project file (<c>*.*proj</c>) into its <c>Compile</c> items, in declared order. It runs
/// <c>dotnet msbuild</c> as a child process for evaluation only: no restore, no target and no output written.
/// The process gets explicit arguments and no shell, has no build-node reuse, and runs under a deadline and
/// an output budget.
/// </summary>
/// <remarks>
/// <para>Item rules:</para>
/// <list type="bullet">
/// <item>A <c>Compile</c> item outside the project file's directory fails with <c>sources.outside_root</c>.</item>
/// <item>A <c>ProjectReference</c> fails with <c>msbuild.unsupported_project_reference</c>.</item>
/// <item>A <c>PackageReference</c> of the project gives the warning <c>msbuild.ignored_package_reference</c>.
/// A reference that the .NET SDK adds by itself, such as the implicit <c>FSharp.Core</c>, gives no warning.</item>
/// </list>
/// <para>Cancellation stops the MSBuild process tree and throws <see cref="OperationCanceledException"/>.</para>
/// </remarks>
public sealed partial class MsBuildProjectFileProvider : IProjectFileProvider
{
    private const string RequiredSdk = "the .NET SDK 8.0 or later";

    // Variables that a parent build sets for its own SDK. Inherited, they can point evaluation at another SDK.
    private static readonly string[] ParentBuildVariables =
        ["MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly"];

    [GeneratedRegex(@"\bMSB(?<number>\d{4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex MsBuildCode();

    private readonly MsBuildProviderOptions _options;
    private readonly Func<ProcessStartInfo, ProcessLimits, CancellationToken, Task<ProcessRun>> _run;

    /// <summary>A provider with the default options.</summary>
    public MsBuildProjectFileProvider() : this(MsBuildProviderOptions.Default) { }

    /// <summary>A provider with <paramref name="options"/>.</summary>
    public MsBuildProjectFileProvider(MsBuildProviderOptions options) : this(options, BoundedProcess.RunAsync) { }

    internal MsBuildProjectFileProvider(MsBuildProviderOptions options,
        Func<ProcessStartInfo, ProcessLimits, CancellationToken, Task<ProcessRun>> run)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _run = run;
    }

    /// <inheritdoc />
    public ComponentId Identity { get; } = ComponentId.Create("msbuild");

    /// <inheritdoc />
    public IReadOnlyList<string> Patterns { get; } = ["*.*proj"];

    /// <inheritdoc />
    public async Task<Checked<SourceSelection>> ExpandAsync(string projectFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(projectFile);
        var run = await _run(StartInfo(fullPath), new ProcessLimits(_options.Deadline, _options.MaxOutputBytes),
            cancellationToken).ConfigureAwait(false);
        return run switch
        {
            ProcessRun.Exited { ExitCode: 0 } exited => Parse(exited.StandardOutput) switch
            {
                { } project => Select(fullPath, project),
                null => Fail(MsBuildCodes.EvaluationFailed,
                    $"MSBuild evaluation of '{fullPath}' did not give the expected JSON output."),
            },
            ProcessRun.Exited exited => Fail(Classify(fullPath, exited)),
            ProcessRun.NotStarted notStarted => Fail(MsBuildCodes.Unavailable,
                $"The MSBuild provider needs {RequiredSdk}. '{_options.DotnetPath}' did not start: {notStarted.Reason}"),
            ProcessRun.DeadlineExceeded deadline => Fail(MsBuildCodes.DeadlineExceeded,
                $"MSBuild evaluation of '{fullPath}' did not end within {deadline.Deadline.TotalSeconds:0.#} s and was stopped."),
            ProcessRun.OutputLimitExceeded limit => Fail(MsBuildCodes.OutputLimitExceeded,
                $"MSBuild evaluation of '{fullPath}' wrote more than {limit.MaxOutputBytes} bytes and was stopped."),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>The evaluation command: explicit arguments, no shell, no node reuse and no response files.</summary>
    internal ProcessStartInfo StartInfo(string projectFile)
    {
        var startInfo = new ProcessStartInfo(_options.DotnetPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(projectFile) ?? string.Empty,
        };
        foreach (var argument in (string[])
                 [
                     "msbuild", projectFile,
                     "-getItem:Compile", "-getItem:ProjectReference", "-getItem:PackageReference",
                     "-getProperty:MSBuildProjectDirectory", "-getProperty:MSBuildExtensionsPath",
                     "-nologo", "-nodeReuse:false", "-maxCpuCount:1", "-noAutoResponse", "-interactive:false",
                 ])
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (var variable in ParentBuildVariables) startInfo.Environment.Remove(variable);
        return startInfo;
    }

    /// <summary>
    /// A failed run with no MSBuild code, or with MSB1001 (an unknown switch: <c>-getItem</c> needs SDK 8.0),
    /// means no usable SDK. Any other MSBuild code is an evaluation error.
    /// </summary>
    private static Diagnostic Classify(string projectFile, ProcessRun.Exited exited)
    {
        var output = $"{exited.StandardOutput}\n{exited.StandardError}";
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var codes = MsBuildCode().Matches(output).Select(match => match.Groups["number"].Value).ToHashSet();
        if (codes.Count == 0 || codes.Contains("1001"))
            return Error(MsBuildCodes.Unavailable,
                $"The MSBuild provider needs {RequiredSdk}. 'dotnet msbuild' exited with code {exited.ExitCode}: "
                + string.Join(" ", lines.Take(3)));
        var errors = lines.Where(line => MsBuildCode().IsMatch(line)).Take(10);
        return Error(MsBuildCodes.EvaluationFailed,
            $"MSBuild could not evaluate '{projectFile}':{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    private static Checked<SourceSelection> Select(string projectFile, EvaluatedProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var documents = new List<string>();
        foreach (var item in project.Compile)
        {
            var relative = Path.GetRelativePath(project.Directory, item.FullPath);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                diagnostics.Add(Error(ProjectFileSourceCodes.OutsideRoot,
                    $"The Compile item '{item.Identity}' is outside the directory of '{projectFile}'. " +
                    "A project file can give only documents below its own directory."));
            else
                documents.Add(relative.Replace('\\', '/'));
        }

        foreach (var reference in project.ProjectReferences)
            diagnostics.Add(Error(MsBuildCodes.UnsupportedProjectReference,
                $"ProjectReference {reference.Identity} is not supported. Compile one project file at a time."));

        foreach (var package in project.PackageReferences.Where(package => !package.IsImplicitIn(project.SdkDirectory)))
            diagnostics.Add(Diagnostic.Create(DiagnosticSeverity.Warning, MsBuildCodes.IgnoredPackageReference,
                $"PackageReference {package.Identity} is ignored. Compilation uses only the Compile items.", null));

        if (diagnostics.Any(diagnostic => diagnostic.IsError)) return Checked<SourceSelection>.Fail(diagnostics);

        var root = Path.GetDirectoryName(projectFile)!;
        if (SourceSelection.TryParse(root, documents, projectFile, out var selection, out var failure))
            return Checked<SourceSelection>.Of(selection, diagnostics);

        diagnostics.Add(failure.Match(
            emptyRoot: () => Error(ProjectFileSourceCodes.NotPortable, $"'{projectFile}' has no directory."),
            noDocuments: () => Error(ProjectFileSourceCodes.NoDocuments, $"'{projectFile}' declares no Compile item."),
            notPortable: path => Error(ProjectFileSourceCodes.NotPortable,
                $"'{projectFile}' gives the document path '{path}', which is not portable."),
            repeated: path => Error(ProjectFileSourceCodes.RepeatedDocument,
                $"'{projectFile}' declares the Compile item '{path}' more than once.")));
        return Checked<SourceSelection>.Fail(diagnostics);
    }

    /// <summary>The evaluation JSON as a parsed project, or null when the output is not the expected shape.</summary>
    internal static EvaluatedProject? Parse(string output)
    {
        // Evaluation writes only the JSON document. Text before it, such as a warning, is not part of it.
        var start = output.IndexOf('{');
        if (start < 0) return null;
        try
        {
            using var document = JsonDocument.Parse(output.AsMemory(start));
            var root = document.RootElement;
            if (!root.TryGetProperty("Properties", out var properties)
                || !root.TryGetProperty("Items", out var items)
                || Text(properties, "MSBuildProjectDirectory") is not { Length: > 0 } directory)
                return null;
            var compile = Items(items, "Compile");
            var projectReferences = Items(items, "ProjectReference");
            var packageReferences = Items(items, "PackageReference");
            if (compile is null || projectReferences is null || packageReferences is null) return null;
            return new EvaluatedProject(directory, Text(properties, "MSBuildExtensionsPath") ?? string.Empty,
                compile.Value, projectReferences.Value, packageReferences.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ImmutableArray<EvaluatedItem>? Items(JsonElement items, string type)
    {
        if (!items.TryGetProperty(type, out var list)) return [];
        if (list.ValueKind != JsonValueKind.Array) return null;
        var result = ImmutableArray.CreateBuilder<EvaluatedItem>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || Text(item, "Identity") is not { Length: > 0 } identity
                || Text(item, "FullPath") is not { Length: > 0 } fullPath)
                return null;
            result.Add(new EvaluatedItem(identity, fullPath, Text(item, "DefiningProjectFullPath") ?? string.Empty,
                string.Equals(Text(item, "IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase)));
        }
        return result.ToImmutable();
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Diagnostic Error(DiagnosticCode code, string message) =>
        Diagnostic.Create(DiagnosticSeverity.Error, code, message, null);

    private static Checked<SourceSelection> Fail(DiagnosticCode code, string message) => Fail(Error(code, message));

    private static Checked<SourceSelection> Fail(Diagnostic diagnostic) => Checked<SourceSelection>.Fail([diagnostic]);
}

/// <summary>One evaluated MSBuild item.</summary>
internal sealed record EvaluatedItem(string Identity, string FullPath, string DefiningProjectFullPath, bool IsImplicitlyDefined)
{
    /// <summary>True when the .NET SDK, not the project, added the item.</summary>
    internal bool IsImplicitIn(string sdkDirectory) =>
        IsImplicitlyDefined
        || sdkDirectory.Length > 0
        && DefiningProjectFullPath.StartsWith(sdkDirectory, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
}

/// <summary>The parts of an evaluated project file that the provider reads.</summary>
internal sealed record EvaluatedProject(
    string Directory,
    string SdkDirectory,
    ImmutableArray<EvaluatedItem> Compile,
    ImmutableArray<EvaluatedItem> ProjectReferences,
    ImmutableArray<EvaluatedItem> PackageReferences);
