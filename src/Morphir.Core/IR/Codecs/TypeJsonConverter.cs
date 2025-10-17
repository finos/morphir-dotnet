using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morphir.IR.Codecs;

public class TypeJsonConverter(MorphirJsonOptions morphirJsonOptions) : JsonConverter<Type>
{
    public override Type? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
