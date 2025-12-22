using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Morphir.Tooling;

/// <summary>
/// Represents a provider that registers commands and their required services.
/// Use this for modular command registration in plugin-based scenarios.
/// </summary>
public interface ICommandProvider
{
    /// <summary>
    /// Registers commands with the root command and services with the DI container.
    /// </summary>
    /// <param name="rootCommand">The root command to add subcommands to</param>
    /// <param name="services">The service collection to register dependencies</param>
    void RegisterCommands(RootCommand rootCommand, IServiceCollection services);
}
