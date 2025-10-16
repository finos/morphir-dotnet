using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Morphir.IR.Codecs;

namespace Morphir.IR;

[JsonConverter(typeof(PackageNameJsonConverter))]
public sealed record PackageName(ImmutableList<Name> Names):Path(Names)
{
    public new static PackageName Empty => new(ImmutableList<Name>.Empty);
    public new static PackageName FromList(ImmutableList<Name> names) => new(names);
    public new static PackageName FromString(string input) => new(NamesFromString(input).ToImmutableList());
}
