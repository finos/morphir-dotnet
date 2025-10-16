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
            if ((genericDef == typeof(Type<>)) || genericDef.DeclaringType == typeof(Type<>))
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

    private class ClassicTypeJsonConverter<TAttributes>(MorphirJsonOptions morphirJsonOptions):JsonConverter<Type<TAttributes>>
    {
        public ClassicTypeJsonConverter():this(MorphirJsonOptions.Default) { }

        private JsonConverter<Name> NameConverter { get; } =
            (JsonConverter<Name>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(Name));
        
        private JsonConverter<TAttributes> AttributeConverter { get; } =
            (JsonConverter<TAttributes>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(TAttributes));

        // Converter for nested Type<TAttributes> sequences (used by Tuple and others)
        private JsonConverter<System.Collections.Generic.IEnumerable<Type<TAttributes>>> TypeSeqConverter { get; } =
            (JsonConverter<System.Collections.Generic.IEnumerable<Type<TAttributes>>>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(System.Collections.Generic.IEnumerable<Type<TAttributes>>));

        public override bool CanConvert(System.Type typeToConvert)
        {
            return (typeToConvert == typeof(Type<TAttributes>)) 
                   || (typeToConvert == typeof(Type<TAttributes>.Variable))
                   || (typeToConvert == typeof(Type<TAttributes>.Unit))
                   || (typeToConvert == typeof(Type<TAttributes>.Tuple));
        }

        public override Type<TAttributes>? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if(reader.TokenType != JsonTokenType.StartArray) throw new NotSupportedException($"Unexpected token when parsing Classic Type, we expected an array but got: {reader.TokenType}");
            if (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                var typeNodeName = reader.GetString();
                switch (typeNodeName)
                {
                    case "Variable":
                        return ReadVariable(ref reader, typeToConvert, options);
                    case "Unit":
                        return ReadUnit(ref reader, typeToConvert, options);
                    case "Tuple":
                        return ReadTuple(ref reader, typeToConvert, options);
                    default:
                        throw new NotSupportedException($"Unexpected type name when parsing Classic Type: {typeNodeName}");
                }
            }
            
            throw new NotSupportedException($"Unexpected token when parsing Classic Type: {reader.TokenType}");
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
            }
            else if (value is Type<TAttributes>.Tuple tuple)
            {
                WriteTuple(writer, tuple, options);
            }
            else
            {
                throw new NotSupportedException($"Encoding not supported for Classic Type case: {value?.GetType().Name}");
            }
        }
        
        private Type<TAttributes>? ReadUnit(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.Read())
            {
                var attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
                if (attributes == null)
                    throw new JsonException("Malformed \"Unit\" encountered: attributes cannot be null");

                if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
                    return IR.Type.Unit(attributes!);
            }
            throw new JsonException("Malformed Unit encountered: " + reader.TokenType);
        }

        private Type<TAttributes>? ReadVariable(ref Utf8JsonReader reader, System.Type typeToConvert,  JsonSerializerOptions options)
        {
            // We expect that we will be on the tag token
            TAttributes? attributes = default;
            Name? name = default;
            
            if (reader.Read())
            {
                attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
            }

            if (reader.Read())
            {
                name = NameConverter.Read(ref reader, typeToConvert, options);
            }

            if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
            {
                
                if(attributes == null && name == null) 
                    throw new JsonException("Misformed \"Variable\" encountered: \"Attributes\" and \"Name\" cannot be null");
            
                return IR.Type.Variable(attributes!, name!);            
            }

            throw new JsonException("Misformed \"Variable\" encountered: Did not find expected end of array");
        }

        private Type<TAttributes>? ReadTuple(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            TAttributes? attributes = default;
            List<Type<TAttributes>> elementTypes = new();

            // Read attributes
            if (reader.Read())
            {
                attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
                if (attributes == null)
                    throw new JsonException("Malformed \"Tuple\" encountered: attributes cannot be null");
            }

            // Read element types array
            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var elementType = Read(ref reader, typeof(Type<TAttributes>), options);
                    if (elementType == null)
                        throw new JsonException("Malformed \"Tuple\" encountered: element type cannot be null");
                    elementTypes.Add(elementType);
                }

                // We should now be at the end of the element types array
                // Read the closing bracket of the outer Tuple array
                if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
                {
                    var elementSeq = new Seq<Type<TAttributes>>(elementTypes.ToArray());
                    return IR.Type.Tuple(attributes!, elementSeq);
                }
            }

            throw new JsonException("Malformed \"Tuple\" encountered: " + reader.TokenType);
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

        private void WriteTuple(Utf8JsonWriter writer, Type<TAttributes>.Tuple tuple, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("Tuple");
            // Attributes
            AttributeConverter.Write(writer, tuple.Attributes, options);
            // Write the sequence of element types as a JSON array
            writer.WriteStartArray();
            foreach (var elem in tuple.ElementTypes)
            {
                Write(writer, elem, options);
            }
            writer.WriteEndArray();
            writer.WriteEndArray();
        }
    }
}
