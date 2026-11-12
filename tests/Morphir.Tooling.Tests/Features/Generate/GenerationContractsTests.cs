using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.IR;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.Testing;
using Morphir.Tooling.Features.Generate;
using Morphir.Extensions;

namespace Morphir.Tooling.Tests.Features.Generate;

public class GenerationContractsTests
{
    [Test]
    public void RetainedEntryOwnsItsBytes()
    {
        byte[] bytes = [1, 2, 3];
        var entry = new RetainedEntry("module.ion", bytes);
        bytes[0] = 9;
        var copy = entry.CopyBytes();
        copy[1] = 8;
        entry.CopyBytes().Should().Equal(1, 2, 3);
    }

    [Test]
    public void BackendOptionsOutliveTheParseDocument()
    {
        BackendOptions options;
        using (var document = JsonDocument.Parse("false"))
            options = new BackendOptions(new Dictionary<string, JsonElement> { ["optimize"] = document.RootElement });
        options.Values["optimize"].GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task CSharpProviderConsumesAnAlreadyLoadedSemanticModel()
    {
        using var json = JsonDocument.Parse("""{"formatVersion":3,"distribution":["Library",[["example"]],[],{"modules":[]}]}""");
        var distribution = Morphir.Json.Classic.ClassicJson.readDistribution(json.RootElement).ResultValue.Distribution;
        var version = SourceFormatVersionModule.parse("3.1.0").ResultValue;
        var model = new VersionedModel(version, DistributionModel.NewClassic(distribution));
        var retained = new RetainedInput.File("model.json", new RetainedEntry("model.json", "{}"u8));
        var loaded = new LoadedModel(model, ModelDescriptor.From(model), ModelRepresentation.Json, retained);
        IGenerationBackend backend = new MemoryBackend();
        var options = ((Outcome<ValidatedBackendOptions>.Success)backend.ValidateOptions(BackendOptions.Empty)).Value;
        var request = new BackendGenerationRequest(loaded, TargetId.Create("memory"), options);
        var generated = (Outcome<GenerationArtifacts>.Success)await backend.GenerateAsync(request, CancellationToken.None);
        VFileTreeModule.fileCount(generated.Value.Tree).Should().Be(0);
        request.Model.Model.SourceVersion.Text.Should().Be("3.1.0");
    }

    [Test]
    [Category("PlatformSpecific")]
    public void FSharpConsumerCompilesAgainstTheSameContracts()
    {
        var references = ListModule.OfSeq(new[] { typeof(BackendOptions).Assembly.Location, typeof(VersionedModel).Assembly.Location, typeof(VFileTree).Assembly.Location, typeof(TargetId).Assembly.Location });
        var output = GenerationCompiler.executeWithReferences(references, FSharpList<Tuple<string, string>>.Empty, """
            open Morphir.Tooling.Features.Generate
            // Type-checked, not run: dotnet fsi already holds the SDK's FSharp.Core, so it cannot load the
            // F# Morphir.Extensions types, which use the repository's newer FSharp.Core. A compiled consumer can run it.
            let parse (text: string) : RequestedTarget = RequestedTarget.Parse text
            let target = RequestedTarget.Malformed "C#"
            let context = InvocationContext(System.IO.Directory.GetCurrentDirectory())
            let input = ModelInput(System.IO.Path.Combine(context.WorkingDirectory, "model.ion"), InputFormat.Ion)
            let settings = GenerationSettings(input, target, BackendOptions.Empty, PublicationPolicy("generated"), ModelReadLimits.Default)
            printfn "%s:%d" settings.Target.Text settings.Options.Values.Count
            """);
        output.Should().Be("C#:0");
    }

    private sealed record MemoryOptions : ValidatedBackendOptions;

    private sealed class MemoryBackend : IGenerationBackend
    {
        public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("test-memory"), ExtensionName.Create("Memory test backend"), TargetId.Create("memory"),
            [new BackendCapability(ModelFamily.Classic, SourceFormatVersions.Create("3.1.0"), DistributionKind.Library)]);
        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new MemoryOptions());
        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct) =>
            ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new GenerationArtifacts(VFileTreeModule.empty, [])));
    }
}
