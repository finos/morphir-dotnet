// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// Shared by Morphir.Tests and Morphir.Managed.Tests (linked there), which cannot share one test project because the
// AOT-compatible and the managed entry points both build an assembly named morphir.
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling;

namespace Morphir.Tests.Hosts;

/// <summary>
/// What an entry point's real <c>CreateApp</c> composes: every service registration in order, the host identity it
/// registered, and the whole command tree with each symbol's aliases, description, hidden flag, arity and default.
/// </summary>
internal sealed record CompositionSnapshot(IReadOnlyList<string> Services, HostIdentity Host, IReadOnlyList<string> Commands)
{
    /// <summary>
    /// Builds an app through <paramref name="createApp"/>, which is the entry point's own <c>CreateApp</c> with an
    /// observer that runs after its composition, and reads the registrations and the command tree after the build.
    /// </summary>
    public static CompositionSnapshot Of(Func<Action<MorphirCliBuilder>, MorphirCliApp> createApp)
    {
        IServiceCollection? services = null;
        RootCommand? root = null;
        using var app = createApp(builder =>
        {
            services = builder.HostBuilder.Services;
            root = builder.RootCommand;
        });
        var host = app.Services.GetRequiredService<HostIdentity>();
        var commands = new List<string>();
        Describe(root!, "morphir", commands);
        return new([.. services!.Select(Describe)], host, commands);
    }

    private static string Describe(ServiceDescriptor descriptor)
    {
        var implementation = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType?.FullName ?? (descriptor.KeyedImplementationFactory is not null ? "factory" : $"instance {descriptor.KeyedImplementationInstance?.GetType().FullName}")
            : descriptor.ImplementationType?.FullName ?? (descriptor.ImplementationFactory is not null ? "factory" : $"instance {descriptor.ImplementationInstance?.GetType().FullName}");
        var key = descriptor.IsKeyedService ? $" [{descriptor.ServiceKey}]" : "";
        return $"{descriptor.Lifetime} {descriptor.ServiceType.FullName}{key} -> {implementation}";
    }

    private static void Describe(Command command, string path, List<string> lines)
    {
        lines.Add($"command {path} aliases=[{string.Join(' ', command.Aliases.Order(StringComparer.Ordinal))}] hidden={command.Hidden} description={command.Description}");
        foreach (var option in command.Options.OrderBy(option => option.Name, StringComparer.Ordinal))
            lines.Add($"  option {path} {option.Name} aliases=[{string.Join(' ', option.Aliases.Order(StringComparer.Ordinal))}] type={option.ValueType.FullName} " +
                $"arity={option.Arity.MinimumNumberOfValues}..{option.Arity.MaximumNumberOfValues} required={option.Required} hidden={option.Hidden} " +
                $"recursive={option.Recursive} default={(option.HasDefaultValue ? Text(option.GetDefaultValue()) : "none")} description={option.Description}");
        foreach (var argument in command.Arguments)
            lines.Add($"  argument {path} {argument.Name} type={argument.ValueType.FullName} arity={argument.Arity.MinimumNumberOfValues}..{argument.Arity.MaximumNumberOfValues} " +
                $"hidden={argument.Hidden} default={(argument.HasDefaultValue ? Text(argument.GetDefaultValue()) : "none")} description={argument.Description}");
        foreach (var subcommand in command.Subcommands.OrderBy(subcommand => subcommand.Name, StringComparer.Ordinal))
            Describe(subcommand, $"{path} {subcommand.Name}", lines);
    }

    private static string Text(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        System.Collections.IEnumerable items => $"[{string.Join(", ", items.Cast<object?>().Select(Text))}]",
        _ => value.ToString() ?? "",
    };
}
