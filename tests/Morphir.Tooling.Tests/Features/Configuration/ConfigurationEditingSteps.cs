// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using Morphir.Configuration;
using Morphir.Configuration.IO;

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

    private async Task Run(string[] arguments)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Morphir.slnx"))) repo = repo.Parent;
        var configuration = GetType().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_CLI") ?? Path.Combine(repo!.FullName, "src", "Morphir", "bin", configuration, "net10.0", "morphir.dll");
        var info = new ProcessStartInfo(cli.EndsWith(".dll", StringComparison.Ordinal) ? "dotnet" : cli)
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (cli.EndsWith(".dll", StringComparison.Ordinal)) info.ArgumentList.Add(cli);
        foreach (var arg in new[] { "config" }.Concat(arguments).Concat(["--json", "--discovery-boundary", _root])) info.ArgumentList.Add(arg);
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("MORPHIR_", StringComparison.Ordinal)).ToArray()) info.Environment.Remove(key);
        foreach (var key in new[] { "HOME", "USERPROFILE", "APPDATA", "PROGRAMDATA", "XDG_CONFIG_HOME" }) info.Environment[key] = Path.Combine(_root, "home");
        foreach (var pair in _environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        _exit = process.ExitCode; _stdout = await stdout; _stderr = await stderr;
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

    public void Dispose() { _report?.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
