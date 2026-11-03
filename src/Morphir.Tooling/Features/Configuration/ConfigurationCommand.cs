// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Configuration;

/// <summary>Registers inspection commands without reading configuration or constructing a resolver.</summary>
public sealed class ConfigurationCommandProvider : ICommandProvider, IMorphirHostInitializationErrorHandler
{
    private readonly IConfigurationInspectionHost _host;
    private bool _hostInitializationFailed;
    public ConfigurationCommandProvider() : this(new NativeConfigurationInspectionHost()) { }
    public ConfigurationCommandProvider(IConfigurationInspectionHost host) => _host = host;

    void IMorphirHostInitializationErrorHandler.HandleHostInitializationFailure() => _hostInitializationFailed = true;

    public void RegisterCommands(RootCommand rootCommand, IServiceCollection services)
    {
        var config = new ConfigurationCommand("config", _host, _hostInitializationFailed);
        foreach (var name in new[] { "get", "list", "show", "path", "explain" })
            config.Subcommands.Add(new ConfigurationCommand(name, _host, _hostInitializationFailed));
        rootCommand.Subcommands.Add(config);
    }
}

/// <summary>Argument binding and presentation over host-independent query services.</summary>
public sealed class ConfigurationCommand : Command, IMorphirParseErrorHandler
{
    private readonly IConfigurationInspectionHost _host;
    private readonly bool _hostInitializationFailed;
    private readonly Argument<string>? _key;
    private readonly Option<bool> _json = new("--json") { Description = "Emit one structured JSON result" };
    private readonly Option<bool> _origin = new("--show-origin") { Description = "Include winning source paths and positions" };
    private readonly Option<bool> _scope = new("--show-scope") { Description = "Include winning source roles" };
    private readonly Option<bool> _workspace = new("--workspace") { Description = "Inspect workspace primary sources only" };
    private readonly Option<string?> _project = new("--project") { Description = "Inspect one project primary by name or workspace-relative path" };
    private readonly Option<bool> _user = new("--user") { Description = "Inspect adjacent personal sources only" };
    private readonly Option<bool> _global = new("--global") { Description = "Inspect global sources only" };
    private readonly Option<bool> _system = new("--system") { Description = "Inspect system sources only" };
    private readonly Option<string?> _file = new("--file") { Description = "Inspect one explicit file only" };
    private readonly Option<string?> _config = new("--config") { Description = "Overlay an explicitly selected configuration file" };
    private readonly Option<string?> _boundary = new("--discovery-boundary") { Description = "Bound discovery to this directory and its descendants" };
    private readonly Option<string[]> _profiles = new("--profiles") { Description = "Comma-separated profile names; repeatable in order", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
    private readonly Option<bool> _noProfiles = new("--no-profiles") { Description = "Disable profile loading" };
    private readonly Option<bool> _isolated = new("--isolated") { Description = "Exclude machine and personal files" };
    private readonly Option<string[]> _warnings = new("--suppress-warning") { Description = "Suppress a supported warning code; repeatable", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };

    public ConfigurationCommand(string operation, IConfigurationInspectionHost host) : this(operation, host, false) { }

    internal ConfigurationCommand(string operation, IConfigurationInspectionHost host, bool hostInitializationFailed) : base(operation,
        operation == "config" ? "Inspect Morphir configuration" : "Inspect " + operation + " configuration")
    {
        _host = host;
        _hostInitializationFailed = hostInitializationFailed;
        if (operation is "get" or "explain")
        {
            _key = new Argument<string>("key") { Description = "Case-sensitive dotted path; JSON-quoted segments allow literal dots" };
            Arguments.Add(_key);
        }
        foreach (var option in new Option[] { _json, _origin, _scope, _workspace, _project, _user, _global, _system, _file,
                     _config, _boundary, _profiles, _noProfiles, _isolated, _warnings }) Options.Add(option);
        SetAction(Execute);
    }

    public int HandleParseErrors(ParseResult result) => Render(ConfigurationReport.Failure(Name, "config.usage", "Invalid configuration command arguments."), WantsJson(result));

    private static bool WantsJson(ParseResult result) => result.Tokens.Any(t => t.Value is "--json" or "--json=true");

    private int Execute(ParseResult parsed)
    {
        var json = parsed.GetValue(_json);
        ConfigurationReport Failure(string message) => ConfigurationReport.Failure(Name, "config.usage", message);
        if (new[] { parsed.GetValue(_project), parsed.GetValue(_file), parsed.GetValue(_config), parsed.GetValue(_boundary) }
            .Any(value => value is not null && string.IsNullOrWhiteSpace(value)))
            return Render(Failure("Project, file and boundary selectors require a nonempty value."), json);
        var selectors = new[] { parsed.GetValue(_workspace), parsed.GetValue(_project) is not null, parsed.GetValue(_user),
            parsed.GetValue(_global), parsed.GetValue(_system), parsed.GetValue(_file) is not null };
        if (selectors.Count(x => x) > 1) return Render(Failure("Scope selectors are mutually exclusive."), json);
        if (parsed.GetValue(_file) is not null && parsed.GetValue(_config) is not null)
            return Render(Failure("--file and --config cannot be combined."), json);
        if (Name == "config") return Render(Failure("Specify get, list, show, path or explain."), json);
        FSharpList<string> key = FSharpList<string>.Empty;
        if (_key is not null)
        {
            var path = KeyPath.parse(parsed.GetValue(_key)!);
            if (path.IsError) return Render(Failure("Malformed key path. Use dot separators and JSON-quoted literal segments."), json);
            key = path.ResultValue;
        }
        var environment = System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(p => p.Key is string && p.Value is string).ToDictionary(p => (string)p.Key, p => (string)p.Value!, StringComparer.Ordinal);
        var requested = parsed.GetValue(_profiles) ?? [];
        var disabled = parsed.GetValue(_noProfiles);
        if (disabled && requested.Length > 0) return Render(Failure("--profiles conflicts with --no-profiles."), json);
        var names = requested.SelectMany(p => p.Split(',')).Select(p => p.Trim([' ', '\t', '\n', '\r', '\v', '\f'])).ToArray();
        var profileSelection = disabled ? ProfileSelection.Disabled : requested.Length > 0 ? ProfileSelection.NewNames(ListModule.OfSeq(names)) : ProfileSelection.Automatic;
        var profileOptions = new ProfileOptions(profileSelection, DocumentDefaults.profileOptions.MaxDepth, DocumentDefaults.profileOptions.MaxExpansions);
        // Validate the exact shared control grammar before any configuration path can be observed.
        var preflight = Profiles.plan(FSharpList<SourceInput>.Empty,
            MapModule.OfSeq(environment.Select(p => Tuple.Create(p.Key, p.Value))), profileOptions);
        if (preflight.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return Render(Failure("Invalid profile controls. Names must match [a-z][a-z0-9_-]{0,63}."), json);
        if (_hostInitializationFailed)
            return Render(ConfigurationReport.Failure(Name, "config.load", "Host configuration initialization failed. Check appsettings and access permissions."), json);
        var scope = parsed.GetValue(_workspace) ? QueryScope.Workspace : parsed.GetValue(_project) is not null ? QueryScope.Project :
            parsed.GetValue(_user) ? QueryScope.User : parsed.GetValue(_global) ? QueryScope.Global : parsed.GetValue(_system) ? QueryScope.System :
            parsed.GetValue(_file) is { } file ? QueryScope.NewFile(ProjectPath.join(System.Environment.CurrentDirectory, file)) : QueryScope.Effective;
        var invocation = new ConfigurationInvocation(scope, parsed.GetValue(_project), parsed.GetValue(_file) ?? parsed.GetValue(_config),
            parsed.GetValue(_boundary), profileOptions, parsed.GetValue(_isolated), parsed.GetValue(_warnings) ?? [], environment);
        try
        {
            var detailed = _host.Load(invocation);
            var result = Name switch
            {
                "get" => Inspection.get(scope, key, detailed),
                "list" => Inspection.list(scope, detailed),
                "show" => Inspection.show(scope, detailed),
                "path" => Inspection.path(scope, detailed),
                "explain" => Inspection.explain(scope, key, detailed),
                _ => throw new InvalidOperationException("Unknown configuration read command")
            };
            return Render(ConfigurationReport.From(result, parsed.GetValue(_origin), parsed.GetValue(_scope)), json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Render(ConfigurationReport.Failure(Name, "config.load", "Configuration loading failed. Check paths and access permissions."), json);
        }
    }

    private static int Render(ConfigurationReport report, bool json)
    {
        foreach (var diagnostic in report.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Path ?? "config"}: {diagnostic.Code}: {diagnostic.Message}");
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
        else if (report.ExitCode != 2)
        {
            if (report.Operation is "get" or "show")
            {
                if (report.Found && report.Value is { } value) ConsoleOutputHelper.WriteLineToStdout(value.GetRawText());
                foreach (var entry in report.Entries)
                    foreach (var origin in entry.Origins)
                        ConsoleOutputHelper.WriteLineToStdout($"{origin.Key}\t{origin.Source.Role}\t{origin.Source.Path}");
            }
            else if (report.Operation == "list")
                foreach (var entry in report.Entries)
                {
                    var origins = string.Join("; ", entry.Origins.Select(o => $"{o.Source.Role} {o.Source.Path}".Trim()));
                    ConsoleOutputHelper.WriteLineToStdout($"{entry.Key} = {entry.Value.GetRawText()}{(origins.Length > 0 ? "\t" + origins : "")}");
                }
            else if (report.Operation == "path")
                foreach (var source in report.Sources)
                    ConsoleOutputHelper.WriteLineToStdout($"{source.Source.Role}\t{source.Status}\t{source.Source.Path}");
            else
                foreach (var contribution in report.Contributions)
                    ConsoleOutputHelper.WriteLineToStdout($"{(contribution.Winner ? "*" : " ")} {contribution.Source.Role}\t{contribution.Source.Path}\t{contribution.Value?.GetRawText() ?? "<removed>"}");
        }
        return report.ExitCode;
    }
}
