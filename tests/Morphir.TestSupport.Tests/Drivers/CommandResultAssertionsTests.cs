// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The general checks of a command result (design ASRT-1 of the change consolidate-black-box-tests). They read no
/// field of a report: the status of a report belongs to its command.
/// </summary>
public class CommandResultAssertionsTests
{
    private static CommandResult Result(int exitCode = 0, string output = "", string error = "", params string[] written) =>
        new("morphir generate --json", exitCode, output, error, TimeSpan.FromMilliseconds(20), written);

    private static void ShouldFailWithTheCommandTheExitCodeAndBothStreams(Action check, CommandResult result, string reason)
    {
        var message = check.Should().Throw<CommandAssertionException>().Which.Message;
        message.Should().Contain(reason);
        message.Should().Contain("command: " + result.CommandLine);
        message.Should().Contain("exit code: " + result.ExitCode);
        message.Should().Contain("standard output:").And.Contain("standard error:");
        if (result.StandardOutput.Length > 0) message.Should().Contain(result.StandardOutput);
        if (result.StandardError.Length > 0) message.Should().Contain(result.StandardError);
    }

    [Test]
    public void TheExpectedExitCodePasses() => Result(exitCode: 2).ShouldExitWith(2);

    [Test]
    public void AnotherExitCodeFails()
    {
        var result = Result(exitCode: 1, output: "{}", error: "failed to read");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldExitWith(0), result, "The exit code is 1, not 0.");
    }

    [Test]
    public void OneJsonDocumentOnStandardOutputPassesAndGivesTheDocument()
    {
        var document = Result(output: "{ \"success\": true, \"artifacts\": [\"A.cs\"] }\n").ShouldWriteOneJsonDocument();

        document.GetProperty("artifacts")[0].GetString().Should().Be("A.cs");
    }

    [Test]
    public void EmptyStandardOutputFails()
    {
        var result = Result(error: "a log line");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldWriteOneJsonDocument(), result, "Standard output is empty");
    }

    [Test]
    public void TwoDocumentsFail()
    {
        var result = Result(output: "{ \"a\": 1 }\n{ \"b\": 2 }\n");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldWriteOneJsonDocument(), result, "Standard output is not exactly one JSON document");
    }

    [Test]
    public void ALogLineBeforeTheDocumentFails()
    {
        var result = Result(output: "[12:00:01 INF] Generating\n{ \"success\": true }\n");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldWriteOneJsonDocument(), result, "Standard output is not exactly one JSON document");
    }

    [Test]
    public void TextAfterTheDocumentFails()
    {
        var result = Result(output: "{ \"success\": true }\nDone.\n");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldWriteOneJsonDocument(), result, "Standard output is not exactly one JSON document");
    }

    [Test]
    public void AWrittenFilePasses() => Result(written: ["out/A.cs", "out/B.cs"]).ShouldHaveWritten("out/B.cs");

    [Test]
    public void AFileThatTheCommandDidNotWriteFailsAndListsTheWrittenFiles()
    {
        var result = Result(written: ["out/A.cs"]);

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldHaveWritten("out/B.cs"), result, "The command did not write 'out/B.cs'. It wrote: out/A.cs.");
    }

    [Test]
    public void ACommandThatWroteNoFilePasses() => Result().ShouldHaveWrittenNoFile();

    [Test]
    public void AWrittenFileFailsWhenNoneIsExpected()
    {
        var result = Result(written: ["out/A.cs"]);

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.ShouldHaveWrittenNoFile(), result, "The command wrote: out/A.cs.");
    }

    [Test]
    public void TextOnStandardErrorPasses() => Result(error: "error: document-literal at 3:1").StandardErrorShouldContain("document-literal");

    [Test]
    public void TextThatIsOnlyOnStandardOutputFailsTheStandardErrorCheck()
    {
        var result = Result(output: "document-literal");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.StandardErrorShouldContain("document-literal"), result, "Standard error does not contain 'document-literal'.");
    }

    [Test]
    public void TextOnOneOfTheStreamsPassesTheOutputCheck()
    {
        Result(output: "✓ VALID").OutputShouldContain("valid");
        Result(error: "not implemented yet").OutputShouldContain("not implemented yet");
        Result(output: "Schema Version: v3").OutputShouldContainOneOf("INVALID", "schema version");
    }

    [Test]
    public void TextOnNoStreamFailsTheOutputCheck()
    {
        var result = Result(output: "VALID", error: "note");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.OutputShouldContain("Schema Version"), result, "The output does not contain 'Schema Version'.");
        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.OutputShouldContainOneOf("error", "not found"), result, "The output contains none of: 'error', 'not found'.");
    }

    [Test]
    public void NoOutputPassesTheEmptyCheckAndOutputFailsIt()
    {
        Result(output: " \n").OutputShouldBeEmpty();
        var result = Result(error: "a log line");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.OutputShouldBeEmpty(), result, "The command wrote output.");
    }

    [Test]
    public void EmptyStandardOutputPassesAndTextFails()
    {
        Result(error: "a log line").StandardOutputShouldBeEmpty();
        var result = Result(output: "text");

        ShouldFailWithTheCommandTheExitCodeAndBothStreams(() => result.StandardOutputShouldBeEmpty(), result, "Standard output is not empty.");
    }
}
