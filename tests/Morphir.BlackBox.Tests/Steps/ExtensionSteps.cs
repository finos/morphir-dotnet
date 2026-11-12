// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps of <c>extension list</c> and <c>extension runtimes</c>, and of their reports.</summary>
[Binding]
public sealed class ExtensionSteps(IExtensionDriver extensions, ScenarioRun run)
{
    private ExtensionListReport List => ExtensionListReport.Of(run.Result);

    private ExtensionRuntimesReport Runtimes => ExtensionRuntimesReport.Of(run.Result);

    /// <summary><paramref name="options"/> is empty, <c>--local</c>, or <c>--platform</c> and a platform.</summary>
    [When("I list the extensions as JSON with the options {string}")]
    public async Task ListAsJson(string options)
    {
        var words = options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var platform = Array.IndexOf(words, "--platform");
        run.Result = await extensions.ListAsync(new ExtensionListOptions
        {
            Local = words.Contains("--local"),
            Platform = platform >= 0 ? words[platform + 1] : null,
        });
    }

    [When("I list the extensions as text")]
    public async Task ListAsText() => run.Result = await extensions.ListAsync(new ExtensionListOptions { Json = false });

    [When("I report the extension runtimes as JSON")]
    public async Task RuntimesAsJson() => run.Result = await extensions.RuntimesAsync();

    [When("I report the extension runtimes as text")]
    public async Task RuntimesAsText() => run.Result = await extensions.RuntimesAsync(json: false);

    [Then("the extension list report has the host {string} and the extensions {string}")]
    public void TheListHasTheHostAndTheExtensions(string host, string ids)
    {
        var report = List;
        report.Check(report.Host == host, $"The host of the extension list is {report.Host ?? "null"}, not {host}.");
        var listed = report.Extensions.Select(extension => extension.Id).ToArray();
        report.Check(listed.SequenceEqual(ids.Split(", ")), $"The extension list has {string.Join(", ", listed)}, not {ids}.");
    }

    [Then("the extension list report states no deployment and the execution {string}")]
    public void TheListStatesNoDeployment(string execution)
    {
        var report = List;
        report.Check(!report.HasDeployment, "The extension list report states a deployment.");
        report.Check(report.Execution == execution, $"The execution of the extension list is {report.Execution ?? "null"}, not {execution}.");
    }

    [Then("the extension list report states no deployment")]
    public void TheListStatesNoDeploymentAtAll() => List.Check(!List.HasDeployment, "The extension list report states a deployment.");

    [Then("the extension {string} is not available, and its reason names {string}")]
    public void TheExtensionIsNotAvailable(string id, string text)
    {
        var report = List;
        var extension = report.Extensions.SingleOrDefault(item => item.Id == id);
        report.Check(extension is { Available: false } && extension.Reason?.Contains(text, StringComparison.Ordinal) == true,
            $"The extension {id} is available, or its reason does not name {text}.");
    }

    [Then("the extension list text has a row for each extension")]
    public void TheTextHasARowForEachExtension(Table rows)
    {
        var output = run.Result.StandardOutput;
        var lines = output.Replace("\r\n", "\n").Split('\n').Where(row => row.StartsWith('│')).ToArray();
        var expected = rows.Rows.ToArray();
        Check(lines.Length == expected.Length + 1, $"The table has {lines.Length} rows, not a header and {expected.Length} extensions.");
        for (var index = 0; index < expected.Length; index++)
        {
            var row = lines[index + 1];
            Check(row.StartsWith($"│ {expected[index]["id"]} ", StringComparison.Ordinal), $"Row {index + 1} is not the extension {expected[index]["id"]}.");
            if (expected[index]["type"] is { Length: > 0 } type) Check(row.Contains($"│ {type} ", StringComparison.Ordinal), $"Row {index + 1} has not the type {type}.");
        }

        Check(!output.Contains("\u001b[", StringComparison.Ordinal), "Redirected output is not plain text: it has an ANSI code.");
    }

    [Then("the extension runtimes report has the host {string} and the platform of this machine")]
    public void TheRuntimesHaveTheHostAndThePlatform(string host)
    {
        var report = Runtimes;
        report.Check(report.Host == host, $"The host of the runtimes report is {report.Host ?? "null"}, not {host}.");
        report.Check(report.Platform == extensions.Platform, $"The platform of the runtimes report is {report.Platform ?? "null"}, not {extensions.Platform}.");
    }

    /// <summary>The WASM runtime is available, except on the platform where the product refuses it before it loads.</summary>
    [Then("the WASM runtime of the report has the engine version {string} and is available on this machine unless it is win-x64")]
    public void TheWasmRuntimeIsAvailable(string version)
    {
        var report = Runtimes;
        report.Check(report.Runtimes.Count > 0, "The runtimes report has no runtime.");
        var wasm = report.Runtimes[0];
        var withoutWasm = extensions.PlatformHasNoWasm;
        report.Check(wasm.Available == !withoutWasm, $"The WASM runtime is {(wasm.Available ? "available" : "unavailable")} on {extensions.Platform}.");
        if (withoutWasm) report.Check(wasm.Reason == "win_x64_trap_unsupported", $"The reason is {wasm.Reason ?? "null"}, not win_x64_trap_unsupported.");
        report.Check(wasm.EngineVersion == version, $"The engine version is {wasm.EngineVersion ?? "null"}, not {version}.");
    }

    [Then("the runtimes text names the WASM runtime of this machine and the engine {string}")]
    public void TheRuntimesTextNamesTheRuntime(string engine)
    {
        var output = run.Result.StandardOutput;
        var state = extensions.PlatformHasNoWasm ? "wasm: unavailable (win_x64_trap_unsupported)" : "wasm: available";
        Check(output.Contains(state, StringComparison.Ordinal), $"Standard output does not contain '{state}'.");
        Check(output.Contains($"Engine: {engine}", StringComparison.Ordinal), $"Standard output does not name the engine {engine}.");
    }

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
