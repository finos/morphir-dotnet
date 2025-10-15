using Dunet;
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
    public partial record Function(Type<TAttrib> ParameterType, Type<Attribute> ReturnType);
    public partial record Unit;

}

public static class Type
{
    public record Field<TAttribute>(Name Name, Type<TAttribute> Type)
        : Morphir.IR.Type.Field<Type<TAttribute>>(Name, Type)
    {
    }
    
    public static Type<TAttributes>.Variable Variable<TAttributes>(TAttributes attributes, Name name) =>
        new Type<TAttributes>.Variable(name){ Attributes = attributes };
    
    public static Type<TAttributes>.Unit Unit<TAttributes>(TAttributes attributes) =>
        new Type<TAttributes>.Unit(){ Attributes = attributes };
}
