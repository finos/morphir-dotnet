using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Morphir.Classic.IR.Codecs;

namespace Morphir.IR.Codecs;

public record MorphirJsonOptions(MorphirFormatVersion FormatVersion)
{
    private Lazy<JsonSerializerOptions>? _lazyJsonSerializerOptions;
    private Lazy<ConverterReferences>? _lazyConverterReferences;

    // Converter references to avoid reflection calls
    public ConverterReferences ConverterRefs
    {
        get
        {
            _lazyConverterReferences ??= new Lazy<ConverterReferences>(
                () => CreateConverterReferences(this),
                LazyThreadSafetyMode.ExecutionAndPublication);
            return _lazyConverterReferences.Value;
        }
    }

    public JsonSerializerOptions JsonSerializerOptions
    {
        get
        {
            // Thread-safe lazy initialization using Lazy<T>
            _lazyJsonSerializerOptions ??= new Lazy<JsonSerializerOptions>(
                () => CreateJsonSerializerOptions(this),
                LazyThreadSafetyMode.ExecutionAndPublication);

            return _lazyJsonSerializerOptions.Value;
        }
    }

    public MorphirJsonOptions WithFormatVersion(MorphirFormatVersion formatVersion) =>
        new(formatVersion);

    public static MorphirJsonOptions Default { get; } = new(new MorphirFormatVersion.Version2());

    private static ConverterReferences CreateConverterReferences(MorphirJsonOptions options)
    {
        var nameConverter = new NameConverter(options);
        var pathConverter = new PathJsonConverter(nameConverter);
        var modulePathConverter = new ModulePathJsonConverter(nameConverter);
        var packageNameConverter = new PackageNameJsonConverter(nameConverter);
        var fqNameConverter = new FqNameJsonConverter(options, nameConverter, modulePathConverter, packageNameConverter);

        return new ConverterReferences(
            nameConverter,
            pathConverter,
            modulePathConverter,
            packageNameConverter,
            fqNameConverter);
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions(MorphirJsonOptions options)
    {
        var converterRefs = options.ConverterRefs;

        JsonSerializerOptions jsonSerializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new DocumentJsonConverterFactory(options),
                converterRefs.NameConverter,
                new TypeJsonConverter(options),
                new ClassicTypeJsonConverterFactory(options, converterRefs),
                new FieldJsonConverterFactory(options, converterRefs),
                converterRefs.PathConverter,
                converterRefs.ModulePathConverter,
                converterRefs.PackageNameConverter,
                converterRefs.FqNameConverter
            }
        };

        return jsonSerializationOptions;
    }
}

/// <summary>
/// Holds references to converters to avoid reflection calls
/// </summary>
public record ConverterReferences(
    NameConverter NameConverter,
    PathJsonConverter PathConverter,
    ModulePathJsonConverter ModulePathConverter,
    PackageNameJsonConverter PackageNameConverter,
    FqNameJsonConverter FqNameConverter);
