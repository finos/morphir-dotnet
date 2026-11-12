// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>config</c> inspection (<c>get</c>, <c>show</c>, <c>path</c>, <c>explain</c>) and of the
/// containment of its discovery. The work area of the workspace is the root of the inspection workspace, and the
/// home and the machine locations of the product are that root too, so that a scenario controls each source.
/// </summary>
[Binding]
public sealed class ConfigurationInspectionSteps(IConfigurationDriver configuration, IScenarioWorkspace workspace, ScenarioRun run)
{
    private static readonly string[] MachineAndPersonalRoles = ["System", "Global", "UserOverride"];

    private readonly Dictionary<string, string> _variables = [];
    private WorkspaceDirectory? _workingDirectory;
    private ConfigurationReport? _report;

    private WorkspaceDirectory Root => workspace.Work;

    /// <summary>A directory outside the root of the inspection workspace.</summary>
    private WorkspaceDirectory Outside => workspace.Temp.Directory("outside");

    private WorkspaceDirectory GlobalHome => Root.Directory(".morphir");

    private WorkspaceDirectory BareProject => Root.Directory("work", "proj");

    private ConfigurationReport Report => _report ?? throw new InvalidOperationException("The scenario has no inspection report before this step.");

    private JsonElement Json => Report.Document;

    private string Output => run.Result.StandardOutput + run.Result.StandardError;

    private void Write(string relative, string text) => workspace.WriteText(Root.File(relative), text);

    [Given("an inspection workspace with a member and a personal override")]
    public void Workspace()
    {
        Write("morphir/morphir.toml", "[custom]\nmachine='global'\n");
        Write("morphir.toml", "[workspace]\nmembers=['app']\n[custom]\nwinner='workspace'\nworkspace_only='root'\nselected='workspace'\n");
        Write("app/morphir.toml", "[project]\nname='App'\n[custom]\nwinner='member'\nmember='only member'\nempty=''\nempty_array=[]\npassword='never-print-password'\n[custom.reference]\ncommand=['never-execute-this-command']\n[codegen.\"custom.generator\"]\nenabled=true\n");
        Write("app/morphir.user.toml", "[custom]\nwinner='personal'\n");
        Write("app/morphir.profile.ci.toml", "[custom]\nwinner='profile'\n");
        Write("app/src/selected.toml", "[custom]\nwinner='selected'\nselected='selected'\n");
    }

    [Given("the inspection workspace contains sibling YAML")]
    public void Yaml() => Write("morphir.yaml", "custom:\n  yaml_only: true\n");

    [Given("the inspection workspace has an invalid default member")]
    public void InvalidDefaultMember()
    {
        var file = Root.File("morphir.toml");
        workspace.WriteText(file, workspace.ReadText(file).Replace("members=['app']", "members=['app']\ndefault_member='missing'", StringComparison.Ordinal));
    }

    [Given("the inspection member configuration is malformed")]
    public void Malformed() => Write("app/morphir.toml", "[broken never-print-password");

    [Given("the invocation directory contains malformed host appsettings")]
    public void HostSettings() => Write("app/src/appsettings.json", "{not-valid-json");

    [Given("environment profiles contain an empty name")]
    public void InvalidEnvironmentProfile() => _variables["MORPHIR_PROFILES"] = "ci,,metrics";

    [Given("the member contains a TOML table with {int} path segments")]
    public void DeepTable(int depth) => Write("app/morphir.toml",
        "[project]\nname='App'\n[" + string.Join('.', Enumerable.Repeat("a", depth)) + "]\nx=1\npassword='never-print-deep-secret'\n");

    [Then("the nested table at {int} path segments retains its values")]
    public void DeepTableResult(int depth)
    {
        var leaf = Json.GetProperty("value");
        for (var level = 0; level < depth; level++) leaf = leaf.GetProperty("a");
        Check(leaf.GetProperty("x").GetInt64() == 1, "The nested table has no x = 1.");
        Check(leaf.GetProperty("password").GetString() == "[REDACTED]", "The nested secret is not redacted.");
        Check(!Output.Contains("never-print-deep-secret", StringComparison.Ordinal), "The output has the nested secret.");
    }

    [Given("the member contains nonfinite and temporal values")]
    public void SpecialNumbers() => Write("app/morphir.toml", """
        [project]
        name = "App"
        [custom]
        nan = nan
        positive = inf
        negative = -inf
        finite = 4.5
        literal = { "$number" = "NaN" }
        date = 2026-11-03
        secret_nan = nan
        nested = [nan, { number = inf }]
        "a/b~c" = nan
        """);

    private string Placeholder(string value) => value
        .Replace("<workspace>", Root.Path, StringComparison.Ordinal)
        .Replace("<member>", Root.Directory("app").Path, StringComparison.Ordinal)
        .Replace("<outside>", Outside.Path, StringComparison.Ordinal);

    [Given("environment variable {word} is {string}")]
    public void EnvironmentVariable(string name, string value)
    {
        if (value.Contains("<outside>", StringComparison.Ordinal)) workspace.CreateDirectory(Outside);
        _variables[name] = Placeholder(value);
    }

    [When("I inspect without a boundary flag with {string}")]
    public Task InspectWithoutBoundary(string command) => Run(["config", .. Words(command), "--json"]);

    [Then("inspection consults no project source outside {string}")]
    public void NoProjectSourceOutside(string boundary)
    {
        var root = ReportPath.Normalize(Placeholder(boundary));
        var outside = Report.Sources
            .Where(source => source.Format != "memory" && !MachineAndPersonalRoles.Contains(source.Role))
            .Select(source => source.Path!)
            .Where(path => !ReportPath.Contains(root, path))
            .ToArray();
        Check(outside.Length == 0, $"Discovery must stay inside {root}, but it consulted: {string.Join(", ", outside)}.");
    }

    [Then("inspection consults the workspace primary")]
    public void ConsultsWorkspacePrimary() =>
        Check(Report.Sources.Any(source => source.Path == ReportPath.Join(Root.Path, "morphir.toml")), "The inspection did not consult the workspace primary.");

    [Then("inspection consults no sources")]
    public void NoSources() => Check(Json.GetProperty("sources").GetArrayLength() == 0, "The inspection consulted sources.");

    [Then("machine and personal sources are not consulted")]
    public void MachineNotConsulted() =>
        Check(!Report.Sources.Any(source => MachineAndPersonalRoles.Contains(source.Role)), "The inspection consulted a machine or personal source.");

    [Then("machine and personal sources are consulted")]
    public void MachineConsulted()
    {
        var roles = Report.Sources.Select(source => source.Role).ToHashSet();
        Check(MachineAndPersonalRoles.All(roles.Contains), $"The inspection did not consult each of {string.Join(", ", MachineAndPersonalRoles)}.");
    }

    [Then("the reported boundary is {string} from {string}")]
    public void ReportedBoundary(string boundary, string source)
    {
        var containment = Json.GetProperty("containment");
        Check(containment.GetProperty("boundary").GetString() == ReportPath.Normalize(Placeholder(boundary)), "The reported boundary is another directory.");
        Check(containment.GetProperty("boundarySource").GetString() == source, $"The boundary does not come from {source}.");
    }

    [Then("the reported isolation is {word} from {string}")]
    public void ReportedIsolation(string isolated, string source)
    {
        var containment = Json.GetProperty("containment");
        Check(containment.GetProperty("isolated").GetBoolean() == bool.Parse(isolated), $"The reported isolation is not {isolated}.");
        Check(containment.GetProperty("isolationSource").GetString() == source, $"The isolation does not come from {source}.");
    }

    [Then("the inspection diagnostic names {string}")]
    public void DiagnosticNames(string text) => Report.ShouldHaveDiagnosticThatNames(text);

    [Given("a home directory with a global Morphir file and a bare project below it")]
    public void HomeWithGlobalFile()
    {
        workspace.WriteText(GlobalHome.File("morphir.toml"), "[custom]\nglobal='home'\n");
        workspace.CreateDirectory(BareProject);
        _workingDirectory = BareProject;
    }

    [Given("MORPHIR_HOME is the native path of the global Morphir home")]
    public void NativeMorphirHome() => _variables["MORPHIR_HOME"] = GlobalHome.Path;

    [When("I inspect the bare project with {string}")]
    public Task InspectBareProject(string command) => Run(["config", .. Words(command), "--json", "--discovery-boundary", Root.Path]);

    [Then("the global Morphir file is consulted only as a global source")]
    public void GlobalFileOnlyGlobal()
    {
        var path = ReportPath.Normalize(GlobalHome.File("morphir.toml").Path);
        var roles = Report.Sources.Where(source => source.Path == path && source.Status == "loaded").Select(source => source.Role).ToArray();
        Check(roles.SequenceEqual(["Global"]),
            $"The global file loads once, as a global source, and never as a project primary. It loaded as: {string.Join(", ", roles)}.");
    }

    [When("I inspect with {string}")]
    public Task Inspect(string command) => Run(["config", .. Words(command), "--json", "--discovery-boundary", Root.Path]);

    [When("I inspect a key containing a literal dot")]
    public Task Quoted() => Run(["config", "get", "codegen.\"custom.generator\".enabled", "--json", "--discovery-boundary", Root.Path]);

    [When("I request human inspection output")]
    public Task Human() => Run(["config", "show", "--discovery-boundary", Root.Path]);

    [When("I request inspection help")]
    public Task Help() => Run(["config", "get", "--help"]);

    private async Task Run(string[] arguments)
    {
        // The arguments start with the name of the command; the driver gives it.
        run.Result = await configuration.ConfigureAsync(arguments[1..], new ScenarioMachine(_workingDirectory ?? Root.Directory("app", "src"))
        {
            Home = Root,
            Variables = _variables,
        });
        _report = null;
    }

    [Then("inspection exits with {int} and one JSON result")]
    public void Result(int expected)
    {
        run.Result.ShouldExitWith(expected);
        _report = ConfigurationReport.Of(run.Result);
        _report.ShouldMatchSchema(workspace, "config-inspection-result");
        Check(_report.Required("exitCode").GetInt32() == expected, $"The field exitCode of the inspection report is not {expected}.");
    }

    [Then("special values preserve their canonical kinds")]
    public void SpecialValueResult()
    {
        var value = Json.GetProperty("value").GetProperty("custom");
        Check(value.GetProperty("nan").GetProperty("$number").GetString() == "NaN", "nan is not NaN.");
        Check(value.GetProperty("positive").GetProperty("$number").GetString() == "Infinity", "inf is not Infinity.");
        Check(value.GetProperty("negative").GetProperty("$number").GetString() == "-Infinity", "-inf is not -Infinity.");
        Check(value.GetProperty("finite").GetDouble() == 4.5, "finite is not 4.5.");
        Check(value.GetProperty("literal").GetProperty("$number").GetString() == "NaN", "The literal $number table is not kept.");
        Check(value.GetProperty("date").GetString() == "2026-11-03", "The date is not 2026-11-03.");
        Check(value.GetProperty("secret_nan").GetString() == "[REDACTED]", "The secret is not redacted.");
        var types = Json.GetProperty("valueTypes").EnumerateArray().ToArray();
        string? Field(JsonElement type, string name) => type.GetProperty(name).GetString();
        Check(types.Any(t => Field(t, "pointer") == "/value/custom/nan" && Field(t, "kind") == "number" && Field(t, "encoding") == "nonfinite"), "No nonfinite type for nan.");
        Check(types.Any(t => Field(t, "pointer") == "/value/custom/a~1b~0c"), "No type for the escaped key a/b~c.");
        Check(types.Any(t => Field(t, "pointer") == "/value/custom/date" && Field(t, "kind") == "temporal" && Field(t, "encoding") == "LocalDate"), "No temporal type for the date.");
        Check(!types.Any(t => Field(t, "pointer")!.StartsWith("/value/custom/literal", StringComparison.Ordinal)), "The literal table has a type.");
        Check(!types.Any(t => Field(t, "pointer")!.Contains("secret_nan", StringComparison.Ordinal)), "The secret has a type.");
        Check(types.Any(t => Field(t, "pointer")!.StartsWith("/entries/", StringComparison.Ordinal)), "No type for an entry.");
    }

    [Then("nonfinite explanation metadata identifies its value and contribution")]
    public void NonfiniteExplanation()
    {
        var pointers = Json.GetProperty("valueTypes").EnumerateArray().Select(t => t.GetProperty("pointer").GetString()).ToArray();
        Check(pointers.Contains("/value"), "No type for /value.");
        Check(pointers.Any(p => p!.StartsWith("/contributions/", StringComparison.Ordinal)), "No type for a contribution.");
        Check(Json.GetProperty("contributions")[0].GetProperty("value").GetProperty("$number").GetString() == "NaN", "The first contribution is not NaN.");
    }

    [Then("the failure is a usage diagnostic without a source read")]
    public void UsageOnly()
    {
        Check(Report.Diagnostics.Count == 1 && Report.Diagnostics[0].Code == "config.usage", "The report does not have exactly one diagnostic config.usage.");
        Check(Json.GetProperty("sources").GetArrayLength() == 0, "The inspection read a source.");
    }

    [Then("the inspection result operation is {string}")]
    public void Operation(string operation) => Check(Report.Operation == operation, $"The operation is {Report.Operation}, not {operation}.");

    [Then("the inspection value is true")]
    public void True() => Check(Json.GetProperty("value").ValueKind == JsonValueKind.True, "The value is not true.");

    [Then("the inspection value is {string}")]
    public void Value(string value) => Check(Json.GetProperty("value").GetString() == value, $"The value is not {value}.");

    [Then("the inspection value is an empty array")]
    public void EmptyArray() => Check(Json.GetProperty("value").GetArrayLength() == 0, "The value is not an empty array.");

    [Then("the queried key is absent")]
    public void Absent() => Check(!Json.GetProperty("found").GetBoolean(), "The queried key is found.");

    [Then("inspection diagnostics include {string}")]
    public void IncludesDiagnostic(string code) => Report.ShouldHaveDiagnostic(code);

    [Then("inspection diagnostics exclude {string}")]
    public void ExcludesDiagnostic(string code) => Report.ShouldNotHaveDiagnostic(code);

    [Then("the get entry identifies its member origin")]
    public void Origin()
    {
        var origin = Json.GetProperty("entries")[0].GetProperty("origins")[0].GetProperty("source");
        Check(origin.GetProperty("role").GetString() == "MemberPrimary", "The origin is not the member primary.");
        Check(origin.GetProperty("path").GetString() == ReportPath.Join(Root.Path, "app/morphir.toml"), "The origin is not app/morphir.toml.");
    }

    [Then("human inspection succeeds without exposing sensitive values")]
    public void HumanResult()
    {
        run.Result.ShouldExitWith(0);
        Redacted();
    }

    [Then("the explanation identifies workspace and member sources")]
    public void Explanation()
    {
        var contributions = Json.GetProperty("contributions").EnumerateArray().ToArray();
        var paths = contributions.Select(c => c.GetProperty("source").GetProperty("path").GetString()).ToArray();
        Check(paths.Contains(ReportPath.Join(Root.Path, "morphir.toml")) && paths.Contains(ReportPath.Join(Root.Path, "app/morphir.toml")),
            "The explanation does not have the workspace and the member sources.");
        Check(contributions.Last().GetProperty("winner").GetBoolean(), "The last contribution is not the winner.");
    }

    [Then("structured warnings appear in JSON and human diagnostics only on stderr")]
    public void Warning()
    {
        Report.ShouldHaveDiagnostic("config.yaml_beside_toml");
        run.Result.StandardErrorShouldContain("config.yaml_beside_toml");
    }

    [Then("the mixed format warning is suppressed")]
    public void Suppressed() => Report.ShouldNotHaveDiagnostic("config.yaml_beside_toml");

    [Then("neither stream exposes secret values or executes the secret command")]
    public void Redacted()
    {
        Check(!Output.Contains("never-print-password", StringComparison.Ordinal) && !Output.Contains("never-execute-this-command", StringComparison.Ordinal),
            "The output has a secret value.");
        Check(run.Result.StandardOutput.Contains("[REDACTED]", StringComparison.Ordinal), "Standard output has no [REDACTED] value.");
    }

    [Then("inspection help succeeds without configuration diagnostics")]
    public void HelpResult()
    {
        run.Result.ShouldExitWith(0).OutputShouldContain("--profiles");
        Check(!Output.Contains("config.parse", StringComparison.Ordinal) && !Output.Contains("never-print-password", StringComparison.Ordinal),
            "The help has a configuration diagnostic or a secret.");
    }

    private static string[] Words(string command) => command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
