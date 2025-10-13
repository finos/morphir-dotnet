using Morphir.IR;

namespace Morphir.Classic.IR;

public abstract record Type<TAttrib>
{
    public record Variable(Name Name);
}
