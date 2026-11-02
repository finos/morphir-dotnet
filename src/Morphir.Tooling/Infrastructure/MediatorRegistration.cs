using System.Runtime.CompilerServices;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Morphir.Tooling.Infrastructure;

/// <summary>Uses generated handler registrations when runtime assembly enumeration is unavailable.</summary>
internal static class MediatorRegistration
{
    public static void Add(IServiceCollection services)
    {
        if (RuntimeFeature.IsDynamicCodeSupported) services.AddMediator();
        else AddStatic(services);
    }

    internal static void AddStatic(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IMediator))) return;
        var registry = new HandlerRegistry();
        Morphir_Tooling_MediatorHandlers.AddHandlers(services, registry);
        registry.Freeze();
        services.AddSingleton(registry);
        services.TryAddSingleton<INotificationPublisher, ForeachAwaitPublisher>();
        services.TryAddSingleton<IMediator, Mediator>();
        services.TryAddSingleton<IHandlerAuthorizationService, DefaultHandlerAuthorizationService>();
        services.TryAddSingleton<IAuthorizationContextProvider, DefaultAuthorizationContextProvider>();
    }
}
