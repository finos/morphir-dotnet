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
    
}
