// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The assertions of the two machine-output forms (adopt-streaming-json, test-driver-contract): <c>--json=document</c>
/// is one JSON document, <c>--json</c> is JSON Lines that end with one <c>completed</c> record, and the report types
/// read either form.
/// </summary>
public class JsonOutputAssertionsTests
{
    private const string Report = "{\"success\":true,\"exitCode\":0,\"diagnostics\":[{\"code\":\"a.b\",\"message\":\"m\"}]}";
    private const string StageStarted = "{\"event\":\"stageStarted\",\"stage\":\"sources\"}";
    private const string StageCompleted = "{\"event\":\"stageCompleted\",\"stage\":\"sources\",\"passed\":true}";
    private const string Completed = "{\"event\":\"completed\",\"report\":" + Report + "}";
    private const string LogLine = "[12:00:01 INF] Compiling";

    private static CommandResult Result(string output, int exitCode = 0) =>
        new("morphir compile --json", exitCode, output, "a log line on stderr", TimeSpan.FromMilliseconds(20), []);

    private static CommandResult Lines(params string[] lines) => Result(string.Join("\n", lines) + "\n");

    private static string ShouldFail(Action check, CommandResult result, string reason)
    {
        var message = check.Should().Throw<CommandAssertionException>().Which.Message;
        message.Should().Contain(reason);
        message.Should().Contain("command: " + result.CommandLine).And.Contain("standard output:");
        if (result.StandardOutput.Length > 0) message.Should().Contain(result.StandardOutput);
        return message;
    }

    // Scenario: A JSON command writes one document

    [Test]
    public void OneDocumentGivesTheReport()
    {
        var report = Result("{\n  \"success\": true,\n  \"exitCode\": 0\n}\n").ReportOfDocument();

        report.GetProperty("exitCode").GetInt32().Should().Be(0);
    }

    [Test]
    public void AnEmptyDocumentOutputFails()
    {
        var result = Result(" \n");

        ShouldFail(() => result.ReportOfDocument(), result, "Standard output is empty");
    }

    [Test]
    public void OutputThatIsNotJsonFailsTheDocument()
    {
        var result = Result("Done.\n");

        ShouldFail(() => result.ReportOfDocument(), result, "Standard output is not exactly one JSON document");
    }

    [Test]
    public void TextAfterTheDocumentFails()
    {
        var result = Result(Report + "\nDone.\n");

        ShouldFail(() => result.ReportOfDocument(), result, "Standard output is not exactly one JSON document");
    }

    [Test]
    public void ACompletedRecordIsAStreamNotADocument()
    {
        var result = Lines(Completed);

        ShouldFail(() => result.ReportOfDocument(), result, "is a completed record of a stream, not a report");
    }

    // Scenario: A log line on standard output fails

    [Test]
    public void ALogLineBeforeTheDocumentFailsAndShowsTheLine()
    {
        var result = Result(LogLine + "\n" + Report + "\n");

        var message = ShouldFail(() => result.ReportOfDocument(), result, "Standard output is not exactly one JSON document");

        message.Should().Contain("line 1: " + LogLine);
    }

    [Test]
    public void ALogLineInAStreamFailsAndShowsTheLine()
    {
        var result = Lines(StageStarted, LogLine, Completed);

        ShouldFail(() => result.ReportOfStream(), result, "Line 2 of standard output is not JSON: " + LogLine);
    }

    // Scenario: A stream gives its report

    [Test]
    public void AStreamGivesTheReportOfItsCompletedRecord()
    {
        var result = Lines(StageStarted, StageCompleted, Completed);

        result.ReportOfStream().GetProperty("diagnostics")[0].GetProperty("code").GetString().Should().Be("a.b");
        result.Events().Select(record => record.GetProperty("event").GetString()).Should().Equal("stageStarted", "stageCompleted", "completed");
    }

    [Test]
    public void TheEventsOfAStreamDeeperThanTheDefaultLimitAreReadWithALargerLimit()
    {
        // A record nested 70 levels deep: over the parser's default limit of 64.
        const int depth = 70;
        var deep = "{\"event\":\"note\",\"value\":" + string.Concat(Enumerable.Repeat("[", depth)) + new string(']', depth) + "}";
        var result = Lines(deep, Completed);

        ShouldFail(() => result.Events(), result, "Line 1 of standard output is not JSON");
        result.Events(maxDepth: depth + 1).Select(record => record.GetProperty("event").GetString()).Should().Equal("note", "completed");
    }

    [Test]
    public void TheEventsOfAStreamWithoutACompletedRecordFail()
    {
        var result = Lines(StageStarted, StageCompleted);

        ShouldFail(() => result.Events(), result, "The stream has no completed record.");
    }

    [Test]
    public void AStreamWithCarriageReturnsGivesItsReport() =>
        Result(StageStarted + "\r\n" + Completed + "\r\n").ReportOfStream().GetProperty("success").GetBoolean().Should().BeTrue();

    [Test]
    public void AStreamWithoutACompletedRecordFails()
    {
        var result = Lines(StageStarted, StageCompleted);

        ShouldFail(() => result.ReportOfStream(), result, "The stream has no completed record.");
    }

    [Test]
    public void AStreamWithTwoCompletedRecordsFails()
    {
        var result = Lines(StageStarted, Completed, Completed);

        ShouldFail(() => result.ReportOfStream(), result, "The stream has 2 completed records, on lines 2, 3. It must have one.");
    }

    [Test]
    public void ACompletedRecordThatIsNotLastFailsAndShowsTheLastLine()
    {
        var result = Lines(Completed, StageStarted);

        ShouldFail(() => result.ReportOfStream(), result, "The completed record is on line 1, not on the last line, 2: " + StageStarted);
    }

    [Test]
    public void ALineWithoutAnEventMemberFailsAndShowsTheLine()
    {
        var result = Lines(StageStarted, "{\"stage\":\"sources\"}", Completed);

        ShouldFail(() => result.ReportOfStream(), result, "Line 2 of standard output is not a JSON object with an event member: {\"stage\":\"sources\"}");
    }

    [Test]
    public void ALineWithTwoValuesFailsAndShowsTheLine()
    {
        var result = Lines(StageStarted + " " + StageCompleted, Completed);

        ShouldFail(() => result.ReportOfStream(), result, "Line 1 of standard output is not JSON: " + StageStarted);
    }

    [Test]
    public void ACompletedRecordWithoutAReportFails()
    {
        var result = Lines("{\"event\":\"completed\"}");

        ShouldFail(() => result.ReportOfStream(), result, "The completed record on line 1 has no report object.");
    }

    [Test]
    public void AnIndentedDocumentIsNotAStream()
    {
        var result = Result("{\n  \"event\": \"completed\",\n  \"report\": {}\n}\n");

        ShouldFail(() => result.ReportOfStream(), result, "Line 1 of standard output is not JSON: {");
    }

    [Test]
    public void AnEmptyStreamFails()
    {
        var result = Result("");

        ShouldFail(() => result.ReportOfStream(), result, "Standard output is empty, not a JSON Lines stream.");
    }

    [Test]
    public void ABlankLineInAStreamFails()
    {
        var result = Lines(StageStarted, "", Completed);

        ShouldFail(() => result.ReportOfStream(), result, "Line 2 of standard output is not JSON: ");
    }

    // A command with nothing to stream: its one completed line is also one JSON document.

    [Test]
    public void TheCompletedLineOfACommandWithNoEventsIsAlsoOneDocument()
    {
        var result = Lines(Completed);

        result.ReportOfStream().GetProperty("exitCode").GetInt32().Should().Be(0);
        result.ShouldWriteOneJsonDocument().GetProperty("event").GetString().Should().Be("completed");
    }

    // The report types read either form, so a driver works before and after its command streams.

    [Test]
    public void AReportReadsTheDocumentAndTheStreamAlike()
    {
        var document = CommandReport.Of(Result(Report + "\n"));
        var stream = CommandReport.Of(Lines(StageStarted, Completed));

        stream.Document.GetRawText().Should().Be(document.Document.GetRawText());
        stream.Diagnostics.Should().Equal(document.Diagnostics);
        stream.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("a.b");
    }

    [Test]
    public void ReportOfEitherFormNamesTheStreamProblem()
    {
        var result = Lines(StageStarted, LogLine, Completed);

        ShouldFail(() => CommandReport.Of(result), result, "Line 2 of standard output is not JSON: " + LogLine);
    }

    [Test]
    public void ReportOfEitherFormNamesTheDocumentProblemWhenTheOutputDoesNotStartWithARecord()
    {
        var result = Result(LogLine + "\n" + Report + "\n");

        var message = ShouldFail(() => CommandReport.Of(result), result, "Standard output is not exactly one JSON document");

        message.Should().Contain("line 1: " + LogLine);
    }

    [Test]
    public void AReportReadsAStreamDeeperThanTheDefaultLimit()
    {
        const int depth = 100;
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);
        var result = Lines("{\"event\":\"completed\",\"report\":{\"exitCode\":0,\"deep\":" + deep + "}}");

        CommandReport.Of(result).Member("deep", "a", "a")?.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
    }

    [Test]
    public void AReportOfAStreamChecksTheExitCode()
    {
        var result = Result(Completed + "\n", exitCode: 1);

        ShouldFail(() => CommandReport.Of(result), result, "exitCode is 0, but the exit code is 1");
    }
}
