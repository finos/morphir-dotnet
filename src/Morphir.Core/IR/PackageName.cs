using System.Collections.Immutable;

namespace Morphir.IR;

public sealed record PackageName(ImmutableList<Name> Names) : Path(Names)
{
    public new static PackageName Empty => new(ImmutableList<Name>.Empty);
    public new static PackageName FromList(params ImmutableList<Name> names) => new(names);
    public new static PackageName FromString(string input) => new(NamesFromString(input).ToImmutableList());
}
