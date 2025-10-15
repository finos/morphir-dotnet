namespace Morphir.IR;

public abstract record Type
{
    public required Document Metadata { get; set; }
    public sealed record Variable(Name Name) : Type;

    public sealed record Unit() : Type;

    public record Field<T>(Name Name, T Type)
    {
        public Field<TResult> Map<TResult>(Func<T, TResult> mapper) =>
            new(Name, mapper(Type));
    }
}
