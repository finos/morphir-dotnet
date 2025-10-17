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

        private JsonConverter<FqName> FqNameConverter { get; } =
            (JsonConverter<FqName>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(FqName));

        public override bool CanConvert(System.Type typeToConvert)
        {
            return (typeToConvert == typeof(Type<TAttributes>))
                   || (typeToConvert == typeof(Type<TAttributes>.Variable))
                   || (typeToConvert == typeof(Type<TAttributes>.Reference))
                   || (typeToConvert == typeof(Type<TAttributes>.Tuple))
                   || (typeToConvert == typeof(Type<TAttributes>.Record))
                   || (typeToConvert == typeof(Type<TAttributes>.ExtensibleRecord))
                   || (typeToConvert == typeof(Type<TAttributes>.Function))
                   || (typeToConvert == typeof(Type<TAttributes>.Unit));
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
                    case "Reference":
                        return ReadReference(ref reader, typeToConvert, options);
                    case "Tuple":
                        return ReadTuple(ref reader, typeToConvert, options);
                    case "Record":
                        return ReadRecord(ref reader, typeToConvert, options);
                    case "ExtensibleRecord":
                        return ReadExtensibleRecord(ref reader, typeToConvert, options);
                    case "Function":
                        return ReadFunction(ref reader, typeToConvert, options);
                    case "Unit":
                        return ReadUnit(ref reader, typeToConvert, options);
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
            else if (value is Type<TAttributes>.Reference reference)
            {
                WriteReference(writer, reference, options);
            }
            else if (value is Type<TAttributes>.Tuple tuple)
            {
                WriteTuple(writer, tuple, options);
            }
            else if (value is Type<TAttributes>.Record record)
            {
                WriteRecord(writer, record, options);
            }
            else if (value is Type<TAttributes>.ExtensibleRecord extensibleRecord)
            {
                WriteExtensibleRecord(writer, extensibleRecord, options);
            }
            else if (value is Type<TAttributes>.Function function)
            {
                WriteFunction(writer, function, options);
            }
            else if (value is Type<TAttributes>.Unit unit)
            {
                WriteUnit(writer, unit, options);
            }
            else
            {
                throw new NotSupportedException($"Encoding not supported for Classic Type case: {value?.GetType().Name}");
            }
        }
        
        private Type<TAttributes>? ReadReference(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            TAttributes? attributes = default;
            FqName? typeName = null;
            List<Type<TAttributes>> typeParameters = new();

            // Read attributes
            if (reader.Read())
            {
                attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
                if (attributes == null)
                    throw new JsonException("Malformed \"Reference\" encountered: attributes cannot be null");
            }

            // Read FqName
            if (reader.Read())
            {
                typeName = FqNameConverter.Read(ref reader, typeof(FqName), options);
                if (typeName == null)
                    throw new JsonException("Malformed \"Reference\" encountered: typeName cannot be null");
            }

            // Read type parameters array
            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var typeParam = Read(ref reader, typeof(Type<TAttributes>), options);
                    if (typeParam == null)
                        throw new JsonException("Malformed \"Reference\" encountered: type parameter cannot be null");
                    typeParameters.Add(typeParam);
                }

                // Read the closing bracket of the outer Reference array
                if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
                {
                    var typeParamSeq = new Seq<Type<TAttributes>>(typeParameters.ToArray());
                    return new Type<TAttributes>.Reference(typeName!, typeParamSeq) { Attributes = attributes! };
                }
            }

            throw new JsonException("Malformed \"Reference\" encountered: " + reader.TokenType);
        }

        private Type<TAttributes>? ReadRecord(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            TAttributes? attributes = default;
            List<Type.Field<TAttributes>> fieldTypes = new();

            // Read attributes
            if (reader.Read())
            {
                attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
                if (attributes == null)
                    throw new JsonException("Malformed \"Record\" encountered: attributes cannot be null");
            }

            // Read field types array
            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
            {
                var fieldConverter = (JsonConverter<Type.Field<TAttributes>>)options.GetConverter(typeof(Type.Field<TAttributes>));
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var field = fieldConverter.Read(ref reader, typeof(Type.Field<TAttributes>), options);
                    if (field == null)
                        throw new JsonException("Malformed \"Record\" encountered: field cannot be null");
                    fieldTypes.Add(field);
                }

                // Read the closing bracket of the outer Record array
                if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
                {
                    var fieldSeq = new Seq<Type.Field<TAttributes>>(fieldTypes.ToArray());
                    return new Type<TAttributes>.Record(fieldSeq) { Attributes = attributes! };
                }
            }

            throw new JsonException("Malformed \"Record\" encountered: " + reader.TokenType);
        }

        private Type<TAttributes>? ReadExtensibleRecord(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            TAttributes? attributes = default;
            Name? variableName = null;
            List<Type.Field<TAttributes>> fieldTypes = new();

            // Read attributes
            if (reader.Read())
            {
                attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
                if (attributes == null)
                    throw new JsonException("Malformed \"ExtensibleRecord\" encountered: attributes cannot be null");
            }

            // Read variable name
            if (reader.Read())
            {
                variableName = NameConverter.Read(ref reader, typeof(Name), options);
                if (variableName == null)
                    throw new JsonException("Malformed \"ExtensibleRecord\" encountered: variableName cannot be null");
            }

            // Read field types array
            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
            {
                var fieldConverter = (JsonConverter<Type.Field<TAttributes>>)options.GetConverter(typeof(Type.Field<TAttributes>));
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var field = fieldConverter.Read(ref reader, typeof(Type.Field<TAttributes>), options);
                    if (field == null)
                        throw new JsonException("Malformed \"ExtensibleRecord\" encountered: field cannot be null");
                    fieldTypes.Add(field);
                }

                // Read the closing bracket of the outer ExtensibleRecord array
                if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
                {
                    var fieldSeq = new Seq<Type.Field<TAttributes>>(fieldTypes.ToArray());
                    return new Type<TAttributes>.ExtensibleRecord(variableName!, fieldSeq) { Attributes = attributes! };
                }
            }

            throw new JsonException("Malformed \"ExtensibleRecord\" encountered: " + reader.TokenType);
        }

        private Type<TAttributes>? ReadFunction(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            TAttributes? attributes = default;
            Type<TAttributes>? parameterType = null;
            Type<TAttributes>? returnType = null;

            // Read attributes
            if (reader.Read())
            {
                attributes = AttributeConverter.Read(ref reader, typeof(TAttributes), options);
                if (attributes == null)
                    throw new JsonException("Malformed \"Function\" encountered: attributes cannot be null");
            }

            // Read parameter type
            if (reader.Read())
            {
                parameterType = Read(ref reader, typeof(Type<TAttributes>), options);
                if (parameterType == null)
                    throw new JsonException("Malformed \"Function\" encountered: parameterType cannot be null");
            }

            // Read return type
            if (reader.Read())
            {
                returnType = Read(ref reader, typeof(Type<TAttributes>), options);
                if (returnType == null)
                    throw new JsonException("Malformed \"Function\" encountered: returnType cannot be null");
            }

            // Read the closing bracket of the Function array
            if (reader.Read() && reader.TokenType == JsonTokenType.EndArray)
            {
                return new Type<TAttributes>.Function(parameterType!, returnType!) { Attributes = attributes! };
            }

            throw new JsonException("Malformed \"Function\" encountered: " + reader.TokenType);
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
            Name? name = null;
            
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

        private void WriteReference(Utf8JsonWriter writer, Type<TAttributes>.Reference reference, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("Reference");
            // Attributes
            AttributeConverter.Write(writer, reference.Attributes, options);
            // FqName
            FqNameConverter.Write(writer, reference.TypeName, options);
            // Type parameters
            writer.WriteStartArray();
            foreach (var typeParam in reference.TypeParameters)
            {
                Write(writer, typeParam, options);
            }
            writer.WriteEndArray();
            writer.WriteEndArray();
        }

        private void WriteRecord(Utf8JsonWriter writer, Type<TAttributes>.Record record, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("Record");
            // Attributes
            AttributeConverter.Write(writer, record.Attributes, options);
            // Field types
            writer.WriteStartArray();
            var fieldConverter = (JsonConverter<Type.Field<TAttributes>>)options.GetConverter(typeof(Type.Field<TAttributes>));
            foreach (var field in record.FieldTypes)
            {
                fieldConverter.Write(writer, field, options);
            }
            writer.WriteEndArray();
            writer.WriteEndArray();
        }

        private void WriteExtensibleRecord(Utf8JsonWriter writer, Type<TAttributes>.ExtensibleRecord extensibleRecord, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("ExtensibleRecord");
            // Attributes
            AttributeConverter.Write(writer, extensibleRecord.Attributes, options);
            // Variable name
            NameConverter.Write(writer, extensibleRecord.VariableName, options);
            // Field types
            writer.WriteStartArray();
            var fieldConverter = (JsonConverter<Type.Field<TAttributes>>)options.GetConverter(typeof(Type.Field<TAttributes>));
            foreach (var field in extensibleRecord.FieldTypes)
            {
                fieldConverter.Write(writer, field, options);
            }
            writer.WriteEndArray();
            writer.WriteEndArray();
        }

        private void WriteFunction(Utf8JsonWriter writer, Type<TAttributes>.Function function, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue("Function");
            // Attributes
            AttributeConverter.Write(writer, function.Attributes, options);
            // Parameter type
            Write(writer, function.ParameterType, options);
            // Return type
            Write(writer, function.ReturnType, options);
            writer.WriteEndArray();
        }
    }
}
