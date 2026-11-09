using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Scenarios;

/// <summary>Static generation services for the existing CLI host and mediator.</summary>
public sealed class GenerationScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Without --input, generate reads the IR of the most recent compile (REC-1).
            services.TryAddSingleton<IGenerationSettingsResolver>(provider => new RecordGenerationSettingsResolver(
                new CliGenerationSettingsResolver(),
                provider.GetService<ICompileConfigurationLoader>() ?? new FileSystemCompileConfigurationLoader()));
            services.TryAddSingleton<BoundedModelCapture>();
            services.TryAddSingleton<IModelLoader, GenerationModelLoader>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IGenerationBackend, FSharpGenerationBackend>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IGenerationBackend, CSharpGenerationBackend>());
            services.TryAddSingleton<IGenerationBackendResolver, RegisteredBackendResolver>();
            services.TryAddSingleton<IGenerationPublisher, GenerationPublisher>();
        });
        builder.AddCommand((IServiceProvider services) => new GenerateCommand(services));
    }
}
