using FluentAssertions;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class StaticMediatorTests
{
    [Test]
    public async Task GeneratedRegistryDispatchesWithoutAssemblyEnumeration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        MediatorRegistration.AddStatic(services);
        services.AddSingleton<IGenerationSettingsResolver, CliGenerationSettingsResolver>();
        services.AddSingleton<BoundedModelCapture>();
        services.AddSingleton<IModelLoader, GenerationModelLoader>();
        services.AddSingleton<IGenerationBackendResolver>(_ => new RegisteredBackendResolver([]));
        services.AddSingleton<IGenerationPublisher, GenerationPublisher>();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<HandlerRegistry>().GetRegistrationsForMessageType(typeof(GenerateRequest)).Count.Should().Be(1);
        object request = new GenerateRequest(new(), new(Environment.CurrentDirectory));
        var outcome = await provider.GetRequiredService<IMediator>().InvokeAsync<Outcome<GenerationSuccess>>(request);
        ((Outcome<GenerationSuccess>.Failure)outcome).Error.Diagnostics[0].Code.Should().Be("missing-input");
    }
}
