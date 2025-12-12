using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Morphir.Classic.IR.Codecs;

namespace Morphir.IR.Codecs;

public record MorphirJsonOptions(MorphirFormatVersion FormatVersion)
{
    private Lazy<JsonSerializerOptions>? _lazyJsonSerializerOptions;

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

    private static JsonSerializerOptions CreateJsonSerializerOptions(MorphirJsonOptions options)
    {
        JsonSerializerOptions jsonSerializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new DocumentJsonConverterFactory(options),
                new NameConverter(options),
                new TypeJsonConverter(options),
                new ClassicTypeJsonConverterFactory(options),
                new FieldJsonConverterFactory(options),
                new PathJsonConverter(options),
                new ModulePathJsonConverter(options),
                new PackageNameJsonConverter(options),
                new FqNameJsonConverter(options)
            }
        };

        return jsonSerializationOptions;
    }
}
