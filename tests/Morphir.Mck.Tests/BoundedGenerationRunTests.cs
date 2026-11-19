// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.TestSupport.Drivers;
using Morphir.TestSupport.Fixtures;
using Morphir.Toolchain.IO;

namespace Morphir.Mck.Tests;

[Category("PlatformSpecific")]
public sealed class BoundedGenerationRunTests
{
    // Independent oracle bytes supply a controlled peer, never the real adapter's implementation.
    private sealed class Inputs : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("bounded-reader-run-");
        public string Root => _sandbox.Work;
        public string Binary { get; }
        public Inputs()
        {
            Binary = _sandbox.PathOf("adapter.bin");
            File.WriteAllText(Binary, "controlled executable identity");
            foreach (var entry in FixtureCatalog.BoundedOrders.Files)
            {
                var relative = entry.RelativePath[FixtureCatalog.BoundedOrders.Directory.Length..];
                var path = Path.Combine(Root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, FixtureCatalog.ReadBytes(entry));
            }
        }
        public BoundedRunSelection Selection => new(Root, Binary, [], Binary, "test-revision");
        public void Dispose() => _sandbox.Dispose();
    }

    private static byte[] Reply(JsonElement request, Action<JsonObject>? change = null)
    {
        var expected = JsonNode.Parse(FixtureCatalog.ReadText(FixtureCatalog.BoundedOrders.Expected))!.AsObject();
        var facts = new JsonObject
        {
            ["scope"] = "native-reader",
            ["revision"] = request.GetProperty("snapshot").GetString(),
            ["irVersion"] = request.GetProperty("irVersion").GetString(),
            ["pages"] = expected["domainInventory"]!["pages"]!.DeepClone(),
            ["finalPageBody"] = expected["domainInventory"]!["finalPageBody"]!.DeepClone(),
            ["classicAttributes"] = expected["classicAttributes"]!.DeepClone()
        };
        foreach (var field in new[] { "distribution", "package", "pathBudget", "dependencies", "modules" })
            facts[field] = expected[field]!.DeepClone();
        var response = new JsonObject
        {
            ["scope"] = "native-reader-adapter",
            ["caseId"] = request.GetProperty("caseId").GetString(),
            ["binding"] = JsonNode.Parse(request.GetProperty("binding").GetRawText()),
            ["facts"] = facts
        };
        change?.Invoke(response);
        return Encoding.UTF8.GetBytes(response.ToJsonString());
    }

    [Test]
    public async Task MissingExplicitAdapterIsUsageAndNeverLaunches()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = await MckApplication.RunAsync(["bounded-generation", "run"], stdout, stderr);
        exit.Should().Be(2);
        using var result = JsonDocument.Parse(stdout.ToString());
        result.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.usage");
    }

    [Test]
    public async Task AllEightSmallCasesPassCorrectnessWithoutAResourcePass()
    {
        using var inputs = new Inputs();
        var requests = new List<JsonElement>();
        var result = await BoundedGenerationRun.RunAsync(inputs.Selection, (request, _) =>
        {
            requests.Add(request.Clone());
            return Task.FromResult(Reply(request));
        }, CancellationToken.None);
        result.ExitCode.Should().Be(0);
        requests.Should().HaveCount(8);
        using var report = JsonDocument.Parse(result.Output);
        report.RootElement.GetProperty("correctness").GetProperty("state").GetString().Should().Be("passed");
        report.RootElement.GetProperty("resources").GetProperty("state").GetString().Should().Be("not-run");
        report.RootElement.GetProperty("resources").GetProperty("reasons")[0].GetString().Should().Be("resource.small_case");
        report.RootElement.GetProperty("binding").GetProperty("adapterRevision").GetString().Should().Be("test-revision");
        report.RootElement.GetProperty("binding").GetProperty("binarySha256").GetString().Should().HaveLength(64);
    }

    [Test]
    [Arguments("malformed")]
    [Arguments("oversized")]
    [Arguments("mismatch")]
    [Arguments("identity")]
    [Arguments("revision")]
    [Arguments("duplicate")]
    public async Task AnInvalidObservationNeverPasses(string fault)
    {
        using var inputs = new Inputs();
        var result = await BoundedGenerationRun.RunAsync(inputs.Selection, (request, _) => Task.FromResult(fault switch
        {
            "malformed" => "{"u8.ToArray(),
            "oversized" => new byte[BoundedGenerationRun.MaxObservationBytes + 1],
            "duplicate" => "{\"scope\":1,\"scope\":2}"u8.ToArray(),
            "mismatch" => Reply(request, reply => reply["facts"]!["finalPageBody"]!["referencedIntegerLiteral"] = 74),
            "revision" => Reply(request, reply => reply["facts"]!["revision"] = "foreign"),
            _ => Reply(request, reply => reply["binding"]!["binarySha256"] = new string('0', 64))
        }), CancellationToken.None);
        result.ExitCode.Should().Be(1);
        using var report = JsonDocument.Parse(result.Output);
        report.RootElement.GetProperty("correctness").GetProperty("state").GetString().Should().Be("failed");
        report.RootElement.GetProperty("resources").GetProperty("state").GetString().Should().Be("not-run");
    }

    [Test]
    public async Task ProductionDotNetObserverMatchesTheIndependentCorpusInAllEightCases()
    {
        using var inputs = new Inputs();
        var result = await BoundedGenerationRun.RunAsync(inputs.Selection, (request, _) =>
            Task.FromResult(Encoding.UTF8.GetBytes(BoundedGenerationObserveCommand.Observe(request))), CancellationToken.None);
        result.ExitCode.Should().Be(0, result.Output);
    }

    [Test]
    public async Task NativeCaptureRejectsOutputBeyondItsByteBudget()
    {
        using var sandbox = TestSandbox.Create("bounded-adapter-output-");
        using var slot = await ProcessGate.Shared.EnterAsync();
        var start = ProcessHelper.StartInfo("write-both", "100000");
        Func<Task> act = () => AdapterCapture.RunAsync(start.FileName, start.ArgumentList.ToArray(),
            sandbox.Work, [], TimeSpan.FromSeconds(10), 128, CancellationToken.None);
        await act.Should().ThrowAsync<IOException>();
    }

    [Test]
    public async Task NativeDeadlineIncludesPipesInheritedAfterRootExit()
    {
        using var sandbox = TestSandbox.Create("bounded-adapter-drains-");
        using var slot = await ProcessGate.Shared.EnterAsync();
        var script = sandbox.PathOf("script.json");
        File.WriteAllText(script, new JsonObject { ["modules"] = new JsonObject { ["*"] = new JsonArray(new JsonObject { ["holdStdio"] = 30000 }) } }.ToJsonString());
        var fake = Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");
        Func<Task> act = () => AdapterCapture.RunAsync(DotnetHost.Path,
            new[] { "exec", fake, "--script", script, "extension", "worker" }, sandbox.Work,
            [], TimeSpan.FromSeconds(2), 128, CancellationToken.None);
        await act.Should().ThrowAsync<IOException>();
    }

    [Test]
    [Arguments("relative-adapter")]
    [Arguments("")]
    public async Task InvalidExplicitSelectionNeverLaunches(string adapter)
    {
        using var inputs = new Inputs();
        var launched = false;
        var result = await BoundedGenerationRun.RunAsync(inputs.Selection with { Executable = adapter }, (request, _) =>
        {
            launched = true;
            return Task.FromResult(Reply(request));
        }, CancellationToken.None);
        launched.Should().BeFalse();
        result.ExitCode.Should().Be(2);
    }

    [Test]
    public async Task ChangingTheIndependentOracleIsRefusedBeforeLaunch()
    {
        using var inputs = new Inputs();
        File.AppendAllText(Path.Combine(inputs.Root, "expected.json"), " ");
        var launched = false;
        var result = await BoundedGenerationRun.RunAsync(inputs.Selection, (request, _) =>
        {
            launched = true;
            return Task.FromResult(Reply(request));
        }, CancellationToken.None);
        launched.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }
}
