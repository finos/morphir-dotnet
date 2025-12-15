using FluentValidation;

namespace Morphir.Tooling.Features.VerifyIR;

// Command message
public record VerifyIR(
    string FilePath,
    int? SchemaVersion = null,
    bool JsonOutput = false,
    bool Quiet = false
);

// Result type
public record VerifyIRResult(
    bool IsValid,
    string SchemaVersion,
    string DetectionMethod,
    string FilePath,
    List<ValidationError> Errors,
    DateTime Timestamp
);

public record ValidationError(
    string Path,
    string Message,
    object? Expected = null,
    object? Found = null,
    int? Line = null,
    int? Column = null
);

// Handler (pure function)
public static class VerifyIRHandler
{
    public static async Task<VerifyIRResult> Handle(
        VerifyIR command,
        Infrastructure.JsonSchema.SchemaValidator validator,
        CancellationToken ct)
    {
        try
        {
            // 1. Load and parse JSON
            var jsonContent = await File.ReadAllTextAsync(command.FilePath, ct);

            // 2. Detect or use specified version
            var version = command.SchemaVersion?.ToString() ?? VersionDetector.DetectVersion(jsonContent);

            // 3. Validate against schema
            var validationResult = await validator.ValidateAsync(
                jsonContent,
                version,
                ct);

            // 4. Return pure result (no side effects)
            return new VerifyIRResult(
                IsValid: validationResult.IsValid,
                SchemaVersion: version,
                DetectionMethod: command.SchemaVersion.HasValue ? "manual" : "auto",
                FilePath: command.FilePath,
                Errors: validationResult.Errors,
                Timestamp: DateTime.UtcNow
            );
        }
        catch (FileNotFoundException)
        {
            // Handle file not found
            return new VerifyIRResult(
                IsValid: false,
                SchemaVersion: command.SchemaVersion?.ToString() ?? "unknown",
                DetectionMethod: command.SchemaVersion.HasValue ? "manual" : "auto",
                FilePath: command.FilePath,
                Errors: [new ValidationError(
                    Path: "$",
                    Message: $"File does not exist: {command.FilePath}",
                    Expected: "Existing file",
                    Found: "File not found"
                )],
                Timestamp: DateTime.UtcNow
            );
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Handle malformed JSON
            return new VerifyIRResult(
                IsValid: false,
                SchemaVersion: command.SchemaVersion?.ToString() ?? "unknown",
                DetectionMethod: command.SchemaVersion.HasValue ? "manual" : "auto",
                FilePath: command.FilePath,
                Errors: [new ValidationError(
                    Path: "$",
                    Message: $"Malformed JSON: {ex.Message}",
                    Expected: "Valid JSON",
                    Found: "Invalid JSON syntax"
                )],
                Timestamp: DateTime.UtcNow
            );
        }
    }
}

// FluentValidation rules
public class VerifyIRValidator : AbstractValidator<VerifyIR>
{
    public VerifyIRValidator()
    {
        RuleFor(x => x.FilePath)
            .NotEmpty()
            .Must(File.Exists).WithMessage("File does not exist: {PropertyValue}");

        RuleFor(x => x.SchemaVersion)
            .InclusiveBetween(1, 3)
            .When(x => x.SchemaVersion.HasValue)
            .WithMessage("Schema version must be 1, 2, or 3");
    }
}
