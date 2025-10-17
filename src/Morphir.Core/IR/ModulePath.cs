using System.Collections.Immutable;

namespace Morphir.IR;

public sealed record ModulePath(ImmutableList<Name> Names):Path(Names)
{
    public new static ModulePath Empty => new(ImmutableList<Name>.Empty);
    public new static ModulePath FromList(params ImmutableList<Name> names) => new(names);
    public new static ModulePath FromString(string input) => new(NamesFromString(input).ToImmutableList());
}
