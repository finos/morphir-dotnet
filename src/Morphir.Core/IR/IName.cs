using System.Collections.Immutable;

namespace Morphir.IR;

public interface IName
{
    IImmutableList<string> ToList();
}
