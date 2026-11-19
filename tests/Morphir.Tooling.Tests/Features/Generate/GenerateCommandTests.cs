using FluentAssertions;
using Morphir.Tooling.Tests.Integration.CLI;
using Morphir.Testing;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// The text output of <c>generate</c>, checked with the common corpus of the product (GenerationCases) in the test
/// process. The other cases of the command are in tests/Morphir.BlackBox.Tests/Integration/FSharpGeneration.feature.
/// </summary>
[Category("PlatformSpecific")]
public sealed class GenerateCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-command-" + Guid.NewGuid().ToString("N"));
    public GenerateCommandTests() => Directory.CreateDirectory(_root);

    [Test]
    public async Task HumanOutputReservesStdoutForArtifacts()
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4", "json");
        var output = Path.Combine(_root, "output");
        var response = await CliTestHelper.ForTarget().ExecuteCommandAsync(
            $"generate --input \"{input}\" --target fsharp --output \"{output}\"", timeout: TimeSpan.FromSeconds(30));
        response.ExitCode.Should().Be(0, response.CombinedOutput);
        response.StandardOutput.Should().Be("Inventory.fs");
        GenerationCases.checkDailyRate("4", "json", "file", [Tuple.Create("Inventory.fs", File.ReadAllText(Path.Combine(output, "Inventory.fs")))]);
    }

    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
