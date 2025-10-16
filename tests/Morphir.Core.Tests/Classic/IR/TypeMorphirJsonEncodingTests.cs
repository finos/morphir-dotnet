using Morphir.IR;

namespace Morphir.Classic.IR;

public class TypeMorphirJsonEncodingTests
{
    [Test]   
    [Arguments("MorphirEncoder","""["Variable",{},["morphir","encoder"]]""")]
    public async Task It_Should_Encode_A_Variable_Type_Appropriately(string variableNameInput, string expected)
    {
        var variableName = Name.FromString(variableNameInput);
        var actual = Type.Variable(Unit.Default, variableName);
        var encoded = MorphirJson.EncodeAsString(actual);
        await Assert.That(encoded).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_A_Unit_Type_Correctly()
    {
        var tpe = Type.Unit(Unit.Default);
        var encoded = MorphirJson.EncodeAsString(tpe);
        var expected = """["Unit",{}]""";
        await Assert.That(encoded).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_A_Tuple_Type_Correctly()
    {
        // Tuple with two element types: Unit and a Variable named "x"
        Type<Unit> elem1 = Type.Unit(Unit.Default);
        Type<Unit> elem2 = Type.Variable(Unit.Default, Name.FromString("x"));
        var elements = new Seq<Type<Unit>>(new[] { elem1, elem2 });
        var tuple = new Type<Unit>.Tuple(elements) { Attributes = Unit.Default };

        var encoded = MorphirJson.EncodeAsString(tuple);
        var expected = """["Tuple",{},[["Unit",{}],["Variable",{},["x"]]]]""";
        await Assert.That(encoded).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Decode_A_Tuple_Type_Correctly()
    {
        var json = """["Tuple",{},[["Unit",{}],["Variable",{},["x"]]]]""";
        var decoded = MorphirJson.DecodeFromString<Type<Unit>>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded).IsTypeOf<Type<Unit>.Tuple>();

            var tuple = (Type<Unit>.Tuple)decoded!;
            await Assert.That(tuple.ElementTypes.Count).IsEqualTo(2);
            await Assert.That(tuple.ElementTypes[0]).IsTypeOf<Type<Unit>.Unit>();
            await Assert.That(tuple.ElementTypes[1]).IsTypeOf<Type<Unit>.Variable>();

            var variable = (Type<Unit>.Variable)tuple.ElementTypes[1];
            await Assert.That(variable.Name).IsEqualTo(Name.FromString("x"));
        }
    }

    [Test]
    public async Task It_Should_Encode_Field_Correctly()
    {
        var sut = Type.Field.Create(Name.FromString("UnitField"), Type.Unit(Unit.Default));
        var actual = MorphirJson.EncodeAsString(sut);
        var expected = """{"name":["unit","field"],"tpe":["Unit",{}]}""";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Decode_Field_Correctly()
    {
        var json = """{"name":["unit","field"],"tpe":["Unit",{}]}""";
        var decoded = MorphirJson.DecodeFromString<Type.Field<Unit>>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Name).IsEqualTo(Name.FromString("UnitField"));
            await Assert.That(decoded.Type).IsTypeOf<Type<Unit>.Unit>();
        }
    }

}
