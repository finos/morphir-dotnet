using FluentAssertions;
using Morphir.IR;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

public class GenerationBackendResolverTests
{
    private static readonly ModelDescriptor Model = new(SourceFormatVersionModule.parse("4.0.0").ResultValue, ModelFamily.V4, DistributionKind.Library);

    [Test]
    public void ASecondTargetNeedsOnlyARegistration()
    {
        var first = new FakeBackend("fsharp-provider", "fsharp");
        var second = new FakeBackend("another-provider", "another");
        var resolver = new RegisteredBackendResolver([first, second]);
        ((Outcome<IGenerationBackend>.Success)resolver.Resolve(new("another"), Model)).Value.Should().BeSameAs(second);
        first.Invocations.Should().Be(0);
        second.Invocations.Should().Be(0);
    }

    [Test]
    public void UnknownTargetsListAvailableExactIds()
    {
        var provider = new FakeBackend("fsharp-provider", "fsharp");
        var failure = (Outcome<IGenerationBackend>.Failure)new RegisteredBackendResolver([provider]).Resolve(new("FSharp"), Model);
        failure.Error.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Error.Diagnostics[0].Code.Should().Be("unknown-target");
        failure.Error.Diagnostics[0].Message.Should().Contain("fsharp");
        provider.Invocations.Should().Be(0);
    }

    [Test]
    [Arguments("version")]
    [Arguments("kind")]
    [Arguments("family")]
    public void IncompatibleCapabilitiesRefuseWithoutInvokingAProvider(string dimension)
    {
        var model = dimension switch
        {
            "version" => Model with { Version = SourceFormatVersionModule.parse("4.1.0").ResultValue },
            "kind" => Model with { Kind = DistributionKind.Specs },
            _ => Model with { Family = ModelFamily.Classic }
        };
        var provider = new FakeBackend("fsharp-provider", "fsharp");
        var failure = (Outcome<IGenerationBackend>.Failure)new RegisteredBackendResolver([provider]).Resolve(new("fsharp"), model);
        failure.Error.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Error.Diagnostics[0].Code.Should().Be("incompatible-model");
        provider.Invocations.Should().Be(0);
    }

    [Test]
    public void AmbiguityNamesBothProvidersAndInvokesNeither()
    {
        var first = new FakeBackend("first", "fsharp");
        var second = new FakeBackend("second", "fsharp");
        var failure = (Outcome<IGenerationBackend>.Failure)new RegisteredBackendResolver([second, first]).Resolve(new("fsharp"), Model);
        failure.Error.Diagnostics[0].Code.Should().Be("ambiguous-backend");
        failure.Error.Diagnostics[0].Message.Should().Contain("first").And.Contain("second");
        first.Invocations.Should().Be(0);
        second.Invocations.Should().Be(0);
    }

    private sealed class FakeBackend(string identity, string target) : IGenerationBackend
    {
        public int Invocations { get; private set; }
        public BackendDescriptor Descriptor { get; } = new(identity, new(target), [new(ModelFamily.V4, ["4.0.0"], DistributionKind.Library)]);
        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) { Invocations++; throw new InvalidOperationException("Resolver must not invoke the provider"); }
        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct) { Invocations++; throw new InvalidOperationException("Resolver must not invoke the provider"); }
    }
}
