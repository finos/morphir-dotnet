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

    public bool IsClassic => this switch
    {
        Version2 _ => true,
        Experimental _ => false,
        _ => false
    };
    
    public record Version2():MorphirFormatVersion("2"){}
    
    public record Experimental() : MorphirFormatVersion("3.0-Experimental");
    
    public static MorphirFormatVersion Parse(string text, MorphirFormatVersion fallbackVersion) =>
        text switch
        {
            "2" => new Version2(),
            "3.0-Experimental" => new Experimental(),
            _ => fallbackVersion
        };

    public static MorphirFormatVersion V2 => new Version2();
}
