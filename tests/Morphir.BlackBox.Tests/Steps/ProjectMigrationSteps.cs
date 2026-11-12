// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>project migrate</c> and of the containment of its discovery. The work area of the workspace is
/// the classic project: <c>morphir.json</c> is its source and <c>morphir.toml</c> its target.
/// </summary>
[Binding]
public sealed class ProjectMigrationSteps(IMigrationDriver migration, IConfigurationDriver configuration, IScenarioWorkspace workspace, ScenarioRun run)
{
    private const string Existing = "# preserve comment\n[project]\nname = 'Orders' # preserve inline\n[unrelated]\nvalue='keep'\n";

    private readonly Dictionary<string, string> _environment = [];
    private string _source = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order'],custom:{keep:true}}\n";
    private WorkspaceDirectory? _working;
    private MigrationReport? _report;
    private byte[]? _targetBytes;
    private DateTime? _targetTimestamp;

    private WorkspaceDirectory Root => workspace.Work;

    private WorkspaceDirectory Outside => workspace.Temp.Directory("outside");

    private WorkspaceFile Source => Root.File("morphir.json");

    private WorkspaceFile Target => Root.File("morphir.toml");

    private MigrationReport Report => _report ?? throw new InvalidOperationException("The scenario has no migration report before this step.");

    private string Output => run.Result.StandardOutput + run.Result.StandardError;

    [Given("a classic migration project")]
    public void Project()
    {
        _working = Root;
        workspace.WriteText(Source, _source);
    }

    [Given("malformed migration host appsettings")]
    public void MalformedHost() => workspace.WriteText((_working ?? Root).File("appsettings.json"), "{broken never-print-host-input");

    [Given("migration fixture {string}")]
    public void Fixture(string fixture)
    {
        var extra = fixture switch
        {
            "dependency" => ",dependencies:['unrepresentable']",
            "decorations" => ",decorations:{legacy:'unsupported'}",
            "local-dependency" => ",localDependencies:['../other']",
            "null" => ",custom:{missing:null}",
            "canonical-conflict" => ",project:{name:'Orders'}",
            "secrets" => ",custom:{password:'never-print-literal',reference:{env:'never-print-reference'}}",
            _ => "",
        };
        if (extra.Length > 0)
        {
            _source = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order']" + extra + "}\n";
            workspace.WriteText(Source, _source);
        }

        if (fixture == "ir") workspace.WriteText(Root.File("morphir-ir.json"), "{formatVersion:3,distribution:[]}\n");
    }

    [Given("the migration target contains equivalent values and comments")]
    public void EqualTarget() => workspace.WriteText(Target, Existing);

    [Given("a conflicting migration target and ambient overrides")]
    public void ConflictingTarget()
    {
        workspace.WriteText(Target, "[project]\nsource_directory='wrong'\n");
        _targetBytes = workspace.ReadBytes(Target);
        _environment["MORPHIR_PROJECT__SOURCE_DIRECTORY"] = "masked";
        workspace.WriteText(Root.File("morphir.user.toml"), "[project]\nsource_directory='masked'\n");
    }

    [Given("the migration project is one workspace member")]
    public void Member()
    {
        workspace.WriteText(Root.File("sibling", "morphir.json"), "{name:'Sibling',exposedModules:[]}\n");
        workspace.CreateDirectory(Root.Directory("member", "elm-src"));
        workspace.WriteText(Root.File("member", "morphir.json"), _source);
        workspace.WriteText(Target, "[workspace]\nmembers=['member','sibling']\nout_dir='base-out'\n");
        _targetBytes = workspace.ReadBytes(Target);
        _working = Root.Directory("member", "elm-src");
    }

    [Given("an unlisted migration project invoked from {string}")]
    public void Unlisted(string working)
    {
        workspace.Delete(Source);
        foreach (var member in new[] { "app", "other" })
        {
            workspace.CreateDirectory(Root.Directory(member, "src"));
            workspace.WriteText(Root.File(member, "morphir.json"), _source);
        }

        workspace.WriteText(Target, "[workspace]\nmembers=['other']\n");
        _targetBytes = workspace.ReadBytes(Target);
        _working = working == "root" ? Root : Root.Directory(working);
    }

    [Then("the unlisted migration leaves every project unchanged")]
    public void UnlistedUnchanged()
    {
        Check(workspace.ReadBytes(Target).SequenceEqual(_targetBytes!), "The migration changed the workspace target.");
        foreach (var member in new[] { "app", "other" })
        {
            Check(!workspace.Exists(Root.File(member, "morphir.toml")), $"The migration made a target for {member}.");
            Check(workspace.ReadText(Root.File(member, "morphir.json")) == _source, $"The migration changed the source of {member}.");
        }
    }

    [When("I migrate the project with {string}")]
    public Task Migrate(string command) => Run(Words(command), json: true);

    private string Placeholder(string value) => value
        .Replace("<root>", Root.Path, StringComparison.Ordinal)
        .Replace("<member>", Root.Directory("member").Path, StringComparison.Ordinal)
        .Replace("<outside>", Outside.Path, StringComparison.Ordinal);

    [Given("migration environment variable {word} is {string}")]
    public void MigrationEnvironmentVariable(string name, string value)
    {
        if (value.Contains("<outside>", StringComparison.Ordinal)) workspace.CreateDirectory(Outside);
        _environment[name] = Placeholder(value);
    }

    [Given("a personal override masks the migration source directory")]
    public void PersonalMask() => workspace.WriteText(Root.File("morphir.user.toml"), "[project]\nsource_directory='masked'\n");

    [When("I migrate the project without a boundary flag with {string}")]
    public Task MigrateWithoutBoundary(string command) => Run(Words(command), json: true, boundaryFlag: false);

    [Then("the migration plan reads no workspace")]
    public void NoWorkspace() =>
        Check(Report.Required("verification", "baseWorkspace").ValueKind == JsonValueKind.Null, "The plan read the workspace above the boundary.");

    [Then("migration verification keeps the classic source directory")]
    public void ClassicSourceDirectory()
    {
        Check(Report.Required("verification", "verified").GetBoolean(), "The migration is not verified.");
        Check(Report.Text("verification", "after", "project", "source_directory") == "elm-src",
            "Personal overrides never take part in migration verification, but the verified source directory is not elm-src.");
        Check(!run.Result.StandardOutput.Contains("masked", StringComparison.Ordinal), "Standard output has the masked value.");
    }

    [When("I migrate the project with human output")]
    public Task Human() => Run(["--dry-run"], json: false);

    private async Task Run(string[] arguments, bool json, bool boundaryFlag = true)
    {
        if (workspace.Exists(Target)) _targetTimestamp = workspace.LastWriteTimeUtc(Target);
        string[] command = [.. arguments, .. json ? ["--json"] : Array.Empty<string>(), .. boundaryFlag ? ["--discovery-boundary", Root.Path] : Array.Empty<string>()];
        run.Result = await migration.MigrateProjectAsync(command, new ScenarioMachine(_working ?? Root) { Variables = _environment });
        _report = null;
    }

    [Then("migration exits {int} with outcome {string}")]
    public void Result(int exit, string outcome)
    {
        run.Result.ShouldExitWith(exit);
        _report = MigrationReport.Of(run.Result);
        Check(_report.Required("exitCode").GetInt32() == exit, $"The field exitCode of the migration report is not {exit}.");
        _report.ShouldHaveOutcome(outcome);
        _report.ShouldMatchSchema(workspace, "project-migration-result");
    }

    [Then("the migration source remains byte identical")]
    public void SourceUnchanged() => Check(workspace.ReadBytes(Source).SequenceEqual(Encoding.UTF8.GetBytes(_source)), "The migration changed its source.");

    [Then("the migration report verifies independent equivalence")]
    public void Verified() => Check(Report.Required("verification", "verified").GetBoolean(), "The migration is not verified.");

    [Then("no migration target exists")]
    public void NoTarget() => Check(!workspace.Exists(Target), "The migration made a target.");

    /// <summary>
    /// Reads the written file back with <c>config get</c>, with its path relative to the work area, in which the
    /// command starts: the CLI joins an absolute Windows path with backslashes to its working directory
    /// (<c>evidence/slice-3-moves.md</c>, the findings about the product). The same check with the configuration
    /// reader of the product stays in Morphir.Tooling.Tests.
    /// </summary>
    [Then("migration writes {string} with the legacy defaults")]
    public async Task Persisted(string relative)
    {
        var file = relative;
        foreach (var (key, expected) in new[] { ("project.source_directory", "elm-src"), ("frontend.language", "elm"), ("pipeline.backend", "checkpoint"), ("pipeline.checkpoint_format", "morphir-json") })
        {
            var read = await Read(key, file);
            read.Check(read.Text("value") == expected, $"The migrated file {relative} has {key} = {read.Member("value")?.GetRawText() ?? "nothing"}, not '{expected}'.");
        }

        var version = await Read("ir.format_version", file);
        version.Check(!version.Required("found").GetBoolean(), $"The migrated file {relative} has ir.format_version.");
    }

    private async Task<ConfigurationReport> Read(string key, string file) =>
        ConfigurationReport.Of(await configuration.ConfigureAsync(
            ["get", key, "--file", file, "--isolated", "--json", "--discovery-boundary", Root.Path], new ScenarioMachine(Root)));

    [Then("migration preserves the existing equivalent text")]
    public void ExistingText()
    {
        var text = workspace.ReadText(Target);
        Check(text.Contains("# preserve comment\n[project]\nname = 'Orders' # preserve inline\n", StringComparison.Ordinal) && text.Contains("value='keep'", StringComparison.Ordinal),
            "The migration did not preserve the text of the existing target.");
    }

    [Then("the repeated migration preserves the target timestamp")]
    public void Repeat() => Check(workspace.LastWriteTimeUtc(Target) == _targetTimestamp, "The repeated migration wrote the target again.");

    [Then("migration reports blocking diagnostics on stderr")]
    public void BlockingDiagnostics()
    {
        Check(!string.IsNullOrWhiteSpace(run.Result.StandardError), "Standard error is empty.");
        Check(Report.Diagnostics.Any(diagnostic => diagnostic.Severity == "error"), "The migration report has no error diagnostic.");
    }

    [Then("migration reports both conflicting values and origins")]
    public void ConflictValues()
    {
        var conflicts = Report.Required("conflicts").EnumerateArray().Where(value => value.GetProperty("key").GetString() == "project.source_directory").ToArray();
        Check(conflicts.Length == 1, "The migration report does not have exactly one conflict of project.source_directory.");
        var conflict = conflicts[0];
        Check(conflict.GetProperty("sourceValue").GetString() == "elm-src", "The source value of the conflict is not elm-src.");
        Check(conflict.GetProperty("targetValue").GetString() == "wrong", "The target value of the conflict is not wrong.");
        Check(conflict.GetProperty("source").GetProperty("path").GetString()!.EndsWith("/morphir.json", StringComparison.Ordinal), "The source of the conflict is not morphir.json.");
        Check(conflict.GetProperty("target").GetProperty("path").GetString()!.EndsWith("/morphir.toml", StringComparison.Ordinal), "The target of the conflict is not morphir.toml.");
    }

    [Then("migration preserves the conflicting target")]
    public void TargetUnchanged() => Check(workspace.ReadBytes(Target).SequenceEqual(_targetBytes!), "The migration changed the conflicting target.");

    [Then("migration changes only the selected member target")]
    public void OnlyMember()
    {
        Check(workspace.ReadBytes(Target).SequenceEqual(_targetBytes!), "The migration changed the workspace target.");
        Check(workspace.Exists(Root.File("member", "morphir.toml")), "The migration made no target for the member.");
        Check(!workspace.Exists(Root.File("sibling", "morphir.toml")), "The migration made a target for the sibling.");
        Check(workspace.ReadText(Root.File("sibling", "morphir.json")) == "{name:'Sibling',exposedModules:[]}\n", "The migration changed the source of the sibling.");
        Check(workspace.ReadText(Root.File("member", "morphir.json")) == _source, "The migration changed the source of the member.");
    }

    [Then("migration streams redact secret inputs")]
    public void Redacted()
    {
        Check(!Output.Contains("never-print-literal", StringComparison.Ordinal) && !Output.Contains("never-print-reference", StringComparison.Ordinal), "The output has a secret input.");
        Check(run.Result.StandardOutput.Contains("[REDACTED]", StringComparison.Ordinal), "Standard output has no [REDACTED] value.");
    }

    [Then("migration human output reports a verified preview")]
    public void HumanResult()
    {
        run.Result.ShouldExitWith(0);
        var stdout = run.Result.StandardOutput;
        Check(stdout.Contains("preview", StringComparison.Ordinal) && stdout.Contains("morphir.toml", StringComparison.Ordinal) && stdout.Contains("verified", StringComparison.Ordinal),
            "Standard output does not report a verified preview of morphir.toml.");
        Check(!workspace.Exists(Target), "The preview made a target.");
    }

    private static string[] Words(string command) => command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
