using System.Text.Json;

namespace Morphir.Tooling.Features.VerifyIR;

/// <summary>
/// Detects the Morphir IR version from JSON content by analyzing the formatVersion field.
/// </summary>
public static class VersionDetector
{
    /// <summary>
    /// Detects the IR version from JSON content.
    /// Defaults to version 3 if detection fails or formatVersion is missing.
    /// </summary>
    public static string DetectVersion(string jsonContent)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;

            // Try to get the formatVersion field
            if (root.TryGetProperty("formatVersion", out var formatVersionElement))
            {
                if (formatVersionElement.ValueKind == JsonValueKind.Number)
                {
                    var formatVersion = formatVersionElement.GetInt32();

                    // Validate the version is in supported range
                    if (formatVersion >= 1 && formatVersion <= 3)
                    {
                        return formatVersion.ToString();
                    }
                }
            }

            // Default to v3 if formatVersion is missing or invalid
            return "3";
        }
        catch (JsonException)
        {
            // If JSON parsing fails, default to v3
            return "3";
        }
    }
}
