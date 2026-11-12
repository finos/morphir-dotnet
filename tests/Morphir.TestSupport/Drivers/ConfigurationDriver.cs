// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The machine that a scenario of discovery and containment gives to the product. The workspace contains the
/// process as for each command, and then the product gets exactly the Morphir variables of the scenario: the
/// variables <c>MORPHIR_HOME</c>, <c>MORPHIR_DISCOVERY_BOUNDARY</c> and <c>MORPHIR_ISOLATED</c> of the workspace are
/// removed, because these scenarios test them.
/// </summary>
/// <param name="WorkingDirectory">The directory in which the command starts.</param>
public sealed record ScenarioMachine(WorkspaceDirectory WorkingDirectory)
{
    /// <summary>The variables of the workspace that the scenario controls: the product does not get them from the workspace.</summary>
    public static IReadOnlyList<string> ControlledVariables { get; } = ["MORPHIR_HOME", "MORPHIR_DISCOVERY_BOUNDARY", "MORPHIR_ISOLATED"];

    /// <summary>The variables that a <see cref="Home"/> sets: the home and the machine configuration locations.</summary>
    public static IReadOnlyList<string> HomeVariables { get; } = ["HOME", "USERPROFILE", "APPDATA", "PROGRAMDATA", "XDG_CONFIG_HOME"];

    /// <summary>
    /// True when the system configuration root of this machine (<c>/etc</c>) is a symbolic link, as on macOS. The
    /// product does not follow such a link for an edit, so a scenario of a system edit expects a refusal here.
    /// </summary>
    public static bool SystemConfigurationRootIsALink { get; } = !OperatingSystem.IsWindows() && new DirectoryInfo("/etc").LinkTarget is not null;

    /// <summary>The time limit of one command of a scenario of this kind.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The home and the machine configuration locations of the product. Null keeps those of the workspace.</summary>
    public WorkspaceDirectory? Home { get; init; }

    /// <summary>The variables of the scenario, for example <c>MORPHIR_ISOLATED</c>. They are set after <see cref="Home"/>.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>The options of a command in this machine.</summary>
    public CliRunOptions RunOptions()
    {
        var environment = ControlledVariables.ToDictionary(name => name, string? (_) => null);
        if (Home is { } home)
        {
            foreach (var name in HomeVariables) environment[name] = home.Path;
        }

        foreach (var (name, value) in Variables) environment[name] = value;
        return new CliRunOptions { Timeout = Timeout, WorkingDirectory = WorkingDirectory.Path, Environment = environment };
    }
}

/// <summary>The <c>config</c> command as an intent: inspect or edit the configuration of a project.</summary>
public interface IConfigurationDriver
{
    /// <summary>Runs <c>config</c> with <paramref name="arguments"/> (the operation and its options) in the machine of the scenario.</summary>
    Task<CommandResult> ConfigureAsync(IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>config</c> and gives it to the CLI driver.</summary>
public sealed class ConfigurationDriver(ICliDriver cli) : IConfigurationDriver
{
    public Task<CommandResult> ConfigureAsync(IReadOnlyList<string> arguments, ScenarioMachine machine, CancellationToken cancellation = default) =>
        cli.RunAsync(["config", .. arguments], machine.RunOptions(), cancellation);
}

/// <summary>
/// The form of a path in a configuration or migration report: absolute, with <c>/</c> on each platform, with no
/// <c>.</c> or <c>..</c> segment. A check compares paths in this form.
/// </summary>
public static class ReportPath
{
    /// <summary>The path in the form of a report.</summary>
    public static string Normalize(string path)
    {
        path = path.Replace('\\', '/');
        var (prefix, body) = path switch
        {
            _ when path.StartsWith("//", StringComparison.Ordinal) => ("//", path.TrimStart('/')),
            _ when path.StartsWith('/') => ("/", path.TrimStart('/')),
            _ when path.Length >= 3 && path[1] == ':' && path[2] == '/' => (path[..3], path[3..]),
            _ => ("", path),
        };

        var parts = new List<string>();
        foreach (var part in body.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return prefix + string.Join('/', parts);
    }

    /// <summary>The path of <paramref name="relative"/> below <paramref name="root"/>, in the form of a report.</summary>
    public static string Join(string root, string relative) => Normalize(root.Replace('\\', '/').TrimEnd('/') + "/" + relative);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or below it.</summary>
    public static bool Contains(string root, string path)
    {
        root = Normalize(root);
        path = Normalize(path);
        return path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
    }
}

/// <summary>
/// The JSON report of <c>config ... --json</c>: an inspection (<c>get</c>, <c>show</c>, <c>path</c>,
/// <c>explain</c>) or an edit (<c>set</c>, <c>unset</c>). Its <c>exitCode</c> must agree with the exit code of the
/// command.
/// </summary>
public sealed class ConfigurationReport : JsonCommandReport
{
    private ConfigurationReport(CommandResult result) : base(result, "configuration")
    {
    }

    /// <summary>The operation of an inspection, for example <c>get</c>.</summary>
    public string? Operation => Text("operation");

    /// <summary>The outcome of an edit: <c>written</c>, <c>preview</c>, <c>unchanged</c> or <c>failed</c>.</summary>
    public string? Outcome => Text("outcome");

    /// <summary>The sources that the inspection consulted, or the sources of the effective result of an edit.</summary>
    public IReadOnlyList<ConfigurationSource> Sources =>
    [
        .. (Member("sources") ?? Member("effective", "sources")) is { ValueKind: JsonValueKind.Array } sources
            ? sources.EnumerateArray().Select(ConfigurationSource.Of)
            : [],
    ];

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static ConfigurationReport Of(CommandResult result) => new(result);

    /// <summary>The outcome of an edit is <paramref name="outcome"/>.</summary>
    public ConfigurationReport ShouldHaveOutcome(string outcome)
    {
        Check(Outcome == outcome, $"The outcome of the configuration report is {Outcome ?? "null"}, not {outcome}.");
        return this;
    }
}

/// <summary>One source of a configuration report. <paramref name="Path"/> is in the form of <see cref="ReportPath"/>.</summary>
public sealed record ConfigurationSource(string? Path, string? Role, string? Format, string? Status)
{
    internal static ConfigurationSource Of(JsonElement item)
    {
        static string? Field(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var source = item.TryGetProperty("source", out var inner) ? inner : item;
        return new ConfigurationSource(Field(source, "path"), Field(source, "role"), Field(source, "format"), Field(item, "status"));
    }
}
