using FluentAssertions;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationIonLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-ion-load-" + Guid.NewGuid().ToString("N"));
    public GenerationIonLoaderTests() => Directory.CreateDirectory(_root);
    private static async Task<Outcome<LoadedModel>> Load(string path, ModelReadLimits? limits = null) =>
        await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(path), limits ?? ModelReadLimits.Default, default);

    [Test]
    [Arguments("3.0.0", "text")]
    [Arguments("3.0.0", "binary")]
    [Arguments("3.0.0", "tree")]
    [Arguments("3.1.0", "text")]
    [Arguments("3.1.0", "binary")]
    [Arguments("3.1.0", "tree")]
    [Arguments("4.0.0", "text")]
    [Arguments("4.0.0", "binary")]
    [Arguments("4.0.0", "tree")]
    public async Task ReadsNativeIonViewsWithExactSourceIdentity(string version, string format)
    {
        var input = GenerationInputFixtures.DailyRate(_root, version, format);
        var result = await Load(input);
        if (result is Outcome<LoadedModel>.Failure failure) throw new InvalidOperationException(string.Join("; ", failure.Error.Diagnostics));
        var model = ((Outcome<LoadedModel>.Success)result).Value;
        model.Model.SourceVersion.Text.Should().Be(version);
        model.Descriptor.Family.Should().Be(version.StartsWith('4') ? ModelFamily.V4 : ModelFamily.Classic);
        model.Representation.Should().Be(format switch { "text" => ModelRepresentation.IonText, "binary" => ModelRepresentation.IonBinary, _ => ModelRepresentation.IonTree });
    }

    [Test]
    public async Task RecognizedMalformedIonKeepsItsCodecFailure()
    {
        var path = Path.Combine(_root, "broken.ion"); File.WriteAllText(path, "morphir::{ broken");
        var failure = ((Outcome<LoadedModel>.Failure)await Load(path)).Error;
        failure.Diagnostics[0].Code.Should().NotBe("malformed-json").And.NotBe("unsupported-format");
        failure.Diagnostics[0].Path.Should().Be(path);
    }

    [Test]
    public async Task TreeDiagnosticsNameTheMemberAndItsLogicalLocation()
    {
        var path = GenerationInputFixtures.DailyRate(_root, "4.0.0", "tree");
        const string relative = "pkg/car/rental/inventory/daily-rate.value.ion";
        var file = Path.Combine(path, relative);
        File.WriteAllText(file, File.ReadAllText(file).Replace("5000", "true"));
        // A structurally invalid value must fail in the codec, independently of backend type checking.
        File.WriteAllText(file, File.ReadAllText(file).Replace("(\n", "(bogus "));
        var failure = ((Outcome<LoadedModel>.Failure)await Load(path)).Error;
        failure.Diagnostics[0].Path.Should().Be(relative);
        failure.Diagnostics[0].Location.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task IonNodeBudgetCoversTheWholeTree()
    {
        var path = GenerationInputFixtures.DailyRate(_root, "4.0.0", "tree");
        var failure = ((Outcome<LoadedModel>.Failure)await Load(path, new(MaxNodes: 1))).Error;
        failure.Diagnostics[0].Code.Should().Contain("limit");
    }
    public void Dispose() => Directory.Delete(_root, true);
}
