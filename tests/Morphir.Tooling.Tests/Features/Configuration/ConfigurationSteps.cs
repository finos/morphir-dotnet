// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Json.Schema;
using FluentAssertions;
using Morphir.Configuration;

namespace Morphir.Tooling.Tests.Features.Configuration;

[Binding]
public sealed class ConfigurationSteps : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-inspection-" + Guid.NewGuid().ToString("N"));
    private int _exit;
    private string _stdout = "";
    private string _stderr = "";
    private JsonDocument? _json;
    private readonly Dictionary<string, string> _variables = [];

    [Given("an inspection workspace with a member and a personal override")]
    public void Workspace()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "src"));
        Directory.CreateDirectory(Path.Combine(_root, "morphir"));
        File.WriteAllText(Path.Combine(_root, "morphir", "morphir.toml"), "[custom]\nmachine='global'\n");
        File.WriteAllText(Path.Combine(_root, "morphir.toml"), "[workspace]\nmembers=['app']\n[custom]\nwinner='workspace'\nworkspace_only='root'\nselected='workspace'\n");
        File.WriteAllText(Path.Combine(_root, "app", "morphir.toml"), "[project]\nname='App'\n[custom]\nwinner='member'\nmember='only member'\nempty=''\nempty_array=[]\npassword='never-print-password'\n[custom.reference]\ncommand=['never-execute-this-command']\n[codegen.\"custom.generator\"]\nenabled=true\n");
        File.WriteAllText(Path.Combine(_root, "app", "morphir.user.toml"), "[custom]\nwinner='personal'\n");
        File.WriteAllText(Path.Combine(_root, "app", "morphir.profile.ci.toml"), "[custom]\nwinner='profile'\n");
        File.WriteAllText(Path.Combine(_root, "app", "src", "selected.toml"), "[custom]\nwinner='selected'\nselected='selected'\n");
    }

    [Given("the inspection workspace contains sibling YAML")]
    public void Yaml() => File.WriteAllText(Path.Combine(_root, "morphir.yaml"), "custom:\n  yaml_only: true\n");

    [Given("the inspection workspace has an invalid default member")]
    public void InvalidDefaultMember()
    {
        var path = Path.Combine(_root, "morphir.toml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("members=['app']", "members=['app']\ndefault_member='missing'", StringComparison.Ordinal));
    }

    [Given("the inspection member configuration is malformed")]
    public void Malformed() => File.WriteAllText(Path.Combine(_root, "app", "morphir.toml"), "[broken never-print-password");

    [Given("the invocation directory contains malformed host appsettings")]
    public void HostSettings() => File.WriteAllText(Path.Combine(_root, "app", "src", "appsettings.json"), "{not-valid-json");

    [Given("environment profiles contain an empty name")]
    public void InvalidEnvironmentProfile() => _variables["MORPHIR_PROFILES"] = "ci,,metrics";

    [Given("the member contains a TOML table with {int} path segments")]
    public void DeepTable(int depth) => File.WriteAllText(Path.Combine(_root, "app", "morphir.toml"),
        "[project]\nname='App'\n[" + string.Join('.', Enumerable.Repeat("a", depth)) + "]\nx=1\npassword='never-print-deep-secret'\n");

    [Then("the nested table at {int} path segments retains its values")]
    public void DeepTableResult(int depth)
    {
        var leaf = _json!.RootElement.GetProperty("value");
        for (var level = 0; level < depth; level++) leaf = leaf.GetProperty("a");
        leaf.GetProperty("x").GetInt64().Should().Be(1);
        leaf.GetProperty("password").GetString().Should().Be("[REDACTED]");
        (_stdout + _stderr).Should().NotContain("never-print-deep-secret");
    }

    [Given("the member contains nonfinite and temporal values")]
    public void SpecialNumbers() => File.WriteAllText(Path.Combine(_root, "app", "morphir.toml"), """
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

    [When("I inspect with {string}")]
    public Task Inspect(string command) => Run(["config", .. command.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--json", "--discovery-boundary", _root]);

    [When("I inspect a key containing a literal dot")]
    public Task Quoted() => Run(["config", "get", "codegen.\"custom.generator\".enabled", "--json", "--discovery-boundary", _root]);

    [When("I request human inspection output")]
    public Task Human() => Run(["config", "show", "--discovery-boundary", _root]);

    [When("I request inspection help")]
    public Task Help() => Run(["config", "get", "--help"]);

    private static DirectoryInfo RepositoryRoot()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Morphir.slnx"))) repo = repo.Parent;
        repo.Should().NotBeNull();
        return repo!;
    }

    private async Task Run(string[] arguments)
    {
        var repo = RepositoryRoot();
        var configuration = typeof(ConfigurationSteps).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_CLI") ?? Path.Combine(repo!.FullName, "src", "Morphir", "bin", configuration, "net10.0", "morphir.dll");
        var info = new ProcessStartInfo(cli.EndsWith(".dll", StringComparison.Ordinal) ? "dotnet" : cli)
        {
            WorkingDirectory = Path.Combine(_root, "app", "src"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (cli.EndsWith(".dll", StringComparison.Ordinal)) info.ArgumentList.Add(cli);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var variable in info.Environment.Keys.Where(k => k.StartsWith("MORPHIR_", StringComparison.Ordinal)).ToArray()) info.Environment.Remove(variable);
        info.Environment["HOME"] = _root;
        info.Environment["USERPROFILE"] = _root;
        info.Environment["APPDATA"] = _root;
        info.Environment["PROGRAMDATA"] = _root;
        info.Environment["XDG_CONFIG_HOME"] = _root;
        foreach (var variable in _variables) info.Environment[variable.Key] = variable.Value;
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

    [Then("inspection exits with {int} and one JSON result")]
    public void Result(int expected)
    {
        _exit.Should().Be(expected, _stdout + _stderr);
        _json = JsonDocument.Parse(_stdout, new JsonDocumentOptions { MaxDepth = 256 });
        _json.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        // Parallel scenarios must not mutate the library's global schema registry.
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "docs", "spec", "schemas", "config-inspection-result.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        schema.Evaluate(_json.RootElement).IsValid.Should().BeTrue("the command report must match its published JSON schema");
        _json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(expected);
    }

    [Then("special values preserve their canonical kinds")]
    public void SpecialValueResult()
    {
        var value = _json!.RootElement.GetProperty("value").GetProperty("custom");
        value.GetProperty("nan").GetProperty("$number").GetString().Should().Be("NaN");
        value.GetProperty("positive").GetProperty("$number").GetString().Should().Be("Infinity");
        value.GetProperty("negative").GetProperty("$number").GetString().Should().Be("-Infinity");
        value.GetProperty("finite").GetDouble().Should().Be(4.5);
        value.GetProperty("literal").GetProperty("$number").GetString().Should().Be("NaN");
        value.GetProperty("date").GetString().Should().Be("2026-11-03");
        value.GetProperty("secret_nan").GetString().Should().Be("[REDACTED]");
        var types = _json.RootElement.GetProperty("valueTypes").EnumerateArray().ToArray();
        types.Should().Contain(t => t.GetProperty("pointer").GetString() == "/value/custom/nan" && t.GetProperty("kind").GetString() == "number" && t.GetProperty("encoding").GetString() == "nonfinite");
        types.Should().Contain(t => t.GetProperty("pointer").GetString() == "/value/custom/a~1b~0c");
        types.Should().Contain(t => t.GetProperty("pointer").GetString() == "/value/custom/date" && t.GetProperty("kind").GetString() == "temporal" && t.GetProperty("encoding").GetString() == "LocalDate");
        types.Should().NotContain(t => t.GetProperty("pointer").GetString()!.StartsWith("/value/custom/literal", StringComparison.Ordinal));
        types.Should().NotContain(t => t.GetProperty("pointer").GetString()!.Contains("secret_nan", StringComparison.Ordinal));
        types.Should().Contain(t => t.GetProperty("pointer").GetString()!.StartsWith("/entries/", StringComparison.Ordinal));
    }

    [Then("nonfinite explanation metadata identifies its value and contribution")]
    public void NonfiniteExplanation()
    {
        var pointers = _json!.RootElement.GetProperty("valueTypes").EnumerateArray().Select(t => t.GetProperty("pointer").GetString()).ToArray();
        pointers.Should().Contain("/value");
        pointers.Should().Contain(p => p!.StartsWith("/contributions/", StringComparison.Ordinal));
        _json.RootElement.GetProperty("contributions")[0].GetProperty("value").GetProperty("$number").GetString().Should().Be("NaN");
    }

    [Then("the failure is a usage diagnostic without a source read")]
    public void UsageOnly()
    {
        var diagnostics = _json!.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        diagnostics.Should().ContainSingle();
        diagnostics[0].GetProperty("code").GetString().Should().Be("config.usage");
        _json.RootElement.GetProperty("sources").GetArrayLength().Should().Be(0);
    }

    [Then("the inspection result operation is {string}")]
    public void Operation(string operation) => _json!.RootElement.GetProperty("operation").GetString().Should().Be(operation);

    [Then("the inspection value is true")]
    public void True() => _json!.RootElement.GetProperty("value").GetBoolean().Should().BeTrue();

    [Then("the inspection value is {string}")]
    public void Value(string value) => _json!.RootElement.GetProperty("value").GetString().Should().Be(value);

    [Then("the inspection value is an empty array")]
    public void EmptyArray() => _json!.RootElement.GetProperty("value").GetArrayLength().Should().Be(0);

    [Then("the queried key is absent")]
    public void Absent() => _json!.RootElement.GetProperty("found").GetBoolean().Should().BeFalse();

    [Then("inspection diagnostics include {string}")]
    public void IncludesDiagnostic(string code) => _json!.RootElement.GetProperty("diagnostics").EnumerateArray()
        .Select(d => d.GetProperty("code").GetString()).Should().Contain(code);

    [Then("inspection diagnostics exclude {string}")]
    public void ExcludesDiagnostic(string code) => _json!.RootElement.GetProperty("diagnostics").EnumerateArray()
        .Select(d => d.GetProperty("code").GetString()).Should().NotContain(code);

    [Then("the get entry identifies its member origin")]
    public void Origin()
    {
        var origin = _json!.RootElement.GetProperty("entries")[0].GetProperty("origins")[0].GetProperty("source");
        origin.GetProperty("role").GetString().Should().Be("MemberPrimary");
        origin.GetProperty("path").GetString().Should().Be(ProjectPath.join(_root, "app/morphir.toml"));
    }

    [Then("human inspection succeeds without exposing sensitive values")]
    public void HumanResult()
    {
        _exit.Should().Be(0, _stdout + _stderr);
        (_stdout + _stderr).Should().NotContain("never-print-password").And.NotContain("never-execute-this-command");
        _stdout.Should().Contain("[REDACTED]");
    }

    [Then("the explanation identifies workspace and member sources")]
    public void Explanation()
    {
        var contributions = _json!.RootElement.GetProperty("contributions").EnumerateArray().ToArray();
        contributions.Select(c => c.GetProperty("source").GetProperty("path").GetString()).Should().Contain(ProjectPath.join(_root, "morphir.toml")).And.Contain(ProjectPath.join(_root, "app/morphir.toml"));
        contributions.Last().GetProperty("winner").GetBoolean().Should().BeTrue();
    }

    [Then("structured warnings appear in JSON and human diagnostics only on stderr")]
    public void Warning()
    {
        _json!.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()).Should().Contain("config.yaml_beside_toml");
        _stderr.Should().Contain("config.yaml_beside_toml");
    }

    [Then("the mixed format warning is suppressed")]
    public void Suppressed() => _json!.RootElement.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()).Should().NotContain("config.yaml_beside_toml");

    [Then("neither stream exposes secret values or executes the secret command")]
    public void Redacted()
    {
        (_stdout + _stderr).Should().NotContain("never-print-password").And.NotContain("never-execute-this-command");
        _stdout.Should().Contain("[REDACTED]");
    }

    [Then("inspection help succeeds without configuration diagnostics")]
    public void HelpResult()
    {
        _exit.Should().Be(0, _stdout + _stderr);
        (_stdout + _stderr).Should().Contain("--profiles").And.NotContain("config.parse").And.NotContain("never-print-password");
    }

    public void Dispose()
    {
        _json?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
