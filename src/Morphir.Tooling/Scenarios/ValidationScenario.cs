using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Scenarios;

/// <summary>The built-in analyzers, their registry entries and <c>morphir validate</c>, for both hosts.</summary>
public sealed class ValidationScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton<AnalyzerRegistry>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensionSource, AnalyzerExtensionSource>());
            // The model loader of morphir generate, with its limits; TryAdd keeps the one GenerationScenario registers.
            services.TryAddSingleton<BoundedModelCapture>();
            services.TryAddSingleton<IModelLoader, GenerationModelLoader>();
            services.TryAddSingleton<IAnalyzerConfiguration>(_ => new LayeredAnalyzerConfiguration(Containment.CurrentEnvironment()));
            services.TryAddSingleton<ModelValidation>();
        });
        builder.AddCommand((IServiceProvider services) => new ValidateCommand(services));
    }
}
