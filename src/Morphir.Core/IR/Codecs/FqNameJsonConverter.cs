using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morphir.IR.Codecs;

/// <summary>
/// JSON converter for serializing and deserializing <see cref="FqName"/> instances.
/// </summary>
/// <remarks>
/// FqNames are serialized as JSON arrays with three elements: [PackagePath, ModulePath, LocalName].
/// Example: A FqName with package ["my","package"], module ["my","module"], and name ["myFunc"]
/// is serialized as: [[["my"],["package"]],[["my"],["module"]],["my","func"]]
/// </remarks>
public class FqNameJsonConverter : JsonConverter<FqName>
{
    private readonly JsonConverter<PackageName> _packagePathConverter;
    private readonly JsonConverter<ModulePath> _modulePathConverter;
    private readonly JsonConverter<Name> _nameConverter;

    public FqNameJsonConverter(
        MorphirJsonOptions morphirJsonOptions,
        JsonConverter<Name> nameConverter,
        JsonConverter<ModulePath> modulePathConverter,
        JsonConverter<PackageName> packageNameConverter)
    {
        _nameConverter = nameConverter;
        _modulePathConverter = modulePathConverter;
        _packagePathConverter = packageNameConverter;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FqNameJsonConverter"/> class with default options.
    /// </summary>
    public FqNameJsonConverter() : this(
        MorphirJsonOptions.Default,
        new NameConverter(),
        new ModulePathJsonConverter(),
        new PackageNameJsonConverter())
    {
    }

    /// <summary>
    /// Reads and deserializes an FqName from JSON.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the start of the FqName array.</param>
    /// <param name="typeToConvert">The type being converted (unused).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>
    /// A <see cref="FqName"/> instance deserialized from JSON.
    /// </returns>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is malformed or not an array with exactly 3 elements.
    /// </exception>
    /// <remarks>
    /// Expected JSON format: [PackagePath, ModulePath, LocalName]
    /// Each element is serialized according to its respective converter.
    /// </remarks>
    public override FqName? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Unexpected token when parsing FqName, expected StartArray but got: {reader.TokenType}");

        PackageName? packagePath = null;
        ModulePath? modulePath = null;
        Name? localName = null;

        // Read PackagePath
        if (reader.Read())
        {
            packagePath = _packagePathConverter.Read(ref reader, typeof(PackageName), options);
            if (packagePath == null)
                throw new JsonException("Malformed FqName encountered: packagePath cannot be null");
        }
        else
        {
            throw new JsonException("Malformed FqName encountered: expected PackagePath element");
        }

        // Read ModulePath
        if (reader.Read())
        {
            modulePath = _modulePathConverter.Read(ref reader, typeof(ModulePath), options);
            if (modulePath == null)
                throw new JsonException("Malformed FqName encountered: modulePath cannot be null");
        }
        else
        {
            throw new JsonException("Malformed FqName encountered: expected ModulePath element");
        }

        // Read LocalName
        if (reader.Read())
        {
            localName = _nameConverter.Read(ref reader, typeof(Name), options);
            if (localName == null)
                throw new JsonException("Malformed FqName encountered: localName cannot be null");
        }
        else
        {
            throw new JsonException("Malformed FqName encountered: expected LocalName element");
        }

        // Read end of array
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Malformed FqName encountered: expected end of array");

        return new FqName(packagePath, modulePath, localName);
    }

    /// <summary>
    /// Writes and serializes an FqName to JSON.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The FqName instance to serialize.</param>
    /// <param name="options">The serializer options.</param>
    /// <remarks>
    /// Serializes the FqName as a JSON array: [PackagePath, ModulePath, LocalName].
    /// Example output: [[["my"],["package"]],[["my"],["module"]],["my","func"]]
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, FqName value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        _packagePathConverter.Write(writer, value.PackagePath, options);
        _modulePathConverter.Write(writer, value.ModulePath, options);
        _nameConverter.Write(writer, value.LocalName, options);

        writer.WriteEndArray();
    }
}
