// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Tooling.Tests.Features.WorkspaceMigration;

[Binding]
public sealed class WorkspaceMigrationSteps : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-workspace-cli-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, byte[]> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Bytes, DateTime Timestamp)> _targets = new(StringComparer.Ordinal);
    private string _working = "";
    private string _stdout = "";
    private string _stderr = "";
    private int _exit;
    private JsonDocument? _report;
    private string? _pendingJournal;
    private string? _pendingTarget;
    private string? _uncertainDirectory;
    private const string ExistingMember = "# existing member stays intact\n[project]\nname='Existing'\nsource_directory='kept'\n";
    private const string ExistingWorkspace = "# preserve workspace\n[workspace]\nmembers=['existing']\ndefault_member='existing'\nout_dir='kept-output'\n[custom]\nkeep=true\n";

    [Given("an empty workspace migration repository")]
    public void Empty()
    {
        Directory.CreateDirectory(_root);
        _working = _root;
    }

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
        var path = Path.Combine(_root, relative, "morphir.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        _sources[ProjectPath.normalize(path)] = File.ReadAllBytes(path);
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

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
        Write("morphir.toml", "[workspace]\nmembers=['old']\nout_dir='shared'\n" +
            (mode == "incompatible" ? "[pipeline]\nbackend='root-specific'\n" : ""));
        Write("old/morphir.toml", "[project]\nname='Old'\n");
        Write("app/morphir.toml", "[project]\nname='App'\n");
    }

    [Then("workspace migration excludes root conversion")]
    public void ExcludedRootConversion()
    {
        var root = ProjectPath.normalize(_root);
        _report!.RootElement.GetProperty("projects").EnumerateArray()
            .Should().NotContain(p => p.GetProperty("projectPath").GetString() == root);
    }

    [Then("workspace migration reports the new member original context")]
    public void OriginalAdoptionContext()
    {
        var root = ProjectPath.normalize(_root);
        var effects = _report!.RootElement.GetProperty("adoptionEffects").EnumerateArray()
            .Where(e => e.GetProperty("projectPath").GetString() == root + "/app").ToArray();
        effects.Single(e => e.GetProperty("kind").GetString() == "ownership").GetProperty("before").GetString().Should().Be(root + "/app");
        effects.Single(e => e.GetProperty("kind").GetString() == "shared-output-root").GetProperty("before").GetString().Should().Be(root + "/app/.morphir/out");
    }

    [Given("a hidden workspace migration member primary")]
    public void HiddenMember()
    {
        var original = Path.Combine(_root, "apps", "a", "morphir.json");
        var hidden = Path.Combine(_root, "apps", "a", ".config", "morphir", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(hidden)!);
        File.Move(original, hidden);
        _sources.Remove(ProjectPath.normalize(original));
        _sources[ProjectPath.normalize(hidden)] = File.ReadAllBytes(hidden);
    }

    [Given("a declared empty migration workspace")]
    public void EmptyWorkspace() => Write("morphir.toml", "[workspace]\nmembers=[]\n");

    [Given("a workspace migration root project")]
    public void RootProject() => Classic("", "{name:'Root',exposedModules:[]}\n");

    [Given("workspace migration invoked from a bounded child")]
    public void BoundedChild()
    {
        RootProject();
        _working = Path.Combine(_root, "apps", "a");
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
            _ => throw new ArgumentException("Unknown workspace fixture")
        };
        Classic("apps/b", text);
    }

    [Given("workspace migration has pending durable recovery")]
    public void PendingRecovery()
    {
        _pendingTarget = ProjectPath.normalize(Path.Combine(_root, "apps", "a", "migration-guard.toml"));
        var expected = ConfigurationTransaction.Observe(_pendingTarget);
        var transaction = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback)
                throw new IOException("Controlled test interruption requiring explicit recovery");
        });
        var applied = transaction.Apply(new ConfigurationTransactionPlan([expected],
            [new ConfigurationFileChange(_pendingTarget, expected, System.Text.Encoding.UTF8.GetBytes("retained recovery guard"))]));
        applied.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        _pendingJournal = applied.Artifacts.First(artifact => artifact.Kind == ConfigurationArtifactKind.Journal).Path;
        File.Exists(_pendingJournal).Should().BeTrue();
    }

    [Given("workspace migration has interrupted directory recovery")]
    public async Task InterruptedDirectoryRecovery()
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(GetType().Assembly.Location);
        foreach (var argument in new[] { "--maximum-parallel-tests", "1", "--treenode-filter", "/*/*/*/*[Category=WorkspaceMigrationRecoveryWorker]" })
            info.ArgumentList.Add(argument);
        info.Environment["MORPHIR_WORKSPACE_RECOVERY_CHILD_ROOT"] = _root;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        process.ExitCode.Should().Be(79, await output + await error);
        _uncertainDirectory = ProjectPath.normalize(Path.Combine(_root, "new"));
        var pending = new ConfigurationTransaction().InspectPending([ProjectPath.normalize(_root)]);
        pending.Should().NotBeEmpty();
        _pendingJournal = pending[0].JournalPath;
        pending[0].UncertainDirectories.Should().Contain(_uncertainDirectory);
    }

    [Given("malformed workspace migration appsettings")]
    public void InvalidHost() => Write("appsettings.json", "{never-print-host-value");

    // Containment scenarios. Paths use <root> and <outside> placeholders.
    private readonly Dictionary<string, string> _environment = [];
    private string Outside => _root + "-outside";

    private string Placeholder(string value) => value
        .Replace("<root>", _root, StringComparison.Ordinal)
        .Replace("<outside>", Outside, StringComparison.Ordinal);

    [Given("workspace migration environment variable {word} is {string}")]
    public void WorkspaceEnvironmentVariable(string name, string value)
    {
        if (value.Contains("<outside>", StringComparison.Ordinal)) Directory.CreateDirectory(Outside);
        _environment[name] = Placeholder(value);
    }

    [When("I migrate the workspace with boundary {string} and {string}")]
    public Task MigrateWithBoundary(string boundary, string arguments) =>
        Run(["workspace", "migrate", .. arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--discovery-boundary", Placeholder(boundary), "--json"]);

    [Then("workspace migration diagnostics include {string}")]
    public void WorkspaceDiagnostic(string code) => _report!.RootElement.GetProperty("diagnostics").EnumerateArray()
        .Select(d => d.GetProperty("code").GetString()).Should().Contain(code);

    [Then("workspace migration diagnostics name {string}")]
    public void WorkspaceDiagnosticNames(string text) => _report!.RootElement.GetProperty("diagnostics").EnumerateArray()
        .Select(d => d.GetProperty("message").GetString()).Should().Contain(message => message!.Contains(text, StringComparison.Ordinal));
    [When("I migrate the workspace with {string}")]
    public Task Migrate(string arguments) => Run(["workspace", "migrate", .. arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--json"]);

    [When("I migrate the workspace with human output")]
    public Task Human() => Run(["workspace", "migrate", "--dry-run"]);

    [When("I request the configuration migration alias")]
    public Task Alias() => Run(["config", "migrate"]);

    private async Task Run(string[] arguments)
    {
        _targets.Clear();
        foreach (var path in Directory.GetFiles(_root, "*.toml", SearchOption.AllDirectories))
            _targets[path] = (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));
        var repo = Repo();
        var configuration = GetType().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_CLI") ?? Path.Combine(repo, "src", "Morphir", "bin", configuration, "net10.0", "morphir.dll");
        var dll = cli.EndsWith(".dll", StringComparison.Ordinal);
        var info = new ProcessStartInfo(dll ? "dotnet" : cli)
        {
            WorkingDirectory = _working,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (dll) info.ArgumentList.Add(cli);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("MORPHIR_", StringComparison.Ordinal)).ToArray()) info.Environment.Remove(key);
        foreach (var pair in _environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        _exit = process.ExitCode;
        _stdout = await stdout;
        _stderr = await stderr;
    }

    [Then("workspace migration exits {int} with outcome {string}")]
    public void Result(int exitCode, string outcome)
    {
        _exit.Should().Be(exitCode, _stdout + _stderr);
        _report?.Dispose();
        _report = JsonDocument.Parse(_stdout, new JsonDocumentOptions { MaxDepth = 256 });
        _report.RootElement.GetProperty("outcome").GetString().Should().Be(outcome);
        _report.RootElement.GetProperty("exitCode").GetInt32().Should().Be(exitCode);
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(Repo(), "docs", "spec", "schemas", "workspace-migration-result.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        schema.Evaluate(_report.RootElement).IsValid.Should().BeTrue("every workspace response must match the published schema");
    }

    [Then("workspace migration retains every classic source")]
    public void SourceBytes()
    {
        foreach (var source in _sources) File.ReadAllBytes(source.Key).Should().Equal(source.Value);
    }

    [Then("workspace migration verifies the aggregate and every conversion")]
    public void Verified()
    {
        _report!.RootElement.GetProperty("verified").GetBoolean().Should().BeTrue();
        foreach (var project in _report.RootElement.GetProperty("projects").EnumerateArray())
            project.GetProperty("verification").GetProperty("verified").GetBoolean().Should().BeTrue();
    }

    [Then("workspace migration reports members {string}")]
    public void Members(string expected) => _report!.RootElement.GetProperty("members").EnumerateArray().Select(e => e.GetString())
        .Should().Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries));

    [Then("workspace migration reports excluded {string}")]
    public void Excluded(string paths)
    {
        var inventory = _report!.RootElement.GetProperty("inventory").EnumerateArray().ToArray();
        foreach (var path in paths.Split(',')) inventory.Should().Contain(e => e.GetProperty("relativePath").GetString() == path && e.GetProperty("kind").GetString() == "Excluded");
    }

    [Then("workspace migration reports boundary {string}")]
    public void Boundary(string relative) => _report!.RootElement.GetProperty("inventory").EnumerateArray().Should()
        .Contain(e => e.GetProperty("relativePath").GetString() == relative && e.GetProperty("kind").GetString() == "Boundary");

    [Then("workspace migration writes no destinations")]
    public void NoDestinations()
    {
        var current = Directory.GetFiles(_root, "*.toml", SearchOption.AllDirectories);
        current.Should().BeEquivalentTo(_targets.Keys);
        foreach (var target in _targets) File.ReadAllBytes(target.Key).Should().Equal(target.Value.Bytes);
    }

    [Then("workspace migration retains the existing member and default")]
    public void ExistingRetained()
    {
        File.ReadAllText(Path.Combine(_root, "existing", "morphir.toml")).Should().Be(ExistingMember);
        var text = File.ReadAllText(Path.Combine(_root, "morphir.toml"));
        text.Should().Contain("default_member='existing'").And.Contain("out_dir='kept-output'").And.Contain("keep=true").And.Contain("# preserve workspace");
        _report!.RootElement.GetProperty("members").EnumerateArray().Select(e => e.GetString()).Should().Contain("existing");
    }

    [Then("workspace migration retains root project identity")]
    public void RootIdentity() => _report!.RootElement.GetProperty("rootIsProject").GetBoolean().Should().BeTrue();

    [Then("workspace migration keeps the hidden member anchor")]
    public void HiddenAnchor()
    {
        var project = _report!.RootElement.GetProperty("projects").EnumerateArray()
            .Single(p => p.GetProperty("projectPath").GetString()!.EndsWith("/apps/a", StringComparison.Ordinal));
        project.GetProperty("target").GetProperty("path").GetString().Should().EndWith("/apps/a/.config/morphir/config.toml");
        File.Exists(Path.Combine(_root, "apps", "a", ".config", "morphir", "config.toml")).Should().BeTrue();
    }

    [Then("workspace migration preserves every target byte and timestamp")]
    public void Repeat()
    {
        _targets.Should().NotBeEmpty();
        foreach (var target in _targets)
        {
            File.ReadAllBytes(target.Key).Should().Equal(target.Value.Bytes);
            File.GetLastWriteTimeUtc(target.Key).Should().Be(target.Value.Timestamp);
        }
    }

    [Then("workspace migration reports diagnostics on stderr")]
    public void Diagnostics()
    {
        _stderr.Should().NotBeNullOrWhiteSpace();
        _report!.RootElement.GetProperty("diagnostics").EnumerateArray().Should().Contain(d => d.GetProperty("severity").GetString() == "error");
    }

    [Then("workspace migration preserves and reports pending recovery")]
    public void PendingRecoveryReport()
    {
        File.Exists(_pendingJournal).Should().BeTrue();
        File.ReadAllText(_pendingTarget!).Should().Be("retained recovery guard");
        _report!.RootElement.GetProperty("recoveryArtifacts").EnumerateArray()
            .Should().Contain(artifact => artifact.GetProperty("kind").GetString() == "Journal");
        _report.RootElement.GetProperty("unresolvedPaths").EnumerateArray().Select(path => path.GetString())
            .Should().Contain(_pendingTarget);
        _report.RootElement.GetProperty("verified").GetBoolean().Should().BeFalse();
        // The scenario now explicitly requests recovery after verifying the command preserved evidence.
        new ConfigurationTransaction().Recover(_pendingJournal!).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        File.Exists(_pendingTarget).Should().BeFalse();
        File.Exists(_pendingJournal).Should().BeFalse();
    }

    [Then("workspace migration reports uncertain directory ownership")]
    public void UncertainDirectoryReport()
    {
        File.Exists(_pendingJournal).Should().BeTrue();
        Directory.Exists(_uncertainDirectory).Should().BeTrue();
        _report!.RootElement.GetProperty("uncertainDirectories").EnumerateArray().Select(path => path.GetString())
            .Should().Contain(_uncertainDirectory);
        _report.RootElement.GetProperty("preservedDirectories").EnumerateArray().Should().BeEmpty();
        var transaction = new ConfigurationTransaction();
        transaction.Recover(_pendingJournal!).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        var recovered = transaction.Recover(_pendingJournal!, [_uncertainDirectory!]);
        recovered.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        recovered.PreservedDirectories.Should().Contain(_uncertainDirectory);
        Directory.Exists(_uncertainDirectory).Should().BeTrue();
        File.Exists(_pendingJournal).Should().BeFalse();
    }

    [Then("workspace migration accounts for unsupported raw fields")]
    public void RawLedger() => _report!.RootElement.GetProperty("projects").EnumerateArray()
        .SelectMany(project => project.GetProperty("ledger").EnumerateArray())
        .Should().Contain(field => field.GetProperty("rawPath").GetString() == "dependencies"
            && field.GetProperty("disposition").GetString() == "blocking");

    [Then("workspace migration streams redact secret values")]
    public void Redaction()
    {
        (_stdout + _stderr).Should().NotContain("never-print-literal").And.NotContain("never-print-reference");
        _stdout.Should().Contain("[REDACTED]");
    }

    [Then("workspace migration human output reports verified preview")]
    public void HumanResult()
    {
        _exit.Should().Be(0, _stdout + _stderr);
        _stdout.Should().Contain("preview").And.Contain("verified").And.Contain("apps/a").And.Contain("apps/b");
    }

    [Then("configuration migration remains unavailable")]
    public void NoAlias()
    {
        _exit.Should().NotBe(0);
        (_stdout + _stderr).Should().Contain("config.usage");
    }

    private static string Repo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Morphir.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    public void Dispose()
    {
        _report?.Dispose();
        if (Directory.Exists(Outside)) Directory.Delete(Outside, true);
        if (!Directory.Exists(_root)) return;
        if (Directory.EnumerateFiles(_root, ".morphir-edit-*", SearchOption.AllDirectories).Any())
        {
            Console.Error.WriteLine("Retained workspace migration CLI recovery evidence: " + _root);
            return;
        }
        Directory.Delete(_root, true);
    }
}
