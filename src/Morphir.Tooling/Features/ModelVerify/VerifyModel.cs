using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentValidation;
using Morphir.IR.Files;

namespace Morphir.Tooling.Features.ModelVerify;

/// <summary>
/// The <c>model verify</c> command: verify the model at <paramref name="FilePath"/>, a single file or the directory of a
/// document tree, against the schema of its IR version, or of <paramref name="SchemaVersion"/> when it is given.
/// </summary>
public record VerifyModel(
    string FilePath,
    int? SchemaVersion = null,
    bool JsonOutput = false,
    bool Quiet = false
);

/// <summary>The layouts of a model, as the report names them.</summary>
public static class ModelLayouts
{
    /// <summary>One JSON file.</summary>
    public const string SingleFile = "single-file";

    /// <summary>A directory of files: a manifest, and a file for each module, type and value.</summary>
    public const string DocumentTree = "document-tree";
}

/// <summary>
/// The report of <c>model verify</c>. The members of the first release keep their names (<c>IsValid</c> is read by the
/// release check of finos/morphir); the later members are camelCase.
/// </summary>
public record VerifyModelResult(
    bool IsValid,
    string SchemaVersion,
    string DetectionMethod,
    string FilePath,
    List<ValidationError> Errors,
    DateTime Timestamp
)
{
    /// <summary>
    /// The exit code of the run that wrote the report: 0 for a valid model, 1 for one that is not valid, 2 for
    /// arguments that do not parse or a <c>--schema-version</c> that contradicts the manifest of a tree. The report
    /// states it, so a usage failure is told from a model that is not valid. The member <c>IsValid</c> keeps its name:
    /// the release check of finos/morphir reads it.
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; } = IsValid ? 0 : 1;

    /// <summary>
    /// How the model is stored: <see cref="ModelLayouts.SingleFile"/> or <see cref="ModelLayouts.DocumentTree"/>.
    /// Absent from the report of a command line that does not parse, which read no model.
    /// </summary>
    [JsonPropertyName("layout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Layout { get; init; }

    /// <summary>
    /// The number of files that were checked: 1 for a single file that was read, each tree file (JSON or YAML) of a
    /// document tree, and 0 when no file was read.
    /// </summary>
    [JsonPropertyName("filesChecked")]
    public int FilesChecked { get; init; }

    /// <summary>
    /// The files that were compacted, each in an envelope of a declared schema: as paths from the root of a tree, or the
    /// path of a single file as <see cref="VerifyModelResult.FilePath"/> gives it. A plain model has none.
    /// </summary>
    [JsonPropertyName("compactedFiles")]
    public List<string> CompactedFiles { get; init; } = [];

    /// <summary>The source-generated contract of the result, for the shared JSON output writer.</summary>
    public static JsonTypeInfo<VerifyModelResult> JsonInfo => VerifyModelJsonContext.Default.VerifyModelResult;
}

/// <summary>
/// One error of a verification: where it is (<c>Path</c>: the JSON location in its file as a JSON Pointer, or <c>$</c>
/// for an input that was not read), what is wrong, and what the schema expected and found. In a document tree the
/// error names its <c>file</c>, as a path from the root of the tree, and a diagnostic of the tree's rules has its
/// stable <c>code</c>.
/// </summary>
public record ValidationError(
    string Path,
    string Message,
    object? Expected = null,
    object? Found = null,
    int? Line = null,
    int? Column = null
)
{
    /// <summary>The file of a document tree that the error is in, or null for a single file and for the whole tree.</summary>
    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? File { get; init; }

    /// <summary>The stable code of a diagnostic, such as <c>missing_member</c>, or null for a schema error.</summary>
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }
}

// Source-generated serialization: the trimmed executable disables reflection-based JSON.
// Expected and Found are typed object; string, their only runtime type, is registered too.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VerifyModelResult))]
[JsonSerializable(typeof(string))]
internal partial class VerifyModelJsonContext : JsonSerializerContext;

/// <summary>
/// Verifies a model: a directory is a document tree (<see cref="TreeVerification"/>), and any other path a single
/// file, checked against the schema of its IR version. A v4 single file is also decoded with
/// <c>V4Json.readDistribution</c>, as a tree's documents are, and the decoder's error is an error of the report. A
/// compacted single file is expanded first (<see cref="CompactedFiles"/>), and its plain document is checked; a
/// refused envelope or context is an error with its <c>context.</c> code.
/// </summary>
public static class VerifyModelHandler
{
    public static async Task<VerifyModelResult> Handle(
        VerifyModel command,
        Infrastructure.JsonSchema.SchemaValidator validator,
        CancellationToken ct)
    {
        if (Directory.Exists(command.FilePath))
            return await TreeVerification.VerifyDirectoryAsync(command.FilePath, command.SchemaVersion, validator, ct);

        try
        {
            // 1. Load the JSON; a compacted file is checked and expanded, and the rest checks its plain document.
            var jsonContent = await File.ReadAllTextAsync(command.FilePath, ct);
            List<string> compactedFiles = [];
            if (CompactedFiles.TopLevelSchema(jsonContent) is { } schema)
            {
                var expanded = CompactedFiles.Expand(command.FilePath, jsonContent);
                if (MorphirFile.IsDeclared(schema)) compactedFiles.Add(command.FilePath);
                if (expanded.Json is not { } plain)
                {
                    // A single file has no file of a tree: the report's FilePath names it.
                    var refusals = expanded.Errors.Select(error => TreeVerification.ErrorOf(error) with { File = null });
                    return NotValid(command, filesChecked: 1, [.. refusals]) with { CompactedFiles = compactedFiles };
                }
                jsonContent = plain;
            }

            // 2. Detect or use specified version
            var version = GivenVersion(command) ?? VersionDetector.DetectVersion(jsonContent);

            // 3. Validate against schema
            var validationResult = await validator.ValidateAsync(
                jsonContent,
                version,
                ct);

            // 4. A v4 model is also decoded, which checks what the parent's v4 schema leaves open.
            var errors = validationResult.Errors;
            if (version == "4")
                errors = [.. errors, .. await Infrastructure.JsonSchema.SchemaValidator.DecodeV4Async(jsonContent, ct)];

            // 5. Return pure result (no side effects)
            return new VerifyModelResult(
                IsValid: errors.Count == 0,
                SchemaVersion: version,
                DetectionMethod: DetectionMethod(command),
                FilePath: command.FilePath,
                Errors: errors,
                Timestamp: DateTime.UtcNow
            )
            {
                Layout = ModelLayouts.SingleFile,
                FilesChecked = 1,
                CompactedFiles = compactedFiles,
            };
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return NotValid(command, filesChecked: 0, new ValidationError(
                Path: "$",
                Message: $"File does not exist: {command.FilePath}",
                Expected: "Existing file",
                Found: "File not found"
            ));
        }
        catch (System.Text.Json.JsonException ex)
        {
            return NotValid(command, filesChecked: 1, new ValidationError(
                Path: "$",
                Message: $"Malformed JSON: {ex.Message}",
                Expected: "Valid JSON",
                Found: "Invalid JSON syntax"
            ));
        }
    }

    private static string? GivenVersion(VerifyModel command) =>
        command.SchemaVersion?.ToString(CultureInfo.InvariantCulture);

    private static string DetectionMethod(VerifyModel command) => command.SchemaVersion.HasValue ? "manual" : "auto";

    /// <summary>The result of a single file that is not there, is not JSON, or whose envelope or context is refused.</summary>
    private static VerifyModelResult NotValid(VerifyModel command, int filesChecked, params List<ValidationError> errors) =>
        new(
            IsValid: false,
            SchemaVersion: GivenVersion(command) ?? "unknown",
            DetectionMethod: DetectionMethod(command),
            FilePath: command.FilePath,
            Errors: errors,
            Timestamp: DateTime.UtcNow)
        {
            Layout = ModelLayouts.SingleFile,
            FilesChecked = filesChecked,
        };
}

// FluentValidation rules
public class VerifyModelValidator : AbstractValidator<VerifyModel>
{
    public VerifyModelValidator()
    {
        RuleFor(x => x.FilePath)
            .NotEmpty()
            .Must(path => File.Exists(path) || Directory.Exists(path)).WithMessage("File does not exist: {PropertyValue}");

        RuleFor(x => x.SchemaVersion)
            .InclusiveBetween(1, 4)
            .When(x => x.SchemaVersion.HasValue)
            .WithMessage("Schema version must be 1, 2, 3 or 4");
    }
}
