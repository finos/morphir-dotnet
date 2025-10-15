using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.IR;
using Morphir.IR.Codecs;

namespace Morphir.Classic.IR.Codecs;

public class ClassicTypeJsonConverterFactory(MorphirJsonOptions morphirJsonOptions): JsonConverterFactory
{
    public ClassicTypeJsonConverterFactory():this(MorphirJsonOptions.Default) { }
    
    public override bool CanConvert(System.Type typeToConvert)
    {
        if (typeToConvert.IsGenericType)
        {
            var genericDef = typeToConvert.GetGenericTypeDefinition();
            if (genericDef.DeclaringType == typeof(Type<>))
            {
                return true;
            }
        }
        return false;
    }

    public override JsonConverter? CreateConverter(System.Type typeToConvert, JsonSerializerOptions options)
    {
        // Expecting Classic.IR.Type<TAttributes>
        var attrs = typeToConvert.GetGenericArguments();
        if (attrs.Length != 1)
        {
            throw new NotSupportedException($"Unsupported type for ClassicTypeJsonConverterFactory: {typeToConvert}");
        }

        var attributeType = attrs[0];
        var converterType = typeof(ClassicTypeJsonConverter<>).MakeGenericType(attributeType);
        return (JsonConverter?)Activator.CreateInstance(converterType, morphirJsonOptions);
    }
    
    public class ClassicTypeJsonConverter<TAttributes>(MorphirJsonOptions morphirJsonOptions):JsonConverter<Type<TAttributes>>
    {
        public ClassicTypeJsonConverter():this(MorphirJsonOptions.Default) { }

        private JsonConverter<Name> NameConverter { get; } =
            (JsonConverter<Name>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(Name));
        
        private JsonConverter<TAttributes> AttributeConverter { get; } =
            (JsonConverter<TAttributes>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(TAttributes));

        public override bool CanConvert(System.Type typeToConvert)
        {
            return (typeToConvert == typeof(Type<TAttributes>)) 
                   || (typeToConvert == typeof(Type<TAttributes>.Variable))
                   || (typeToConvert == typeof(Type<TAttributes>.Unit));
        }

        public override Type<TAttributes>? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }

        public override void Write(Utf8JsonWriter writer, Type<TAttributes> value, JsonSerializerOptions options)
        {
            if (value is Type<TAttributes>.Variable variable)
            {
                WriteVariable(writer, variable, options);
            }
            else if (value is Type<TAttributes>.Unit unit)
            {
                WriteUnit(writer, unit, options);
            } else
            {
                throw new NotSupportedException($"Encoding not supported for Classic Type case: {value?.GetType().Name}");
            }
        }

        private void WriteVariable(Utf8JsonWriter writer, Type<TAttributes>.Variable variable,
            JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("Variable");
            // Attributes
            AttributeConverter.Write(writer, variable.Attributes, options);
            // Name
            NameConverter.Write(writer, variable.Name, options);
            writer.WriteEndArray();
        }

        private void WriteUnit(Utf8JsonWriter writer, Type<TAttributes>.Unit unit, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("Unit");
            AttributeConverter.Write(writer, unit.Attributes, options);
            writer.WriteEndArray();
        }
    
    }
}
