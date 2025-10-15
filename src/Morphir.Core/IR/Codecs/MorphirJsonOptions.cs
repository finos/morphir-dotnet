using System.Text.Json;
using Morphir.Classic.IR.Codecs;

namespace Morphir.IR.Codecs;

public class MorphirJsonOptions {
    public MorphirJsonOptions(MorphirFormatVersion morphirFormatVersion)
    {
        FormatVersion = morphirFormatVersion;
        JsonSerializerOptions = ToJsonSerializerOptions(this);   
    }
    
    public MorphirFormatVersion FormatVersion { get; }
    public JsonSerializerOptions JsonSerializerOptions { get; }
    
    public static MorphirJsonOptions Default { get; } = new (new MorphirFormatVersion.Version2());

        private static JsonSerializerOptions ToJsonSerializerOptions(MorphirJsonOptions options)
        {
            JsonSerializerOptions jsonSerializationOptions = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters =
                {
                    new NameConverter(options), 
                    new TypeJsonConverter(options), 
                    new ClassicTypeJsonConverterFactory(options)
                }
            };
            
            return jsonSerializationOptions;
        }
}
