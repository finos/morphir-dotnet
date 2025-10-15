using System.Text.Json;
using Morphir.IR.Codecs;

namespace Morphir.IR;

public static class MorphirJson
{
    public static string EncodeAsString<T>(T value) =>
        JsonSerializer.Serialize(value, MorphirJsonOptions.Default.JsonSerializerOptions);
    
    public static string EncodeAsString<T>(T value, MorphirJsonOptions options) =>
        JsonSerializer.Serialize(value, options.JsonSerializerOptions);
}


