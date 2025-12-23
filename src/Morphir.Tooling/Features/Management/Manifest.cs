using System.Text.Json.Serialization;

namespace Morphir.Tooling.Features.Management;

/// <summary>
/// Represents a manifest for a Morphir distribution, tool, or extension.
/// Manifests use JSON with comments (JSONC) format but may have .json extension.
/// </summary>
public sealed record Manifest
{
    /// <summary>
    /// Name of the distribution, tool, or extension.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Version string (e.g., "1.0.0", "2.1.3-beta").
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Platform/Runtime Identifier (e.g., "linux-x64", "win-x64", "osx-arm64").
    /// </summary>
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>
    /// Source URL where this artifact was downloaded from.
    /// </summary>
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }

    /// <summary>
    /// Optional SHA256 hash of the downloaded artifact for integrity verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary>
    /// Optional description of the artifact.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Timestamp when this artifact was installed (ISO 8601 format).
    /// </summary>
    [JsonPropertyName("installedAt")]
    public DateTime? InstalledAt { get; init; }

    /// <summary>
    /// Optional metadata as key-value pairs.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Type of managed artifact.
/// </summary>
public enum ArtifactType
{
    /// <summary>
    /// Core Morphir distribution.
    /// </summary>
    Dist,

    /// <summary>
    /// Auxiliary CLI tool/utility.
    /// </summary>
    Tool,

    /// <summary>
    /// Optional add-on/plugin/extension.
    /// </summary>
    Extension
}
