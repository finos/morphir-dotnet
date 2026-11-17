using System.CommandLine;
using System.CommandLine.Parsing;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Cli;

namespace Morphir.Tooling.Tests.Cli;

/// <summary>
/// The <c>--json[=stream|document]</c> option and the bare-flag rewrite (adopt-streaming-json, task 2.1). Each row
/// of the probe table of design decision JSON-3 has a test here, on a throwaway command with the option, with or
/// without a positional input.
/// </summary>
public sealed class OptionValueRewriteTests
{
    private static readonly IReadOnlyDictionary<string, string> JsonDefault =
        new Dictionary<string, string> { ["--json"] = "stream" };

    private sealed record Probe(Command Command, Option<string> Json, Argument<string[]>? Input);

    private static Probe WithInput(string? subcommand = null)
    {
        var json = JsonOutputOption.Create();
        var input = new Argument<string[]>("input") { Arity = ArgumentArity.ZeroOrMore };
        Command command = subcommand is null ? new RootCommand() : new Command(subcommand);
        command.Options.Add(json);
        command.Arguments.Add(input);
        return new Probe(command, json, input);
    }

    private static Probe WithoutInput()
    {
        var json = JsonOutputOption.Create();
        return new Probe(new RootCommand { json }, json, null);
    }

    private static ParseResult ParseRewritten(Probe probe, params string[] args) =>
        probe.Command.Parse(OptionValueRewrite.Apply(args, JsonDefault));

    private static string[] InputOf(Probe probe, ParseResult result) => result.GetValue(probe.Input!) ?? [];

    [Test]
    public void ABareFlagStreams()
    {
        var probe = WithInput();
        var result = ParseRewritten(probe, "--json");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Stream);
    }

    [Test]
    public void AModeIsGivenWithAnEqualsSign()
    {
        var result = ParseRewritten(WithInput(), "--json=document");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Document);
    }

    [Test]
    public void WithoutTheFlagThereIsNoJsonOutput()
    {
        var result = ParseRewritten(WithInput(), "model.json");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.None);
    }

    [Test]
    public void TheNextArgumentStaysTheCommands()
    {
        var probe = WithInput();
        var result = ParseRewritten(probe, "--json", "model.json");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Stream);
        InputOf(probe, result).Should().Equal("model.json");
    }

    [Test]
    public void AnInlineModeLeavesTheNextArgumentToTheCommand()
    {
        var probe = WithInput();
        var result = ParseRewritten(probe, "--json=document", "model.json");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Document);
        InputOf(probe, result).Should().Equal("model.json");
    }

    [Test]
    public void ASeparatedModeIsRefusedWithAHint()
    {
        var result = ParseRewritten(WithoutInput(), "--json", "document");

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("'document'");
        result.UnmatchedTokens.Should().Equal("document");
        OptionValueRewrite.HintFor(result.UnmatchedTokens[0], JsonOutputOption.Name, JsonOutputOption.Modes)
            .Should().Be("--json=document");
    }

    [Test]
    public void AnArgumentThatNamesNoModeGetsNoHint() =>
        OptionValueRewrite.HintFor("model.json", JsonOutputOption.Name, JsonOutputOption.Modes).Should().BeNull();

    [Test]
    [Arguments("--json=bogus")]
    [Arguments("--json=lines")]
    public void AnUnknownModeIsRefusedAndTheKnownModesAreListed(string arg)
    {
        var result = ParseRewritten(WithoutInput(), arg);

        var message = result.Errors.Should().ContainSingle().Which.Message;
        message.Should().Contain("'stream'").And.Contain("'document'");
    }

    [Test]
    public void TheColonSeparatorIsAcceptedQuietly()
    {
        var result = ParseRewritten(WithoutInput(), "--json:document");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Document);
    }

    [Test]
    public void TheFormerBooleanValueIsRefused() =>
        ParseRewritten(WithoutInput(), "--json=true").Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("'stream'").And.Contain("'document'");

    [Test]
    public void AFlagAfterTheDoubleDashStaysAPositionalArgument()
    {
        var probe = WithInput();
        var result = ParseRewritten(probe, "--", "--json");

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.None);
        InputOf(probe, result).Should().Equal("--json");
    }

    [Test]
    public void TheRewriteChangesOnlyTheFlagsOfItsTable()
    {
        string[] args = ["--json", "--jsonx", "--json=document", "--compact", "--", "--json"];

        OptionValueRewrite.Apply(args, JsonDefault)
            .Should().Equal("--json=stream", "--jsonx", "--json=document", "--compact", "--", "--json");
        OptionValueRewrite.Apply(args, new Dictionary<string, string>()).Should().Equal(args);
    }

    [Test]
    [Category("PlatformSpecific")] // A response file on the real file system, read by the parser.
    public void TheRewriteAppliesToTheFlagsInAResponseFile()
    {
        using var sandbox = TestSandbox.Create();
        var responseFile = sandbox.PathOf("arguments.rsp");
        File.WriteAllText(responseFile, "--json\nmodel.json\n");
        var probe = WithInput();

        var result = OptionValueRewrite.Parse(probe.Command, ["@" + responseFile]);

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Stream);
        InputOf(probe, result).Should().Equal("model.json");
    }

    [Test]
    public void ADoubleDashInAResponseFileEndsTheRewriteForTheRestOfTheCommandLine()
    {
        // A response file is expanded in place, so its "--" applies to the arguments after it on the command line.
        var configuration = new ParserConfiguration { ResponseFileTokenReplacer = ResponseFiles(("options.rsp", ["--json", "--"])) };
        var probe = WithInput();

        var result = OptionValueRewrite.Parse(probe.Command, ["@options.rsp", "--json"], configuration);

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Stream);
        InputOf(probe, result).Should().Equal("--json");
    }

    [Test]
    public void AResponseFileAfterTheDoubleDashIsNotExpanded()
    {
        var configuration = new ParserConfiguration { ResponseFileTokenReplacer = ResponseFiles(("options.rsp", ["--json"])) };
        var probe = WithInput();

        var result = OptionValueRewrite.Parse(probe.Command, ["--", "@options.rsp"], configuration);

        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.None);
        InputOf(probe, result).Should().Equal("@options.rsp");
    }

    [Test]
    [Category("PlatformSpecific")] // A response file on the real file system, read by the parser.
    public void AMissingResponseFileIsStillReportedByTheParser()
    {
        using var sandbox = TestSandbox.Create();
        var missing = sandbox.PathOf("missing.rsp");

        var result = OptionValueRewrite.Parse(WithInput().Command, ["@" + missing]);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("missing.rsp");
    }

    [Test]
    public void TheCommandLineOfAJsonOutputCommandIsRewritten()
    {
        var probe = WithInput("convert");
        var root = new RootCommand { probe.Command };

        var result = OptionValueRewrite.Parse(root, ["convert", "--json", "model.json"]);

        result.Errors.Should().BeEmpty();
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.Stream);
        InputOf(probe, result).Should().Equal("model.json");
    }

    [Test]
    public void ACommandWithABooleanJsonOptionKeepsItsMeaning()
    {
        // Until a command moves onto JsonOutputOption, its bare --json is a boolean flag and is not rewritten.
        var json = new Option<bool>("--json");
        var input = new Argument<string[]>("input") { Arity = ArgumentArity.ZeroOrMore };
        var legacy = new Command("validate") { json, input };
        var root = new RootCommand { legacy, WithInput("convert").Command };

        var result = OptionValueRewrite.Parse(root, ["validate", "--json", "model.json"]);

        result.Errors.Should().BeEmpty();
        result.GetValue(json).Should().BeTrue();
        result.GetValue(input).Should().Equal("model.json");
        JsonOutputOption.ModeOf(result).Should().Be(JsonOutputMode.None);
    }

    [Test]
    [Arguments(new string[0], JsonOutputMode.None)]
    [Arguments(new[] { "--json" }, JsonOutputMode.Stream)]
    [Arguments(new[] { "--json=stream" }, JsonOutputMode.Stream)]
    [Arguments(new[] { "--json=document" }, JsonOutputMode.Document)]
    [Arguments(new[] { "--json:document" }, JsonOutputMode.Document)]
    [Arguments(new[] { "--json=bogus" }, JsonOutputMode.Stream)]
    [Arguments(new[] { "--", "--json=document" }, JsonOutputMode.None)]
    [Arguments(new[] { "--jsonx" }, JsonOutputMode.None)]
    public void ScanningTheArgumentsFindsTheMode(string[] args, JsonOutputMode expected) =>
        JsonOutputOption.ScanTokens(args).Should().Be(expected);

    [Test]
    [Arguments("--json=document", "model.json")]
    [Arguments("--json", "document")]
    [Arguments("--json=bogus", "model.json")]
    public void ScanningTheTokensOfAParseFindsTheMode(string first, string second)
    {
        // A parse result splits "--json=document" into the option token and its value token; a parse-error renderer
        // scans those tokens.
        var result = OptionValueRewrite.Parse(WithoutInput().Command, [first, second]);
        var expected = first == "--json=document" ? JsonOutputMode.Document : JsonOutputMode.Stream;

        JsonOutputOption.ScanTokens(result.Tokens.Select(token => token.Value).ToList()).Should().Be(expected);
    }

    private static TryReplaceToken ResponseFiles(params (string Name, string[] Tokens)[] files) =>
        (string token, out IReadOnlyList<string>? replacement, out string? error) =>
        {
            var file = files.FirstOrDefault(f => f.Name == token);
            replacement = file.Tokens;
            error = file.Tokens is null ? $"Response file not found '{token}'." : null;
            return file.Tokens is not null;
        };
}
