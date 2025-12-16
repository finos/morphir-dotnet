using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Morphir.IR.Codecs;

namespace Morphir.IR;

public static class MorphirJson
{
    private static Lazy<MorphirJsonOptions> _defaultOptions = new(() => MorphirJsonOptions.Default);

    /// <summary>
    /// Encodes a value to a JSON string using the default options.
    /// </summary>
    /// <remarks>
    /// This method uses reflection-based JSON serialization with custom converters.
    /// The types are preserved through converter registration, making this safe for trimming.
    /// </remarks>
    [RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed. The types are preserved through custom converter registration.")]
    public static string EncodeAsString<T>(T value) =>
        JsonSerializer.Serialize(value, _defaultOptions.Value.JsonSerializerOptions);

    /// <summary>
    /// Encodes a value to a JSON string using the specified options.
    /// </summary>
    /// <remarks>
    /// This method uses reflection-based JSON serialization with custom converters.
    /// The types are preserved through converter registration, making this safe for trimming.
    /// </remarks>
    [RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed. The types are preserved through custom converter registration.")]
    public static string EncodeAsString<T>(T value, MorphirJsonOptions options) =>
        JsonSerializer.Serialize(value, options.JsonSerializerOptions);

    /// <summary>
    /// Decodes a JSON string to a value using the default options.
    /// </summary>
    /// <remarks>
    /// This method uses reflection-based JSON serialization with custom converters.
    /// The types are preserved through converter registration, making this safe for trimming.
    /// </remarks>
    [RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed. The types are preserved through custom converter registration.")]
    public static T? DecodeFromString<T>(string inputJson) =>
        JsonSerializer.Deserialize<T>(inputJson, _defaultOptions.Value.JsonSerializerOptions);

    /// <summary>
    /// Decodes a JSON string to a value using the specified options.
    /// </summary>
    /// <remarks>
    /// This method uses reflection-based JSON serialization with custom converters.
    /// The types are preserved through converter registration, making this safe for trimming.
    /// </remarks>
    [RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed. The types are preserved through custom converter registration.")]
    public static T? DecodeFromString<T>(string inputJson, MorphirJsonOptions options) =>
        JsonSerializer.Deserialize<T>(inputJson, options.JsonSerializerOptions);
}


