// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Mck;

namespace Morphir.Mck.Tests;

/// <summary>In-process parser controls. Only input patches are reproduced; Receipt.check owns all decisions.</summary>
[Category("PlatformSpecific")]
public sealed class BoundedGenerationReportCheckCommandTests
{
    private sealed record Invocation(int ExitCode, JsonElement Result);

    private static async Task<Invocation> Invoke(params string[] arguments)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = await MckApplication.RunAsync(arguments, stdout, stderr);
        using var document = JsonDocument.Parse(stdout.ToString());
        document.RootElement.GetProperty("exitCode").GetInt32().Should().Be(exitCode);
        return new Invocation(exitCode, document.RootElement.Clone());
    }

    // Mother for the frozen replay input files. Every invocation owns a separate sandbox.
    private sealed class ReplayInputs : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("bounded-replay-command-");
        public string Schema { get; }
        public string Controls { get; }
        public string Receipt { get; }

        public ReplayInputs(JsonElement? control = null)
        {
            Schema = _sandbox.PathOf("schema.json");
            Controls = _sandbox.PathOf("controls.json");
            Receipt = _sandbox.PathOf("receipt.json");
            File.WriteAllBytes(Schema, FixtureCatalog.ReadBytes(FixtureCatalog.BoundedGenerationReceipts.Schema));
            File.WriteAllBytes(Controls, FixtureCatalog.ReadBytes(FixtureCatalog.BoundedGenerationReceipts.Controls));
            var sample = JsonNode.Parse(FixtureCatalog.ReadText(FixtureCatalog.BoundedGenerationReceipts.Sample))!.AsObject();
            if (control is { } selected)
                foreach (var operation in selected.GetProperty("patch").EnumerateArray()) ApplyPatch(sample, operation);
            File.WriteAllText(Receipt, sample.ToJsonString());
        }

        public string[] Arguments => ["mck", "bounded-generation", "report-check", "--schema", Schema, "--controls", Controls, "--receipt", Receipt];
        public void Dispose() => _sandbox.Dispose();
    }

    // The same object-member-only RFC 6902 vocabulary used by BoundedGenerationReceiptTests.fs.
    private static void ApplyPatch(JsonObject root, JsonElement operation)
    {
        var path = operation.GetProperty("path").GetString()!;
        path.Should().StartWith("/");
        var parts = path[1..].Split('/').Select(token => token.Replace("~1", "/").Replace("~0", "~")).ToArray();
        var parent = root;
        foreach (var part in parts[..^1]) parent = parent[part]!.AsObject();
        var name = parts[^1];
        switch (operation.GetProperty("op").GetString())
        {
            case "replace":
                parent.ContainsKey(name).Should().BeTrue();
                parent[name] = JsonNode.Parse(operation.GetProperty("value").GetRawText());
                break;
            case "add":
                parent[name] = JsonNode.Parse(operation.GetProperty("value").GetRawText());
                break;
            case "remove":
                parent.Remove(name).Should().BeTrue();
                break;
            default: throw new InvalidOperationException("Unsupported frozen control patch");
        }
    }

    [Test]
    public async Task All19FrozenControlsReplayThroughTheRealParser()
    {
        using var controls = JsonDocument.Parse(FixtureCatalog.ReadBytes(FixtureCatalog.BoundedGenerationReceipts.Controls));
        var cases = controls.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        cases.Should().HaveCount(19);
        foreach (var control in cases)
        {
            using var inputs = new ReplayInputs(control);
            var invocation = await Invoke(inputs.Arguments);
            var expected = control.GetProperty("expected");
            var id = control.GetProperty("id").GetString()!;
            if (expected.TryGetProperty("refusal", out var refusal))
            {
                invocation.ExitCode.Should().Be(1, id);
                invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be(refusal.GetString(), id);
                invocation.Result.GetProperty("correctness").ValueKind.Should().Be(JsonValueKind.Null, id);
                invocation.Result.GetProperty("resources").ValueKind.Should().Be(JsonValueKind.Null, id);
            }
            else
            {
                invocation.Result.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null, id);
                foreach (var name in new[] { "correctness", "resources" })
                {
                    var actualOutcome = invocation.Result.GetProperty(name);
                    var expectedOutcome = expected.GetProperty(name);
                    actualOutcome.GetProperty("state").GetString().Should().Be(expectedOutcome.GetProperty("state").GetString(), id);
                    actualOutcome.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString())
                        .Should().Equal(expectedOutcome.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()), id);
                }
                var bothPassed = expected.GetProperty("correctness").GetProperty("state").GetString() == "passed"
                    && expected.GetProperty("resources").GetProperty("state").GetString() == "passed";
                invocation.ExitCode.Should().Be(bothPassed ? 0 : 1, id);
            }
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("--unknown")]
    [Arguments("--schema")]
    public async Task InvalidInvocationReturnsOneUsageDocument(string extra)
    {
        var extras = extra.Length == 0 ? Array.Empty<string>() : new[] { extra };
        var invocation = await Invoke(["mck", "bounded-generation", "report-check", .. extras]);
        invocation.ExitCode.Should().Be(2);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.usage");
        invocation.Result.GetProperty("correctness").ValueKind.Should().Be(JsonValueKind.Null);
        invocation.Result.GetProperty("resources").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    [Arguments("--unknown")]
    [Arguments("--expected-sha256=9999999999999999999999999999999999999999999999999999999999999999")]
    public async Task UnknownAndExpectationOverrideOptionsAreUsageErrors(string option)
    {
        using var inputs = new ReplayInputs();
        var invocation = await Invoke([.. inputs.Arguments, option]);
        invocation.ExitCode.Should().Be(2);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.usage");
    }
    [Test]
    public async Task MissingFileReturnsOperationalError()
    {
        using var inputs = new ReplayInputs();
        File.Delete(inputs.Schema);
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(2);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.io");
    }

    [Test]
    public async Task SubstitutingControlsAndBothReceiptDigestsCannotReplaceTheExpectation()
    {
        using var inputs = new ReplayInputs();
        foreach (var path in new[] { inputs.Controls, inputs.Receipt })
            File.WriteAllText(path, File.ReadAllText(path).Replace(new string('8', 64), new string('9', 64), StringComparison.Ordinal));
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("case_expectation_source_mismatch");
    }

    [Test]
    public async Task AlteredSchemaIsStillRefusedByTheLandedValidator()
    {
        using var inputs = new ReplayInputs();
        File.AppendAllText(inputs.Schema, " ");
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_shape");
    }

}
