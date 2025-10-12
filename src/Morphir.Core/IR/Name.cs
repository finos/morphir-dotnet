using System.Collections.Immutable;
using Vogen;

namespace Morphir.IR;

[ValueObject<string>]
public readonly partial struct Name
{
    public static IImmutableList<string> ToList<T>(T name) where T : IName => name.ToList();
}
