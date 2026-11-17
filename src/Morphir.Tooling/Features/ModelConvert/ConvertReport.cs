// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Morphir.IR.Trees;

namespace Morphir.Tooling.Features.ModelConvert;

/// <summary>
/// One diagnostic or notice of <c>model convert</c>: a stable dotted code, <c>error</c> or <c>info</c>, a message and
/// the file it concerns (the path as the command line gave it, joined with the path inside a tree), if any.
/// </summary>
public sealed record ConvertDiagnostic(
    string Code,
    string Severity,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Location)
{
    /// <summary>The severity of a diagnostic that stops the conversion.</summary>
    public const string Error = "error";

    /// <summary>The severity of a notice, which does not.</summary>
    public const string Info = "info";

    /// <summary>A diagnostic that stops the conversion.</summary>
    public static ConvertDiagnostic Failure(string code, string message, string? location) => new(code, Error, message, location);

    /// <summary>The diagnostic of <paramref name="problem"/> of the model at <paramref name="model"/>, as given.</summary>
    public static ConvertDiagnostic Of(ConversionProblem problem, string model) =>
        Failure(problem.Code, problem.Message, ModelConvertPaths.Join(model, problem.File));

    /// <summary>The notice of <paramref name="notice"/> of the model at <paramref name="model"/>, as given.</summary>
    public static ConvertDiagnostic Of(ConversionNotice notice, string model) =>
        new(notice.Code, Info, notice.Message, ModelConvertPaths.Join(model, notice.File));

    /// <summary>The diagnostic as one stderr line: <c>location: severity code: message</c>.</summary>
    public override string ToString() => $"{(Location is null ? "" : Location + ": ")}{Severity} {Code}: {Message}";
}

/// <summary>
/// The report of <c>morphir model convert</c> (cli-model-conversion): the one document of <c>--json=document</c>, and
/// the <c>report</c> of the last record of <c>--json</c>. Members are camelCase. <c>success</c> is true exactly when
/// <c>exitCode</c> is 0. Paths are as the command line gave them; a file of a tree is the output joined with its path
/// in the tree, with <c>/</c>. <c>irVersion</c> is the text <c>"3"</c> or <c>"4"</c>. The compaction members are
/// left out unless the run asked for compaction.
/// </summary>
public sealed record ConvertReport(
    bool Success,
    int ExitCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Input,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Output,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InputLayout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OutputLayout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InputEncoding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OutputEncoding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IrVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompactMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompactPolicy,
    string[] FilesWritten,
    ConvertDiagnostic[] Diagnostics,
    ConvertDiagnostic[] Notices)
{
    /// <summary>The report of arguments that do not parse: exit code 2, with one diagnostic per message.</summary>
    public static ConvertReport Usage(IEnumerable<string> messages) =>
        new(false, ModelConvertCodes.UsageExitCode, null, null, null, null, null, null, null, null, null, [],
            [.. messages.Select(message => ConvertDiagnostic.Failure(ModelConvertCodes.InvalidArguments, message, null))], []);

    /// <summary>The indented JSON of <c>--json=document</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonInfo);

    /// <summary>The source-generated contract of the report, for the shared <see cref="Cli.JsonOutputWriter{TReport}"/>.</summary>
    public static JsonTypeInfo<ConvertReport> JsonInfo => ConvertJsonContext.Relaxed.ConvertReport;
}

/// <summary>The stable codes and exit codes of <c>model convert</c> that are not a store's or a codec's.</summary>
public static class ModelConvertCodes
{
    /// <summary>The exit code of a usage error: arguments that do not parse, or a run with nothing to do.</summary>
    public const int UsageExitCode = 2;

    /// <summary>The exit code of a refusal.</summary>
    public const int RefusedExitCode = 1;

    /// <summary>The exit code of a cancelled run.</summary>
    public const int CancelledExitCode = 130;

    /// <summary>Arguments that do not parse (exit code 2).</summary>
    public const string InvalidArguments = "model.invalid_arguments";

    /// <summary>The run changes neither the layout nor the encoding (exit code 2).</summary>
    public const string NothingToDo = "model.nothing_to_do";

    /// <summary><c>--input</c> names nothing.</summary>
    public const string InputNotFound = "model.input_not_found";

    /// <summary><c>--output</c> already holds a file, or is a file where a directory is needed, or the reverse.</summary>
    public const string OutputNotEmpty = "model.output_not_empty";

    /// <summary><c>--output</c> is the input, is inside it, or holds it.</summary>
    public const string OutputOverlapsInput = "model.output_overlaps_input";

    /// <summary>The input is compacted, which this version does not read yet.</summary>
    public const string UnsupportedContext = "model.unsupported_context";

    /// <summary>A file of the output could not be written.</summary>
    public const string WriteFailed = "model.write_failed";

    /// <summary>A file of the input could not be read.</summary>
    public const string ReadFailed = "model.read_failed";

    /// <summary>The run was cancelled (exit code 130).</summary>
    public const string Cancelled = "model.cancelled";
}

/// <summary>The wire names of the layouts and encodings in the report and in <c>--layout</c>.</summary>
public static class ModelConvertNames
{
    /// <summary>The layout of one file.</summary>
    public const string SingleFile = "single-file";

    /// <summary>The layout of a document tree.</summary>
    public const string DocumentTree = "document-tree";

    /// <summary>The encoding of a file without a compaction envelope.</summary>
    public const string Plain = "plain";

    /// <summary>The encoding of a file with a compaction envelope.</summary>
    public const string Compacted = "compacted";

    /// <summary>The layouts that <c>--layout</c> accepts.</summary>
    public static IReadOnlyList<string> Layouts { get; } = [DocumentTree, SingleFile];

    /// <summary>The wire name of <paramref name="layout"/>.</summary>
    public static string Of(ModelLayout layout) => layout == ModelLayout.DocumentTree ? DocumentTree : SingleFile;

    /// <summary>The layout of a wire name that <c>--layout</c> accepted.</summary>
    public static ModelLayout LayoutOf(string name) => name == DocumentTree ? ModelLayout.DocumentTree : ModelLayout.SingleFile;
}

/// <summary>Paths in the report: as the command line gave them, with a tree's files joined with <c>/</c>.</summary>
public static class ModelConvertPaths
{
    /// <summary><paramref name="given"/>, or <paramref name="given"/> joined with the tree path <paramref name="file"/>.</summary>
    public static string Join(string given, string? file) =>
        file is null ? given : given.TrimEnd('/', '\\') + "/" + file.Replace('\\', '/');
}
