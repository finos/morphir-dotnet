using System.Collections.Immutable;
using Funcky;
using StaticCs;

namespace Morphir.IR;

[DiscriminatedUnion]
public abstract partial record Document
{
    public sealed partial record Array(IImmutableList<Document> Items) : Document;
    
    public abstract partial record DocumentValue : Document;

    public sealed partial record Boolean(bool Value) : DocumentValue;

    public sealed partial record DocNumber(decimal Value) : DocumentValue;
    
    public sealed partial record Object(ImmutableDictionary<string, Document> Items) : Document;
    
}

