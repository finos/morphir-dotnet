// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Microsoft.FSharp.Collections;

namespace Morphir.Tooling.Tests.Features.ProjectMigration;

[Binding]
public sealed class ProjectMigrationSteps : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-migrate-cli-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _environment = [];
    private string _working = "";
    private string _source = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order'],custom:{keep:true}}\n";
    private string _stdout = "";
    private string _stderr = "";
    private int _exit;
    private JsonDocument? _report;
    private byte[]? _targetBytes;
    private DateTime? _targetTimestamp;
    private const string Existing = "# preserve comment\n[project]\nname = 'Orders' # preserve inline\n[unrelated]\nvalue='keep'\n";
    private string Source => Path.Combine(_root, "morphir.json");
    private string Target => Path.Combine(_root, "morphir.toml");

    [Given("a classic migration project")]
    public void Project()
    {
        Directory.CreateDirectory(_root);
        _working = _root;
        File.WriteAllText(Source, _source);
    }

    [Given("malformed migration host appsettings")]
    public void MalformedHost() => File.WriteAllText(Path.Combine(_working, "appsettings.json"), "{broken never-print-host-input");

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
            _ => ""
        };
        if (extra.Length > 0)
        {
            _source = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order']" + extra + "}\n";
            File.WriteAllText(Source, _source);
        }
        if (fixture == "ir") File.WriteAllText(Path.Combine(_root, "morphir-ir.json"), "{formatVersion:3,distribution:[]}\n");
    }

    [Given("the migration target contains equivalent values and comments")]
    public void EqualTarget() => File.WriteAllText(Target, Existing);

    [Given("a conflicting migration target and ambient overrides")]
    public void ConflictingTarget()
    {
        File.WriteAllText(Target, "[project]\nsource_directory='wrong'\n");
        _targetBytes = File.ReadAllBytes(Target);
        _environment["MORPHIR_PROJECT__SOURCE_DIRECTORY"] = "masked";
        File.WriteAllText(Path.Combine(_root, "morphir.user.toml"), "[project]\nsource_directory='masked'\n");
    }

    [Given("the migration project is one workspace member")]
    public void Member()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sibling"));
        File.WriteAllText(Path.Combine(_root, "sibling", "morphir.json"), "{name:'Sibling',exposedModules:[]}\n");
        Directory.CreateDirectory(Path.Combine(_root, "member", "elm-src"));
        File.WriteAllText(Path.Combine(_root, "member", "morphir.json"), _source);
        File.WriteAllText(Target, "[workspace]\nmembers=['member','sibling']\nout_dir='base-out'\n");
        _targetBytes = File.ReadAllBytes(Target);
        _working = Path.Combine(_root, "member", "elm-src");
    }

    [When("I migrate the project with {string}")]
    public Task Migrate(string command) => Run(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), true);

    [When("I migrate the project with human output")]
    public Task Human() => Run(["--dry-run"], false);

    private async Task Run(string[] arguments, bool json)
    {
        if (File.Exists(Target)) _targetTimestamp = File.GetLastWriteTimeUtc(Target);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Morphir.slnx"))) repo = repo.Parent;
        var configuration = GetType().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_CLI") ?? Path.Combine(repo!.FullName, "src", "Morphir", "bin", configuration, "net10.0", "morphir.dll");
        var info = new ProcessStartInfo(cli.EndsWith(".dll", StringComparison.Ordinal) ? "dotnet" : cli)
        {
            WorkingDirectory = _working,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (cli.EndsWith(".dll", StringComparison.Ordinal)) info.ArgumentList.Add(cli);
        foreach (var arg in new[] { "project", "migrate" }.Concat(arguments).Concat(json ? ["--json"] : []).Concat(["--discovery-boundary", _root])) info.ArgumentList.Add(arg);
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("MORPHIR_", StringComparison.Ordinal)).ToArray()) info.Environment.Remove(key);
        foreach (var pair in _environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        _exit = process.ExitCode; _stdout = await stdout; _stderr = await stderr;
    }

    [Then("migration exits {int} with outcome {string}")]
    public void Result(int exit, string outcome)
    {
        _exit.Should().Be(exit, _stdout + _stderr);
        _report?.Dispose();
        _report = JsonDocument.Parse(_stdout, new JsonDocumentOptions { MaxDepth = 256 });
        _report.RootElement.GetProperty("exitCode").GetInt32().Should().Be(exit);
        _report.RootElement.GetProperty("outcome").GetString().Should().Be(outcome);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Morphir.slnx"))) repo = repo.Parent;
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(repo!.FullName, "docs", "spec", "schemas", "project-migration-result.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        schema.Evaluate(_report.RootElement).IsValid.Should().BeTrue("every migration response must match the published contract");
    }

    [Then("the migration source remains byte identical")]
    public void SourceUnchanged() => File.ReadAllBytes(Source).Should().Equal(System.Text.Encoding.UTF8.GetBytes(_source));

    [Then("the migration report verifies independent equivalence")]
    public void Verified() => _report!.RootElement.GetProperty("verification").GetProperty("verified").GetBoolean().Should().BeTrue();

    [Then("no migration target exists")]
    public void NoTarget() => File.Exists(Target).Should().BeFalse();

    [Then("migration writes {string} with the legacy defaults")]
    public void Persisted(string relative)
    {
        var path = Path.Combine(_root, relative);
        var document = ((SourceContent.Available)ConfigurationReader.ReadText(path, File.ReadAllText(path)).Content).Item;
        foreach (var (key, expected) in new[] { ("project.source_directory", "elm-src"), ("frontend.language", "elm"), ("pipeline.backend", "checkpoint"), ("pipeline.checkpoint_format", "morphir-json") })
            ConfigTree.tryFind(KeyPath.parse(key).ResultValue, document.Values)!.Value.Should().Be(ConfigValue.NewString(expected));
        ConfigTree.tryFind(ListModule.OfSeq(new[] { "ir", "format_version" }), document.Raw).Should().BeNull();
    }

    [Then("migration preserves the existing equivalent text")]
    public void ExistingText() => File.ReadAllText(Target).Should().Contain("# preserve comment\n[project]\nname = 'Orders' # preserve inline\n").And.Contain("value='keep'");

    [Then("the repeated migration preserves the target timestamp")]
    public void Repeat() => File.GetLastWriteTimeUtc(Target).Should().Be(_targetTimestamp);

    [Then("migration reports blocking diagnostics on stderr")]
    public void BlockingDiagnostics()
    {
        _stderr.Should().NotBeNullOrWhiteSpace();
        _report!.RootElement.GetProperty("diagnostics").EnumerateArray().Should().Contain(d => d.GetProperty("severity").GetString() == "error");
    }

    [Then("migration reports both conflicting values and origins")]
    public void ConflictValues()
    {
        var conflict = _report!.RootElement.GetProperty("conflicts").EnumerateArray().Single(value => value.GetProperty("key").GetString() == "project.source_directory");
        conflict.GetProperty("sourceValue").GetString().Should().Be("elm-src");
        conflict.GetProperty("targetValue").GetString().Should().Be("wrong");
        conflict.GetProperty("source").GetProperty("path").GetString().Should().EndWith("/morphir.json");
        conflict.GetProperty("target").GetProperty("path").GetString().Should().EndWith("/morphir.toml");
    }

    [Then("migration preserves the conflicting target")]
    public void TargetUnchanged() => File.ReadAllBytes(Target).Should().Equal(_targetBytes!);

    [Then("migration changes only the selected member target")]
    public void OnlyMember()
    {
        File.ReadAllBytes(Target).Should().Equal(_targetBytes!);
        File.Exists(Path.Combine(_root, "member", "morphir.toml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "sibling", "morphir.toml")).Should().BeFalse();
        File.ReadAllText(Path.Combine(_root, "sibling", "morphir.json")).Should().Be("{name:'Sibling',exposedModules:[]}\n");
        File.ReadAllText(Path.Combine(_root, "member", "morphir.json")).Should().Be(_source);
    }

    [Then("migration streams redact secret inputs")]
    public void Redacted()
    {
        (_stdout + _stderr).Should().NotContain("never-print-literal").And.NotContain("never-print-reference");
        _stdout.Should().Contain("[REDACTED]");
    }

    [Then("migration human output reports a verified preview")]
    public void HumanResult()
    {
        _exit.Should().Be(0, _stdout + _stderr);
        _stdout.Should().Contain("preview").And.Contain("morphir.toml").And.Contain("verified");
        File.Exists(Target).Should().BeFalse();
    }

    public void Dispose() { _report?.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
