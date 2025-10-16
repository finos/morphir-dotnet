using System.Text.Json;
using Morphir.IR.Codecs;

namespace Morphir.IR;

public static class MorphirJson
{
    
    public static string EncodeAsString<T>(T value) =>
        JsonSerializer.Serialize(value, MorphirJsonOptions.Default.JsonSerializerOptions);
    
    public static string EncodeAsString<T>(T value, MorphirJsonOptions options) =>
        JsonSerializer.Serialize(value, options.JsonSerializerOptions);

    public static T? DecodeFromString<T>(string inputJson) =>
        JsonSerializer.Deserialize<T>(inputJson, MorphirJsonOptions.Default.JsonSerializerOptions);
    
    public static T? DecodeFromString<T>(string inputJson, MorphirJsonOptions options) =>
        JsonSerializer.Deserialize<T>(inputJson, options.JsonSerializerOptions);
}


