using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Generate;

using Morphir.TestSupport;
using Morphir.Extensions;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class CSharpGenerationPublicationTests : IDisposable
{
    public void Dispose() => TolerantDirectory.TryDelete(_root);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-csharp-publication-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(_root, "output");
    private string Input => Path.Combine(_root, "model.json");

    private async Task<GenerationArtifacts> Generate(string? naming = null)
    {
        var input = GenerationInputFixtures.DailyRate(_root, "4", "json");
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(input), ModelReadLimits.Default, default)).Value;
        var backend = new CSharpGenerationBackend();
        var options = naming is null ? BackendOptions.Empty : CSharpGenerationBackendTests.ParseOptions("namespace", System.Text.Json.JsonSerializer.Serialize(naming));
        var validated = CSharpGenerationBackendTests.Validated(backend, options);
        return ((Outcome<GenerationArtifacts>.Success)await backend.GenerateAsync(new(loaded, TargetId.Create("csharp"), validated), default)).Value;
    }

    private async Task<Outcome<GenerationPublication>> Publish(GenerationArtifacts artifacts, CancellationToken ct = default) =>
        await new GenerationPublisher().PublishAsync(artifacts, new(Input), new(Output), ct);

    private Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(Output, path), path => Convert.ToBase64String(File.ReadAllBytes(path)));

    [Test]
    [Arguments("../escape.cs")]
    [Arguments("rooted")]
    public async Task UnsafeArtifactPathsCannotReplaceRealCSharpOutput(string badPath)
    {
        var original = await Generate();
        (await Publish(original)).Should().BeOfType<Outcome<GenerationPublication>.Success>();
        File.WriteAllText(Path.Combine(Output, "User.cs"), "// user");
        var before = Snapshot();
        if (badPath == "rooted") badPath = Path.Combine(_root, "escape.cs");
        var next = await Generate("Changed.Namespace");
        var invalid = next with { Tree = VFileTreeModule.addFile(VFileModule.ofText(badPath, "// invalid extra artifact"), next.Tree) };
        var result = (Outcome<GenerationPublication>.Failure)await Publish(invalid);
        result.Error.Diagnostics[0].Code.Should().Be("invalid-artifacts");
        Snapshot().Should().BeEquivalentTo(before);
        File.Exists(Path.Combine(_root, "escape.cs")).Should().BeFalse();
    }

    [Test]
    [Arguments("same")]
    [Arguments("input-below-output")]
    [Arguments("output-below-input")]
    public async Task OverlapRefusalPreservesRealCSharpSources(string relation)
    {
        var original = await Generate();
        (await Publish(original)).Should().BeOfType<Outcome<GenerationPublication>.Success>();
        var before = Snapshot();
        var input = relation switch { "same" => Output, "input-below-output" => Path.Combine(Output, "input.json"), _ => _root };
        var result = await new GenerationPublisher().PublishAsync(await Generate("Changed.Namespace"), new(input), new(Output), default);
        ((Outcome<GenerationPublication>.Failure)result).Error.Diagnostics[0].Code.Should().Be("input-output-overlap");
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Test]
    public async Task StagingFailurePreservesThePreviousManifestAndSources()
    {
        (await Publish(await Generate())).Should().BeOfType<Outcome<GenerationPublication>.Success>();
        var before = Snapshot();
        var next = await Generate("Changed.Namespace");
        var broken = VFileModule.ofStream("Broken.cs", new StreamSource(FSharpFunc<Unit, Stream>.FromConverter(_ => throw new IOException("Owned test staging failure")), FSharpOption<long>.None));
        var tree = VFileTreeModule.addFile(broken, next.Tree);
        var order = VFileTreeModule.tryGet(VFileTreeModule.declaredOrder, tree).Value;
        tree = VFileTreeModule.set(VFileTreeModule.declaredOrder, ListModule.OfSeq(order.Append("Broken.cs")), tree);
        var result = (Outcome<GenerationPublication>.Failure)await Publish(next with { Tree = tree });
        result.Error.Kind.Should().Be(GenerationFailureKind.Operational);
        result.Error.Diagnostics[0].Code.Should().Be("publication-failed");
        Snapshot().Should().BeEquivalentTo(before);
    }

    [Test]
    public async Task CancellationBeforePublicationPreservesRealCSharpOutput()
    {
        (await Publish(await Generate())).Should().BeOfType<Outcome<GenerationPublication>.Success>();
        var before = Snapshot();
        var next = await Generate("Changed.Namespace");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var result = (Outcome<GenerationPublication>.Failure)await Publish(next, cancelled.Token);
        result.Error.Kind.Should().Be(GenerationFailureKind.Cancelled);
        Snapshot().Should().BeEquivalentTo(before);
    }
}
