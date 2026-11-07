using FluentAssertions;
using Morphir.Tooling.Features.Generate;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-profile-" + Guid.NewGuid().ToString("N"));
    public GenerationProfileTests() => Directory.CreateDirectory(_root);
    private static async Task<GenerationFailure> Refused(string path)
    {
        var outcome = await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(path), ModelReadLimits.Default, default);
        return ((Outcome<LoadedModel>.Failure)outcome).Error;
    }
    [Test]
    [Arguments("yaml")]
    [Arguments("yml")]
    [Arguments("unknown")]
    public async Task YamlModelReportsItsFormatBoundary(string extension)
    {
        var path = Path.Combine(_root, "model." + extension);
        File.WriteAllText(path, "formatVersion: 4\ndistribution: {}\n");
        var error = await Refused(path);
        error.Diagnostics[0].Code.Should().Be("unsupported-format");
        error.Diagnostics[0].Message.Should().Contain("YAML");
    }
    [Test]
    [Arguments("manifest.json")]
    [Arguments("manifest.yaml")]
    [Arguments("manifest.yml")]
    public async Task JsonAndYamlTreesReportTheirBoundary(string name)
    {
        File.WriteAllText(Path.Combine(_root, name), "{}");
        (await Refused(_root)).Diagnostics[0].Code.Should().Be("unsupported-format");
    }
    [Test]
    [Arguments("{\"envelopeVersion\":\"0.1.0\",\"payload\":{}}")]
    [Arguments("{\"morphir_model\":{\"ir\":{}}}")]
    [Arguments("{\"jsonrpc\":\"2.0\",\"method\":\"morphir.backend.generateModel\",\"params\":{}}")]
    public async Task MepJsonDoesNotDefaultToBareIr(string json)
    {
        var path = Path.Combine(_root, "envelope.json"); File.WriteAllText(path, json);
        (await Refused(path)).Diagnostics[0].Code.Should().Be("unsupported-profile");
    }
    [Test]
    [Arguments("ion")]
    [Arguments("ionb")]
    public async Task MorphirModelWrappersRefuseAdmissionInTextAndBinary(string extension)
    {
        var fixture = Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirIon", "mep", "fixtures", "rich-model." + extension);
        var error = await Refused(fixture);
        error.Diagnostics[0].Code.Should().Be("unsupported-profile");
        error.Diagnostics[0].Message.Should().Contain("admission");
    }
    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
