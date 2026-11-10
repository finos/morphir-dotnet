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
            // A selected backend extension (generate --extension, [codegen.<target>].extension) runs as a MEP guest process.
            services.TryAddSingleton<IBackendExtensionConnector, MepBackendExtensionConnector>();
            services.TryAddSingleton<IGenerationPublisher, GenerationPublisher>();
            // compile --target runs the same stages on the IR that it wrote (CLI-2).
            services.TryAddSingleton<Morphir.Tooling.Features.Compile.CompileGenerationStages>();
        });
        builder.AddCommand((IServiceProvider services) => new GenerateCommand(services));
    }
}
