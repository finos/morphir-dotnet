using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Morphir.IR.Codecs;

namespace Morphir.IR;

[JsonConverter(typeof(ModulePathJsonConverter))]
public sealed record ModulePath(ImmutableList<Name> Names):Path(Names)
{
    public new static ModulePath Empty => new(ImmutableList<Name>.Empty);
    public new static ModulePath FromList(ImmutableList<Name> names) => new(names);
    public new static ModulePath FromString(string input) => new(NamesFromString(input).ToImmutableList());
}
