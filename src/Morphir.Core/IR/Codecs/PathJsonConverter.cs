using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morphir.IR.Codecs;

/// <summary>
/// JSON converter for serializing and deserializing <see cref="Path"/> instances.
/// </summary>
/// <remarks>
/// Paths are serialized as JSON arrays of Name arrays.
/// Each Name in the path is serialized according to the NameConverter rules.
/// Example: A path with names ["alpha"], ["beta"], ["gamma"] is serialized as:
/// [["alpha"],["beta"],["gamma"]]
/// </remarks>
public class PathJsonConverter(MorphirJsonOptions morphirJsonOptions) : JsonConverter<Path>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PathJsonConverter"/> class with default options.
    /// </summary>
    public PathJsonConverter() : this(MorphirJsonOptions.Default) { }

    /// <summary>
    /// Gets the converter for serializing and deserializing individual names.
    /// </summary>
    private JsonConverter<Name> NameConverter { get; } =
        (JsonConverter<Name>)morphirJsonOptions.JsonSerializerOptions.GetConverter(typeof(Name));

    /// <summary>
    /// Reads and deserializes a Path from JSON.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the start of the Path array.</param>
    /// <param name="typeToConvert">The type being converted (unused).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>
    /// A <see cref="Path"/> instance deserialized from JSON.
    /// </returns>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is malformed or not an array.
    /// </exception>
    /// <remarks>
    /// Expected JSON format: [["name1"],["name2"],["name3"]]
    /// Each element is a Name array.
    /// </remarks>
    public override Path? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Unexpected token when parsing Path, expected StartArray but got: {reader.TokenType}");

        var names = ImmutableList.CreateBuilder<Name>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var name = NameConverter.Read(ref reader, typeof(Name), options);
            if (name == null)
                throw new JsonException("Malformed Path encountered: name cannot be null");
            names.Add(name);
        }

        return new Path(names.ToImmutable());
    }

    /// <summary>
    /// Writes and serializes a Path to JSON.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The Path instance to serialize.</param>
    /// <param name="options">The serializer options.</param>
    /// <remarks>
    /// Serializes the Path as a JSON array of Name arrays.
    /// Example output: [["alpha"],["beta"],["gamma"]]
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, Path value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var name in value.Names)
        {
            NameConverter.Write(writer, name, options);
        }

        writer.WriteEndArray();
    }
}
