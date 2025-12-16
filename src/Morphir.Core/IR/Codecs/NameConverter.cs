using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Seq = LanguageExt.Seq;

namespace Morphir.IR.Codecs;

public class NameConverter(MorphirJsonOptions morphirJsonOptions) : JsonConverter<Name>
{
    public NameConverter() : this(MorphirJsonOptions.Default) { }

    public override Name? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var builder = ImmutableList.CreateBuilder<string>();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                var item = reader.GetString();
                if (item != null) builder.Add(item);
            }

            if (reader.TokenType == JsonTokenType.EndArray)
                return Name.FromList(builder.ToImmutable());
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, Name value, JsonSerializerOptions options)
    {
        if (morphirJsonOptions.FormatVersion.IsClassic)
        {
            writer.WriteStartArray();
            foreach (var segment in value.Segments)
            {
                writer.WriteStringValue(segment);
            }

            writer.WriteEndArray();
        }
        else
        {
            writer.WriteStringValue(value.ToKebabCase());
        }
    }
}
