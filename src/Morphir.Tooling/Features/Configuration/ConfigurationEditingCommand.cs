// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Configuration;

/// <summary>Argument binding and presentation; all editing policy belongs to shared libraries.</summary>
public sealed class ConfigurationEditingCommand : Command, IMorphirParseErrorHandler
{
    private readonly IConfigurationEditingHost _host;
    private readonly bool _hostInitializationFailed;
    private readonly Argument<string> _key = new("key");
    private readonly Argument<string>? _value;
    private readonly Option<bool> _json = new("--json");
    private readonly Option<bool> _dryRun = new("--dry-run");
    private readonly Option<string> _type = new("--type") { Description = "Input type: string, bool, int, number, array or object" };
    private readonly Option<bool> _workspace = new("--workspace");
    private readonly Option<string?> _project = new("--project");
    private readonly Option<bool> _user = new("--user");
    private readonly Option<bool> _global = new("--global");
    private readonly Option<bool> _system = new("--system");
    private readonly Option<string?> _file = new("--file");
    private readonly Option<string?> _config = new("--config");
    private readonly Option<string[]> _profiles = new("--profiles") { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
    private readonly Option<bool> _noProfiles = new("--no-profiles");
    private readonly Option<bool> _isolated = new("--isolated");
    private readonly Option<string[]> _warnings = new("--suppress-warning") { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };

    public ConfigurationEditingCommand(string operation, IConfigurationEditingHost host, bool hostInitializationFailed = false)
        : base(operation, "Plan or apply one scoped TOML configuration edit")
    {
        _host = host; _hostInitializationFailed = hostInitializationFailed;
        Arguments.Add(_key);
        if (operation == "set") { _value = new("value"); Arguments.Add(_value); Options.Add(_type); }
        foreach (var option in new Option[] { _json, _dryRun, _workspace, _project, _user, _global, _system, _file,
                     _config, _boundary, _profiles, _noProfiles, _isolated, _warnings }) Options.Add(option);
        SetAction(Execute);
    }

    private readonly Option<string?> _boundary = new("--discovery-boundary");
    public int HandleParseErrors(ParseResult parsed) => Render(ConfigurationEditReport.Failure(Name, "config.usage"), parsed.Tokens.Any(t => t.Value is "--json" or "--json=true"));

    private int Execute(ParseResult parsed)
    {
        var json = parsed.GetValue(_json);
        var key = parsed.GetValue(_key)!;
        if (KeyPath.parse(key).IsError) return Render(ConfigurationEditReport.Failure(Name, "config.usage"), json);
        if (new[] { parsed.GetValue(_project), parsed.GetValue(_file), parsed.GetValue(_config), parsed.GetValue(_boundary) }
            .Any(value => value is not null && string.IsNullOrWhiteSpace(value)))
            return Render(ConfigurationEditReport.Failure(Name, "config.usage"), json);
        var selectors = new[] { parsed.GetValue(_workspace), parsed.GetValue(_project) is not null, parsed.GetValue(_user),
            parsed.GetValue(_global), parsed.GetValue(_system), parsed.GetValue(_file) is not null };
        if (selectors.Count(x => x) > 1 || parsed.GetValue(_file) is not null && parsed.GetValue(_config) is not null)
            return Render(ConfigurationEditReport.Failure(Name, "config.usage"), json);
        var kind = (parsed.GetValue(_type) ?? "string") switch
        {
            "string" => EditValueType.String,
            "bool" or "boolean" => EditValueType.Boolean,
            "int" or "integer" => EditValueType.Integer,
            "number" => EditValueType.Number,
            "array" => EditValueType.Array,
            "object" => EditValueType.Object,
            _ => null
        };
        if (kind is null || _value is not null && Editing.parseValue(kind, parsed.GetValue(_value)!).IsError)
            return Render(ConfigurationEditReport.Failure(Name, "config.usage"), json);
        if (parsed.GetValue(_file) is { } target && !target.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            return Render(ConfigurationEditReport.Failure(Name, "config.edit_format"), json);
        var environment = System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(p => p.Key is string && p.Value is string).ToDictionary(p => (string)p.Key, p => (string)p.Value!, StringComparer.Ordinal);
        var requested = parsed.GetValue(_profiles) ?? [];
        var disabled = parsed.GetValue(_noProfiles);
        if (disabled && requested.Length > 0) return Render(ConfigurationEditReport.Failure(Name, "config.usage"), json);
        var names = requested.SelectMany(p => p.Split(',')).Select(p => p.Trim([' ', '\t', '\n', '\r', '\v', '\f'])).ToArray();
        var profileSelection = disabled ? ProfileSelection.Disabled : requested.Length > 0 ? ProfileSelection.NewNames(ListModule.OfSeq(names)) : ProfileSelection.Automatic;
        var profiles = new ProfileOptions(profileSelection, DocumentDefaults.profileOptions.MaxDepth, DocumentDefaults.profileOptions.MaxExpansions);
        var preflight = Profiles.plan(FSharpList<SourceInput>.Empty, MapModule.OfSeq(environment.Select(p => Tuple.Create(p.Key, p.Value))), profiles);
        if (preflight.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return Render(ConfigurationEditReport.Failure(Name, "config.usage"), json);
        if (_hostInitializationFailed) return Render(ConfigurationEditReport.Failure(Name, "config.load"), json);
        var scope = parsed.GetValue(_workspace) ? QueryScope.Workspace : parsed.GetValue(_project) is not null ? QueryScope.Project :
            parsed.GetValue(_user) ? QueryScope.User : parsed.GetValue(_global) ? QueryScope.Global : parsed.GetValue(_system) ? QueryScope.System :
            parsed.GetValue(_file) is { } file ? QueryScope.NewFile(ProjectPath.join(System.Environment.CurrentDirectory, file)) : QueryScope.Effective;
        var invocation = new ConfigurationInvocation(scope, parsed.GetValue(_project), parsed.GetValue(_config), parsed.GetValue(_boundary), profiles,
            parsed.GetValue(_isolated), parsed.GetValue(_warnings) ?? [], environment);
        var request = new EditRequest(_value is null ? EditOperation.Unset : EditOperation.NewSet(parsed.GetValue(_value)!, kind), key);
        try { return Render(_host.Execute(invocation, request, parsed.GetValue(_dryRun)), json); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Render(ConfigurationEditReport.Failure(Name, "config.edit_io"), json); }
    }

    private static int Render(ConfigurationEditReport report, bool json)
    {
        foreach (var diagnostic in report.Diagnostics) Console.Error.WriteLine($"{diagnostic.Path ?? "config"}: {diagnostic.Code}: {diagnostic.Message}");
        if (json) ConsoleOutputHelper.WriteLineToStdout(report.ToJson());
        else
        {
            ConsoleOutputHelper.WriteLineToStdout($"{report.Outcome}\t{report.Target?.Path ?? "config"}\t{report.Key ?? ""}");
            if (report.Effective is { } effective)
            {
                ConsoleOutputHelper.WriteLineToStdout($"effective = {effective.Value?.GetRawText() ?? "<missing>"}");
                foreach (var contribution in effective.Contributions.Where(c => c.Winner))
                    ConsoleOutputHelper.WriteLineToStdout($"{contribution.Source.Role}\t{contribution.Source.Path}");
            }
            foreach (var artifact in report.RecoveryArtifacts) Console.Error.WriteLine($"recovery\t{artifact.Kind}\t{artifact.Path}");
        }
        return report.ExitCode;
    }
}
