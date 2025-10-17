using Dunet;
using StaticCs;

namespace Morphir.IR;

[Closed]
[Union]
public abstract partial record Attribute
{
    public partial record Empty : Attribute
    {
        public static Empty Instance { get; } = new();
    }
}
