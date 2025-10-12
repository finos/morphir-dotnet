using System.Text.Json;
using Morphir.IR;

namespace Morphir.Classic.IR;

public class ClassicNameTests
{
    [Test]
    public async Task Should_Be_Possible_To_Create()
    {
        ClassicName actual = new (["classic", "name"]);
        await Assert.That(actual).IsEqualTo(ClassicName.FromList(["classic", "name"]));

    }

    [Test]
    [Arguments<string[]>(["value","in","u","s","d"])]
    public async Task Should_Serialize_As_Expected_Json(IReadOnlyList<string> input)
    {
        var name  = ClassicName.FromList(input);
        var json = JsonSerializer.Serialize(name);
        Console.WriteLine("JSON: {0}", json);
        await VerifyJson(json).UseStrictJson();
    }

    [Test]
    [Arguments(new [] {"full", "name"}, "FullName")]
    [Arguments(new [] {"value", "in","u","s","d"}, "ValueInUSD")]
    public async Task Should_Convert_ToTitleCase(string[] input, string expected)
    {
        var name = ClassicName.FromList(input);
        await Assert.That(name.ToTitleCase()).IsEqualTo(expected);
    }
}
