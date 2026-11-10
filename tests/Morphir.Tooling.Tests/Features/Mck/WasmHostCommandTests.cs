// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.Conformance.WasmHost;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Mck;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Mck;

/// <summary>
/// The experimental <c>morphir mck wasm-host run</c> and <c>report-check</c> commands (add-wasm-extension-host,
/// task 1.5): exit statuses 0, 1 and 2, one JSON document on stdout, diagnostics on stderr, and help that says
/// experimental. The runner itself is tested in tests/Morphir.Conformance.Tests.
/// </summary>
public sealed class WasmHostCommandTests
{
    private const string Identity = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly string Root = FindRoot(AppContext.BaseDirectory);
    private static readonly string Kit = Path.Combine(Root, "conformance", "suites", "wasm-host");
    private static readonly string FakeWorker = Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");
    private static readonly string Dotnet = Environment.ProcessPath is { } path && Path.GetFileNameWithoutExtension(path) == "dotnet" ? path : "dotnet";

    private static string FindRoot(string directory) =>
        File.Exists(Path.Combine(directory, "Morphir.slnx")) ? directory : FindRoot(Directory.GetParent(directory)!.FullName);

    private sealed record Invocation(int ExitCode, string Stdout, string Stderr);

    private static async Task<Invocation> InvokeAsync(params string[] arguments)
    {
        var stdout = new StringBuilder();
        var stderr = new StringWriter();
        var root = new RootCommand();
        root.Subcommands.Add(MckCommandProvider.Create(line => stdout.AppendLine(line), stderr));
        var parsed = root.Parse(arguments);
        using var gate = await ProcessGate.Shared.EnterAsync();
        var exit = parsed.Errors.Count > 0 && parsed.CommandResult.Command is IMorphirParseErrorHandler handler
            ? handler.HandleParseErrors(parsed)
            : await parsed.InvokeAsync();
        return new Invocation(exit, stdout.ToString(), stderr.ToString());
    }

    private static JsonObject Document(Invocation invocation)
    {
        var parse = () => JsonNode.Parse(invocation.Stdout);
        parse.Should().NotThrow("stdout holds only the result: {0}", invocation.Stdout);
        return parse()!.AsObject();
    }

    /// <summary>A fake host whose script answers every module with these actions.</summary>
    private static string[] FakeHost(TestSandbox sandbox, JsonArray actions)
    {
        var script = sandbox.PathOf("script.json");
        File.WriteAllText(script, new JsonObject { ["buildIdentity"] = Identity, ["modules"] = new JsonObject { ["*"] = actions } }.ToJsonString());
        return ["--host", Dotnet, "--host-arg", "exec", "--host-arg", FakeWorker, "--host-arg", "--script", "--host-arg", script];
    }

    private static JsonArray Refusal() =>
    [
        new JsonObject
        {
            ["event"] = new JsonObject
            {
                ["event"] = "refused",
                ["error"] = new JsonObject { ["code"] = "extension.abi_unsupported", ["message"] = "env.open_socket" },
            },
        },
        new JsonObject { ["exit"] = 3 },
    ];

    /// <summary>A checkout in a sandbox whose corpus holds one case, with its module and the suite generated from it.</summary>
    private static string OneCaseKit(TestSandbox sandbox, string caseId)
    {
        var fixtures = Path.Combine("tests", "Fixtures", "MepWasm");
        Directory.CreateDirectory(sandbox.PathOf(fixtures, "fixtures", "wasm"));
        File.Copy(Path.Combine(Root, fixtures, "PROVENANCE.md"), sandbox.PathOf(fixtures, "PROVENANCE.md"));
        var corpus = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, fixtures, "wasm-host.cases.json")))!.AsObject();
        var cases = corpus["cases"]!.AsArray();
        foreach (var other in cases.Where(node => node!["caseId"]!.GetValue<string>() != caseId).ToList()) cases.Remove(other);
        var module = cases.Single()!["module"]!.GetValue<string>();
        var wasm = corpus["modules"]![module]!["wasm"]!.GetValue<string>();
        File.WriteAllText(sandbox.PathOf(fixtures, "wasm-host.cases.json"), corpus.ToJsonString());
        File.Copy(Path.Combine(Root, fixtures, wasm), sandbox.PathOf([fixtures, .. wasm.Split('/')]));
        var suite = sandbox.PathOf("conformance", "suites", "wasm-host");
        Directory.CreateDirectory(suite);
        foreach (var file in Directory.GetFiles(Kit)) File.Copy(file, Path.Combine(suite, Path.GetFileName(file)));
        Generation.writeRepository(sandbox.Root).IsOk.Should().BeTrue("the reduced suite generates");
        return suite;
    }

    [Test]
    [Arguments("mck")]
    [Arguments("mck wasm-host")]
    [Arguments("mck wasm-host run")]
    [Arguments("mck wasm-host report-check")]
    public void EveryCommandIsMarkedExperimentalAndInternal(string path)
    {
        Command command = MckCommandProvider.Create(_ => { }, TextWriter.Null);
        foreach (var name in path.Split(' ').Skip(1)) command = command.Subcommands.Single(sub => sub.Name == name);
        command.Description.Should().StartWith("[experimental, internal]");
    }

    [Test]
    public async Task RunWithoutItsArgumentsExitsTwoWithOneJsonDocument()
    {
        var result = await InvokeAsync("mck", "wasm-host", "run");
        result.ExitCode.Should().Be(2);
        var document = Document(result);
        document["experimental"]!.GetValue<bool>().Should().BeTrue();
        document["error"]!["code"]!.GetValue<string>().Should().Be("mck.usage");
        result.Stderr.Should().Contain("--kit is required");
    }

    [Test]
    public async Task RunWithoutTheHostsPlatformExitsTwoBeforeAnyCase()
    {
        await using var sandbox = TestSandbox.Create("morphir-mck-run-");
        var report = sandbox.PathOf("wasm-host.json");
        var result = await InvokeAsync([
            "mck", "wasm-host", "run", "--kit", Kit, .. FakeHost(sandbox, Refusal()), "--offering", "test", "--engine", "fake", "--report", report
        ]);
        result.ExitCode.Should().Be(2);
        Document(result)["error"]!["code"]!.GetValue<string>().Should().Be("mck.usage");
        result.Stderr.Should().Contain("--platform is required").And.Contain("a worker reports none of them");
        File.Exists(report).Should().BeFalse("no case ran");
    }

    [Test]
    public async Task AnUnknownOptionExitsTwoWithOneJsonDocument()
    {
        var result = await InvokeAsync("mck", "wasm-host", "report-check", "--no-such-option");
        result.ExitCode.Should().Be(2);
        Document(result)["error"]!["code"]!.GetValue<string>().Should().Be("mck.usage");
    }

    [Test]
    public async Task ReportCheckOfAMissingReportExitsTwo()
    {
        var result = await InvokeAsync("mck", "wasm-host", "report-check", "--kit", Kit, "--report", Path.Combine(Root, "no-such-report.json"));
        result.ExitCode.Should().Be(2);
        Document(result)["exitCode"]!.GetValue<int>().Should().Be(2);
    }

    [Test]
    public async Task RunExitsZeroWhenEveryApplicableCasePassedAndReportCheckAgrees()
    {
        await using var sandbox = TestSandbox.Create("morphir-mck-run-");
        var kit = OneCaseKit(sandbox, "classification/unknown-namespace");
        var report = sandbox.PathOf("out", "wasm-host.json");
        var run = await InvokeAsync([
            "mck", "wasm-host", "run", "--kit", kit, .. FakeHost(sandbox, Refusal()), "--offering", "test", "--platform", "test-platform", "--engine", "fake", "--report", report
        ]);
        run.ExitCode.Should().Be(0, run.Stderr);
        var document = Document(run);
        document["totals"]!["passed"]!.GetValue<int>().Should().Be(1);
        document["report"]!.GetValue<string>().Should().Be(report);
        run.Stderr.Should().Contain("classification/unknown-namespace: passed", "progress goes to stderr");

        var check = await InvokeAsync("mck", "wasm-host", "report-check", "--kit", kit, "--report", report);
        check.ExitCode.Should().Be(0, check.Stderr);
        Document(check)["valid"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task RunOfTheWholeKitExitsOneWhenCasesFailAndReportCheckRecomputesIt()
    {
        await using var sandbox = TestSandbox.Create("morphir-mck-run-");
        var report = sandbox.PathOf("wasm-host.json");
        var run = await InvokeAsync([
            "mck", "wasm-host", "run", "--kit", Kit, .. FakeHost(sandbox, Refusal()), "--offering", "test", "--platform", "test-platform", "--engine", "fake", "--report", report
        ]);
        run.ExitCode.Should().Be(1);
        var totals = Document(run)["totals"]!;
        totals["cases"]!.GetValue<int>().Should().Be(59, "the corpus at finos/morphir 938f798 has 59 cases");
        totals["notApplicable"]!.GetValue<int>().Should().BeGreaterThan(0, "the byte-profile cases, since the host declares only Extism");
        totals["failed"]!.GetValue<int>().Should().BeGreaterThan(0);
        File.Exists(report).Should().BeTrue();

        var check = await InvokeAsync("mck", "wasm-host", "report-check", "--kit", Kit, "--report", report);
        check.ExitCode.Should().Be(1, "the recomputed outcomes have failures");
        Document(check)["valid"]!.GetValue<bool>().Should().BeTrue(check.Stderr);

        var json = JsonNode.Parse(File.ReadAllText(report))!.AsObject();
        json["cases"]!.AsArray().RemoveAt(0);
        File.WriteAllText(report, json.ToJsonString());
        var edited = await InvokeAsync("mck", "wasm-host", "report-check", "--kit", Kit, "--report", report);
        edited.ExitCode.Should().Be(1);
        Document(edited)["valid"]!.GetValue<bool>().Should().BeFalse();
        edited.Stderr.Should().Contain("has no result for the case wasm-host-");
    }

    [Test]
    [Arguments(CliHost.Morphir)]
    [Arguments(CliHost.DotnetTool)]
    public async Task BothHostsHaveTheExperimentalCommands(CliHost host)
    {
        var helper = new CliTestHelper(null, host);
        var help = await helper.ExecuteCommandAsync("mck wasm-host run --help", timeout: TimeSpan.FromMinutes(5));
        help.ExitCode.Should().Be(0, help.CombinedOutput);
        // Help goes to stderr in this CLI, as for every command; stdout stays free for results.

        var usage = await helper.ExecuteCommandAsync("mck wasm-host report-check", timeout: TimeSpan.FromMinutes(5));
        usage.ExitCode.Should().Be(2, usage.CombinedOutput);
        using var document = JsonDocument.Parse(usage.StandardOutput);
        document.RootElement.GetProperty("command").GetString().Should().Be("mck wasm-host report-check");
    }
}
