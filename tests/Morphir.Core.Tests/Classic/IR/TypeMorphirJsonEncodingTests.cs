using Morphir.IR;
using Path = Morphir.IR.Path;

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

    [Test]
    public async Task It_Should_Encode_Path_With_Single_Segment_Names()
    {
        var path = Path.FromList(
            Name.FromList("alpha"),
            Name.FromList("beta"),
            Name.FromList("gamma")
        );
        var actual = MorphirJson.EncodeAsString(path);
        var expected = """[["alpha"],["beta"],["gamma"]]""";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_Path_With_Multi_Segment_Names()
    {
        var path = Path.FromList(
            Name.FromList("alpha", "omega"),
            Name.FromList("beta", "delta"),
            Name.FromList("gamma")
        );
        var actual = MorphirJson.EncodeAsString(path);
        var expected = """[["alpha","omega"],["beta","delta"],["gamma"]]""";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_Empty_Path()
    {
        var path = Path.Empty;
        var actual = MorphirJson.EncodeAsString(path);
        var expected = "[]";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Decode_Path_With_Single_Segment_Names()
    {
        var json = """[["alpha"],["beta"],["gamma"]]""";
        var decoded = MorphirJson.DecodeFromString<Path>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Names.Count).IsEqualTo(3);
            await Assert.That(decoded.Names[0]).IsEqualTo(Name.FromList("alpha"));
            await Assert.That(decoded.Names[1]).IsEqualTo(Name.FromList("beta"));
            await Assert.That(decoded.Names[2]).IsEqualTo(Name.FromList("gamma"));
        }
    }

    [Test]
    public async Task It_Should_Decode_Path_With_Multi_Segment_Names()
    {
        var json = """[["alpha","omega"],["beta","delta"],["gamma"]]""";
        var decoded = MorphirJson.DecodeFromString<Path>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Names.Count).IsEqualTo(3);
            await Assert.That(decoded.Names[0]).IsEqualTo(Name.FromList("alpha", "omega"));
            await Assert.That(decoded.Names[1]).IsEqualTo(Name.FromList("beta", "delta"));
            await Assert.That(decoded.Names[2]).IsEqualTo(Name.FromList("gamma"));
        }
    }

    [Test]
    public async Task It_Should_Decode_Empty_Path()
    {
        var json = "[]";
        var decoded = MorphirJson.DecodeFromString<Path>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Names).IsEmpty();
            await Assert.That(decoded).IsEqualTo(Path.Empty);
        }
    }

}
