using System.Text.Json;
using System.Text.RegularExpressions;

namespace Morphir.Tooling.Features.ModelVerify;

/// <summary>
/// Detects the Morphir IR version from JSON content by analyzing the formatVersion field.
/// </summary>
public static partial class VersionDetector
{
    /// <summary>
    /// Detects the IR version from JSON content: the whole number <c>formatVersion</c> 1 to 4, the release
    /// <c>"3.1.0"</c> as v3, or a v4 release such as <c>"4.0.0"</c> as v4. Defaults to version 3 if detection fails
    /// or formatVersion is missing.
    /// </summary>
    public static string DetectVersion(string jsonContent)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonContent, Infrastructure.JsonSchema.SchemaValidator.ModelDocumentOptions);
            return VersionOf(document.RootElement) ?? "3";
        }
        catch (JsonException)
        {
            // If JSON parsing fails, default to v3
            return "3";
        }
    }

    /// <summary>
    /// The IR version that the <c>formatVersion</c> of a model or of a tree file states, as <see cref="DetectVersion"/>
    /// reads it, or null when it states none of them.
    /// </summary>
    public static string? VersionOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("formatVersion", out var formatVersion))
            return null;

        return formatVersion.ValueKind switch
        {
            JsonValueKind.Number when formatVersion.TryGetInt32(out var number) && number is >= 1 and <= 4 => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String when formatVersion.GetString() == "3.1.0" => "3",
            JsonValueKind.String when V4Release().IsMatch(formatVersion.GetString()!) => "4",
            _ => null,
        };
    }

    /// <summary>
    /// The classic v3 format version that a single file states itself: <c>"3"</c> for the integer 3,
    /// <see cref="Release310"/> for the release string <c>"3.1.0"</c>, or null for any other <c>formatVersion</c>, for
    /// none, and for content that is not JSON. A file with none is read as v3 by <see cref="DetectVersion"/>, but states
    /// no version.
    /// </summary>
    public static string? StatedV3Version(string jsonContent)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonContent, Infrastructure.JsonSchema.SchemaValidator.ModelDocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("formatVersion", out var formatVersion))
                return null;

            return formatVersion.ValueKind switch
            {
                JsonValueKind.Number when formatVersion.TryGetInt32(out var number) && number == 3 => "3",
                JsonValueKind.String when formatVersion.GetString() == Release310 => Release310,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The classic release that a v3 Specs single file and every file of a v3 tree say.</summary>
    public const string Release310 = "3.1.0";

    /// <summary>A release of the v4 family, as the v4 schema spells it: <c>4.&lt;minor&gt;.&lt;patch&gt;</c>.</summary>
    [GeneratedRegex(@"^4\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex V4Release();
}
