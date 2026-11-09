using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Validate;

namespace Morphir.Tooling.Scenarios;

/// <summary>The built-in analyzers and their registry entries, for both hosts.</summary>
public sealed class ValidationScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton<AnalyzerRegistry>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensionSource, AnalyzerExtensionSource>());
        });
}
