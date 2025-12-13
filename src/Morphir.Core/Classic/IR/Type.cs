using System.Text.Json.Serialization;
using Dunet;
using Morphir.Classic.IR.Codecs;
using Morphir.IR;
//using Type = Morphir.IR.Type;

namespace Morphir.Classic.IR;

[Union]
public abstract partial record Type<TAttrib>
{
    public required TAttrib Attributes { get; init; }

    public partial record Variable(Name Name);
    public partial record Reference(FqName TypeName, Seq<Type<TAttrib>> TypeParameters);
    public partial record Tuple(Seq<Type<TAttrib>> ElementTypes);
    public partial record Record(Seq<Type.Field<TAttrib>> FieldTypes);
    public partial record ExtensibleRecord(Name VariableName, Seq<Type.Field<TAttrib>> FieldTypes);
    public partial record Function(Type<TAttrib> ParameterType, Type<TAttrib> ReturnType);
    public partial record Unit;
}

public static class Type
{
    [JsonConverter(typeof(FieldJsonConverterFactory))]
    public record Field<TAttribute>(Name Name, Type<TAttribute> Type)
        : Morphir.IR.Type.Field<Type<TAttribute>>(Name, Type)
    {
    }

    public static class Field
    {
        public static Field<T> Create<T>(Name name, Type<T> type) => new(name, type);
        public static Func<Name, Func<Type<T>, Field<T>>> Create<T>() =>
            name => type => new(name, type);
    }

    public static Seq<Field<T>> Fields<T>(params IEnumerable<(Name Name, Type<T> Type)> fields) =>
        toSeq(fields.Select(pair => new Field<T>(pair.Name, pair.Type)));

    public static Type<T>.Tuple Tuple<T>(T attributes, params Seq<Type<T>> elementTypes) =>
        new(elementTypes) { Attributes = attributes };

    public static Type<TAttributes>.Variable Variable<TAttributes>(TAttributes attributes, Name name) =>
        new(name) { Attributes = attributes };

    public static Type<TAttributes>.Unit Unit<TAttributes>(TAttributes attributes) =>
        new() { Attributes = attributes };
}
