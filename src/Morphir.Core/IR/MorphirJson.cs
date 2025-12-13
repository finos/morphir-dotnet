using System.Text.Json;
using Morphir.IR.Codecs;

namespace Morphir.IR;

public static class MorphirJson
{
    private static Lazy<MorphirJsonOptions> _defaultOptions = new(() => MorphirJsonOptions.Default);

    public static string EncodeAsString<T>(T value) =>
        JsonSerializer.Serialize(value, _defaultOptions.Value.JsonSerializerOptions);

    public static string EncodeAsString<T>(T value, MorphirJsonOptions options) =>
        JsonSerializer.Serialize(value, options.JsonSerializerOptions);

    public static T? DecodeFromString<T>(string inputJson) =>
        JsonSerializer.Deserialize<T>(inputJson, _defaultOptions.Value.JsonSerializerOptions);

    public static T? DecodeFromString<T>(string inputJson, MorphirJsonOptions options) =>
        JsonSerializer.Deserialize<T>(inputJson, options.JsonSerializerOptions);
}


