// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>workspace migrate</c> and of the containment of its discovery. The work area of the workspace is
/// the repository whose classic projects the migration adopts.
/// </summary>
[Binding]
public sealed class WorkspaceMigrationSteps(IMigrationDriver migration, IConfigurationDriver configuration, IScenarioWorkspace workspace, ScenarioRun run)
{
    private const string ExistingMember = "# existing member stays intact\n[project]\nname='Existing'\nsource_directory='kept'\n";
    private const string ExistingWorkspace = "# preserve workspace\n[workspace]\nmembers=['existing']\ndefault_member='existing'\nout_dir='kept-output'\n[custom]\nkeep=true\n";

    // The files by their path relative to the root.
    private readonly Dictionary<string, byte[]> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Bytes, DateTime Timestamp)> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _environment = [];
    private WorkspaceDirectory? _working;
    private MigrationReport? _report;

    private WorkspaceDirectory Root => workspace.Work;

    private WorkspaceDirectory Outside => workspace.Temp.Directory("outside");

    private MigrationReport Report => _report ?? throw new InvalidOperationException("The scenario has no migration report before this step.");

    private JsonElement Json => Report.Document;

    private string Output => run.Result.StandardOutput + run.Result.StandardError;

    /// <summary>The root of the repository in the form of a report path.</summary>
    private string ReportRoot => ReportPath.Normalize(Root.Path);

    [Given("an empty workspace migration repository")]
    public void Empty() => _working = Root;

    [Given("a workspace migration repository")]
    public void Repository()
    {
        Empty();
        Project("apps/a");
        Project("apps/b");
    }

    [Given("workspace migration project {string}")]
    public void Project(string relative) => Classic(relative, "{name:'" + relative.Replace('/', '.') + "',sourceDirectory:'elm-src',exposedModules:[],custom:{keep:true}}\n");

    private void Classic(string relative, string text)
    {
        var path = relative.Length == 0 ? "morphir.json" : relative + "/morphir.json";
        workspace.WriteText(Root.File(path), text);
        _sources[path] = workspace.ReadBytes(Root.File(path));
    }

    private void Write(string relative, string text) => workspace.WriteText(Root.File(relative), text);

    [Given("workspace migration skipped and nested projects")]
    public void Skipped()
    {
        foreach (var path in new[] { ".git/internal", "node_modules/package", "bin/generated", "obj/generated", "nested/child" }) Project(path);
        Write("nested/morphir.toml", "[workspace]\nmembers=['child']\n");
    }

    [Given("an existing workspace migration member and default")]
    public void Existing()
    {
        Write("morphir.toml", ExistingWorkspace);
        Write("existing/morphir.toml", ExistingMember);
    }

    [Given("workspace migration adoption fixture {string}")]
    public void AdoptionFixture(string mode)
    {
        Write("morphir.toml", "[workspace]\nmembers=['old']\nout_dir='shared'\n" + (mode == "incompatible" ? "[pipeline]\nbackend='root-specific'\n" : ""));
        Write("old/morphir.toml", "[project]\nname='Old'\n");
        Write("app/morphir.toml", "[project]\nname='App'\n");
    }

    [Then("workspace migration excludes root conversion")]
    public void ExcludedRootConversion() =>
        Check(!Json.GetProperty("projects").EnumerateArray().Any(p => p.GetProperty("projectPath").GetString() == ReportRoot), "The migration converted the root.");

    [Then("workspace migration reports the new member original context")]
    public void OriginalAdoptionContext()
    {
        var effects = Json.GetProperty("adoptionEffects").EnumerateArray().Where(e => e.GetProperty("projectPath").GetString() == ReportRoot + "/app").ToArray();
        string? Before(string kind)
        {
            var matches = effects.Where(e => e.GetProperty("kind").GetString() == kind).ToArray();
            Check(matches.Length == 1, $"The migration report does not have exactly one {kind} effect of app.");
            return matches[0].GetProperty("before").GetString();
        }

        Check(Before("ownership") == ReportRoot + "/app", "The ownership of app before the migration is not app.");
        Check(Before("shared-output-root") == ReportRoot + "/app/.morphir/out", "The output root of app before the migration is not app/.morphir/out.");
    }

    [Given("a hidden workspace migration member primary")]
    public void HiddenMember()
    {
        const string original = "apps/a/morphir.json", hidden = "apps/a/.config/morphir/config.json";
        workspace.WriteText(Root.File(hidden), workspace.ReadText(Root.File(original)));
        workspace.Delete(Root.File(original));
        _sources.Remove(original);
        _sources[hidden] = workspace.ReadBytes(Root.File(hidden));
    }

    [Given("a declared empty migration workspace")]
    public void EmptyWorkspace() => Write("morphir.toml", "[workspace]\nmembers=[]\n");

    [Given("a workspace migration root project")]
    public void RootProject() => Classic("", "{name:'Root',exposedModules:[]}\n");

    [Given("workspace migration invoked from a bounded child")]
    public void BoundedChild()
    {
        RootProject();
        _working = Root.Directory("apps", "a");
    }

    [Given("workspace migration fixture {string}")]
    public void Fixture(string fixture)
    {
        var text = fixture switch
        {
            "dependencies" => "{name:'Blocked',dependencies:['unsupported'],exposedModules:[]}",
            "decorations" => "{name:'Blocked',decorations:{unsupported:true},exposedModules:[]}",
            "canonical-conflict" => "{name:'Blocked',project:{name:'Blocked'},exposedModules:[]}",
            "invalid-json5" => "{name:'Blocked',exposedModules:[}",
            "unsupported-secrets" => "{name:'Private',exposedModules:[],dependencies:[{env:'never-print-reference'}],custom:{password:'never-print-literal'}}",
            "deep" => "{name:'Deep',exposedModules:[],custom:" + string.Concat(Enumerable.Repeat("{child:", 120)) + "true" + new string('}', 120) + "}",
            "secrets" => "{name:'Private',exposedModules:[],custom:{password:'never-print-literal',ref:{env:'never-print-reference'}}}",
            _ => throw new ArgumentException("Unknown workspace fixture", nameof(fixture)),
        };
        Classic("apps/b", text);
    }

    [Given("malformed workspace migration appsettings")]
    public void InvalidHost() => Write("appsettings.json", "{never-print-host-value");

    private string Placeholder(string value) => value
        .Replace("<root>", Root.Path, StringComparison.Ordinal)
        .Replace("<outside>", Outside.Path, StringComparison.Ordinal);

    [Given("workspace migration environment variable {word} is {string}")]
    public void WorkspaceEnvironmentVariable(string name, string value)
    {
        if (value.Contains("<outside>", StringComparison.Ordinal)) workspace.CreateDirectory(Outside);
        _environment[name] = Placeholder(value);
    }

    [When("I migrate the workspace with boundary {string} and {string}")]
    public Task MigrateWithBoundary(string boundary, string arguments) =>
        Run([.. Words(arguments), "--discovery-boundary", Placeholder(boundary), "--json"]);

    [Then("workspace migration diagnostics include {string}")]
    public void WorkspaceDiagnostic(string code) => Report.ShouldHaveDiagnostic(code);

    [Then("workspace migration diagnostics name {string}")]
    public void WorkspaceDiagnosticNames(string text) => Report.ShouldHaveDiagnosticThatNames(text);

    [When("I migrate the workspace with {string}")]
    public Task Migrate(string arguments) => Run([.. Words(arguments), "--json"]);

    [When("I migrate the workspace with human output")]
    public Task Human() => Run(["--dry-run"]);

    /// <summary><c>config migrate</c>: a command of the configuration family that must not exist.</summary>
    [When("I request the configuration migration alias")]
    public async Task Alias()
    {
        RememberTargets();
        run.Result = await configuration.ConfigureAsync(["migrate"], Machine);
        _report = null;
    }

    private ScenarioMachine Machine => new(_working ?? Root) { Variables = _environment };

    private async Task Run(string[] arguments)
    {
        RememberTargets();
        run.Result = await migration.MigrateWorkspaceAsync(arguments, Machine);
        _report = null;
    }

    private void RememberTargets()
    {
        _targets.Clear();
        foreach (var file in TomlFiles()) _targets[file] = (workspace.ReadBytes(Root.File(file)), workspace.LastWriteTimeUtc(Root.File(file)));
    }

    private IEnumerable<string> TomlFiles() => workspace.FilesIn(Root).Where(file => file.EndsWith(".toml", StringComparison.Ordinal));

    [Then("workspace migration exits {int} with outcome {string}")]
    public void Result(int exitCode, string outcome)
    {
        run.Result.ShouldExitWith(exitCode);
        _report = MigrationReport.Of(run.Result);
        _report.ShouldHaveOutcome(outcome);
        Check(_report.Required("exitCode").GetInt32() == exitCode, $"The field exitCode of the migration report is not {exitCode}.");
        _report.ShouldMatchSchema(workspace, "workspace-migration-result");
    }

    [Then("workspace migration retains every classic source")]
    public void SourceBytes()
    {
        foreach (var (path, bytes) in _sources)
            Check(workspace.ReadBytes(SourceFile(path)).SequenceEqual(bytes), $"The migration changed the classic source {path}.");
    }

    [Then("workspace migration verifies the aggregate and every conversion")]
    public void Verified()
    {
        Check(Json.GetProperty("verified").GetBoolean(), "The migration is not verified.");
        foreach (var project in Json.GetProperty("projects").EnumerateArray())
            Check(project.GetProperty("verification").GetProperty("verified").GetBoolean(), $"The conversion of {project.GetProperty("projectPath").GetString()} is not verified.");
    }

    [Then("workspace migration reports members {string}")]
    public void Members(string expected)
    {
        var members = Json.GetProperty("members").EnumerateArray().Select(e => e.GetString()).ToArray();
        Check(members.SequenceEqual(expected.Split(',', StringSplitOptions.RemoveEmptyEntries)), $"The members are {string.Join(",", members)}, not {expected}.");
    }

    [Then("workspace migration reports excluded {string}")]
    public void Excluded(string paths)
    {
        var inventory = Json.GetProperty("inventory").EnumerateArray().ToArray();
        foreach (var path in paths.Split(','))
            Check(inventory.Any(e => e.GetProperty("relativePath").GetString() == path && e.GetProperty("kind").GetString() == "Excluded"), $"The inventory does not exclude {path}.");
    }

    [Then("workspace migration reports boundary {string}")]
    public void Boundary(string relative) =>
        Check(Json.GetProperty("inventory").EnumerateArray().Any(e => e.GetProperty("relativePath").GetString() == relative && e.GetProperty("kind").GetString() == "Boundary"),
            $"The inventory has no boundary {relative}.");

    [Then("workspace migration writes no destinations")]
    public void NoDestinations()
    {
        var current = TomlFiles().ToHashSet(StringComparer.Ordinal);
        Check(current.SetEquals(_targets.Keys), "The migration made or removed a TOML file.");
        foreach (var (path, target) in _targets)
            Check(workspace.ReadBytes(SourceFile(path)).SequenceEqual(target.Bytes), $"The migration changed {path}.");
    }

    [Then("workspace migration retains the existing member and default")]
    public void ExistingRetained()
    {
        Check(workspace.ReadText(Root.File("existing", "morphir.toml")) == ExistingMember, "The migration changed the existing member.");
        var text = workspace.ReadText(Root.File("morphir.toml"));
        Check(new[] { "default_member='existing'", "out_dir='kept-output'", "keep=true", "# preserve workspace" }.All(part => text.Contains(part, StringComparison.Ordinal)),
            "The migration did not keep the existing workspace text.");
        Check(Json.GetProperty("members").EnumerateArray().Any(e => e.GetString() == "existing"), "The members do not include existing.");
    }

    [Then("workspace migration retains root project identity")]
    public void RootIdentity() => Check(Json.GetProperty("rootIsProject").GetBoolean(), "The root is not a project.");

    [Then("workspace migration keeps the hidden member anchor")]
    public void HiddenAnchor()
    {
        var projects = Json.GetProperty("projects").EnumerateArray().Where(p => p.GetProperty("projectPath").GetString()!.EndsWith("/apps/a", StringComparison.Ordinal)).ToArray();
        Check(projects.Length == 1, "The report does not have exactly one project apps/a.");
        Check(projects[0].GetProperty("target").GetProperty("path").GetString()!.EndsWith("/apps/a/.config/morphir/config.toml", StringComparison.Ordinal),
            "The target of apps/a is not .config/morphir/config.toml.");
        Check(workspace.Exists(Root.File("apps", "a", ".config", "morphir", "config.toml")), "The migration did not write .config/morphir/config.toml.");
    }

    [Then("workspace migration preserves every target byte and timestamp")]
    public void Repeat()
    {
        Check(_targets.Count > 0, "The repository has no target.");
        foreach (var (path, target) in _targets)
        {
            var file = SourceFile(path);
            Check(workspace.ReadBytes(file).SequenceEqual(target.Bytes), $"The migration changed {path}.");
            Check(workspace.LastWriteTimeUtc(file) == target.Timestamp, $"The migration wrote {path} again.");
        }
    }

    [Then("workspace migration reports diagnostics on stderr")]
    public void Diagnostics()
    {
        Check(!string.IsNullOrWhiteSpace(run.Result.StandardError), "Standard error is empty.");
        Check(Report.Diagnostics.Any(diagnostic => diagnostic.Severity == "error"), "The migration report has no error diagnostic.");
    }

    [Then("workspace migration accounts for unsupported raw fields")]
    public void RawLedger() =>
        Check(Json.GetProperty("projects").EnumerateArray().SelectMany(project => project.GetProperty("ledger").EnumerateArray())
            .Any(field => field.GetProperty("rawPath").GetString() == "dependencies" && field.GetProperty("disposition").GetString() == "blocking"),
            "The ledger has no blocking field dependencies.");

    [Then("workspace migration streams redact secret values")]
    public void Redaction()
    {
        Check(!Output.Contains("never-print-literal", StringComparison.Ordinal) && !Output.Contains("never-print-reference", StringComparison.Ordinal), "The output has a secret value.");
        Check(run.Result.StandardOutput.Contains("[REDACTED]", StringComparison.Ordinal), "Standard output has no [REDACTED] value.");
    }

    [Then("workspace migration human output reports verified preview")]
    public void HumanResult()
    {
        run.Result.ShouldExitWith(0);
        var stdout = run.Result.StandardOutput;
        Check(new[] { "preview", "verified", "apps/a", "apps/b" }.All(part => stdout.Contains(part, StringComparison.Ordinal)), "Standard output does not report a verified preview of apps/a and apps/b.");
    }

    [Then("configuration migration remains unavailable")]
    public void NoAlias()
    {
        Check(run.Result.ExitCode != 0, "config migrate exits with 0.");
        Check(Output.Contains("config.usage", StringComparison.Ordinal), "The output has no config.usage.");
    }

    /// <summary>The file of a path relative to the root.</summary>
    private WorkspaceFile SourceFile(string path) => Root.File(path);

    private static string[] Words(string command) => command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
