using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationPublisherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-publisher-" + Guid.NewGuid().ToString("N"));
    public GenerationPublisherTests() => Directory.CreateDirectory(_root);
    private string Output => Path.Combine(_root, "output");
    private static GenerationArtifacts Artifacts(params (string Path, string Text)[] files)
    {
        var tree = VFileTreeModule.fromFiles(ListModule.OfSeq(files.Select(file => VFileModule.ofText(file.Path, file.Text))));
        return new(VFileTreeModule.set(VFileTreeModule.declaredOrder, ListModule.OfSeq(files.Select(file => file.Path)), tree), []);
    }
    private async Task<Outcome<GenerationPublication>> Publish(GenerationArtifacts artifacts, CancellationToken ct = default) =>
        await new GenerationPublisher().PublishAsync(artifacts, new(Path.Combine(_root, "source.json")), new(Output), ct);

    [Test]
    public async Task OwnershipManifestPreservesDeclaredOrderAndRepeatReceiptsDoNotRewrite()
    {
        var artifacts = Artifacts(("B.fs", "module B"), ("A.fs", "module A"));
        var first = ((Outcome<GenerationPublication>.Success)await Publish(artifacts)).Value;
        first.Artifacts.Should().Equal("B.fs", "A.fs");
        first.Written.Should().BeEquivalentTo("B.fs", "A.fs");
        var manifest = Path.Combine(Output, GenerationDefaults.OwnershipManifest);
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        json.RootElement.EnumerateArray().Select(value => value.GetString()).Should().Equal("B.fs", "A.fs");
        var unchangedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var path in Directory.GetFiles(Output)) File.SetLastWriteTimeUtc(path, unchangedAt);
        var repeated = ((Outcome<GenerationPublication>.Success)await Publish(artifacts)).Value;
        repeated.Written.Should().BeEmpty(); repeated.Removed.Should().BeEmpty();
        repeated.Unchanged.Should().BeEquivalentTo("B.fs", "A.fs");
        foreach (var path in Directory.GetFiles(Output)) File.GetLastWriteTimeUtc(path).Should().Be(unchangedAt);
    }

    [Test]
    public async Task CancellationBeforePublicationCreatesNoOutput()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        ((Outcome<GenerationPublication>.Failure)await Publish(Artifacts(("A.fs", "module A")), cancellation.Token)).Error.Kind.Should().Be(GenerationFailureKind.Cancelled);
        Directory.Exists(Output).Should().BeFalse();
    }
    public void Dispose() => Directory.Delete(_root, true);
}
