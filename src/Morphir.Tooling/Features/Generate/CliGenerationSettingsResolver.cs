using System.Text.Json;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Resolves explicit invocation values. No workspace discovery or configuration I/O.</summary>
public sealed class CliGenerationSettingsResolver : IGenerationSettingsResolver
{
    public ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides overrides, InvocationContext context, CancellationToken ct)
    {
        Outcome<GenerationSettings> Fail(string code, string message) =>
            new Outcome<GenerationSettings>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation, code, message));

        if (ct.IsCancellationRequested)
            return ValueTask.FromResult<Outcome<GenerationSettings>>(new Outcome<GenerationSettings>.Failure(
                GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.")));

        foreach (var (name, value) in new[] { ("input", overrides.Input), ("target", overrides.Target), ("output", overrides.Output) })
            if (string.IsNullOrWhiteSpace(value))
                return ValueTask.FromResult(Fail("missing-" + name, $"Specify --{name}."));

        var format = overrides.InputFormat switch
        {
            null or "auto" => InputFormat.Auto,
            "json" => InputFormat.Json,
            "ion" => InputFormat.Ion,
            _ => (InputFormat?)null
        };
        if (format is null)
            return ValueTask.FromResult(Fail("invalid-input-format", "Input format must be auto, json or ion."));

        var options = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var option in overrides.Options ?? [])
        {
            var separator = option.IndexOf('=');
            if (separator < 1 || string.IsNullOrWhiteSpace(option[..separator]))
                return ValueTask.FromResult(Fail("invalid-option", "Options require a nonempty KEY followed by =VALUE."));
            var key = option[..separator];
            var value = option[(separator + 1)..];
            try
            {
                using var document = JsonDocument.Parse(value);
                options[key] = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                using var document = JsonDocument.Parse("\"" + JsonEncodedText.Encode(value) + "\"");
                options[key] = document.RootElement.Clone();
            }
        }

        try
        {
            var input = Path.GetFullPath(overrides.Input!, context.WorkingDirectory);
            var output = Path.GetFullPath(overrides.Output!, context.WorkingDirectory);
            return ValueTask.FromResult<Outcome<GenerationSettings>>(new Outcome<GenerationSettings>.Success(
                new(new(input, format.Value), new(overrides.Target!), new(options), new(output), ModelReadLimits.Default)));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValueTask.FromResult(Fail("invalid-path", "Input, output and invocation directory must be valid filesystem paths."));
        }
    }
}
