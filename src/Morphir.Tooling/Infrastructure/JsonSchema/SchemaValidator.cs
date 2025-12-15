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
        var evaluationResults = schema.Evaluate(jsonDocument.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
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
                        errors.Add(new Features.VerifyIR.ValidationError(
                            Path: detail.InstanceLocation.ToString(),
                            Message: errorValue ?? $"Validation failed for key: {errorKey}",
                            Expected: null, // TODO: Extract from schema
                            Found: null,    // TODO: Extract from instance
                            Line: null,     // TODO: Calculate from JSON
                            Column: null    // TODO: Calculate from JSON
                        ));
                    }
                }
            }
        }

        return errors;
    }
}

public record SchemaValidationResult(
    bool IsValid,
    List<Features.VerifyIR.ValidationError> Errors
);
