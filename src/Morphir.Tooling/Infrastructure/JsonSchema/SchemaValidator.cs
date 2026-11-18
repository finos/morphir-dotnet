using Json.Schema;
using System.Text.Json;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaValidator
{
    /// <summary>
    /// The deepest nesting of a model that is read. A Morphir IR tree nests deeply: the LCR example of finos/morphir
    /// has depth 67, above the default limit of 64 of System.Text.Json.
    /// </summary>
    public const int MaxModelDepth = 512;

    /// <summary>The options with which a model is parsed, here and in version detection.</summary>
    public static readonly JsonDocumentOptions ModelDocumentOptions = new() { MaxDepth = MaxModelDepth };

    private readonly SchemaLoader _schemaLoader;

    public SchemaValidator(SchemaLoader schemaLoader)
    {
        _schemaLoader = schemaLoader;
    }

    /// <summary>Validates a single-file model against the schema of the IR format version <paramref name="schemaVersion"/>.</summary>
    public async Task<SchemaValidationResult> ValidateAsync(
        string jsonContent,
        string schemaVersion,
        CancellationToken ct)
    {
        // Load schema (cached)
        var schema = await _schemaLoader.LoadSchemaAsync(schemaVersion, ct);
        return await ValidateAsync(jsonContent, schema, ct);
    }

    /// <summary>Validates one file of a v4 document tree against the schema of its kind.</summary>
    public async Task<SchemaValidationResult> ValidateTreeFileAsync(
        string jsonContent,
        TreeFileKind kind,
        CancellationToken ct)
    {
        var schema = await _schemaLoader.LoadTreeFileSchemaAsync(kind, ct);
        return await ValidateAsync(jsonContent, schema, ct);
    }

    private async Task<SchemaValidationResult> ValidateAsync(
        string jsonContent,
        global::Json.Schema.JsonSchema schema,
        CancellationToken ct)
    {
        // Parse JSON
        using var jsonDocument = JsonDocument.Parse(jsonContent, ModelDocumentOptions);

        // Validate using json-everything
        // Note: Morphir schemas contain recursive type definitions which are valid in JSON Schema
        var evaluationResults = await EvaluateOnLargeStack(schema, jsonDocument.RootElement, ct);

        // Convert to our error format
        var errors = ConvertToValidationErrors(evaluationResults);

        return new SchemaValidationResult(
            IsValid: evaluationResults.IsValid,
            Errors: errors
        );
    }

    private static Task<EvaluationResults> EvaluateOnLargeStack(
        global::Json.Schema.JsonSchema schema, JsonElement model, CancellationToken ct) =>
        OnLargeStack(() => schema.Evaluate(model, new EvaluationOptions
        {
            // The tree of results, so an error below a subschema that holds (a branch of an anyOf or a
            // oneOf that another branch satisfies) is told from an error of the model.
            OutputFormat = OutputFormat.Hierarchical,
            RequireFormatValidation = false  // Don't require format validation
        }), "Morphir IR schema evaluation", ct);

    /// <summary>
    /// Decodes a single-file IR v4 model with <c>V4Json.readDistribution</c>, the reader of every v4 spelling, and
    /// gives its error with the JSON location as a JSON Pointer and the reader's rule as the code; or none. The
    /// parent's <c>morphir-ir-v4.json</c> checks the types and values of a module in every spelling, but it tries the
    /// spellings in turn, so it locates a fault in a module only at the module; the decode names the node itself, and
    /// checks every rule of the reader (see <c>Schemas/PROVENANCE.md</c>). It runs on the stack of schema evaluation,
    /// as decoding recurses for each level of the model.
    /// </summary>
    public static async Task<List<Features.ModelVerify.ValidationError>> DecodeV4Async(string jsonContent, CancellationToken ct)
    {
        using var jsonDocument = JsonDocument.Parse(jsonContent, ModelDocumentOptions);
        var root = jsonDocument.RootElement;
        var read = await OnLargeStack(() => Morphir.Json.V4.V4Json.readDistribution(root), "Morphir IR v4 decoding", ct);
        if (read.IsOk) return [];

        var error = read.ErrorValue;
        return [new Features.ModelVerify.ValidationError(Features.ModelVerify.StoreLocation.Pointer(error.Location), error.Message)
        {
            Code = error.Rule,
        }];
    }

    /// <summary>
    /// Decodes a single-file classic v3 model with <c>ClassicJson.readDistributionStrict</c>, the reader that
    /// <c>model convert</c> uses, and gives its error with the JSON location as a JSON Pointer and the reader's rule
    /// as the code; or none. It names a member that the classic model has no place for (<c>unknown_member</c>),
    /// which the v3 schemas leave open, so a model that a conversion would lose content of is not valid. It runs on
    /// the stack of schema evaluation, as <see cref="DecodeV4Async"/> does.
    /// </summary>
    public static async Task<List<Features.ModelVerify.ValidationError>> DecodeClassicAsync(string jsonContent, CancellationToken ct)
    {
        using var jsonDocument = JsonDocument.Parse(jsonContent, ModelDocumentOptions);
        var root = jsonDocument.RootElement;
        var read = await OnLargeStack(() => Morphir.Json.Classic.ClassicJson.readDistributionStrict(root), "Morphir IR v3 decoding", ct);
        if (read.IsOk) return [];

        var error = read.ErrorValue;
        return [new Features.ModelVerify.ValidationError(Features.ModelVerify.StoreLocation.Pointer(error.Location), error.Message)
        {
            Code = error.Rule,
        }];
    }

    /// <summary>Runs <paramref name="work"/> on a thread with the stack of <see cref="LargeStack.Size"/>.</summary>
    private static Task<T> OnLargeStack<T>(Func<T> work, string name, CancellationToken ct) => LargeStack.RunAsync(work, name, ct);

    /// <summary>
    /// The errors of the evaluation: each error of a subschema that fails, read from the root down through the
    /// subschemas that fail only. A subschema that holds is not entered, so the errors of a branch of an
    /// <c>anyOf</c> or a <c>oneOf</c> whose node another branch accepts are not errors of the model. An error that
    /// two subschemas give at one location with the same message is listed once.
    /// </summary>
    private List<Features.ModelVerify.ValidationError> ConvertToValidationErrors(EvaluationResults results)
    {
        var errors = new List<Features.ModelVerify.ValidationError>();
        var seen = new HashSet<(string, string)>();
        var pending = new Stack<EvaluationResults>();
        pending.Push(results);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.IsValid) continue;

            foreach (var (errorKey, errorValue) in node.Errors ?? new Dictionary<string, string>())
            {
                var path = node.InstanceLocation.ToString();
                var message = errorValue ?? $"Validation failed for key: {errorKey}";
                if (!seen.Add((path, message))) continue;

                errors.Add(new Features.ModelVerify.ValidationError(
                    Path: path,
                    Message: message,
                    Expected: ExtractExpectedValue(errorKey, errorValue),
                    Found: ExtractFoundValue(errorKey, errorValue),
                    Line: null,     // Calculating line numbers requires tracking position during parse
                    Column: null    // Calculating columns requires tracking position during parse
                ));
            }

            // In document order: the stack takes the last detail first.
            var details = node.Details ?? [];
            for (var i = details.Count - 1; i >= 0; i--) pending.Push(details[i]);
        }

        return errors;
    }

    private object? ExtractExpectedValue(string errorKey, string? errorMessage)
    {
        try
        {
            // For type errors, extract expected type from message
            if (errorKey == "type" && errorMessage != null)
            {
                // Message format: 'Value is "string" but should be "integer"'
                var shouldBeIndex = errorMessage.IndexOf("should be");
                if (shouldBeIndex > 0)
                {
                    var typeStart = errorMessage.IndexOf('"', shouldBeIndex);
                    var typeEnd = errorMessage.IndexOf('"', typeStart + 1);
                    if (typeStart > 0 && typeEnd > typeStart)
                    {
                        return errorMessage.Substring(typeStart + 1, typeEnd - typeStart - 1);
                    }
                }
            }

            // For required fields
            if (errorKey == "required" && errorMessage != null)
            {
                // Message format: 'Required properties ["formatVersion"] are not present'
                return "required property";
            }

            // For const/enum violations
            if ((errorKey == "const" || errorKey == "enum") && errorMessage != null)
            {
                // Try to extract expected value from message
                var expectedIndex = errorMessage.IndexOf("Expected");
                if (expectedIndex >= 0)
                {
                    return errorMessage.Substring(expectedIndex);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private object? ExtractFoundValue(string errorKey, string? errorMessage)
    {
        try
        {
            // For type errors, extract found type from message
            if (errorKey == "type" && errorMessage != null)
            {
                // Message format: 'Value is "string" but should be "integer"'
                var isIndex = errorMessage.IndexOf("Value is");
                if (isIndex >= 0)
                {
                    var typeStart = errorMessage.IndexOf('"', isIndex);
                    var typeEnd = errorMessage.IndexOf('"', typeStart + 1);
                    if (typeStart > 0 && typeEnd > typeStart)
                    {
                        return errorMessage.Substring(typeStart + 1, typeEnd - typeStart - 1);
                    }
                }
            }

            // For required fields
            if (errorKey == "required" && errorMessage != null)
            {
                return "undefined (missing)";
            }

            // For other errors, try to provide some context
            if (errorMessage != null && errorMessage.Length > 0)
            {
                return "see message for details";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

public record SchemaValidationResult(
    bool IsValid,
    List<Features.ModelVerify.ValidationError> Errors
);
