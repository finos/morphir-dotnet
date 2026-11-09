// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Compilation;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// The compile command and its registries, for both hosts. It registers no frontend: a host adds frontends
/// with <c>TryAddEnumerable</c> registrations of <see cref="IFrontend"/>, and project-file providers with
/// registrations of <see cref="IProjectFileProvider"/> (ARCH-1). A configured frontend extension goes to the one
/// <see cref="IFrontendExtensionConnector"/>; a transport registers its connector before this scenario's default.
/// </summary>
public sealed class CompilationScenario : IMorphirCliScenario
{
    /// <inheritdoc />
    public void Configure(MorphirCliBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IIrWriter, ClassicIrWriter>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IIrWriter, V4IrWriter>());
            services.TryAddSingleton(provider => WriterRegistry.Create(provider.GetServices<IIrWriter>()));
            services.TryAddSingleton(provider => ProjectFileProviderRegistry.Create(provider.GetServices<IProjectFileProvider>()));
            services.TryAddSingleton<ICompileConfigurationLoader, FileSystemCompileConfigurationLoader>();
            services.TryAddSingleton<ICompileSettingsResolver, ProjectCompileSettingsResolver>();
            services.TryAddSingleton<IFrontendResolver, FrontendResolver>();
            services.TryAddSingleton<ISourceSelector, SourceSelector>();
            services.TryAddSingleton<ISourceCapture, FileSourceCapture>();
            services.TryAddSingleton<ICompilePublisher, OutRootCompilePublisher>();
            // The one extension seam (FE-2): a configured extension runs as a MEP 0.2.0-draft.1 guest process.
            services.TryAddSingleton<IFrontendExtensionConnector, MepFrontendExtensionConnector>();
            services.TryAddSingleton<ICompileRunner, CompileRunner>();
        });
        builder.AddCommand((IServiceProvider services) => new CompileCommand(services));
    }
}
