// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using Morphir.Configuration;
using Morphir.Configuration.IO;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Configuration;

[Binding]
public sealed class ConfigurationEditingSteps : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-edit-cli-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _environment = [];
    private string _original = "# keep comment\n[project]\nname='App'\n[custom]\nvalue = 'before' # keep inline\n";
    private string _stdout = "";
    private string _stderr = "";
    private int _exit;
    private JsonDocument? _report;
    private string Target => Path.Combine(_root, "morphir.toml");

    [Given("a configuration editing project")]
    public void Project()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Target, _original);
    }

    [Given("the editing project has mixed primary layouts and a personal override")]
    public void MixedPersonalLayouts()
    {
        File.Delete(Target);
        File.WriteAllText(Path.Combine(_root, "morphir.yaml"), "project: {name: App}\ncustom: {value: lower}\n");
        Directory.CreateDirectory(Path.Combine(_root, ".morphir"));
        File.WriteAllText(Path.Combine(_root, ".morphir", "morphir.toml"), "[project]\nname='App'\n");
        File.WriteAllText(Path.Combine(_root, "morphir.user.toml"), "[custom]\nvalue='before'\n");
    }

    [Given("the editing project is a workspace with two members")]
    public void MemberWorkspace()
    {
        File.WriteAllText(Target, "[workspace]\nmembers=['a','b']\ndefault_member='a'\nout_dir='root-out'\n");
        foreach (var name in new[] { "a", "b" })
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
            File.WriteAllText(Path.Combine(_root, name, "morphir.toml"), "[project]\nname='" + name.ToUpperInvariant() + "'\n");
        }
        File.WriteAllText(Path.Combine(_root, "b", "morphir.user.toml"), "[custom]\nvalue='before'\n");
    }

    [Then("editing uses the existing personal target")]
    public void PersonalTarget()
    {
        _report!.RootElement.GetProperty("target").GetProperty("path").GetString().Should().Be(ProjectPath.join(_root, "morphir.user.toml"));
        File.Exists(Path.Combine(_root, ".morphir", "morphir.user.toml")).Should().BeFalse();
    }

    [Then("the personal edit preserves member workspace ownership")]
    public void MemberOwnership()
    {
        _report!.RootElement.GetProperty("target").GetProperty("role").GetString().Should().Be("UserOverride");
        _report.RootElement.GetProperty("diagnostics").EnumerateArray().Should().Contain(d => d.GetProperty("code").GetString() == "config.member_output");
        var tree = new FileSystemProjectTree(_root);
        var discovered = ProjectSystem.Discover(tree, ProjectPath.normalize(_root), "b");
        var resolution = ProjectSystem.Load(tree, discovered.Discovery, discovered.Selection.Projects.Single());
        resolution.OwningWorkspace!.Value.OutputDirectory!.Value.Should().Be(ProjectPath.join(_root, "root-out"));
        File.ReadAllText(Path.Combine(_root, "b", "morphir.user.toml")).Should().Contain("edited");
    }

    [Given("an editing environment winner")]
    public void EnvironmentWinner() => _environment["MORPHIR_CUSTOM__VALUE"] = "environment";

    [Given("the editing target has an opaque reference")]
    public void Reference() => File.AppendAllText(Target, "reference={env='never-print-reference-input'}\n");

    [Then("editing streams redact opaque reference inputs")]
    public void RedactedReference() => (_stdout + _stderr).Should().NotContain("never-print-reference-input");

    [Then("editing preserves unrelated comments")]
    public void Comments() => File.ReadAllText(Target).Should().Contain("# keep comment").And.Contain("# keep inline");

    [When("I edit configuration with {string}")]
    public Task Edit(string command) => Run(command.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // Containment scenarios. Paths use <root> and <inner> placeholders.
    private string? _workingDirectory;
    private string Inner => Path.Combine(_root, "inner");
    private string Placeholder(string value) => value.Replace("<root>", _root, StringComparison.Ordinal).Replace("<inner>", Inner, StringComparison.Ordinal);

    [Given("the editing project is a member below an outer workspace")]
    public void BelowOuterWorkspace()
    {
        File.WriteAllText(Target, "[workspace]\nmembers=['inner/app']\n[custom]\nouter='workspace'\n");
        var app = Path.Combine(Inner, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "morphir.toml"), "[project]\nname='App'\n[custom]\nvalue='before'\n");
        _workingDirectory = app;
    }

    [Given("the editing home has a global configuration")]
    public void GlobalConfiguration()
    {
        Directory.CreateDirectory(Path.Combine(_root, "home", "morphir"));
        File.WriteAllText(Path.Combine(_root, "home", "morphir", "morphir.toml"), "[custom]\nglobal='machine'\n");
    }

    [Given("editing environment variable {word} is {string}")]
    public void EditingEnvironmentVariable(string name, string value) => _environment[name] = Placeholder(value);

    [When("I edit configuration without a boundary flag with {string}")]
    public Task EditWithoutBoundary(string command) => Run(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), boundaryFlag: false);

    private static readonly string[] MachineAndPersonalRoles = ["System", "Global", "UserOverride"];

    private IEnumerable<JsonElement> PlanSources() =>
        _report!.RootElement.GetProperty("effective").GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("source"));

    [Then("the editing plan consults no project source outside {string}")]
    public void PlanStaysInside(string boundary)
    {
        var root = ProjectPath.normalize(Placeholder(boundary));
        PlanSources()
            .Where(s => s.GetProperty("format").GetString() != "memory" && !MachineAndPersonalRoles.Contains(s.GetProperty("role").GetString()))
            .Select(s => s.GetProperty("path").GetString()!)
            .Should().OnlyContain(path => ProjectPath.contains(root, path), $"discovery must stay inside {root}");
        _report!.RootElement.GetProperty("target").GetProperty("path").GetString().Should().StartWith(root);
    }

    [Then("the editing plan consults no machine or personal sources")]
    public void PlanIsolated() =>
        PlanSources().Select(s => s.GetProperty("role").GetString()).Should().NotContain(MachineAndPersonalRoles);

    private async Task Run(string[] arguments, bool boundaryFlag = true)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Morphir.slnx"))) repo = repo.Parent;
        var configuration = GetType().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_CLI") ?? Path.Combine(repo!.FullName, "src", "Morphir", "bin", configuration, "net10.0", "morphir.dll");
        var info = new ProcessStartInfo(cli.EndsWith(".dll", StringComparison.Ordinal) ? "dotnet" : cli)
        {
            WorkingDirectory = _workingDirectory ?? _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (cli.EndsWith(".dll", StringComparison.Ordinal)) info.ArgumentList.Add(cli);
        foreach (var arg in new[] { "config" }.Concat(arguments).Concat(boundaryFlag ? ["--json", "--discovery-boundary", _root] : ["--json"])) info.ArgumentList.Add(arg);
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("MORPHIR_", StringComparison.Ordinal)).ToArray()) info.Environment.Remove(key);
        foreach (var key in new[] { "HOME", "USERPROFILE", "APPDATA", "PROGRAMDATA", "XDG_CONFIG_HOME" }) info.Environment[key] = Path.Combine(_root, "home");
        foreach (var pair in _environment) info.Environment[pair.Key] = pair.Value;
        var result = await ChildProcess.RunAsync(info, TimeSpan.FromSeconds(60));
        _exit = result.ExitCode; _stdout = result.StandardOutput; _stderr = result.StandardError;
    }

    [When("I set a complete typed object")]
    public Task TypedObject() => Run(["set", "custom.value", "{\"keep\":true,\"items\":[1,2]}", "--type", "object"]);

    [When("I set an opaque typed reference")]
    public Task TypedReference() => Run(["set", "custom.reference", "{\"env\":\"never-print-reference-input\"}", "--type", "object", "--dry-run"]);

    [When("I edit a literal dotted key")]
    public Task LiteralKey() => Run(["set", "codegen.\"custom.generator\".enabled", "false", "--type", "bool"]);

    [Then("the persisted editing value has type {string}")]
    public void PersistedType(string kind)
    {
        var target = _report!.RootElement.GetProperty("target").GetProperty("path").GetString()!;
        var key = KeyPath.parse(_report.RootElement.GetProperty("key").GetString()!).ResultValue;
        var source = ConfigurationReader.ReadFile(new SourceIdentity(target, DocumentFormat.Toml, SourceRole.RootPrimary, 0, null));
        source.Content.Should().BeOfType<SourceContent.Available>();
        var value = ConfigTree.tryFind(key, ((SourceContent.Available)source.Content).Item.Values)!.Value;
        (kind switch
        {
            "string" => value is ConfigValue.String,
            "boolean" => value is ConfigValue.Boolean,
            "integer" => value is ConfigValue.Integer,
            "number" => value is ConfigValue.Number,
            "array" => value is ConfigValue.Array,
            "object" => value is ConfigValue.Object,
            _ => false
        }).Should().BeTrue();
    }

    private string? _lowerText;
    [Given("a classic JSON value below the editing target")]
    public void LowerJson()
    {
        _lowerText = "{name:'App',exposedModules:[],custom:{value:'lower'}}";
        File.WriteAllText(Path.Combine(_root, "morphir.json"), _lowerText);
    }

    [Then("the lower JSON bytes and origin are retained")]
    public void LowerJsonResult()
    {
        File.ReadAllText(Path.Combine(_root, "morphir.json")).Should().Be(_lowerText);
        _report!.RootElement.GetProperty("effective").GetProperty("contributions").EnumerateArray().Should()
            .Contain(c => c.GetProperty("winner").GetBoolean() && c.GetProperty("source").GetProperty("path").GetString()!.EndsWith("/morphir.json", StringComparison.Ordinal));
    }

    [Then("system preview respects the native no-follow policy")]
    public void SystemPreview()
    {
        var linkedSystemRoot = !OperatingSystem.IsWindows() && new DirectoryInfo("/etc").LinkTarget is not null;
        Result(linkedSystemRoot ? 2 : 0, linkedSystemRoot ? "failed" : "preview");
        if (linkedSystemRoot) _report!.RootElement.GetProperty("diagnostics").EnumerateArray().Should().Contain(d => d.GetProperty("code").GetString() == "config.edit_target");
        else _report!.RootElement.GetProperty("target").GetProperty("role").GetString().Should().Be("System");
    }

    [Then("editing exits {int} with outcome {string}")]
    public void Result(int exit, string outcome)
    {
        _exit.Should().Be(exit, _stdout + _stderr);
        _report = JsonDocument.Parse(_stdout, new JsonDocumentOptions { MaxDepth = 256 });
        _report.RootElement.GetProperty("exitCode").GetInt32().Should().Be(exit);
        _report.RootElement.GetProperty("outcome").GetString().Should().Be(outcome);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Morphir.slnx"))) repo = repo.Parent;
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(repo!.FullName, "docs", "spec", "schemas", "config-edit-result.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        schema.Evaluate(_report.RootElement).IsValid.Should().BeTrue("every editing response must match the published schema");
    }

    [Then("the editing target retains its original bytes")]
    public void Unchanged() => File.ReadAllText(Target).Should().Be(_original);

    [Then("the editing effective value is {string}")]
    public void Effective(string value) => _report!.RootElement.GetProperty("effective").GetProperty("value").GetString().Should().Be(value);

    public void Dispose() { _report?.Dispose(); TolerantDirectory.TryDelete(_root); }
}
