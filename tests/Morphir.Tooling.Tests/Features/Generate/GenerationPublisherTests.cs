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
    [Arguments("same")]
    [Arguments("input-below-output")]
    [Arguments("output-below-input")]
    public async Task InputOutputOverlapCannotReplaceEarlierOutput(string relation)
    {
        await Publish(Artifacts(("prior.fs", "prior")));
        var input = relation switch { "same" => Output, "input-below-output" => Path.Combine(Output, "prior.fs"), _ => _root };
        var before = File.ReadAllBytes(Path.Combine(Output, GenerationDefaults.OwnershipManifest));
        var result = await new GenerationPublisher().PublishAsync(Artifacts(("next.fs", "next")), new(input), new(Output), default);
        ((Outcome<GenerationPublication>.Failure)result).Error.Diagnostics[0].Code.Should().Be("input-output-overlap");
        File.ReadAllText(Path.Combine(Output, "prior.fs")).Should().Be("prior");
        File.ReadAllBytes(Path.Combine(Output, GenerationDefaults.OwnershipManifest)).Should().Equal(before);
        File.Exists(Path.Combine(Output, "next.fs")).Should().BeFalse();
    }

    [Test]
    [Arguments("traversal")]
    [Arguments("rooted")]
    [Arguments("case-collision")]
    [Arguments("generation-errors")]
    public async Task UnsafeArtifactsPreserveFilesAndOwnership(string problem)
    {
        await Publish(Artifacts(("prior.fs", "prior")));
        var artifacts = problem switch
        {
            "traversal" => Artifacts(("../escape.fs", "bad")),
            "rooted" => Artifacts((Path.Combine(_root, "escape.fs"), "bad")),
            "case-collision" => Artifacts(("A.fs", "one"), ("a.fs", "two")),
            _ => Artifacts(("next.fs", "next"))
        };
        if (problem == "generation-errors") artifacts = artifacts with { Tree = VFileTreeModule.report(VMessageModule.create(MessageSeverity.Error, "refused"), artifacts.Tree) };
        var before = Directory.GetFiles(Output).ToDictionary(path => path, File.ReadAllText);
        (await Publish(artifacts)).Should().BeOfType<Outcome<GenerationPublication>.Failure>();
        Directory.GetFiles(Output).ToDictionary(path => path, File.ReadAllText).Should().Equal(before);
        File.Exists(Path.Combine(_root, "escape.fs")).Should().BeFalse();
    }

    [Test]
    public async Task OutputLinksIncludingAncestorsAndExistingLeafFilesAreRefused()
    {
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "linked"); Directory.CreateSymbolicLink(link, outside);
        var result = await new GenerationPublisher().PublishAsync(Artifacts(("A.fs", "module A")), new(Path.Combine(_root, "source.json")), new(Path.Combine(link, "output")), default);
        result.Should().BeOfType<Outcome<GenerationPublication>.Failure>();
        Directory.Exists(Path.Combine(outside, "output")).Should().BeFalse();
        Directory.Delete(link);
        Directory.CreateDirectory(Output);
        var original = Path.Combine(outside, "A.fs"); File.WriteAllText(original, "original");
        File.CreateSymbolicLink(Path.Combine(Output, "A.fs"), original);
        (await Publish(Artifacts(("A.fs", "replacement")))).Should().BeOfType<Outcome<GenerationPublication>.Failure>();
        File.ReadAllText(original).Should().Be("original");
        File.Delete(Path.Combine(Output, "A.fs"));
    }

    [Test]
    public async Task SiblingNamesWithACommonPrefixDoNotCountAsOverlap()
    {
        var input = Path.Combine(_root, "output-model.json"); File.WriteAllText(input, "{}");
        var result = await new GenerationPublisher().PublishAsync(Artifacts(("A.fs", "module A")), new(input), new(Output), default);
        result.Should().BeOfType<Outcome<GenerationPublication>.Success>();
    }

    [Test]
    public async Task InvalidFilesystemPathsReturnStructuredFailures()
    {
        var result = await new GenerationPublisher().PublishAsync(Artifacts(("A.fs", "module A")), new(Path.Combine(_root, "source.json")), new("\0invalid"), default);
        ((Outcome<GenerationPublication>.Failure)result).Error.Diagnostics[0].Code.Should().Be("publication-path");
        Directory.Exists(Output).Should().BeFalse();
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
