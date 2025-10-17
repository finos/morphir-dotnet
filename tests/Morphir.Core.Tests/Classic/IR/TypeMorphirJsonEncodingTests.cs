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

    [Test]
    public async Task It_Should_Encode_ModulePath()
    {
        var modulePath = ModulePath.FromList(
            Name.FromList("morphir"),
            Name.FromList("sdk"),
            Name.FromList("basics")
        );
        var actual = MorphirJson.EncodeAsString(modulePath);
        var expected = """[["morphir"],["sdk"],["basics"]]""";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Decode_ModulePath()
    {
        var json = """[["morphir"],["sdk"],["basics"]]""";
        var decoded = MorphirJson.DecodeFromString<ModulePath>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Names.Count).IsEqualTo(3);
            await Assert.That(decoded.Names[0]).IsEqualTo(Name.FromList("morphir"));
            await Assert.That(decoded.Names[1]).IsEqualTo(Name.FromList("sdk"));
            await Assert.That(decoded.Names[2]).IsEqualTo(Name.FromList("basics"));
        }
    }

    [Test]
    public async Task It_Should_Encode_PackageName()
    {
        var packageName = PackageName.FromList(
            Name.FromList("my"),
            Name.FromList("company"),
            Name.FromList("package")
        );
        var actual = MorphirJson.EncodeAsString(packageName);
        var expected = """[["my"],["company"],["package"]]""";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Decode_PackageName()
    {
        var json = """[["my"],["company"],["package"]]""";
        var decoded = MorphirJson.DecodeFromString<PackageName>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Names.Count).IsEqualTo(3);
            await Assert.That(decoded.Names[0]).IsEqualTo(Name.FromList("my"));
            await Assert.That(decoded.Names[1]).IsEqualTo(Name.FromList("company"));
            await Assert.That(decoded.Names[2]).IsEqualTo(Name.FromList("package"));
        }
    }

    [Test]
    public async Task It_Should_Encode_Empty_ModulePath()
    {
        var modulePath = ModulePath.Empty;
        var actual = MorphirJson.EncodeAsString(modulePath);
        var expected = "[]";
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_Empty_PackageName()
    {
        var packageName = PackageName.Empty;
        var actual = MorphirJson.EncodeAsString(packageName);
        var expected = "[]";
        await Assert.That(actual).IsEqualTo(expected);
    }

    #region FqName Tests

    [Test]
    [Arguments("morphir", "sdk", "basics", "int", """[[["morphir"]],[["sdk"],["basics"]],["int"]]""")]
    [Arguments("my", "company", "my.module", "myFunction", """[[["my"]],[["company"],["my","module"]],["my","function"]]""")]
    [Arguments("org", "example", "domain.core", "processData", """[[["org"]],[["example"],["domain","core"]],["process","data"]]""")]
    public async Task It_Should_Encode_FqName_With_Parameterized_Values(
        string packageNameInput,
        string modulePathInput1,
        string modulePathInput2,
        string localNameInput,
        string expected)
    {
        var packageName = PackageName.FromList(Name.FromString(packageNameInput));
        var modulePath = ModulePath.FromList(Name.FromString(modulePathInput1), Name.FromString(modulePathInput2));
        var localName = Name.FromString(localNameInput);

        var fqName = new FqName(packageName, modulePath, localName);
        var actual = MorphirJson.EncodeAsString(fqName);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_FqName_With_Single_Segment_Components()
    {
        var packageName = PackageName.FromList(Name.FromList("morphir"));
        var modulePath = ModulePath.FromList(Name.FromList("sdk"), Name.FromList("basics"));
        var localName = Name.FromList("int");

        var fqName = new FqName(packageName, modulePath, localName);
        var actual = MorphirJson.EncodeAsString(fqName);
        var expected = """[[["morphir"]],[["sdk"],["basics"]],["int"]]""";

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_FqName_With_Multi_Segment_Components()
    {
        var packageName = PackageName.FromList(Name.FromList("my", "company"));
        var modulePath = ModulePath.FromList(Name.FromList("core", "domain"), Name.FromList("utils"));
        var localName = Name.FromList("process", "data");

        var fqName = new FqName(packageName, modulePath, localName);
        var actual = MorphirJson.EncodeAsString(fqName);
        var expected = """[[["my","company"]],[["core","domain"],["utils"]],["process","data"]]""";

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task It_Should_Encode_FqName_With_Empty_Package_And_Module()
    {
        var packageName = PackageName.Empty;
        var modulePath = ModulePath.Empty;
        var localName = Name.FromList("standalone");

        var fqName = new FqName(packageName, modulePath, localName);
        var actual = MorphirJson.EncodeAsString(fqName);
        var expected = """[[],[],["standalone"]]""";

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""[[["morphir"]],[["sdk"],["basics"]],["int"]]""", "morphir", "sdk", "basics", "int")]
    [Arguments("""[[["my"]],[["company"],["my","module"]],["my","function"]]""", "my", "company", "my.module", "myFunction")]
    [Arguments("""[[["org"]],[["example"],["domain","core"]],["process","data"]]""", "org", "example", "domain.core", "processData")]
    public async Task It_Should_Decode_FqName_With_Parameterized_Values(
        string json,
        string expectedPackageName,
        string expectedModulePath1,
        string expectedModulePath2,
        string expectedLocalName)
    {
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.PackagePath.Names.Count).IsEqualTo(1);
            await Assert.That(decoded.PackagePath.Names[0]).IsEqualTo(Name.FromString(expectedPackageName));
            await Assert.That(decoded.ModulePath.Names.Count).IsEqualTo(2);
            await Assert.That(decoded.ModulePath.Names[0]).IsEqualTo(Name.FromString(expectedModulePath1));
            await Assert.That(decoded.ModulePath.Names[1]).IsEqualTo(Name.FromString(expectedModulePath2));
            await Assert.That(decoded.LocalName).IsEqualTo(Name.FromString(expectedLocalName));
        }
    }

    [Test]
    public async Task It_Should_Decode_FqName_With_Single_Segment_Components()
    {
        var json = """[[["morphir"]],[["sdk"],["basics"]],["int"]]""";
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.PackagePath.Names.Count).IsEqualTo(1);
            await Assert.That(decoded.PackagePath.Names[0]).IsEqualTo(Name.FromList("morphir"));
            await Assert.That(decoded.ModulePath.Names.Count).IsEqualTo(2);
            await Assert.That(decoded.ModulePath.Names[0]).IsEqualTo(Name.FromList("sdk"));
            await Assert.That(decoded.ModulePath.Names[1]).IsEqualTo(Name.FromList("basics"));
            await Assert.That(decoded.LocalName).IsEqualTo(Name.FromList("int"));
        }
    }

    [Test]
    public async Task It_Should_Decode_FqName_With_Multi_Segment_Components()
    {
        var json = """[[["my","company"]],[["core","domain"],["utils"]],["process","data"]]""";
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.PackagePath.Names.Count).IsEqualTo(1);
            await Assert.That(decoded.PackagePath.Names[0]).IsEqualTo(Name.FromList("my", "company"));
            await Assert.That(decoded.ModulePath.Names.Count).IsEqualTo(2);
            await Assert.That(decoded.ModulePath.Names[0]).IsEqualTo(Name.FromList("core", "domain"));
            await Assert.That(decoded.ModulePath.Names[1]).IsEqualTo(Name.FromList("utils"));
            await Assert.That(decoded.LocalName).IsEqualTo(Name.FromList("process", "data"));
        }
    }

    [Test]
    public async Task It_Should_Decode_FqName_With_Empty_Package_And_Module()
    {
        var json = """[[],[],["standalone"]]""";
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.PackagePath).IsEqualTo(PackageName.Empty);
            await Assert.That(decoded.ModulePath).IsEqualTo(ModulePath.Empty);
            await Assert.That(decoded.LocalName).IsEqualTo(Name.FromList("standalone"));
        }
    }

    [Test]
    [Arguments("morphir", "sdk", "basics", "int")]
    [Arguments("my", "company", "my.module", "myFunction")]
    [Arguments("org", "example", "domain.core", "processData")]
    public async Task It_Should_RoundTrip_FqName_Serialization(
        string packageNameInput,
        string modulePathInput1,
        string modulePathInput2,
        string localNameInput)
    {
        // Arrange
        var packageName = PackageName.FromList(Name.FromString(packageNameInput));
        var modulePath = ModulePath.FromList(Name.FromString(modulePathInput1), Name.FromString(modulePathInput2));
        var localName = Name.FromString(localNameInput);
        var original = new FqName(packageName, modulePath, localName);

        // Act
        var json = MorphirJson.EncodeAsString(original);
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded).IsEqualTo(original);
            await Assert.That(decoded!.PackagePath).IsEqualTo(original.PackagePath);
            await Assert.That(decoded.ModulePath).IsEqualTo(original.ModulePath);
            await Assert.That(decoded.LocalName).IsEqualTo(original.LocalName);
        }
    }

    [Test]
    public async Task It_Should_RoundTrip_FqName_With_Multi_Segment_Names()
    {
        // Arrange
        var packageName = PackageName.FromList(
            Name.FromList("my", "company", "name")
        );
        var modulePath = ModulePath.FromList(
            Name.FromList("core", "domain"),
            Name.FromList("business", "logic"),
            Name.FromList("utils")
        );
        var localName = Name.FromList("process", "customer", "data");
        var original = new FqName(packageName, modulePath, localName);

        // Act
        var json = MorphirJson.EncodeAsString(original);
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded).IsEqualTo(original);
            await Assert.That(decoded!.PackagePath.Names[0]).IsEqualTo(Name.FromList("my", "company", "name"));
            await Assert.That(decoded.ModulePath.Names[0]).IsEqualTo(Name.FromList("core", "domain"));
            await Assert.That(decoded.ModulePath.Names[1]).IsEqualTo(Name.FromList("business", "logic"));
            await Assert.That(decoded.ModulePath.Names[2]).IsEqualTo(Name.FromList("utils"));
            await Assert.That(decoded.LocalName).IsEqualTo(Name.FromList("process", "customer", "data"));
        }
    }

    [Test]
    public async Task It_Should_RoundTrip_FqName_With_Empty_Components()
    {
        // Arrange
        var original = new FqName(PackageName.Empty, ModulePath.Empty, Name.FromList("function"));

        // Act
        var json = MorphirJson.EncodeAsString(original);
        var decoded = MorphirJson.DecodeFromString<FqName>(json);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded).IsEqualTo(original);
            await Assert.That(decoded!.PackagePath).IsEqualTo(PackageName.Empty);
            await Assert.That(decoded.ModulePath).IsEqualTo(ModulePath.Empty);
            await Assert.That(decoded.LocalName).IsEqualTo(Name.FromList("function"));
        }
    }

    #endregion

}
