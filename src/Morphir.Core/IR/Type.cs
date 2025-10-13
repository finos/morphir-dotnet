namespace Morphir.IR;

public abstract record Type
{
    public required Document Metadata { get; set; }
    public sealed record Variable(Name Name) : Type;
}
