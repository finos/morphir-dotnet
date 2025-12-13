using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.IR;
using Morphir.IR.Codecs;

namespace Morphir.Classic.IR.Codecs;

/// <summary>
/// JSON converter factory for serializing and deserializing <see cref="Type.Field{TAttributes}"/> instances.
/// </summary>
/// <remarks>
/// This factory creates specialized converters for Field types with different attribute types.
/// Fields are serialized as JSON objects with two properties:
/// <list type="bullet">
/// <item><description>"name" - The field name as a Name array</description></item>
/// <item><description>"tpe" - The field type as a serialized Type</description></item>
/// </list>
/// Example JSON: {"name":["field","name"],"tpe":["Unit",{}]}
/// </remarks>
public class FieldJsonConverterFactory(MorphirJsonOptions morphirJsonOptions) : JsonConverterFactory
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FieldJsonConverterFactory"/> class with default options.
    /// </summary>
    public FieldJsonConverterFactory() : this(MorphirJsonOptions.Default) { }

    /// <summary>
    /// Determines whether this factory can convert the specified type.
    /// </summary>
    /// <param name="typeToConvert">The type to check for conversion support.</param>
    /// <returns>
    /// <c>true</c> if the type is <see cref="Type.Field{TAttributes}"/>; otherwise, <c>false</c>.
    /// </returns>
    public override bool CanConvert(System.Type typeToConvert)
    {
        if (typeToConvert.IsGenericType)
        {
            var genericDef = typeToConvert.GetGenericTypeDefinition();
            if (genericDef == typeof(Type.Field<>))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Creates a JSON converter for the specified Field type.
    /// </summary>
    /// <param name="typeToConvert">The type to create a converter for.</param>
    /// <param name="options">The serializer options (unused in this implementation).</param>
    /// <returns>
    /// A <see cref="FieldJsonConverter{TAttributes}"/> instance configured for the attribute type.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the type is not a generic type with exactly one type argument.
    /// </exception>
    public override JsonConverter? CreateConverter(System.Type typeToConvert, JsonSerializerOptions options)
    {
        // Expecting Type.Field<TAttributes>
        var attrs = typeToConvert.GetGenericArguments();
        if (attrs.Length != 1)
        {
            throw new NotSupportedException($"Unsupported type for FieldJsonConverterFactory: {typeToConvert}");
        }

        var attributeType = attrs[0];
        var converterType = typeof(FieldJsonConverter<>).MakeGenericType(attributeType);
        return (JsonConverter?)Activator.CreateInstance(converterType, morphirJsonOptions);
    }

    /// <summary>
    /// JSON converter for <see cref="Type.Field{TAttributes}"/> instances.
    /// </summary>
    /// <typeparam name="TAttributes">The type of attributes associated with the field type.</typeparam>
    private class FieldJsonConverter<TAttributes>(MorphirJsonOptions morphirJsonOptions) : JsonConverter<Type.Field<TAttributes>>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FieldJsonConverter{TAttributes}"/> class with default options.
        /// </summary>
        public FieldJsonConverter() : this(MorphirJsonOptions.Default) { }

        /// <summary>
        /// Gets the converter for serializing and deserializing field names.
        /// </summary>
        private JsonConverter<Name> NameConverter { get; } =
            (JsonConverter<Name>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(Name));

        /// <summary>
        /// Gets the converter for serializing and deserializing field types.
        /// </summary>
        private JsonConverter<Type<TAttributes>> TypeConverter { get; } =
            (JsonConverter<Type<TAttributes>>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(Type<TAttributes>));

        /// <summary>
        /// Reads and deserializes a Field from JSON.
        /// </summary>
        /// <param name="reader">The JSON reader positioned at the start of the Field object.</param>
        /// <param name="typeToConvert">The type being converted (unused).</param>
        /// <param name="options">The serializer options.</param>
        /// <returns>
        /// A <see cref="Type.Field{TAttributes}"/> instance deserialized from JSON.
        /// </returns>
        /// <exception cref="JsonException">
        /// Thrown when the JSON is malformed or missing required properties.
        /// </exception>
        /// <remarks>
        /// Expected JSON format: {"name":["field","name"],"tpe":["Type",...]}
        /// Both "name" and "tpe" properties are required.
        /// </remarks>
        public override Type.Field<TAttributes>? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Unexpected token when parsing Field, expected StartObject but got: {reader.TokenType}");

            Name? name = null;
            Type<TAttributes>? type = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (name == null || type == null)
                        throw new JsonException("Malformed Field encountered: both 'name' and 'tpe' must be present");

                    return new Type.Field<TAttributes>(name, type);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propertyName = reader.GetString();
                    reader.Read(); // Move to the property value

                    switch (propertyName)
                    {
                        case "name":
                            name = NameConverter.Read(ref reader, typeof(Name), options);
                            break;
                        case "tpe":
                            type = TypeConverter.Read(ref reader, typeof(Type<TAttributes>), options);
                            break;
                        default:
                            throw new JsonException($"Unexpected property '{propertyName}' in Field");
                    }
                }
            }

            throw new JsonException("Malformed Field encountered: unexpected end of JSON");
        }

        /// <summary>
        /// Writes and serializes a Field to JSON.
        /// </summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The Field instance to serialize.</param>
        /// <param name="options">The serializer options.</param>
        /// <remarks>
        /// Serializes the Field as a JSON object with two properties:
        /// <list type="bullet">
        /// <item><description>"name" - The field name serialized as a Name array</description></item>
        /// <item><description>"tpe" - The field type serialized using the Type converter</description></item>
        /// </list>
        /// Example output: {"name":["field","name"],"tpe":["Unit",{}]}
        /// </remarks>
        public override void Write(Utf8JsonWriter writer, Type.Field<TAttributes> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("name");
            NameConverter.Write(writer, value.Name, options);

            writer.WritePropertyName("tpe");
            TypeConverter.Write(writer, value.Type, options);

            writer.WriteEndObject();
        }
    }
}
