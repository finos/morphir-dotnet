using Dunet;
using Morphir.IR;

namespace Morphir.Classic.IR;

[Union]
public partial record Value<TTypeAttributes, TValueAttributes>
{
    public TValueAttributes Attributes { get; init; }

    public partial record Constructor(FqName FqName);
    public partial record Variable(Name Name);
    public partial record Unit;
}

public static class Value
{
    
}
