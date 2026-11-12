// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>morphir mck</c>, which runs the separately installed <c>morphir-mck</c> (cli-tool-dispatch). The
/// folder <c>tools</c> of the work area has the stub of the tool; the folder <c>empty</c> has no tool.
/// </summary>
[Binding]
public sealed partial class ToolRoutingSteps(IToolRoutingDriver routing, IScenarioWorkspace workspace, ScenarioRun run)
{
    private readonly Dictionary<string, string> _variables = [];
    private readonly List<WorkspaceDirectory> _path = [];
    private bool _currentDirectoryOnPath;
    private string? _standardInput;
    private WorkspaceFile? _stub;
    private CommandResult? _standalone;

    private WorkspaceDirectory Tools => workspace.Directory("tools");

    private WorkspaceDirectory Empty => workspace.Directory("empty");

    private ToolRoutingOptions Options => new()
    {
        Path = _path, CurrentDirectoryOnPath = _currentDirectoryOnPath, Variables = _variables, StandardInput = _standardInput,
    };

    [GeneratedRegex(@"(?m)^\s+mck\b")]
    private static partial Regex HelpRowOfMck();

    [Given("the stub morphir-mck is on the search path")]
    public void TheStubIsOnThePath()
    {
        _stub = routing.InstallStub(Tools);
        _path.Add(Tools);
    }

    [Given("no tool is on the search path")]
    public void NoToolIsOnThePath()
    {
        workspace.CreateDirectory(Empty);
        _stub = routing.InstallStub(Tools);
        _path.Add(Empty);
    }

    [Given("the stub morphir-mck is in the current directory, and the current directory is on the search path")]
    public void TheStubIsInTheCurrentDirectory()
    {
        routing.InstallStub(workspace.Work);
        _currentDirectoryOnPath = true;
    }

    [Given("the stub morphir-mck is in the current directory")]
    public void TheStubIsInTheCurrentDirectoryOnly() => routing.InstallStub(workspace.Work);

    [Given("the stub of a tool {string} is on the search path")]
    public void TheStubOfAToolIsOnThePath(string name)
    {
        routing.InstallStub(Tools, name);
        _path.Add(Tools);
    }

    [Given("MORPHIR_MCK is the relative name of the tool")]
    public void MorphirMckIsRelative() => _variables["MORPHIR_MCK"] = routing.ExecutableName("morphir-mck");

    [Given("MORPHIR_MCK is the absolute path of the stub")]
    public void MorphirMckIsTheStub() => _variables["MORPHIR_MCK"] = (_stub ?? throw new InvalidOperationException("The scenario has no stub before this step.")).Path;

    [Given("MORPHIR_MCK is the absolute path of a tool that is not there")]
    public void MorphirMckIsMissing() => _variables["MORPHIR_MCK"] = workspace.File("missing-morphir-mck").Path;

    [Given("the tool exits with {int}")]
    public void TheToolExitsWith(int code) => _variables["MCK_STUB_EXIT"] = code.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Given("the tool reads its standard input, and the input is {string}")]
    public void TheToolReadsItsInput(string input)
    {
        _variables["MCK_STUB_STDIN"] = "1";
        _standardInput = Regex.Unescape(input);
    }

    [When("I run mck with the arguments")]
    public async Task RunMckWithTheArguments(Table arguments) =>
        run.Result = await routing.RunAsync(["mck", .. arguments.Rows.Select(row => row["argument"])], Options);

    [When("I run the command {string} on the search path")]
    public async Task RunTheCommand(string command) =>
        run.Result = await routing.RunAsync(command.Split(' ', StringSplitOptions.RemoveEmptyEntries), Options);

    [When("I run mck with {string} and the tool alone with the same arguments")]
    public async Task RunRoutedAndStandalone(string arguments)
    {
        var words = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _standalone = await routing.RunStubAsync(_stub ?? throw new InvalidOperationException("The scenario has no stub before this step."), words, Options);
        run.Result = await routing.RunAsync(["mck", .. words], Options);
    }

    [When("I run mck with the arguments and the tool alone with the same arguments")]
    public async Task RunRoutedAndStandaloneWithTheArguments(Table arguments)
    {
        string[] words = [.. arguments.Rows.Select(row => row["argument"])];
        _standalone = await routing.RunStubAsync(_stub ?? throw new InvalidOperationException("The scenario has no stub before this step."), words, Options);
        run.Result = await routing.RunAsync(["mck", .. words], Options);
    }

    [Then("the tool received the arguments")]
    public void TheToolReceivedTheArguments(Table arguments)
    {
        var expected = arguments.Rows.Select(row => row["argument"]).ToArray();
        var received = StubReport.Of(run.Result).Arguments;
        Check(received.SequenceEqual(expected), $"The tool received: {string.Join(" | ", received)}. Expected: {string.Join(" | ", expected)}.");
    }

    [Then("the routed run writes and exits as the tool alone")]
    public void TheRoutedRunIsTheStandaloneRun()
    {
        var alone = _standalone ?? throw new InvalidOperationException("The scenario did not run the tool alone.");
        Check(run.Result.ExitCode == alone.ExitCode, $"The routed run exits with {run.Result.ExitCode}, and the tool alone with {alone.ExitCode}.");
        Check(run.Result.StandardOutput == alone.StandardOutput, "The CLI wrote something of its own to standard output.");
        Check(run.Result.StandardError == alone.StandardError, "The routed run and the tool alone wrote other text to standard error.");
    }

    [Then("the tool read the input {string} in the directory of the tool alone, the work area")]
    public void TheToolReadTheInput(string input)
    {
        var routed = StubReport.Of(run.Result);
        var alone = StubReport.Of(_standalone ?? throw new InvalidOperationException("The scenario did not run the tool alone."));
        Check(routed.StandardInput == Regex.Unescape(input), $"The tool read '{routed.StandardInput}', not the input of the caller.");
        Check(routed.WorkingDirectory == alone.WorkingDirectory, $"The tool ran in {routed.WorkingDirectory}, not in {alone.WorkingDirectory}.");
        Check(routed.WorkingDirectory.TrimEnd('/', '\\').EndsWith("work", StringComparison.Ordinal), $"The tool ran in {routed.WorkingDirectory}, not in the work area.");
    }

    [Then("standard output is not a report of the tool")]
    public void StandardOutputIsNotAReportOfTheTool() =>
        Check(!run.Result.StandardOutput.Contains("\"args\"", StringComparison.Ordinal), "The tool ran: standard output has its report.");

    [Then("the exit code is neither 0 nor {int}")]
    public void TheExitCodeIsNeither(int code) =>
        Check(run.Result.ExitCode != 0 && run.Result.ExitCode != code, $"The exit code is {run.Result.ExitCode}.");

    [Then("the help lists the command mck")]
    public void TheHelpListsMck() =>
        Check(HelpRowOfMck().IsMatch(run.Result.StandardOutput + run.Result.StandardError), "The help does not list the routed command mck.");

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
