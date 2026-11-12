// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>config set</c> and <c>config unset</c>, and of the containment of an edit. The work area of the
/// workspace is the editing project, and its folder <c>home</c> is the home and the machine locations of the product.
/// </summary>
[Binding]
public sealed class ConfigurationEditingSteps(IConfigurationDriver configuration, IScenarioWorkspace workspace, ScenarioRun run)
{
    private static readonly string[] MachineAndPersonalRoles = ["System", "Global", "UserOverride"];

    private readonly Dictionary<string, string> _environment = [];
    private readonly string _original = "# keep comment\n[project]\nname='App'\n[custom]\nvalue = 'before' # keep inline\n";
    private WorkspaceDirectory? _workingDirectory;
    private ConfigurationReport? _report;
    private string? _lowerText;

    private WorkspaceDirectory Root => workspace.Work;

    private WorkspaceDirectory Home => Root.Directory("home");

    private WorkspaceDirectory Inner => Root.Directory("inner");

    private WorkspaceFile Target => Root.File("morphir.toml");

    private ConfigurationReport Report => _report ?? throw new InvalidOperationException("The scenario has no editing report before this step.");

    private string Output => run.Result.StandardOutput + run.Result.StandardError;

    private void Write(string relative, string text) => workspace.WriteText(Root.File(relative), text);

    [Given("a configuration editing project")]
    public void Project()
    {
        workspace.CreateDirectory(Home);
        workspace.WriteText(Target, _original);
    }

    [Given("the editing project has mixed primary layouts and a personal override")]
    public void MixedPersonalLayouts()
    {
        workspace.Delete(Target);
        Write("morphir.yaml", "project: {name: App}\ncustom: {value: lower}\n");
        Write(".morphir/morphir.toml", "[project]\nname='App'\n");
        Write("morphir.user.toml", "[custom]\nvalue='before'\n");
    }

    [Given("the editing project is a workspace with two members")]
    public void MemberWorkspace()
    {
        workspace.WriteText(Target, "[workspace]\nmembers=['a','b']\ndefault_member='a'\nout_dir='root-out'\n");
        foreach (var name in new[] { "a", "b" }) Write($"{name}/morphir.toml", "[project]\nname='" + name.ToUpperInvariant() + "'\n");
        Write("b/morphir.user.toml", "[custom]\nvalue='before'\n");
    }

    [Then("editing uses the existing personal target")]
    public void PersonalTarget()
    {
        Check(Report.Text("target", "path") == ReportPath.Join(Root.Path, "morphir.user.toml"), "The edit does not use the existing personal target.");
        Check(!workspace.Exists(Root.File(".morphir", "morphir.user.toml")), "The edit made a new personal target in .morphir.");
    }

    /// <summary>
    /// The parts of the old check that need only the command. The check of the owning workspace with the project
    /// system of the product stays in Morphir.Tooling.Tests.
    /// </summary>
    [Then("the personal edit is a member output of the personal file")]
    public void MemberOutput()
    {
        Check(Report.Text("target", "role") == "UserOverride", "The target of the edit is not the personal file.");
        Report.ShouldHaveDiagnostic("config.member_output");
        Check(workspace.ReadText(Root.File("b", "morphir.user.toml")).Contains("edited", StringComparison.Ordinal), "The personal file of member b has no edited value.");
    }

    [Given("an editing environment winner")]
    public void EnvironmentWinner() => _environment["MORPHIR_CUSTOM__VALUE"] = "environment";

    [Given("the editing target has an opaque reference")]
    public void Reference() => workspace.WriteText(Target, workspace.ReadText(Target) + "reference={env='never-print-reference-input'}\n");

    [Then("editing streams redact opaque reference inputs")]
    public void RedactedReference() => Check(!Output.Contains("never-print-reference-input", StringComparison.Ordinal), "The output has the reference input.");

    [Then("editing preserves unrelated comments")]
    public void Comments()
    {
        var text = workspace.ReadText(Target);
        Check(text.Contains("# keep comment", StringComparison.Ordinal) && text.Contains("# keep inline", StringComparison.Ordinal), "The edit removed a comment.");
    }

    [When("I edit configuration with {string}")]
    public Task Edit(string command) => Run(Words(command));

    private string Placeholder(string value) =>
        value.Replace("<root>", Root.Path, StringComparison.Ordinal).Replace("<inner>", Inner.Path, StringComparison.Ordinal);

    [Given("the editing project is a member below an outer workspace")]
    public void BelowOuterWorkspace()
    {
        workspace.WriteText(Target, "[workspace]\nmembers=['inner/app']\n[custom]\nouter='workspace'\n");
        workspace.WriteText(Inner.File("app", "morphir.toml"), "[project]\nname='App'\n[custom]\nvalue='before'\n");
        _workingDirectory = Inner.Directory("app");
    }

    [Given("the editing home has a global configuration")]
    public void GlobalConfiguration() => workspace.WriteText(Home.File("morphir", "morphir.toml"), "[custom]\nglobal='machine'\n");

    [Given("editing environment variable {word} is {string}")]
    public void EditingEnvironmentVariable(string name, string value) => _environment[name] = Placeholder(value);

    [When("I edit configuration without a boundary flag with {string}")]
    public Task EditWithoutBoundary(string command) => Run(Words(command), boundaryFlag: false);

    private IEnumerable<ConfigurationSource> PlanSources() => Report.Sources;

    [Then("the editing plan consults no project source outside {string}")]
    public void PlanStaysInside(string boundary)
    {
        var root = ReportPath.Normalize(Placeholder(boundary));
        var outside = PlanSources()
            .Where(source => source.Format != "memory" && !MachineAndPersonalRoles.Contains(source.Role))
            .Select(source => source.Path!)
            .Where(path => !ReportPath.Contains(root, path))
            .ToArray();
        Check(outside.Length == 0, $"Discovery must stay inside {root}, but the plan consulted: {string.Join(", ", outside)}.");
        Check(Report.Text("target", "path")?.StartsWith(root, StringComparison.Ordinal) == true, $"The target of the edit is not inside {root}.");
    }

    [Then("the editing plan consults no machine or personal sources")]
    public void PlanIsolated() =>
        Check(!PlanSources().Any(source => MachineAndPersonalRoles.Contains(source.Role)), "The plan consulted a machine or personal source.");

    private async Task Run(string[] arguments, bool boundaryFlag = true)
    {
        string[] command = [.. arguments, .. boundaryFlag ? ["--json", "--discovery-boundary", Root.Path] : new[] { "--json" }];
        run.Result = await configuration.ConfigureAsync(command, new ScenarioMachine(_workingDirectory ?? Root)
        {
            Home = Home,
            Variables = _environment,
        });
        _report = null;
    }

    [When("I set a complete typed object")]
    public Task TypedObject() => Run(["set", "custom.value", "{\"keep\":true,\"items\":[1,2]}", "--type", "object"]);

    [When("I set an opaque typed reference")]
    public Task TypedReference() => Run(["set", "custom.reference", "{\"env\":\"never-print-reference-input\"}", "--type", "object", "--dry-run"]);

    [When("I edit a literal dotted key")]
    public Task LiteralKey() => Run(["set", "codegen.\"custom.generator\".enabled", "false", "--type", "bool"]);

    /// <summary>
    /// Reads the edited key back with <c>config get</c> on the target file. The same check with the configuration
    /// reader of the product stays in Morphir.Tooling.Tests.
    /// </summary>
    [Then("the persisted editing value has type {string}")]
    public async Task PersistedType(string kind)
    {
        var edited = Report;
        var target = edited.Text("target", "path") ?? throw new CommandAssertionException("The editing report has no target path.", run.Result);
        var key = edited.Text("key") ?? throw new CommandAssertionException("The editing report has no key.", run.Result);
        var read = ConfigurationReport.Of(await configuration.ConfigureAsync(
            ["get", key, "--file", target, "--isolated", "--json", "--discovery-boundary", Root.Path], new ScenarioMachine(Root) { Home = Home }));
        var value = read.Required("value");
        var number = value.ValueKind == JsonValueKind.Number;
        // JSON writes the TOML float 2.0 as 2, so the TOML text of the key tells an integer from a float.
        var integer = number && IsTomlInteger(workspace.ReadText(Target), key.Split('.')[^1]);
        var matches = kind switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => integer,
            "number" => number && !integer,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Use string, boolean, integer, number, array or object."),
        };
        read.Check(matches, $"The persisted value of {key} is {value.GetRawText()}, not a value of the type {kind}.");
    }

    /// <summary>True when the one line of <paramref name="name"/> in the TOML text has an integer value.</summary>
    private bool IsTomlInteger(string toml, string name)
    {
        var values = System.Text.RegularExpressions.Regex.Matches(toml, $@"(?m)^\s*{System.Text.RegularExpressions.Regex.Escape(name)}\s*=\s*(?<value>[^#\r\n]+)")
            .Select(match => match.Groups["value"].Value.Trim().Replace("_", "", StringComparison.Ordinal))
            .ToArray();
        Check(values.Length == 1, $"The TOML text has {values.Length} lines for the key {name}, not one.");
        return System.Text.RegularExpressions.Regex.IsMatch(values[0], @"^[+-]?(\d+|0x[0-9a-fA-F]+|0o[0-7]+|0b[01]+)$");
    }

    [Given("a classic JSON value below the editing target")]
    public void LowerJson()
    {
        _lowerText = "{name:'App',exposedModules:[],custom:{value:'lower'}}";
        Write("morphir.json", _lowerText);
    }

    [Then("the lower JSON bytes and origin are retained")]
    public void LowerJsonResult()
    {
        Check(workspace.ReadText(Root.File("morphir.json")) == _lowerText, "The edit changed the classic JSON file.");
        Check(Report.Required("effective", "contributions").EnumerateArray().Any(c => c.GetProperty("winner").GetBoolean()
            && c.GetProperty("source").GetProperty("path").GetString()!.EndsWith("/morphir.json", StringComparison.Ordinal)),
            "The winner of the effective value is not the classic JSON file.");
    }

    [Then("system preview respects the native no-follow policy")]
    public void SystemPreview()
    {
        var linked = ScenarioMachine.SystemConfigurationRootIsALink;
        Result(linked ? 2 : 0, linked ? "failed" : "preview");
        if (linked) Report.ShouldHaveDiagnostic("config.edit_target");
        else Check(Report.Text("target", "role") == "System", "The target of the system preview is not the system file.");
    }

    [Then("editing exits {int} with outcome {string}")]
    public void Result(int exit, string outcome)
    {
        run.Result.ShouldExitWith(exit);
        _report = ConfigurationReport.Of(run.Result);
        Check(_report.Required("exitCode").GetInt32() == exit, $"The field exitCode of the editing report is not {exit}.");
        _report.ShouldHaveOutcome(outcome);
        _report.ShouldMatchSchema(workspace, "config-edit-result");
    }

    [Then("the editing target retains its original bytes")]
    public void Unchanged() => Check(workspace.ReadText(Target) == _original, "The edit changed the target.");

    [Then("the editing effective value is {string}")]
    public void Effective(string value) =>
        Check(Report.Text("effective", "value") == value, $"The effective value is {Report.Text("effective", "value") ?? "null"}, not {value}.");

    private static string[] Words(string command) => command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
