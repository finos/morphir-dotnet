using Json.Schema;
using System.Text.Json;

namespace Morphir.Tooling.Infrastructure.JsonSchema;

public class SchemaValidator
{
    private readonly SchemaLoader _schemaLoader;

    public SchemaValidator(SchemaLoader schemaLoader)
    {
        _schemaLoader = schemaLoader;
    }

    public async Task<SchemaValidationResult> ValidateAsync(
        string jsonContent,
        string schemaVersion,
        CancellationToken ct)
    {
        // Load schema (cached)
        var schema = await _schemaLoader.LoadSchemaAsync(schemaVersion, ct);

        // Parse JSON
        var jsonDocument = JsonDocument.Parse(jsonContent);

        // Validate using json-everything
        // Note: Morphir schemas contain recursive type definitions which are valid in JSON Schema
        var evaluationResults = schema.Evaluate(jsonDocument.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = false  // Don't require format validation
        });

        // Convert to our error format
        var errors = ConvertToValidationErrors(evaluationResults);

        return new SchemaValidationResult(
            IsValid: evaluationResults.IsValid,
            Errors: errors
        );
    }

    private List<Features.VerifyIR.ValidationError> ConvertToValidationErrors(EvaluationResults results)
    {
        var errors = new List<Features.VerifyIR.ValidationError>();

        if (!results.IsValid)
        {
            // Traverse the results to collect all errors
            foreach (var detail in results.Details ?? [])
            {
                if (!detail.IsValid && detail.Errors != null)
                {
                    foreach (var (errorKey, errorValue) in detail.Errors)
                    {
                        // Extract expected and found values from the validation context
                        var expectedValue = ExtractExpectedValue(errorKey, errorValue);
                        var foundValue = ExtractFoundValue(errorKey, errorValue);

                        errors.Add(new Features.VerifyIR.ValidationError(
                            Path: detail.InstanceLocation.ToString(),
                            Message: errorValue ?? $"Validation failed for key: {errorKey}",
                            Expected: expectedValue,
                            Found: foundValue,
                            Line: null,     // Calculating line numbers requires tracking position during parse
                            Column: null    // Calculating columns requires tracking position during parse
                        ));
                    }
                }
            }
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
    List<Features.VerifyIR.ValidationError> Errors
);
