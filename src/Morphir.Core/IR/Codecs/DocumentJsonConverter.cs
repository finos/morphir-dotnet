using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morphir.IR.Codecs;

/// <summary>
/// JSON converter for serializing and deserializing <see cref="Document"/> instances.
/// </summary>
/// <remarks>
/// <para>Documents map naturally to JSON with the following format:</para>
/// <list type="bullet">
/// <item><description>Null → null</description></item>
/// <item><description>Boolean → true/false</description></item>
/// <item><description>String → "string value"</description></item>
/// <item><description>Integer → 42</description></item>
/// <item><description>Number → 3.14</description></item>
/// <item><description>Array → [...]</description></item>
/// <item><description>Object → {"key": value}</description></item>
/// </list>
/// <para>Extended types use tagged format:</para>
/// <list type="bullet">
/// <item><description>Uri → {"$uri": "https://example.com"}</description></item>
/// <item><description>Uuid → {"$uuid": "550e8400-e29b-41d4-a716-446655440000"}</description></item>
/// <item><description>Bytes → {"$bytes": "SGVsbG8="} (base64)</description></item>
/// </list>
/// </remarks>
public class DocumentJsonConverter : JsonConverter<Document>
{
    public DocumentJsonConverter(MorphirJsonOptions morphirJsonOptions)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentJsonConverter"/> class with default options.
    /// </summary>
    public DocumentJsonConverter()
    {
    }

    /// <summary>
    /// Reads and deserializes a Document from JSON.
    /// </summary>
    public override Document? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => new Document.Null(),
            JsonTokenType.True => new Document.Boolean(true),
            JsonTokenType.False => new Document.Boolean(false),
            JsonTokenType.String => new Document.String(reader.GetString()!),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => throw new JsonException($"Unexpected token when parsing Document: {reader.TokenType}")
        };
    }

    /// <summary>
    /// Reads a number token and determines if it's an Integer or Number.
    /// </summary>
    private Document ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var intValue))
        {
            return new Document.Integer(intValue);
        }

        if (reader.TryGetDecimal(out var decValue))
        {
            return new Document.Number(decValue);
        }

        throw new JsonException($"Unable to parse number value: {reader.GetString()}");
    }

    /// <summary>
    /// Reads an array and recursively deserializes its elements.
    /// </summary>
    private Document ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var items = ImmutableList.CreateBuilder<Document>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var element = Read(ref reader, typeof(Document), options);
            if (element == null)
                throw new JsonException("Array element cannot be null");
            items.Add(element);
        }

        return new Document.Array(items.ToImmutable());
    }

    /// <summary>
    /// Reads an object and determines if it's a tagged type or regular Object.
    /// </summary>
    private Document ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        // Peek ahead to check for tagged format
        var startDepth = reader.CurrentDepth;

        if (!reader.Read())
            throw new JsonException("Unexpected end of JSON when reading object");

        // Check for empty object
        if (reader.TokenType == JsonTokenType.EndObject)
        {
            return new Document.Object(ImmutableDictionary<string, Document>.Empty);
        }

        // Read first property name
        if (reader.TokenType != JsonTokenType.PropertyName)
            throw new JsonException($"Expected property name but got: {reader.TokenType}");

        var firstKey = reader.GetString()!;

        // Check if this is a tagged format
        if (firstKey.StartsWith("$"))
        {
            return ReadTaggedFormat(ref reader, firstKey, options, startDepth);
        }

        // Otherwise, read as regular object
        return ReadRegularObject(ref reader, firstKey, options);
    }

    /// <summary>
    /// Reads a tagged format object ($uri, $uuid, $bytes).
    /// </summary>
    private Document ReadTaggedFormat(ref Utf8JsonReader reader, string tag, JsonSerializerOptions options, int startDepth)
    {
        // Read the value
        if (!reader.Read())
            throw new JsonException($"Expected value for tag {tag}");

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Tagged format {tag} requires string value");

        var value = reader.GetString()!;

        // Read end of object
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException($"Expected end of object after {tag} tag");

        return tag switch
        {
            "$uri" => new Document.Uri(new Uri(value)),
            "$uuid" => new Document.Uuid(Guid.Parse(value)),
            "$bytes" => new Document.Bytes(Convert.FromBase64String(value).ToImmutableArray()),
            _ => throw new JsonException($"Unknown tagged format: {tag}")
        };
    }

    /// <summary>
    /// Reads a regular object (key-value pairs).
    /// </summary>
    private Document ReadRegularObject(ref Utf8JsonReader reader, string firstKey, JsonSerializerOptions options)
    {
        var items = ImmutableDictionary.CreateBuilder<string, Document>();

        // Add first key-value pair (we already read the key)
        if (!reader.Read())
            throw new JsonException("Expected value after property name");

        var firstValue = Read(ref reader, typeof(Document), options);
        if (firstValue == null)
            throw new JsonException("Object value cannot be null");
        items.Add(firstKey, firstValue);

        // Read remaining key-value pairs
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name but got: {reader.TokenType}");

            var key = reader.GetString()!;

            if (!reader.Read())
                throw new JsonException("Expected value after property name");

            var value = Read(ref reader, typeof(Document), options);
            if (value == null)
                throw new JsonException("Object value cannot be null");
            items.Add(key, value);
        }

        return new Document.Object(items.ToImmutable());
    }

    /// <summary>
    /// Writes and serializes a Document to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, Document value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case Document.Null:
                writer.WriteNullValue();
                break;
            case Document.Boolean b:
                writer.WriteBooleanValue(b.Value);
                break;
            case Document.String s:
                writer.WriteStringValue(s.Value);
                break;
            case Document.Uri u:
                WriteUri(writer, u);
                break;
            case Document.Uuid u:
                WriteUuid(writer, u);
                break;
            case Document.Bytes b:
                WriteBytes(writer, b);
                break;
            case Document.Number n:
                writer.WriteNumberValue(n.Value);
                break;
            case Document.Integer i:
                writer.WriteNumberValue(i.Value);
                break;
            case Document.Array a:
                WriteArray(writer, a, options);
                break;
            case Document.Object o:
                WriteObject(writer, o, options);
                break;
            default:
                throw new JsonException($"Unknown Document type: {value.GetType().Name}");
        }
    }

    /// <summary>
    /// Writes a Uri document using tagged format.
    /// </summary>
    private void WriteUri(Utf8JsonWriter writer, Document.Uri uriDoc)
    {
        writer.WriteStartObject();
        writer.WriteString("$uri", uriDoc.Value.ToString());
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a Uuid document using tagged format.
    /// </summary>
    private void WriteUuid(Utf8JsonWriter writer, Document.Uuid uuidDoc)
    {
        writer.WriteStartObject();
        writer.WriteString("$uuid", uuidDoc.Value.ToString());
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a Bytes document using tagged format with base64 encoding.
    /// </summary>
    private void WriteBytes(Utf8JsonWriter writer, Document.Bytes bytesDoc)
    {
        writer.WriteStartObject();
        writer.WriteString("$bytes", Convert.ToBase64String(bytesDoc.Value.ToArray()));
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes an Array document recursively.
    /// </summary>
    private void WriteArray(Utf8JsonWriter writer, Document.Array arrayDoc, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in arrayDoc.Items)
        {
            Write(writer, item, options);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes an Object document recursively.
    /// </summary>
    private void WriteObject(Utf8JsonWriter writer, Document.Object objectDoc, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (key, value) in objectDoc.Items)
        {
            writer.WritePropertyName(key);
            Write(writer, value, options);
        }

        writer.WriteEndObject();
    }
}
