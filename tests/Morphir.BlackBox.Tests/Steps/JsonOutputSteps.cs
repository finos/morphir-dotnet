// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;
using Morphir.TestSupport.Fixtures;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of the JSON output convention of every command (adopt-streaming-json, capability cli-json-output). A
/// scenario names a command by its words, for example <c>config show</c>; the steps give its input to the workspace
/// and run it through the driver of its capability, with the <c>--json</c> argument of the intent. Each report is
/// read with <see cref="JsonOutputAssertions"/> and the report type of its command.
/// </summary>
[Binding]
public sealed class JsonOutputSteps(
    IModelValidationDriver validation,
    IGenerationDriver generation,
    IConfigurationDriver configuration,
    IExtensionDriver extensions,
    IMigrationDriver migration,
    IModelVerificationDriver verification,
    IScenarioWorkspace workspace,
    ScenarioRun run)
{
    /// <summary>An option that no command has, for a usage error.</summary>
    private const string UnknownOption = "--no-such-option";

    /// <summary>A classic project of the migrations.</summary>
    private const string ClassicProject = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order']}\n";

    /// <summary>A classic project that a migration cannot represent, so it blocks with diagnostics on stderr.</summary>
    private const string BlockedProject = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order'],dependencies:['unrepresentable']}\n";

    private readonly Dictionary<JsonOutputArgument, CommandResult> _byForm = [];
    private bool _writesALogLine;

    private WorkspaceDirectory Root => workspace.Work;

    private ScenarioMachine Machine => new(Root);

    private WorkspaceFile Model => Root.File("my model.json");

    private WorkspaceDirectory Output => Root.Directory("generated code");

    private WorkspaceDirectory Project => Root.Directory("classic project");

    private WorkspaceDirectory Repository => Root.Directory("classic repository");

    [Given("the input of {string}")]
    public void TheInputOf(string command)
    {
        switch (command)
        {
            case "validate" or "generate" or "ir verify":
                workspace.WriteText(Model, FixtureCatalog.ReadText(FixtureCatalog.Models.V3DailyRate));
                break;
            case "config show":
                workspace.WriteText(Root.File("morphir.toml"), "[custom]\ncaption = 'orders'\n");
                break;
            case "project migrate":
                workspace.WriteText(Project.File("morphir.json"), ClassicProject);
                break;
            case "workspace migrate":
                foreach (var member in new[] { "a", "b" })
                    workspace.WriteText(Repository.File("apps", member, "morphir.json"), $"{{name:'apps.{member}',sourceDirectory:'elm-src',exposedModules:[]}}\n");
                break;
            case "extension list" or "extension runtimes":
                break;
            default:
                throw new InvalidOperationException($"The JSON output scenarios have no command '{command}'.");
        }
    }

    /// <summary>
    /// The input of a run that writes a line to stderr: <c>generate</c> asks a host without dynamic code to format F#,
    /// which it cannot, so it warns; <c>project migrate</c> gets a project that it cannot represent, so it reports its
    /// blocking diagnostics.
    /// </summary>
    [Given("the input of {string} that makes it write a log line")]
    public void TheInputThatWritesALogLine(string command)
    {
        TheInputOf(command);
        _writesALogLine = true;
        if (command == "project migrate") workspace.WriteText(Project.File("morphir.json"), BlockedProject);
        else if (command != "generate") throw new InvalidOperationException($"The JSON output scenarios have no log line of '{command}'.");
    }

    [When("I run {string} with a bare JSON flag")]
    public async Task RunWithABareFlag(string command) => run.Result = await Run(command, JsonOutputArgument.Stream);

    [When("I run {string} with the JSON document")]
    public async Task RunWithTheDocument(string command) => run.Result = await Run(command, JsonOutputArgument.Document);

    [When("I run {string} with the JSON output {string}")]
    public async Task RunWithTheOutput(string command, string mode) => run.Result = await Run(command, Form(mode));

    [When("I run {string} with the JSON mode {string}")]
    public async Task RunWithTheMode(string command, string mode) => run.Result = await Run(command, JsonOutputArgument.WithMode(mode));

    [When("I run {string} with the JSON mode {string} after a space")]
    public async Task RunWithTheModeAfterASpace(string command, string mode) => run.Result = await Run(command, JsonOutputArgument.Separated(mode));

    [When("I run {string} with a bare JSON flag and an option that it does not have")]
    public async Task RunWithAnUnknownOption(string command) => run.Result = await Run(command, JsonOutputArgument.Stream.Then(UnknownOption));

    [When("I run {string} with the former streaming flag")]
    public async Task RunWithTheFormerFlag(string command) => run.Result = await Run(command, JsonOutputArgument.FormerStreamingFlag);

    /// <summary>
    /// The drivers of the commands with a positional input put the <c>--json</c> argument right before it, so the
    /// input is the argument after a bare <c>--json</c>.
    /// </summary>
    [When("I run {string} with a bare JSON flag right before its input")]
    public async Task RunWithTheFlagBeforeTheInput(string command)
    {
        if (command is not ("project migrate" or "workspace migrate" or "ir verify"))
            throw new InvalidOperationException($"'{command}' has no positional input.");
        run.Result = await Run(command, JsonOutputArgument.Stream);
    }

    /// <summary>
    /// Runs the command in both modes. A command that writes files (<c>generate</c>) runs once before, so both runs
    /// find the same files and report the same result.
    /// </summary>
    [When("I run {string} with a bare JSON flag and then with the JSON document")]
    public async Task RunInBothModes(string command)
    {
        if (command == "generate") await Run(command, JsonOutputArgument.Stream);
        _byForm[JsonOutputArgument.Stream] = await Run(command, JsonOutputArgument.Stream);
        run.Result = _byForm[JsonOutputArgument.Document] = await Run(command, JsonOutputArgument.Document);
    }

    [When("I list the extensions with a bare JSON flag and then with the JSON document")]
    public Task ListInBothModes() => RunInBothModes("extension list");

    [Then("standard output is JSON Lines that end with one completed record")]
    public void StandardOutputIsAStream() => run.Result.ReportOfStream(JsonCommandReport.MaxDepth);

    [Then("every line of standard output is a JSON record with an event, and only the last is completed")]
    public void EveryLineIsARecord()
    {
        var records = run.Result.Events(JsonCommandReport.MaxDepth);
        Check(records.Count(IsCompleted) == 1 && IsCompleted(records[^1]), "The stream does not end with its one completed record.");
    }

    [Then("standard output is one completed record")]
    public void StandardOutputIsOneCompletedRecord()
    {
        var records = run.Result.Events(JsonCommandReport.MaxDepth);
        Check(records.Count == 1 && IsCompleted(records[0]), $"Standard output has {records.Count} records, not one completed record.");
    }

    [Then("the output names the JSON modes {string} and {string}")]
    public void TheOutputNamesTheModes(string first, string second) => run.Result.OutputShouldContain(first).OutputShouldContain(second);

    /// <summary>The report of the usage error, read with the report type of the command, has its usage diagnostic.</summary>
    [Then("the report of {string} states the usage failure")]
    public void TheReportStatesTheUsageFailure(string command)
    {
        var result = run.Result;
        switch (command)
        {
            case "validate":
                ValidationReport.Of(result).ShouldHaveDiagnostic("invalid-arguments");
                break;
            case "generate":
                var generated = GenerationReport.Of(result).ShouldFail("invocation");
                Check(generated.Diagnostics.Any(diagnostic => diagnostic.Code == "invalid-arguments"), "The generation report has no diagnostic invalid-arguments.");
                break;
            case "config show":
                ConfigurationReport.Of(result).ShouldHaveDiagnostic("config.usage");
                break;
            case "extension list":
                ExtensionListReport.Of(result).ShouldHaveDiagnostic("extension.invalid_arguments");
                break;
            case "extension runtimes":
                ExtensionRuntimesReport.Of(result).ShouldHaveDiagnostic("extension.invalid_arguments");
                break;
            case "project migrate":
                MigrationReport.Of(result).ShouldHaveOutcome("blocked").ShouldHaveDiagnostic("project.migrate_usage");
                break;
            case "workspace migrate":
                MigrationReport.Of(result).ShouldHaveOutcome("blocked").ShouldHaveDiagnostic("workspace.migrate_usage");
                break;
            case "ir verify":
                var verified = VerificationReport.Of(result).ShouldBeInvalid();
                Check(verified.Errors.Any(error => error.Message.Contains(UnknownOption, StringComparison.Ordinal)), $"No error of the verification report names {UnknownOption}.");
                break;
            default:
                throw new InvalidOperationException($"The JSON output scenarios have no command '{command}'.");
        }

        Check(result.StandardOutput.Contains(UnknownOption, StringComparison.Ordinal), $"The report does not name the option {UnknownOption}.");
    }

    /// <summary>
    /// The document equals the report of the completed record. The time that <c>ir verify</c> states
    /// (<c>Timestamp</c>) is the time of its run, so it is left out of the comparison.
    /// </summary>
    [Then("the document is the report of the completed record")]
    public void TheDocumentIsTheReport()
    {
        var stream = _byForm[JsonOutputArgument.Stream];
        var document = _byForm[JsonOutputArgument.Document];
        Check(stream.ExitCode == document.ExitCode, $"The stream exits with {stream.ExitCode}, and the document with {document.ExitCode}.");
        var report = Node(stream.ReportOfStream(JsonCommandReport.MaxDepth));
        var single = Node(document.ReportOfDocument(JsonCommandReport.MaxDepth));
        report.Remove("Timestamp");
        single.Remove("Timestamp");
        if (!JsonNode.DeepEquals(report, single))
            throw new CommandAssertionException($"The document is not the report of the completed record. The report: {report.ToJsonString()}", document);
    }

    [Then("standard output is one report document whose exit code is the exit code of the command")]
    public void StandardOutputIsOnePipeableDocument()
    {
        var report = run.Result.ReportOfDocument(JsonCommandReport.MaxDepth);
        Check(report.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number && exitCode.GetInt32() == run.Result.ExitCode,
            "The document has no member exitCode that is the exit code of the command.");
    }

    [Then("the report of {string} names its input")]
    public void TheReportNamesItsInput(string command)
    {
        var (named, input) = command switch
        {
            "ir verify" => (VerificationReport.Of(run.Result).FilePath, Model.Name),
            "project migrate" => (MigrationReport.Of(run.Result).Text("source", "path"), "classic project/morphir.json"),
            "workspace migrate" => (MigrationReport.Of(run.Result).Text("rootPath"), "classic repository"),
            _ => throw new InvalidOperationException($"'{command}' has no positional input."),
        };
        // A temporary directory can be reached through a link (macOS /var is /private/var), so only the part of the
        // path below the work area is compared.
        Check(named is not null && ReportPath.Normalize(named).EndsWith("/" + input, StringComparison.Ordinal),
            $"The report names the input {named ?? "null"}, not {input} of the work area.");
    }

    [Then("standard error has a line")]
    public void StandardErrorHasALine()
    {
        Check(_writesALogLine, "The scenario did not ask for a log line.");
        Check(!string.IsNullOrWhiteSpace(run.Result.StandardError), "Standard error is empty.");
    }

    [Then("standard output is the JSON output {string}")]
    public void StandardOutputIsTheOutput(string mode)
    {
        if (Form(mode) == JsonOutputArgument.Stream) run.Result.ReportOfStream(JsonCommandReport.MaxDepth);
        else run.Result.ReportOfDocument(JsonCommandReport.MaxDepth);
    }

    [Then("the member {string} is at the top of the document and at the top of the report of the completed record")]
    public void TheMemberIsAtTheTopOfBoth(string member)
    {
        var stream = ExtensionListReport.Of(_byForm[JsonOutputArgument.Stream]);
        var document = ExtensionListReport.Of(_byForm[JsonOutputArgument.Document]);
        var inReport = stream.Result.ReportOfStream().TryGetProperty(member, out var reported) ? reported.GetRawText() : null;
        var inDocument = document.Result.ReportOfDocument().TryGetProperty(member, out var top) ? top.GetRawText() : null;
        Check(inReport is not null, $"The report of the completed record has no top-level member {member}.");
        Check(inDocument is not null, $"The document has no top-level member {member}.");
        Check(inReport == inDocument, $"The member {member} is {inReport} in the report and {inDocument} in the document.");
    }

    private Task<CommandResult> Run(string command, JsonOutputArgument json) => command switch
    {
        "validate" => validation.ValidateAsync(Model, new ValidationOptions { Json = json }),
        "generate" when _writesALogLine => generation.GenerateAsync(Model, "fsharp", Output, new GenerationOptions { Json = json, WithoutDynamicCode = true }),
        "generate" => generation.GenerateAsync(Model, "csharp", Output, new GenerationOptions { Json = json }),
        "config show" => configuration.ConfigureAsync(["show", "--isolated", "--discovery-boundary", Root.Path], json, Machine),
        "extension list" => extensions.ListAsync(new ExtensionListOptions { Json = json }),
        "extension runtimes" => extensions.RuntimesAsync(json),
        "project migrate" => migration.MigrateProjectAsync(json, [Project.Path, "--dry-run", "--discovery-boundary", Root.Path], Machine),
        "workspace migrate" => migration.MigrateWorkspaceAsync(json, [Repository.Path, "--dry-run", "--discovery-boundary", Root.Path], Machine),
        "ir verify" => verification.VerifyAsync(Model, new VerificationOptions { Json = json }),
        _ => throw new InvalidOperationException($"The JSON output scenarios have no command '{command}'."),
    };

    private static JsonOutputArgument Form(string mode) => mode switch
    {
        "stream" => JsonOutputArgument.Stream,
        "document" => JsonOutputArgument.Document,
        _ => throw new InvalidOperationException($"'{mode}' is not a JSON output: stream or document."),
    };

    private static bool IsCompleted(JsonElement record) =>
        record.GetProperty(JsonOutputAssertions.EventMember).GetString() == JsonOutputAssertions.CompletedEvent;

    private static JsonObject Node(JsonElement report) =>
        JsonNode.Parse(report.GetRawText(), documentOptions: new JsonDocumentOptions { MaxDepth = JsonCommandReport.MaxDepth })!.AsObject();

    private void Check(bool condition, string reason)
    {
        if (!condition) throw new CommandAssertionException(reason, run.Result);
    }
}
