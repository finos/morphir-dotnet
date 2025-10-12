using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Generator.Equals;
using Morphir.Internals;
using Morphir.IR;

namespace Morphir.Classic.IR;

[Equatable]
[JsonConverter(typeof(ClassicNameConverter))]
public partial record ClassicName([property: OrderedEquality] IImmutableList<string> Segments): IName
{
    public IImmutableList<string> ToList() => Segments;
    
    public static ClassicName Create(params string[] segments) => new ([..segments]);
    public static ClassicName FromList(IReadOnlyList<string> segments) => new ([..segments]);

    public override string ToString() => Segments.MakeString("[", ", ", "]");
}

internal class ClassicNameConverter : JsonConverter<ClassicName>
{
    public override ClassicName? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!reader.Read()) return null;
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var builder = ImmutableList.CreateBuilder<string>();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                var item = reader.GetString();
                if(item != null) builder.Add(item);
            }

            if(reader.TokenType == JsonTokenType.EndArray) 
                return new ClassicName(builder.ToImmutable());
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, ClassicName value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var segment in value.Segments)
        {
            writer.WriteStringValue(segment);
        }
        writer.WriteEndArray();
    }
}
