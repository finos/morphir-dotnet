using Dunet;
using Morphir.IR;
using Type = Morphir.IR.Type;

namespace Morphir.Classic.IR;

[Union]
public abstract partial record Type<TAttrib>
{
    public required TAttrib Attributes { get; init; }

    public partial record Variable(Name Name);
    public partial record Tuple(Seq<Type<TAttrib>> Items);
    public partial record Function(Type<TAttrib> ParameterType, Type<Attribute> ReturnType);
    public partial record Unit;

}
