using FluentAssertions;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

[Category("PlatformSpecific")]
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
    [Arguments("line")]
    [Arguments("block")]
    [Arguments("quoted")]
    public async Task ContentDetectionRecognizesHeadersAfterLongComments(string spelling)
    {
        var path = GenerationInputFixtures.DailyRate(_root, "4.0.0", "text");
        var text = File.ReadAllText(path);
        text = spelling switch
        {
            "line" => "//" + new string('x', 1024) + "\n" + text,
            "block" => "/*" + new string('x', 1024) + "*/\n" + text,
            _ => text.Replace("morphir::", "'morphir'::")
        };
        File.WriteAllText(path, text);
        ((Outcome<LoadedModel>.Success)await Load(path)).Value.Representation.Should().Be(ModelRepresentation.IonText);
    }

    [Test]
    public async Task ExplicitJsonRefusesAnIonTreeWithoutSwitchingReaders()
    {
        var path = GenerationInputFixtures.DailyRate(_root, "4.0.0", "tree");
        var result = await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(path, InputFormat.Json), ModelReadLimits.Default, default);
        ((Outcome<LoadedModel>.Failure)result).Error.Diagnostics[0].Code.Should().Be("unsupported-format");
    }

    [Test]
    public async Task IonNodeBudgetCoversTheWholeTree()
    {
        var path = GenerationInputFixtures.DailyRate(_root, "4.0.0", "tree");
        var failure = ((Outcome<LoadedModel>.Failure)await Load(path, new(MaxNodes: 1))).Error;
        failure.Diagnostics[0].Code.Should().Contain("limit");
    }
    public void Dispose() => TolerantDirectory.TryDelete(_root);
}
