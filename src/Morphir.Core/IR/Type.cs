namespace Morphir.IR;

public abstract record Type
{
    public required Document Metadata { get; set; }
    public sealed record Variable(Name Name) : Type;
    public sealed record Reference(FqName TypeName, Seq<Type> TypeParameters) : Type;
    public sealed record Tuple(Seq<Type> ElementTypes) : Type;
    public sealed record Record(Seq<Field> FieldTypes) : Type;
    public sealed record ExtensibleRecord(Name VariableName, Seq<Field> FieldTypes) : Type;
    public sealed record Function(Type ParameterType, Type ReturnType) : Type;
    public sealed record Unit() : Type;

    public record Field<T>(Name Name, T Type)
    {
        public Field<TResult> Map<TResult>(Func<T, TResult> mapper) =>
            new(Name, mapper(Type));
    }
    
    public record Field(Name Name, Type Type) : Field<Type>(Name, Type);
}
