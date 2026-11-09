// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.TaskRecords;

/// <summary>The checks that keep a relative path inside the directory that it is joined onto.</summary>
internal static class RelativeSegments
{
    /// <summary>
    /// Splits <paramref name="text"/> on <c>/</c>. Each segment must be an ordinary name: not empty, not
    /// <c>.</c> or <c>..</c>, with no backslash and no drive prefix. A rooted path is refused.
    /// </summary>
    public static bool TryParse(string text, bool allowTrailingSlash, out ImmutableArray<string> segments, [NotNullWhen(false)] out string? error)
    {
        segments = [];
        if (text.Length == 0)
        {
            error = "The path is empty.";
            return false;
        }
        if (text[0] == '/' || Path.IsPathRooted(text))
        {
            error = $"The path '{text}' is rooted; it must be relative.";
            return false;
        }
        var body = allowTrailingSlash && text.Length > 1 && text[^1] == '/' ? text[..^1] : text;
        var parts = body.Split('/');
        foreach (var part in parts)
        {
            if (!IsOrdinary(part))
            {
                error = $"The path '{text}' has the segment '{part}'; each segment must be an ordinary name, not '.', '..', empty, or a drive or backslash path.";
                return false;
            }
        }
        segments = [.. parts];
        error = null;
        return true;
    }

    private static bool IsOrdinary(string segment) =>
        segment.Length > 0
        && segment != "."
        && segment != ".."
        && !segment.Contains('\\')
        && !(segment.Length >= 2 && char.IsAsciiLetter(segment[0]) && segment[1] == ':');
}

/// <summary>
/// A task id such as <c>compile</c> or <c>generate/scala</c>. Each segment names one directory below the
/// out root, so a task id cannot place its files outside it.
/// </summary>
public sealed record TaskId
{
    private TaskId(string value, ImmutableArray<string> segments)
    {
        Value = value;
        Segments = segments;
    }

    /// <summary>The compile task.</summary>
    public static TaskId Compile { get; } = new("compile", ["compile"]);

    /// <summary>The id as it is written in a record.</summary>
    public string Value { get; }

    /// <summary>The directory segments of the id. The last one names the <c>.dest</c>, <c>.json</c> and <c>.lock</c> files.</summary>
    public ImmutableArray<string> Segments { get; }

    /// <summary>Parses a task id.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out TaskId? id, [NotNullWhen(false)] out string? error)
    {
        id = null;
        if (text is null)
        {
            error = "The task id is missing.";
            return false;
        }
        if (!RelativeSegments.TryParse(text, allowTrailingSlash: false, out var segments, out error)) return false;
        id = new TaskId(text, segments);
        return true;
    }

    /// <inheritdoc />
    public bool Equals(TaskId? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// A path relative to a task's <c>.dest</c> directory or to an install target, as a record writes it:
/// <c>/</c> separators and ordinary segments only. A <c>document-tree</c> path can end with <c>/</c>.
/// </summary>
public sealed record RecordPath
{
    private RecordPath(string value, ImmutableArray<string> segments)
    {
        Value = value;
        Segments = segments;
    }

    /// <summary>The path as it is written in a record.</summary>
    public string Value { get; }

    /// <summary>The segments of the path, without a trailing empty segment.</summary>
    public ImmutableArray<string> Segments { get; }

    /// <summary>Parses a record path.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out RecordPath? path, [NotNullWhen(false)] out string? error)
    {
        path = null;
        if (text is null)
        {
            error = "The path is missing.";
            return false;
        }
        if (!RelativeSegments.TryParse(text, allowTrailingSlash: true, out var segments, out error)) return false;
        path = new RecordPath(text, segments);
        return true;
    }

    /// <summary>Parses a record path that the caller knows is valid.</summary>
    /// <exception cref="ArgumentException">The text is not a valid record path.</exception>
    public static RecordPath Parse(string text) =>
        TryParse(text, out var path, out var error) ? path : throw new ArgumentException(error, nameof(text));

    /// <summary>The path below <paramref name="directory"/>, with native separators.</summary>
    public string Under(string directory) => Path.Combine([directory, .. Segments]);

    /// <inheritdoc />
    public bool Equals(RecordPath? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// A workspace member's declared path relative to the workspace root, such as <c>packages/orders</c>. The root
/// module has the empty path. The member path places the member's task files below the out root.
/// </summary>
public sealed record MemberPath
{
    private MemberPath(string value, ImmutableArray<string> segments)
    {
        Value = value;
        Segments = segments;
    }

    /// <summary>The root module of a workspace, or a project with no workspace.</summary>
    public static MemberPath Root { get; } = new("", []);

    /// <summary>The path as a record writes it in <c>module</c>.</summary>
    public string Value { get; }

    /// <summary>The directory segments of the path.</summary>
    public ImmutableArray<string> Segments { get; }

    /// <summary>Parses a member path. The empty text is the root module.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MemberPath? path, [NotNullWhen(false)] out string? error)
    {
        path = null;
        if (text is null)
        {
            error = "The module path is missing.";
            return false;
        }
        if (text.Length == 0)
        {
            path = Root;
            error = null;
            return true;
        }
        if (!RelativeSegments.TryParse(text, allowTrailingSlash: false, out var segments, out error)) return false;
        path = new MemberPath(text, segments);
        return true;
    }

    /// <summary>
    /// The member path of the project in <paramref name="projectRoot"/>. Use the path that the workspace
    /// declares, not the target of a link, because the declared path places the member's output.
    /// </summary>
    public static Outcome<MemberPath> FromDirectories(string workspaceRoot, string projectRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(projectRoot)).Replace('\\', '/');
        if (relative == ".") return new Outcome<MemberPath>.Success(Root);
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            || !TryParse(relative, out var path, out _))
            return TaskFailures.Of<MemberPath>(GenerationFailureKind.Invocation, "out.member_outside_workspace",
                $"The project {projectRoot} is not inside the workspace {workspaceRoot}.");
        return new Outcome<MemberPath>.Success(path);
    }

    /// <inheritdoc />
    public bool Equals(MemberPath? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Where the out root came from.</summary>
public enum OutRootSource
{
    /// <summary>The <c>--out-dir</c> option.</summary>
    Flag,

    /// <summary>The <c>MORPHIR_OUT_DIR</c> environment variable.</summary>
    Environment,

    /// <summary>The workspace setting <c>workspace.out_dir</c>.</summary>
    Workspace,

    /// <summary><c>&lt;workspace root&gt;/.morphir/out</c>.</summary>
    Default,
}

/// <summary>The values that decide the out root of one command run. The caller supplies the environment.</summary>
/// <param name="CurrentDirectory">The base of a relative <c>--out-dir</c> or <c>MORPHIR_OUT_DIR</c>.</param>
/// <param name="WorkspaceRoot">The enclosing workspace root, or the project root when there is no workspace.</param>
public sealed record OutRootRequest(string CurrentDirectory, string WorkspaceRoot)
{
    /// <summary>The <c>--out-dir</c> value, or null when the option was not given.</summary>
    public string? OutDirFlag { get; init; }

    /// <summary>The command's environment. An empty <c>MORPHIR_OUT_DIR</c> is the same as an absent one.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>The workspace configuration's <c>workspace.out_dir</c> as written, or null when it is not set.</summary>
    public string? ConfiguredOutDir { get; init; }
}

/// <summary>
/// The one out root of a workspace. Every task writes its <c>.dest</c>, <c>.json</c> and <c>.lock</c> below it.
/// </summary>
public sealed record OutRoot
{
    /// <summary>The environment variable that moves the out root.</summary>
    public const string EnvironmentVariable = "MORPHIR_OUT_DIR";

    /// <summary>The out root below the workspace root when nothing else is set.</summary>
    public const string DefaultDirectory = ".morphir/out";

    private OutRoot(string path, OutRootSource source, string workspaceRoot)
    {
        Path = path;
        Source = source;
        WorkspaceRoot = workspaceRoot;
    }

    /// <summary>The absolute out root.</summary>
    public string Path { get; }

    /// <summary>Where the out root came from.</summary>
    public OutRootSource Source { get; }

    /// <summary>The absolute workspace root. Record inputs are relative to it.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// Resolves the out root in this order: <c>--out-dir</c> and <c>MORPHIR_OUT_DIR</c> (relative to the current
    /// directory), <c>workspace.out_dir</c> (relative to the workspace root), and <c>.morphir/out</c> below the
    /// workspace root. A <c>workspace.out_dir</c> that is absolute or holds <c>..</c> is refused.
    /// </summary>
    public static Outcome<OutRoot> Resolve(OutRootRequest request)
    {
        try
        {
            var current = System.IO.Path.GetFullPath(request.CurrentDirectory);
            var workspace = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(request.WorkspaceRoot));
            if (request.OutDirFlag is { } flag)
                return Success(System.IO.Path.GetFullPath(flag, current), OutRootSource.Flag, workspace);
            if (request.Environment.TryGetValue(EnvironmentVariable, out var environment) && environment.Length > 0)
                return Success(System.IO.Path.GetFullPath(environment, current), OutRootSource.Environment, workspace);
            if (request.ConfiguredOutDir is { } configured)
            {
                if (!RelativeSegments.TryParse(configured, allowTrailingSlash: true, out var segments, out var error))
                    return TaskFailures.Of<OutRoot>(GenerationFailureKind.Invocation, "out.invalid_out_dir",
                        $"workspace.out_dir must be a relative path inside the workspace. {error}");
                return Success(System.IO.Path.Combine([workspace, .. segments]), OutRootSource.Workspace, workspace);
            }
            return Success(System.IO.Path.Combine(workspace, ".morphir", "out"), OutRootSource.Default, workspace);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return TaskFailures.Of<OutRoot>(GenerationFailureKind.Invocation, "out.invalid_path", "The out root is not a valid filesystem path.");
        }

        static Outcome<OutRoot> Success(string path, OutRootSource source, string workspace) =>
            new Outcome<OutRoot>.Success(new OutRoot(System.IO.Path.TrimEndingDirectorySeparator(path), source, workspace));
    }

    /// <summary>The files of <paramref name="task"/> for the member at <paramref name="member"/>.</summary>
    public TaskLocation Task(MemberPath member, TaskId task) => new(this, member, task);
}

/// <summary>The <c>.dest</c> directory, the record and the lock file of one task of one member.</summary>
public sealed record TaskLocation
{
    internal TaskLocation(OutRoot root, MemberPath member, TaskId task)
    {
        Root = root;
        Member = member;
        Task = task;
        var directory = Path.Combine([root.Path, .. member.Segments, .. task.Segments[..^1]]);
        var leaf = task.Segments[^1];
        Dest = Path.Combine(directory, leaf + ".dest");
        Record = Path.Combine(directory, leaf + ".json");
        Lock = Path.Combine(directory, leaf + ".lock");
    }

    /// <summary>The out root.</summary>
    public OutRoot Root { get; }

    /// <summary>The member.</summary>
    public MemberPath Member { get; }

    /// <summary>The task.</summary>
    public TaskId Task { get; }

    /// <summary>The task's scratch directory, which each run clears.</summary>
    public string Dest { get; }

    /// <summary>The task's result record.</summary>
    public string Record { get; }

    /// <summary>The empty lock file that serializes runs and readers of the task.</summary>
    public string Lock { get; }
}

/// <summary>Builds the failure outcomes of the task record store.</summary>
internal static class TaskFailures
{
    public static Outcome<T> Of<T>(GenerationFailureKind kind, string code, string message, string? path = null) =>
        new Outcome<T>.Failure(GenerationFailure.One(kind, code, message, path));

    public static Outcome<T> Cancelled<T>() =>
        Of<T>(GenerationFailureKind.Cancelled, "cancelled", "The operation was cancelled.");

    public static Outcome<T> Io<T>(string code, string action, string path, Exception error) =>
        Of<T>(GenerationFailureKind.Operational, code, $"Could not {action} {path}: {error.Message}", path);

    public static bool IsIo(Exception error) => error is IOException or UnauthorizedAccessException;
}
