// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps that keep a page of the documentation true: the commands of its bash blocks give their documented
/// output on the host of the scenario. A page is a fixture of <c>Fixtures/Docs</c>, a link to
/// <c>docs/content/docs</c>.
/// </summary>
[Binding]
public sealed partial class DocumentationSteps(ICliDriver cli, IScenarioWorkspace workspace, ScenarioRun run)
{
    private readonly List<(DocumentedCommand Command, CommandResult Result)> _ran = [];
    private IReadOnlyList<DocumentedCommand>? _commands;

    private IReadOnlyList<DocumentedCommand> Commands => _commands ?? throw new InvalidOperationException("The scenario has no documentation page before this step.");

    /// <summary>The command name of the host of the scenario, as a documented command line starts with it.</summary>
    private string HostCommand => cli.Host == MorphirHost.DotnetTool ? "dotnet-morphir" : "morphir";

    [GeneratedRegex("```bash\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex BashBlock();

    [GeneratedRegex(@"(?<= )(src|broken)/")]
    private static partial Regex WorkAreaPath();

    /// <summary>One <c>$ </c> line of a bash block, with the lines after it as its documented output.</summary>
    public sealed record DocumentedCommand(string Host, string Arguments, string[] Output);

    [Given("the documentation page {string}")]
    public void TheDocumentationPage(string page) => _commands = Parse(Page(page));

    [Given("the section {string} of the documentation page {string}")]
    public void TheSectionOfTheDocumentationPage(string section, string page)
    {
        var text = Page(page);
        var start = text.IndexOf($"## {section}\n", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException($"The page {page} has no section {section}.");
        var end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        _section = end < 0 ? text[start..] : text[start..end];
        _commands = Parse(_section);
    }

    [Then("the page documents these commands")]
    public void ThePageDocumentsTheseCommands(Table commands)
    {
        var documented = Commands.Select(command => $"{command.Host} {command.Arguments}").ToArray();
        var expected = commands.Rows.Select(row => row["command"]).ToArray();
        if (!documented.SequenceEqual(expected))
            throw new InvalidOperationException($"The page documents the commands:\n{string.Join("\n", documented)}\nnot:\n{string.Join("\n", expected)}");
    }

    [Given("the source file {string}:")]
    public void TheSourceFile(string relative, string text) => workspace.WriteText(workspace.File(relative), text + "\n");

    /// <summary>Runs each documented command of the host, with <c>model.json</c> as the model of the scenario.</summary>
    [When("I run each documented command of the host on the model")]
    public Task RunEachCommandOnTheModel() => RunEach(arguments => arguments.Replace("model.json", $"\"{run.Model.Path}\"", StringComparison.Ordinal));

    /// <summary>Runs each documented command of the host, with the paths <c>src/</c> and <c>broken/</c> in the work area.</summary>
    [When("I run each documented command of the host in the work area")]
    public Task RunEachCommandInTheWorkArea() =>
        RunEach(arguments => WorkAreaPath().Replace(arguments, match => workspace.Directory(match.Groups[1].Value).Path + "/"));

    [Then("each documented command exits with {int}")]
    public void EachCommandExitsWith(int exitCode)
    {
        foreach (var (_, result) in _ran) result.ShouldExitWith(exitCode);
    }

    /// <summary>
    /// The output of each command is its documented output: the same JSON, apart from the members that the page shows
    /// with an example path, or the same text.
    /// </summary>
    [Then("each documented command gives its documented output")]
    public void EachCommandGivesItsDocumentedOutput()
    {
        foreach (var (command, result) in _ran)
        {
            var actual = result.StandardOutput.Trim().Replace("\r\n", "\n").Split('\n');
            Check(actual.Length == command.Output.Length, $"The command {command.Arguments} wrote {actual.Length} lines, and the page documents {command.Output.Length}.", result);
            if (command.Arguments.EndsWith("--json-lines", StringComparison.Ordinal))
            {
                for (var line = 0; line < actual.Length; line++)
                    Check(Normalize(actual[line], result) == Normalize(command.Output[line], result: null), $"Line {line + 1} of {command.Arguments} is not the documented line.", result);
            }
            else if (command.Output.Length > 0 && command.Output[0].StartsWith('{'))
            {
                Check(Normalize(string.Join('\n', actual), result) == Normalize(string.Join('\n', command.Output), result: null), $"The JSON of {command.Arguments} is not the documented JSON.", result);
            }
            else
            {
                Check(actual.SequenceEqual(command.Output), $"The output of {command.Arguments} is not the documented output.", result);
            }
        }
    }

    [GeneratedRegex("```toml\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex TomlBlock();

    private string? _section;

    [Given("the morphir.toml of the section in the project directory {string}")]
    public void TheMorphirTomlOfTheSection(string directory)
    {
        var toml = TomlBlock().Match(_section ?? throw new InvalidOperationException("The scenario has no section before this step."));
        if (!toml.Success) throw new InvalidOperationException("The section has no toml block.");
        workspace.WriteText(workspace.File(directory, "morphir.toml"), toml.Groups["body"].Value + "\n");
    }

    /// <summary>
    /// Runs each documented command of the section in the order of the page, with <c>--project</c> and with
    /// <c>--output gen</c> below the project directory, and stops at the first command that fails.
    /// </summary>
    [When("I run the documented commands of the section in the project directory {string}")]
    public async Task RunTheCommandsOfTheSection(string directory)
    {
        _ran.Clear();
        var project = workspace.Directory(directory);
        foreach (var command in Commands)
        {
            var arguments = command.Arguments.Replace("--output gen", $"--output \"{project.Directory("gen").Path}\"", StringComparison.Ordinal);
            var result = await cli.RunCommandLineAsync($"{arguments} --project \"{project.Path}\"", new CliRunOptions { Timeout = TimeSpan.FromSeconds(120) });
            _ran.Add((command, result));
            run.Result = result.ShouldExitWith(0);
        }
    }

    [Then("the documented compile writes the path of the published IR of the project {string}")]
    public void TheCompileWritesThePublishedIr(string directory)
    {
        var (command, result) = _ran.Single(item => item.Command.Arguments.StartsWith("compile", StringComparison.Ordinal));
        var published = workspace.File(directory, ".morphir", "out", "compile.dest", "morphir-ir.json").Path;
        Check(result.StandardOutput.Trim() == published, $"The compile wrote {result.StandardOutput.Trim()}, not the published IR {published}.", result);
        var documented = command.Output.Single();
        Check(published.Replace('\\', '/').EndsWith(documented["/work/demo".Length..], StringComparison.Ordinal), $"The published IR {published} is not at the documented place {documented}.", result);
    }

    [Then("the documented generate writes its documented files to {string} of the project {string}")]
    public void TheGenerateWritesItsDocumentedFiles(string output, string directory)
    {
        var (command, result) = _ran.Single(item => item.Command.Arguments.StartsWith("generate", StringComparison.Ordinal));
        var lines = result.StandardOutput.Trim().Replace("\r\n", "\n").Split('\n');
        Check(lines.SequenceEqual(command.Output), $"The generate wrote {string.Join(", ", lines)}, not the documented {string.Join(", ", command.Output)}.", result);
        foreach (var file in command.Output)
            Check(workspace.Exists(workspace.File(directory, output, file)), $"The generate did not write {file}.", result);
    }

    private async Task RunEach(Func<string, string> paths)
    {
        _ran.Clear();
        foreach (var command in Commands.Where(command => command.Host == HostCommand))
        {
            var result = await cli.RunCommandLineAsync(paths(command.Arguments), new CliRunOptions { Timeout = TimeSpan.FromSeconds(180) });
            _ran.Add((command, result));
            run.Result = result;
        }

        if (_ran.Count == 0) throw new InvalidOperationException($"The page documents no command of the host {HostCommand}.");
    }

    private string Page(string page) => workspace.ReadFixtureText("Docs/" + page).Replace("\r\n", "\n");

    private static List<DocumentedCommand> Parse(string text)
    {
        var commands = new List<DocumentedCommand>();
        foreach (Match block in BashBlock().Matches(text))
        {
            var lines = block.Groups["body"].Value.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].StartsWith("$ ", StringComparison.Ordinal)) continue;
                var output = lines.Skip(index + 1).TakeWhile(line => !line.StartsWith("$ ", StringComparison.Ordinal)).ToArray();
                var words = lines[index][2..].Split(' ', 2);
                commands.Add(new DocumentedCommand(words[0], words.Length > 1 ? words[1] : "", output));
            }
        }

        return commands;
    }

    /// <summary>
    /// The JSON text without the members that the page shows with an example path: <c>output</c>,
    /// <c>sources.root</c> and <c>generation.output</c> of a compile report, and <c>input</c>, which must be the
    /// model of the scenario when the scenario has one.
    /// </summary>
    private string Normalize(string json, CommandResult? result)
    {
        var node = JsonNode.Parse(json)!;
        var report = node["report"]?.AsObject() ?? (node is JsonObject root && (root.ContainsKey("success") || root.ContainsKey("exitCode")) ? root : null);
        if (report is not null)
        {
            report.Remove("output");
            report["sources"]?.AsObject().Remove("root");
            report["generation"]?.AsObject().Remove("output");
            if (result is not null && report["input"] is { } input && run.HasModel)
                Check(input.GetValue<string>() == run.Model.Path, $"The input of the report is {input}, not the model {run.Model.Path}.", result);
            report.Remove("input");
        }

        return node.ToJsonString();
    }

    private static void Check(bool condition, string reason, CommandResult result)
    {
        if (!condition) throw new CommandAssertionException(reason, result);
    }
}
