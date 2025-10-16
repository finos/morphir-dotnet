using Morphir.Classic.IR;
using Morphir.IR;
using Morphir.IR.Codecs;

namespace Morphir.StepDefinitions;

public record ClassicMorphirIrSerializationTestDriver
{
    public MorphirJsonOptions Options { get; set; } = MorphirJsonOptions.Default;
    public string InputJson { get; set; } = string.Empty;
    public string ExpectedJson { get; set; } = string.Empty;
    public Type<IR.PlaceHolder>? DeserializedType { get; set; } = null;
    
    public void WithFormatVersion(string formatVersion) =>
        Options = Options.WithFormatVersion(MorphirFormatVersion.Parse(formatVersion, MorphirFormatVersion.V2));
}
