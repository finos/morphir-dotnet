// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Morphir.Compilation;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Features.Compile;

namespace Morphir.Tooling.Tests.Cli;

/// <summary>
/// The shared JSON output writer (adopt-streaming-json, task 2.2; decisions JSON-2 and JSON-4): in stream mode one
/// compact line per event, each flushed before the next call, and one last <c>completed</c> record with the report;
/// in document mode the report alone, as the command's context writes it.
/// </summary>
public sealed class JsonOutputWriterTests
{
    private static readonly SampleReport Report = new(true, 0, "it's \"done\"", ["A.cs", "B.cs"]);

    /// <summary>A text writer that records its whole text at each flush.</summary>
    private sealed class FlushRecordingWriter : StringWriter
    {
        public List<string> Flushed { get; } = [];

        public override void Flush()
        {
            base.Flush();
            Flushed.Add(ToString());
        }
    }

    private static JsonOutputWriter<SampleReport> Writer(TextWriter stdout, JsonOutputMode mode) =>
        new(stdout, mode, SampleJsonContext.Relaxed.SampleReport);

    private static void StageStarted(JsonOutputWriter<SampleReport> writer, string stage) =>
        writer.Event("stageStarted", members => members.WriteString("stage", stage));

    [Test]
    public void TwoEventsThenCompletedGiveThreeLinesEachFlushedBeforeTheNextCall()
    {
        using var stdout = new FlushRecordingWriter();
        var writer = Writer(stdout, JsonOutputMode.Stream);
        const string first = "{\"event\":\"stageStarted\",\"stage\":\"sources\"}\n";
        const string second = "{\"event\":\"stageStarted\",\"stage\":\"frontend\"}\n";
        const string completed =
            "{\"event\":\"completed\",\"report\":{\"success\":true,\"exitCode\":0,\"message\":\"it's \\\"done\\\"\",\"items\":[\"A.cs\",\"B.cs\"]}}\n";

        StageStarted(writer, "sources");
        stdout.Flushed.Should().Equal(first);
        StageStarted(writer, "frontend");
        stdout.Flushed.Should().Equal(first, first + second);
        writer.Completed(Report);

        stdout.Flushed.Should().Equal(first, first + second, first + second + completed);
    }

    [Test]
    public void EachLineIsOneJsonObjectWithAnEventMember()
    {
        using var stdout = new StringWriter();
        var writer = Writer(stdout, JsonOutputMode.Stream);

        StageStarted(writer, "sources");
        writer.Event("diagnostics", members =>
        {
            members.WriteString("stage", "sources");
            members.WriteStartArray("diagnostics");
            members.WriteEndArray();
        });
        writer.Completed(Report);

        var lines = stdout.ToString().Split('\n');
        lines[^1].Should().BeEmpty("the last line ends with a line end");
        lines[..^1].Select(line => JsonDocument.Parse(line).RootElement.GetProperty("event").GetString())
            .Should().Equal("stageStarted", "diagnostics", "completed");
    }

    [Test]
    public void AStreamLineEndsWithALineFeedWhateverTheNewLineOfTheWriter()
    {
        using var stdout = new StringWriter { NewLine = "\r\n" };

        Writer(stdout, JsonOutputMode.Stream).Completed(Report);

        stdout.ToString().Should().EndWith("}\n").And.NotContain("\r");
    }

    [Test]
    public void DocumentModeWritesOnlyTheReportAsTheContextWritesIt()
    {
        using var stdout = new FlushRecordingWriter();
        var writer = Writer(stdout, JsonOutputMode.Document);

        StageStarted(writer, "sources");
        StageStarted(writer, "frontend");
        stdout.Flushed.Should().BeEmpty("document mode has no events");
        writer.Completed(Report);

        var document = JsonSerializer.Serialize(Report, SampleJsonContext.Relaxed.SampleReport) + "\n";
        document.Should().Contain("\n  \"success\": true", "the sample context writes indented JSON");
        stdout.Flushed.Should().Equal(document);
    }

    [Test]
    public void TheCompletedRecordHoldsTheReportOfTheDocument()
    {
        using var stream = new StringWriter();
        using var document = new StringWriter();

        Writer(stream, JsonOutputMode.Stream).Completed(Report);
        Writer(document, JsonOutputMode.Document).Completed(Report);

        using var completed = JsonDocument.Parse(stream.ToString());
        using var alone = JsonDocument.Parse(document.ToString());
        JsonSerializer.Serialize(completed.RootElement.GetProperty("report")).Should().Be(JsonSerializer.Serialize(alone.RootElement));
    }

    [Test]
    public void EventMembersUseTheEscapingOfTheContext()
    {
        using var stdout = new StringWriter();

        Writer(stdout, JsonOutputMode.Stream).Event("note", members => members.WriteString("message", "it's <here>"));

        stdout.ToString().Should().Be("{\"event\":\"note\",\"message\":\"it's <here>\"}\n");
    }

    [Test]
    public void AReportAsDeepAsItsContextAllowsIsWrittenInBothModes()
    {
        // A report at the context's depth limit: the completed record must not add a level to it.
        var deepest = Nested(SampleJsonContext.DeepMaxDepth);
        using var stream = new StringWriter();
        using var document = new StringWriter();

        new JsonOutputWriter<JsonElement>(stream, JsonOutputMode.Stream, SampleJsonContext.Deep.JsonElement).Completed(deepest);
        new JsonOutputWriter<JsonElement>(document, JsonOutputMode.Document, SampleJsonContext.Deep.JsonElement).Completed(deepest);

        var options = new JsonDocumentOptions { MaxDepth = SampleJsonContext.DeepMaxDepth + 1 };
        using var completed = JsonDocument.Parse(stream.ToString(), options);
        completed.RootElement.GetProperty("report").GetRawText().Should().Be(deepest.GetRawText());
        document.ToString().Should().Be(deepest.GetRawText() + "\n");
    }

    [Test]
    public void CompletedIsWrittenOnceAndLast()
    {
        using var stdout = new StringWriter();
        var writer = Writer(stdout, JsonOutputMode.Stream);
        writer.Completed(Report);

        var again = () => writer.Completed(Report);
        var after = () => StageStarted(writer, "sources");

        again.Should().Throw<InvalidOperationException>();
        after.Should().Throw<InvalidOperationException>();
        stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
    }

    [Test]
    public void AnEventCannotBeNamedCompleted()
    {
        var writer = Writer(new StringWriter(), JsonOutputMode.Stream);

        var completed = () => writer.Event("completed", _ => { });

        completed.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AWriterNeedsAJsonMode()
    {
        var none = () => Writer(new StringWriter(), JsonOutputMode.None);

        none.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void TheCompileDocumentIsTheJsonOfTheReport()
    {
        // --json=document must give the output of the old --json exactly.
        var report = CompileReport.Invocation([Diagnostic.Create(DiagnosticSeverity.Error, CompileCodes.InvalidArguments, "Use 'one' option.", null)]);
        using var stdout = new StringWriter();

        new JsonOutputWriter<CompileReport>(stdout, JsonOutputMode.Document, CompileReport.JsonInfo).Completed(report);

        stdout.ToString().Should().Be(report.ToJson() + "\n");
    }

    private static JsonElement Nested(int depth)
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = depth + 1 });
        return document.RootElement.Clone();
    }
}

/// <summary>A report of a test command.</summary>
public sealed record SampleReport(bool Success, int ExitCode, string Message, string[] Items);

/// <summary>The source-generated contract of <see cref="SampleReport"/>: camelCase and indented, like compile's.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SampleReport))]
[JsonSerializable(typeof(JsonElement))]
internal partial class SampleJsonContext : JsonSerializerContext
{
    /// <summary>The depth limit of <see cref="Deep"/>, above the default of 64.</summary>
    public const int DeepMaxDepth = 80;

    /// <summary>The context with relaxed escaping, as the commands use it.</summary>
    public static SampleJsonContext Relaxed { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>A compact context with a depth limit above the default, as the configuration reports have.</summary>
    public static SampleJsonContext Deep { get; } = new(new JsonSerializerOptions { MaxDepth = DeepMaxDepth });
}
