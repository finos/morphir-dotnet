using Morphir.IR;

namespace Morphir.StepDefinitions;

/// <summary>
/// Test driver for Document JSON encoding/decoding scenarios.
/// Maintains state for Cucumber step definitions.
/// </summary>
public record DocumentJsonEncodingTestDriver
{
    /// <summary>
    /// The Document instance being tested.
    /// </summary>
    public Document? Document { get; set; }

    /// <summary>
    /// The input JSON string to be deserialized.
    /// </summary>
    public string InputJson { get; set; } = string.Empty;

    /// <summary>
    /// The expected/result JSON string after serialization.
    /// </summary>
    public string ExpectedJson { get; set; } = string.Empty;
}
