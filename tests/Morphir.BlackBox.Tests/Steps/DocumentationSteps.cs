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
        _section = Section(Page(page), section) ?? throw new InvalidOperationException($"The page {page} has no section {section}.");
        _commands = Parse(_section);
    }

    /// <summary>
    /// The section of a page whose heading line, at any level, is <paramref name="section"/>. It ends at the next
    /// heading of the same or a higher level, so a deeper heading is part of it. A line in a fenced code block, such
    /// as a <c>#</c> comment of a bash block, is no heading.
    /// </summary>
    private static string? Section(string text, string section)
    {
        var lines = text.Split('\n');
        int? start = null;
        var level = 0;
        var fenced = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            if (fenced || !line.StartsWith('#')) continue;
            var hashes = line.TakeWhile(character => character == '#').Count();
            if (line.Length <= hashes || line[hashes] != ' ') continue;
            if (start is null)
            {
                if (line[(hashes + 1)..] != section) continue;
                start = index;
                level = hashes;
            }
            else if (hashes <= level)
            {
                return string.Join('\n', lines[start.Value..index]);
            }
        }

        return start is null ? null : string.Join('\n', lines[start.Value..]);
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

    /// <summary>Runs each documented command of the host, with the directory <c>model/</c> as the model tree of the scenario.</summary>
    [When("I run each documented command of the host on the model tree")]
    public Task RunEachCommandOnTheModelTree()
    {
        var tree = run.ModelTree ?? throw new InvalidOperationException("The scenario has no model tree before this step.");
        return RunEach(arguments => arguments.Replace(" model/", $" \"{tree.Path}\"", StringComparison.Ordinal));
    }

    /// <summary>Runs each documented command of the host, with the paths <c>src/</c> and <c>broken/</c> in the work area.</summary>
    [When("I run each documented command of the host in the work area")]
    public Task RunEachCommandInTheWorkArea() =>
        RunEach(arguments => WorkAreaPath().Replace(arguments, match => workspace.Directory(match.Groups[1].Value).Path + "/"));

    /// <summary>
    /// Runs each documented command of the host in the work area, with the variables of the documented
    /// <c>export</c> lines of the section. <c>$PWD</c> in a value is the work area.
    /// </summary>
    [When("I run each documented command of the host with the exported variables of the section")]
    public async Task RunEachCommandWithTheExportedVariables()
    {
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var export in Commands.Where(command => command.Host == "export"))
        {
            foreach (var assignment in export.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = assignment.Split('=', 2);
                if (parts.Length != 2) throw new InvalidOperationException($"The export line has no value in {assignment}.");
                variables[parts[0]] = parts[1].Replace("$PWD", workspace.Work.Path, StringComparison.Ordinal);
            }
        }

        if (variables.Count == 0) throw new InvalidOperationException("The section exports no variable.");
        _ran.Clear();
        foreach (var command in Commands.Where(command => command.Host == HostCommand))
        {
            var result = await cli.RunCommandLineAsync(command.Arguments, new CliRunOptions { Timeout = TimeSpan.FromSeconds(120), Environment = variables });
            _ran.Add((command, result));
            run.Result = result;
        }

        if (_ran.Count == 0) throw new InvalidOperationException($"The section documents no command of the host {HostCommand}.");
    }

    /// <summary>
    /// The JSON report of each command has each member of the JSON block of the section, with the same value. The
    /// block is the members of the report, without the braces of the report. A <c>boundary</c> member shows an
    /// example path, and it must be the work area.
    /// </summary>
    [Then("the report of each documented command has the members of the JSON block of the section")]
    public void TheReportHasTheMembersOfTheJsonBlock()
    {
        var block = JsonBlock().Match(_section ?? throw new InvalidOperationException("The scenario has no section before this step."));
        if (!block.Success) throw new InvalidOperationException("The section has no json block.");
        var body = block.Groups["body"].Value.Trim();
        var documented = JsonNode.Parse(body.StartsWith('{') ? body : "{" + body + "}")!.AsObject();
        foreach (var (_, result) in _ran)
        {
            var report = JsonNode.Parse(result.StandardOutput)!.AsObject();
            foreach (var (name, value) in documented)
            {
                Check(report[name] is not null, $"The report has no member {name}.", result);
                var actual = report[name]!.DeepClone();
                var expected = value?.DeepClone();
                if (actual is JsonObject actualObject && expected is JsonObject expectedObject && expectedObject.ContainsKey("boundary"))
                {
                    var boundary = actualObject["boundary"]?.GetValue<string>().Replace('\\', '/').TrimEnd('/');
                    Check(boundary == workspace.Work.Path.Replace('\\', '/').TrimEnd('/'), $"The boundary of the report is {boundary}, not the work area {workspace.Work.Path}.", result);
                    actualObject.Remove("boundary");
                    expectedObject.Remove("boundary");
                }

                Check(JsonNode.DeepEquals(actual, expected), $"The member {name} of the report is {actual?.ToJsonString()}, not the documented {expected?.ToJsonString()}.", result);
            }
        }
    }

    [GeneratedRegex("```json\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex JsonBlock();

    [Then("each documented command exits with {int}")]
    public void EachCommandExitsWith(int exitCode)
    {
        foreach (var (_, result) in _ran) result.ShouldExitWith(exitCode);
    }

    /// <summary>
    /// The output of each command is its documented output: the same JSON, apart from the members that the page shows
    /// with an example path, line by line for a documented stream, or the same text.
    /// </summary>
    [Then("each documented command gives its documented output")]
    public void EachCommandGivesItsDocumentedOutput()
    {
        foreach (var (command, result) in _ran)
        {
            var actual = result.StandardOutput.Trim().Replace("\r\n", "\n").Split('\n');
            Check(actual.Length == command.Output.Length, $"The command {command.Arguments} wrote {actual.Length} lines, and the page documents {command.Output.Length}.", result);
            if (command.Output.Length > 0 && command.Output[0].StartsWith("{\"event\":", StringComparison.Ordinal))
            {
                // A documented stream (--json): each line is one record.
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

    [GeneratedRegex("```csharp\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();

    /// <summary>The text of a C# block of the section, counted from 1.</summary>
    private string CSharpBlockOfTheSection(int number)
    {
        var blocks = CSharpBlock().Matches(_section ?? throw new InvalidOperationException("The scenario has no section before this step."));
        if (number < 1 || number > blocks.Count) throw new InvalidOperationException($"The section has {blocks.Count} C# blocks, not a block {number}.");
        return blocks[number - 1].Groups["body"].Value;
    }

    [Given("the C# block {int} of the section is the source file {string}")]
    public void TheCSharpBlockIsTheSourceFile(int number, string relative) =>
        workspace.WriteText(workspace.File(relative.Split('/')), CSharpBlockOfTheSection(number) + "\n");

    [Then("the file {string} contains the C# block {int} of the section")]
    public void TheFileContainsTheCSharpBlock(string relative, int number)
    {
        var file = workspace.File(relative.Split('/'));
        if (!workspace.Exists(file)) throw new InvalidOperationException($"There is no file {relative}.");
        var text = workspace.ReadText(file).Replace("\r\n", "\n");
        if (!text.Contains(CSharpBlockOfTheSection(number), StringComparison.Ordinal))
            throw new InvalidOperationException($"The file {relative} does not contain the C# block {number} of the section. It has:\n{text}");
    }

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


    [Given("the first C# code block of the documentation page {string} as the file {string}")]
    public void TheFirstCSharpCodeBlockAsTheFile(string page, string relative)
    {
        var block = CSharpBlock().Match(Page(page));
        if (!block.Success) throw new InvalidOperationException($"The page {page} has no C# code block.");
        workspace.WriteText(workspace.File(relative), block.Groups["body"].Value + "\n");
    }

    /// <summary>
    /// Runs each documented command of the section in the order of the page, as written, with the directory as the
    /// working directory, and stops at the first command that fails.
    /// </summary>
    [When("I run the documented commands of the section in the directory {string}")]
    public async Task RunTheCommandsOfTheSectionInTheDirectory(string directory)
    {
        _ran.Clear();
        var workingDirectory = workspace.Directory(directory).Path;
        foreach (var command in Commands)
        {
            var result = await cli.RunCommandLineAsync(command.Arguments, new CliRunOptions { Timeout = TimeSpan.FromSeconds(120), WorkingDirectory = workingDirectory });
            _ran.Add((command, result));
            run.Result = result.ShouldExitWith(0);
        }
    }

    /// <summary>
    /// The compile wrote the path of the published IR of the project, and the page shows that path below
    /// <c>/work/</c> and the directory of the project.
    /// </summary>
    [Then("the documented compile writes the path of the published IR of the project {string}")]
    public void TheCompileWritesThePublishedIr(string directory)
    {
        var (command, result) = _ran.Single(item => item.Command.Arguments.StartsWith("compile", StringComparison.Ordinal));
        var published = workspace.File(directory, ".morphir", "out", "compile.dest", "morphir-ir.json").Path;
        Check(result.StandardOutput.Trim() == published, $"The compile wrote {result.StandardOutput.Trim()}, not the published IR {published}.", result);
        var documented = command.Output.SingleOrDefault() ?? "";
        var example = $"/work/{directory}";
        Check(documented.StartsWith(example + "/", StringComparison.Ordinal), $"The page shows the published IR at {documented}, not below {example}.", result);
        Check(published.Replace('\\', '/').EndsWith(documented[example.Length..], StringComparison.Ordinal), $"The published IR {published} is not at the documented place {documented}.", result);
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
    /// <c>sources.root</c> and <c>generation.output</c> of a compile report, <c>input</c>, which must be the
    /// model of the scenario when the scenario has one, and <c>FilePath</c> (the same rule) and <c>Timestamp</c> of a
    /// verification report.
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

            // A verification report (model verify) names its file in FilePath and the time of its run in Timestamp.
            if (report.ContainsKey("IsValid"))
            {
                if (result is not null && report["FilePath"] is { } file && run.HasModel)
                    Check(file.GetValue<string>() == run.Model.Path, $"The file of the report is {file}, not the model {run.Model.Path}.", result);
                report.Remove("FilePath");
                report.Remove("Timestamp");
            }
        }

        return node.ToJsonString();
    }

    private static void Check(bool condition, string reason, CommandResult result)
    {
        if (!condition) throw new CommandAssertionException(reason, result);
    }
}
