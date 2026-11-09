// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.IR;
using Path = System.IO.Path;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Generate;

namespace Morphir.Tooling.Tests.Features.TaskRecords;

/// <summary>
/// Spec: compile-task-records, "Records follow the ecosystem schema". The oracle is the record example
/// pinned from finos/morphir (tests/Fixtures/MorphirOut), never output of this codec.
/// </summary>
public sealed class TaskRecordCodecTests
{
    private static string UpstreamRecordText() =>
        File.ReadAllText(Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirOut", "compile-record.json"));

    private static TaskRecord Decode(string text)
    {
        var decoding = TaskRecordCodec.Decode(Encoding.UTF8.GetBytes(text));
        return decoding.Should().BeOfType<RecordDecoding.Decoded>().Subject.Record;
    }

    [Test]
    public async Task TheUpstreamRecordExampleDecodesToItsFields()
    {
        var record = Decode(UpstreamRecordText());

        record.Task.Should().Be(TaskId.Compile);
        record.Module.Value.Should().Be("packages/orders");
        record.Language.Should().Be("gleam");
        record.Inputs.Should().BeEmpty();
        record.Value.Select(path => path.Value).Should().Equal("morphir-ir");
        record.Ir!.Path.Value.Should().Be("morphir-ir");
        record.Ir.Layout.Should().Be(IrLayout.DocumentTree);
        record.Ir.Format.Should().Be(IrStorageFormat.Json);
        record.Ir.Version.Version.Should().Be(Versioning.FormatVersion.NewClassic(4));
        record.Ir.Spelling.Should().Be("v4");
        record.Installed.Keys.Should().Equal("/abs/dist");
        record.Installed["/abs/dist"].Select(path => path.Value).Should().Equal("morphir-ir/manifest.json", "morphir-ir/Module.json");
        record.Tombstone.Should().BeFalse();
        record.CompletedAt.Text.Should().Be("2026-09-02T10:00:00Z");
        record.Unknown.Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task TheUpstreamRecordExampleRoundTripsStructurally()
    {
        var written = TaskRecordCodec.Encode(Decode(UpstreamRecordText()));

        JsonNode.DeepEquals(JsonNode.Parse(written), JsonNode.Parse(UpstreamRecordText())).Should().BeTrue(Encoding.UTF8.GetString(written));
        await Task.CompletedTask;
    }

    [Test]
    public async Task UnknownMembersArePreservedOnRewrite()
    {
        var text = UpstreamRecordText().TrimEnd().TrimEnd('}') + """, "inputsHash": "sha256:abc", "zeta": {"nested": [1, 2.50]}}""";
        var record = Decode(text);
        record.Unknown.Select(member => member.Key).Should().Equal("inputsHash", "zeta");

        var rewritten = JsonNode.Parse(TaskRecordCodec.Encode(record))!.AsObject();
        rewritten["inputsHash"]!.GetValue<string>().Should().Be("sha256:abc");
        rewritten["zeta"]!.ToJsonString().Should().Be("""{"nested":[1,2.50]}""");
        await Task.CompletedTask;
    }

    [Test]
    public async Task AnOrdinaryRecordHasNoTombstoneMemberAndATombstoneSaysSo()
    {
        var record = Decode(UpstreamRecordText());
        JsonNode.Parse(TaskRecordCodec.Encode(record))!.AsObject().ContainsKey("tombstone").Should().BeFalse();

        var tombstone = record.ToTombstone();
        var written = JsonNode.Parse(TaskRecordCodec.Encode(tombstone))!.AsObject();
        written["tombstone"]!.GetValue<bool>().Should().BeTrue();
        Decode(written.ToJsonString()).Tombstone.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ATombstoneKeepsTheLedgerAndTimeAndDropsTheProduct()
    {
        var text = UpstreamRecordText().Replace("\"inputs\": [],", "\"inputs\": [\"src/App.fsproj\"],").TrimEnd().TrimEnd('}')
            + """, "inputsHash": "sha256:abc"}""";
        var source = Decode(text);
        source.Inputs.Should().Equal("src/App.fsproj");
        var written = JsonNode.Parse(TaskRecordCodec.Encode(source.ToTombstone()))!.AsObject();

        written.Select(member => member.Key).Should().Equal(
            "schema", "task", "module", "inputs", "value", "installed", "tombstone", "completedAt");
        written["inputs"]!.AsArray().Should().BeEmpty();
        written["value"]!.AsArray().Should().BeEmpty();
        written["installed"]!.ToJsonString().Should().Be("""{"/abs/dist":["morphir-ir/manifest.json","morphir-ir/Module.json"]}""");
        written["completedAt"]!.GetValue<string>().Should().Be("2026-09-02T10:00:00Z");
        await Task.CompletedTask;
    }

    [Test]
    public async Task MembersAreWrittenInTheUpstreamOrder()
    {
        var text = UpstreamRecordText().TrimEnd().TrimEnd('}') + """, "inputsHash": "sha256:abc"}""";
        var written = JsonNode.Parse(TaskRecordCodec.Encode(Decode(text)))!.AsObject();

        written.Select(member => member.Key).Should().Equal(
            "schema", "task", "module", "language", "inputs", "value", "ir", "installed", "completedAt", "inputsHash");
        written["schema"]!.GetValue<int>().Should().Be(1);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("v3", "3")]
    [Arguments("v4", "4")]
    [Arguments("v4.0.0", "4.0.0")]
    [Arguments("v4.1.0-draft.1", "4.1.0-draft.1")]
    public async Task IrVersionsKeepTheirRecordedSpelling(string spelling, string declared)
    {
        var record = Decode(UpstreamRecordText().Replace("\"v4\"", $"\"{spelling}\""));
        record.Ir!.Version.Text.Should().Be(declared);
        JsonNode.Parse(TaskRecordCodec.Encode(record))!["ir"]!["version"]!.GetValue<string>().Should().Be(spelling);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("3", "v3")]
    [Arguments("4", "v4")]
    [Arguments("4.0.0", "v4")]
    [Arguments("4.1.0-draft.1", "v4.1.0-draft.1")]
    public async Task ANewDescriptorUsesTheUpstreamSpellingOfTheProducedVersion(string produced, string spelling)
    {
        var version = Versioning.parse(produced).ResultValue;
        IrDescriptor.Create(RecordPath.Parse("morphir-ir.json"), IrLayout.SingleFile, IrStorageFormat.Json, version)
            .Spelling.Should().Be(spelling);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("not json")]
    [Arguments("[]")]
    [Arguments("""{"schema": 2, "task": "compile", "module": "", "completedAt": "2026-09-02T10:00:00Z"}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "", "completedAt": "2026-09-02T10:00:00Z", "schema": 1}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "../up", "completedAt": "2026-09-02T10:00:00Z"}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "", "value": ["../escape"], "completedAt": "2026-09-02T10:00:00Z"}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "", "installed": {"/t": ["/etc/passwd"]}, "completedAt": "2026-09-02T10:00:00Z"}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "", "ir": {"path": "x.json", "layout": "vfs", "format": "json", "version": "v4"}, "completedAt": "2026-09-02T10:00:00Z"}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "", "ir": {"path": "x.json", "layout": "single-file", "format": "json", "version": "4"}, "completedAt": "2026-09-02T10:00:00Z"}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": ""}""")]
    [Arguments("""{"schema": 1, "task": "compile", "module": "", "completedAt": "yesterday"}""")]
    public async Task ARecordThatIsNotSchemaOneIsRefused(string text)
    {
        TaskRecordCodec.Decode(Encoding.UTF8.GetBytes(text)).Should().BeOfType<RecordDecoding.Invalid>()
            .Which.Reason.Should().NotBeNullOrWhiteSpace();
        await Task.CompletedTask;
    }

    [Test]
    public async Task AMinimalRecordReadsWithDefaults()
    {
        var record = Decode("""{"schema": 1, "task": "compile", "module": "", "completedAt": "2026-09-02T10:00:00Z"}""");
        record.Inputs.Should().BeEmpty();
        record.Value.Should().BeEmpty();
        record.Ir.Should().BeNull();
        record.Language.Should().BeNull();
        record.Installed.Should().BeEmpty();
        record.Tombstone.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task EncodingIsIndentedWithLineFeedsAndDeterministic()
    {
        var record = Decode(UpstreamRecordText());
        var first = TaskRecordCodec.Encode(record);
        first.Should().Equal(TaskRecordCodec.Encode(record));
        Encoding.UTF8.GetString(first).Should().NotContain("\r").And.StartWith("{\n  \"schema\": 1,");
        using var document = JsonDocument.Parse(first);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        await Task.CompletedTask;
    }
}
