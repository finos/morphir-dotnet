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

    // A named mother for real scale receipt inputs; no pilot observations are qualification evidence.
    private sealed class ScaleInputs : IDisposable
    {
        private readonly ReplayInputs _inputs = new();
        public string Cases => _inputs.Controls;
        public string Receipt => _inputs.Receipt;
        public string[] Arguments => ["mck", "bounded-generation", "report-check", "--schema", _inputs.Schema,
            "--scale-cases", Cases, "--receipt", Receipt];

        public ScaleInputs(string id, bool missing = true, bool includeSource = true)
        {
            var bytes = FixtureCatalog.ReadBytes(FixtureCatalog.BoundedGenerationReceipts.ScaleCases);
            File.WriteAllBytes(Cases, bytes);
            var source = JsonNode.Parse(bytes)!.AsObject();
            var selected = source["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
            var receipt = JsonNode.Parse(File.ReadAllText(Receipt))!.AsObject();
            receipt["kit"] = new JsonObject
            {
                ["contractSha256"] = source["contractSha256"]!.DeepClone(),
                ["suiteSha256"] = FixtureCatalog.BoundedGenerationReceipts.ScaleCases.Sha256,
                ["caseId"] = id,
            };
            receipt["fixture"] = selected["fixture"]!.DeepClone();
            if (!includeSource)
            {
                receipt["fixture"]!.AsObject().Remove("modelSha256");
                receipt["fixture"]!.AsObject().Remove("documentCount");
            }
            receipt["execution"] = source["execution"]!.DeepClone();
            receipt["target"] = source["target"]!.DeepClone();
            receipt["tuple"] = source["tuple"]!.DeepClone();
            receipt["guest"]!["artifactSha256"] = source["guestSha256"]!.DeepClone();
            receipt["comparison"] = selected["comparison"]!.DeepClone();
            receipt["comparison"]!["observedSha256"] = selected["comparison"]!["expectedSha256"]!.DeepClone();
            foreach (var measurement in receipt["measurements"]!.AsObject().ToArray())
                if (missing)
                    receipt["measurements"]![measurement.Key] = new JsonObject { ["state"] = "missing", ["reason"] = "Not collected." };
            foreach (var limit in receipt["limits"]!.AsObject().ToArray())
                if (limit.Value!["state"]!.GetValue<string>() == "enforced")
                {
                    limit.Value["state"] = "not-enforced";
                    limit.Value.AsObject().Remove("source");
                    limit.Value["reason"] = "Not enforced.";
                }
            receipt["resources"] = new JsonObject
            {
                ["state"] = "not-run",
                ["reasons"] = missing
                    ? new JsonArray("resource.limit_not_enforced", "resource.measurement_missing")
                    : new JsonArray("resource.limit_not_enforced"),
            };
            File.WriteAllText(Receipt, receipt.ToJsonString());
        }

        public void Change(string pointer, string json)
        {
            var receipt = JsonNode.Parse(File.ReadAllText(Receipt))!.AsObject();
            using var patch = JsonDocument.Parse(new JsonObject
            {
                ["op"] = "replace", ["path"] = pointer, ["value"] = JsonNode.Parse(json),
            }.ToJsonString());
            ApplyPatch(receipt, patch.RootElement);
            File.WriteAllText(Receipt, receipt.ToJsonString());
        }
        public void Dispose() => _inputs.Dispose();
    }

    [Test]
    [Arguments("orders-scale/a", true, true)]
    [Arguments("orders-scale/b", true, true)]
    [Arguments("orders-scale/a", false, false)]
    [Arguments("orders-scale/b", false, false)]
    public async Task ScaleCorrectnessDoesNotQualifyMissingOrUnforcedResources(string id, bool missing, bool source)
    {
        using var inputs = new ScaleInputs(id, missing, source);
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        invocation.Result.GetProperty("correctness").GetProperty("state").GetString().Should().Be("passed");
        invocation.Result.GetProperty("resources").GetProperty("state").GetString().Should().Be("not-run");
    }

    [Test]
    [Arguments("/kit/caseId", "\"orders-scale/unknown\"")]
    [Arguments("/kit/caseId", "\"orders-scale/b\"")]
    [Arguments("/kit/contractSha256", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [Arguments("/kit/suiteSha256", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [Arguments("/fixture/scale", "\"small\"")]
    [Arguments("/fixture/expandedInputBytes", "1086620932")]
    [Arguments("/fixture/largestDocumentBytes", "16561")]
    [Arguments("/fixture/modelSha256", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [Arguments("/fixture/documentCount", "68612")]
    [Arguments("/fixture/documentCount", "\"68611\"")]
    [Arguments("/execution", "\"native-generation\"")]
    [Arguments("/target", "\"other\"")]
    [Arguments("/guest/artifactSha256", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [Arguments("/tuple/protocolVersion", "\"0.2.0-draft.1\"")]
    [Arguments("/tuple/accessVersion", "\"0.1.0-draft.2\"")]
    [Arguments("/tuple/operation", "\"other\"")]
    [Arguments("/tuple/irVersion", "\"3.1.0\"")]
    [Arguments("/tuple/profile", "\"other\"")]
    [Arguments("/tuple/codec", "\"morphir-yaml\"")]
    [Arguments("/tuple/layout", "\"single-file\"")]
    [Arguments("/tuple/contextualEncoding", "\"contextual\"")]
    [Arguments("/comparison/kind", "\"diagnostic-facts\"")]
    [Arguments("/comparison/expectedSha256", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    public async Task ScaleSubstitutionsAreRefused(string pointer, string json)
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        inputs.Change(pointer, json);
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Object);
        invocation.Result.GetProperty("correctness").ValueKind.Should().Be(JsonValueKind.Null);
        invocation.Result.GetProperty("resources").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task ScaleSourceAndReceiptCannotChooseTheirOwnExpectation()
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        File.AppendAllText(inputs.Cases, " ");
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("case_expectation_source_mismatch");
    }

    [Test]
    public async Task ControlsAndScaleCasesAreMutuallyExclusive()
    {
        using var inputs = new ReplayInputs();
        var invocation = await Invoke([.. inputs.Arguments, "--scale-cases", inputs.Controls]);
        invocation.ExitCode.Should().Be(2);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.usage");
    }

    [Test]
    public async Task ScaleSelfConsistentWrongOutputCannotReplacePinnedExpectation()
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        var wrong = JsonSerializer.Serialize(new string('a', 64));
        inputs.Change("/comparison/expectedSha256", wrong);
        inputs.Change("/comparison/observedSha256", wrong);
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("expected_digest_mismatch");
    }

    [Test]
    public async Task ScaleStillUsesPinnedSchemaAndOutcomeReplay()
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        inputs.Change("/resources/state", "\"passed\"");
        inputs.Change("/resources/reasons", "[]");
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("outcome_mismatch");
    }

    [Test]
    public async Task MalformedScaleReceiptIsRefused()
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        File.WriteAllText(inputs.Receipt, "{bad json");
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_shape");
    }

    [Test]
    public async Task NeitherCaseSourceIsAUsageErrorWithOtherwiseValidArguments()
    {
        using var inputs = new ReplayInputs();
        var invocation = await Invoke(["mck", "bounded-generation", "report-check",
            "--schema", inputs.Schema, "--receipt", inputs.Receipt]);
        invocation.ExitCode.Should().Be(2);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.usage");
    }

    [Test]
    public async Task ScaleMissingFileIsAnOperationalError()
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        File.Delete(inputs.Cases);
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(2);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("mck.io");
    }

    [Test]
    public async Task ScaleAlteredSchemaIsRefused()
    {
        using var inputs = new ScaleInputs("orders-scale/a");
        var schema = inputs.Arguments[Array.IndexOf(inputs.Arguments, "--schema") + 1];
        File.AppendAllText(schema, " ");
        var invocation = await Invoke(inputs.Arguments);
        invocation.ExitCode.Should().Be(1);
        invocation.Result.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_shape");
    }
}
