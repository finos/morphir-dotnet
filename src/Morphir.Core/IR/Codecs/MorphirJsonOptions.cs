using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Morphir.Classic.IR.Codecs;

namespace Morphir.IR.Codecs;

public record MorphirJsonOptions(MorphirFormatVersion FormatVersion)
{
    [field: AllowNull, MaybeNull]
    public JsonSerializerOptions JsonSerializerOptions => field ??= ToJsonSerializerOptions(this);
    
    public MorphirJsonOptions WithFormatVersion(MorphirFormatVersion formatVersion) =>
        new(formatVersion);
    
    public static MorphirJsonOptions Default { get; } = new(new MorphirFormatVersion.Version2());

    private static JsonSerializerOptions ToJsonSerializerOptions(MorphirJsonOptions options)
    {
        JsonSerializerOptions jsonSerializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new NameConverter(options),
                new TypeJsonConverter(options),
                new ClassicTypeJsonConverterFactory(options),
                new FieldJsonConverterFactory(options),
                new PathJsonConverter(options),
                new ModulePathJsonConverter(options),
                new PackageNameJsonConverter(options)
            }
        };

        return jsonSerializationOptions;
    }
}
