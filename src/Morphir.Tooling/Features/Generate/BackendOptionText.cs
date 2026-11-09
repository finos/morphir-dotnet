using System.Text.Json;

namespace Morphir.Tooling.Features.Generate;

/// <summary>
/// Parses <c>--option KEY=VALUE</c> texts into backend options. <c>generate</c> and <c>compile --target</c> use
/// this one parser, so both commands give a backend the same options.
/// </summary>
public static class BackendOptionText
{
    /// <summary>
    /// Parses each text as a nonempty key, <c>=</c> and a value. A value that is a JSON document is kept as JSON;
    /// any other value is a JSON string. A later key replaces an earlier one.
    /// </summary>
    public static Outcome<BackendOptions> Parse(IEnumerable<string>? options)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var option in options ?? [])
        {
            var separator = option.IndexOf('=');
            if (separator < 1 || string.IsNullOrWhiteSpace(option[..separator]))
                return new Outcome<BackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "invalid-option", "Options require a nonempty KEY followed by =VALUE."));
            var key = option[..separator];
            var value = option[(separator + 1)..];
            try
            {
                using var document = JsonDocument.Parse(value);
                values[key] = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                using var document = JsonDocument.Parse("\"" + JsonEncodedText.Encode(value) + "\"");
                values[key] = document.RootElement.Clone();
            }
        }
        return new Outcome<BackendOptions>.Success(new BackendOptions(values));
    }
}
