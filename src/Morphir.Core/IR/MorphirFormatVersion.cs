using StaticCs;

namespace Morphir.IR;

[Closed]
public abstract record MorphirFormatVersion
{
    private MorphirFormatVersion(string version)
    {
        Version = version;   
    }
    public string Version { get; }
    
    public record Version2():MorphirFormatVersion("2"){}
}
