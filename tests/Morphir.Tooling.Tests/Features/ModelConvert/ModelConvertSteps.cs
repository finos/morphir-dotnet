// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.TestSupport.Drivers;
using Morphir.TestSupport.Fixtures;
using DriverLayout = Morphir.TestSupport.Drivers.ModelLayout;

namespace Morphir.Tooling.Tests.Features.ModelConvert;

/// <summary>
/// The steps of <c>ModelConvert.feature</c>. Models come from <see cref="FixtureCatalog"/>; the command runs in this
/// process through <see cref="ModelConvertCommandDriver"/>, and its report is read with <see cref="ConversionReport"/>.
/// </summary>
[Binding]
public sealed class ModelConvertSteps : IAsyncDisposable
{
    /// <summary>
    /// The illustrative schema URL of the compaction envelope of the add-model-convert spec (model-name-compaction).
    /// The command refuses any envelope for now, so the URL is not a contract here; the parent's pinned examples
    /// replace this envelope when compacted files are read (add-model-convert 7.2).
    /// </summary>
    private const string EnvelopeSchema = "https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json";

    private readonly ModelConvertCommandDriver _driver = new();
    private ModelLocation? _input;
    private ModelLocation? _output;
    private Task<CommandResult>? _running;
    private CommandResult? _result;
    private ConversionReport? _report;
    private Dictionary<string, string>? _inputBefore;
    private Dictionary<string, string>? _outputBefore;
    private IReadOnlyList<string>? _linesBeforeTheWrite;

    private ScenarioWorkspace Workspace => _driver.Workspace;

    private CommandResult Result => _result ?? throw new InvalidOperationException("No conversion ran in this scenario.");

    private ConversionReport Report => _report ??= ConversionReport.Of(Result);

    // --- Models --------------------------------------------------------------------------------------------------

    [Given("a v4 single-file model")]
    public void V4SingleFile() => PlaceFile(FixtureCatalog.Models.V4DailyRate);

    [Given("a v4 single-file model with two modules")]
    public void V4SingleFileWithTwoModules() => PlaceFile(FixtureCatalog.Models.V4ConstantFolding);

    [Given("a v3 Library single-file model")]
    public void V3LibrarySingleFile() => PlaceFile(FixtureCatalog.Models.V3DailyRate);

    [Given("a v4 document tree whose manifest has a version and a created date")]
    public void V4TreeWithVersionAndCreated() => PlaceTree(FixtureCatalog.Trees.AcmeDemo);

    [Given("a v4 document tree with a missing definition file")]
    public void V4TreeWithAMissingDefinitionFile() =>
        // The module file of billing lists the value total; without its file the tree is refused.
        PlaceTree([.. FixtureCatalog.Trees.AcmeDemo.Where(entry => !entry.RelativePath.EndsWith("/total.value.json", StringComparison.Ordinal))]);

    [Given("a compacted single-file model")]
    public void CompactedSingleFile()
    {
        var envelope = new JsonObject
        {
            ["$schema"] = EnvelopeSchema,
            ["context"] = new JsonObject(),
            ["document"] = JsonNode.Parse(FixtureCatalog.ReadBytes(FixtureCatalog.Models.V4DailyRate)),
        };
        var file = Workspace.File("models", "compacted.json");
        Workspace.WriteText(file, envelope.ToJsonString());
        Place(file);
    }

    // --- The output ------------------------------------------------------------------------------------------------

    [Given("the output is the input itself")]
    public void OutputIsTheInput() => _output = Input;

    [Given("the output is the input's directory")]
    public void OutputIsTheInputsDirectory() => _output = ModelLocation.Of(Workspace.Directory("models"));

    [Given("the output is inside the input")]
    public void OutputIsInsideTheInput() => _output = ModelLocation.Of(Workspace.File("models", "tree", "pkg", "converted.json"));

    [Given("the output is an existing file")]
    public void OutputIsAnExistingFile()
    {
        var file = Workspace.File("out", "existing");
        Workspace.WriteText(file, "kept\n");
        _output = file;
        _outputBefore = Snapshot(file.Path);
    }

    [Given("the output is a directory that holds a file")]
    public void OutputIsADirectoryThatHoldsAFile()
    {
        var directory = Workspace.MakeDirectory("out", "existing");
        Workspace.WriteText(directory.File("notes.txt"), "kept\n");
        _output = directory;
        _outputBefore = Snapshot(directory.Path);
    }

    [Given("the output is an empty directory")]
    public void OutputIsAnEmptyDirectory() => _output = Workspace.MakeDirectory("out", "empty");

    [Given("the store holds each write until the test has read the stream")]
    public void TheStoreHoldsEachWrite() => _driver.Store.HoldWrites();

    // --- Conversions -----------------------------------------------------------------------------------------------

    /// <summary>To a <c>document tree</c> or a <c>single file</c>, the two words of a layout.</summary>
    [When("I convert the model to a {word} {word}")]
    public Task ConvertToALayout(string first, string second) => Convert(new ConversionOptions { Layout = LayoutOf($"{first} {second}") });

    [When("I convert the model without changing its layout")]
    public Task ConvertWithoutALayout() => Convert(new ConversionOptions());

    [When("I convert the model to a document tree with compaction")]
    public Task ConvertToACompactedTree() =>
        Convert(new ConversionOptions { Layout = DriverLayout.DocumentTree, Compact = CompactionArgument.Default });

    [When("I convert the model to a document tree with the compaction mode after a space")]
    public Task ConvertWithASeparatedMode() =>
        Convert(new ConversionOptions { Layout = DriverLayout.DocumentTree, Compact = CompactionArgument.Separated("names") });

    [When("I convert the model to a document tree with the compaction mode {string}")]
    public Task ConvertWithAMode(string mode) =>
        Convert(new ConversionOptions { Layout = DriverLayout.DocumentTree, Compact = CompactionArgument.WithMode(mode) });

    [When("I convert the converted model back to a single file")]
    public async Task ConvertBack()
    {
        Report.ShouldSucceed();
        _input = _output;
        _output = Workspace.File("out", "back.json");
        _report = null;
        _result = await _driver.Conversion.ConvertAsync(Input, _output, new ConversionOptions { Layout = DriverLayout.SingleFile });
    }

    [When("I start converting the model to a document tree")]
    public async Task StartConvertingToATree()
    {
        _output ??= DefaultOutput(DriverLayout.DocumentTree);
        _inputBefore = Snapshot(Input.Path);
        _running = _driver.Conversion.ConvertAsync(Input, _output, new ConversionOptions { Layout = DriverLayout.DocumentTree });
        // The command waits in its write stage until the test releases it.
        if (await Task.WhenAny(_driver.Store.WriteEntered, _running).WaitAsync(TimeSpan.FromMinutes(1)) != _driver.Store.WriteEntered)
            throw new InvalidOperationException($"The conversion ended before its write stage: {(await _running).StandardError}");
        _linesBeforeTheWrite = _driver.FlushedLines;
        _driver.Store.ReleaseWrites();
        _result = await _running.WaitAsync(TimeSpan.FromMinutes(1));
    }

    private async Task Convert(ConversionOptions options)
    {
        _output ??= DefaultOutput(options.Layout ?? DriverLayout.SingleFile);
        _inputBefore = Snapshot(Input.Path);
        _result = await _driver.Conversion.ConvertAsync(Input, _output, options);
    }

    // --- Checks ----------------------------------------------------------------------------------------------------

    [Then("the conversion succeeds")]
    public void Succeeds()
    {
        Result.ExitCode.Should().Be(0, Result.StandardError + Result.StandardOutput);
        Report.ShouldSucceed();
    }

    [Then("the conversion fails with exit code {int} and the diagnostic {string}")]
    public void FailsWith(int exitCode, string code)
    {
        Result.ExitCode.Should().Be(exitCode, Result.StandardError + Result.StandardOutput);
        Report.ExitCode.Should().Be(exitCode);
        Report.Success.Should().BeFalse();
        Report.ShouldHaveDiagnostic(code);
        Report.FilesWritten.Should().BeEmpty();
    }

    [Then("the read and convert stages are on the stream before the write begins")]
    public void StagesBeforeTheWrite()
    {
        var records = (_linesBeforeTheWrite ?? throw new InvalidOperationException("No run was held in its write stage."))
            .Select(Describe).ToArray();
        records.Should().Equal("stageStarted:read", "stageCompleted:read", "stageStarted:convert", "stageCompleted:convert", "stageStarted:write");
        Result.Events().Select(record => Describe(record.GetRawText())).Should().StartWith(records)
            .And.EndWith(new[] { "stageCompleted:write", "completed:" });
    }

    [Then("the stream has one fileWritten record for each file the report lists")]
    public void OneFileWrittenRecordPerFile()
    {
        Report.FilesWritten.Should().NotBeEmpty();
        Report.FileWrittenEvents().Should().Equal(Report.FilesWritten);
    }

    [Then("the report lists every file at the output")]
    public void TheReportListsEveryFile()
    {
        var output = Output.Path;
        var onDisk = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => ModelConvertPathsInTest.Join(output, Path.GetRelativePath(output, path)))
            .Order(StringComparer.Ordinal);
        Report.FilesWritten.Order(StringComparer.Ordinal).Should().Equal(onDisk);
        Report.FilesWritten.Should().Contain(ModelConvertPathsInTest.Join(output, "manifest.json"));
    }

    [Then("the report states the layouts {string} and {string}, the encoding {string} and IR version {string}")]
    public void TheReportStatesTheConversion(string input, string output, string encoding, string irVersion)
    {
        Report.InputLayout.Should().Be(input);
        Report.OutputLayout.Should().Be(output);
        Report.InputEncoding.Should().Be(encoding);
        Report.OutputEncoding.Should().Be(encoding);
        Report.IrVersion.Should().Be(irVersion);
        Report.Input.Should().Be(Input.Path);
        Report.Output.Should().Be(Output.Path);
        Report.CompactMode.Should().BeNull();
        Report.CompactPolicy.Should().BeNull();
    }

    [Then("the report states IR version {string}")]
    public void TheReportStatesTheIrVersion(string irVersion) => Report.IrVersion.Should().Be(irVersion);

    [Then("the report states the input encoding {string}")]
    public void TheReportStatesTheInputEncoding(string encoding) => Report.InputEncoding.Should().Be(encoding);

    [Then("the report states the compaction mode {string}")]
    public void TheReportStatesTheCompactionMode(string mode)
    {
        Report.CompactMode.Should().Be(mode);
        Report.OutputEncoding.Should().Be("compacted");
    }

    [Then("the single file says format version {int}")]
    public void TheSingleFileSaysItsFormatVersion(int version)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Output.Path));
        var formatVersion = document.RootElement.GetProperty("formatVersion");
        formatVersion.ValueKind.Should().Be(JsonValueKind.Number);
        formatVersion.GetInt32().Should().Be(version);
    }

    [Then("the streamed diagnostics equal the report's diagnostics")]
    public void StreamedDiagnosticsEqualTheReports()
    {
        Report.Diagnostics.Should().NotBeEmpty();
        Streamed("diagnostics").Should().Equal(Report.Diagnostics);
    }

    [Then("the streamed notices equal the report's notices")]
    public void StreamedNoticesEqualTheReports()
    {
        Report.Notices.Should().NotBeEmpty();
        Streamed("notices").Should().Equal(Report.Notices);
    }

    [Then("the report has a notice {string} that names {string}")]
    public void TheReportHasANotice(string code, string name)
    {
        Report.ShouldHaveNotice(code);
        Report.Notices.Should().Contain(notice => notice.Code == code && notice.Message.Contains($"'{name}'", StringComparison.Ordinal));
    }

    [Then("the message names the input as {string}")]
    public void TheMessageNamesTheInput(string description) =>
        Report.Diagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains(description, StringComparison.Ordinal)
            && diagnostic.Location == Input.Path);

    [Then("the message suggests {string}")]
    public void TheMessageSuggests(string text) =>
        Report.Diagnostics.Should().Contain(diagnostic => diagnostic.Message.Contains(text, StringComparison.Ordinal));

    [Then("nothing is at the output")]
    public void NothingIsAtTheOutput()
    {
        var output = Path.TrimEndingDirectorySeparator(Output.Path);
        File.Exists(output).Should().BeFalse();
        Directory.Exists(output).Should().BeFalse();
        var parent = Path.GetDirectoryName(output)!;
        if (Directory.Exists(parent))
            Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName)
                .Should().NotContain(name => name!.StartsWith("." + Path.GetFileName(output) + ".convert-", StringComparison.Ordinal),
                    "a refused conversion leaves no staging file or directory");
    }

    [Then("the store was asked neither to read nor to write")]
    public void TheStoreWasNotAsked()
    {
        _driver.Store.Reads.Should().Be(0);
        _driver.Store.Writes.Should().Be(0);
    }

    [Then("the input is unchanged")]
    public void TheInputIsUnchanged() => Snapshot(Input.Path).Should().Equal(_inputBefore);

    [Then("the existing output is unchanged")]
    public void TheExistingOutputIsUnchanged() => Snapshot(Output.Path).Should().Equal(_outputBefore);

    // --- Helpers ---------------------------------------------------------------------------------------------------

    private ModelLocation Input => _input ?? throw new InvalidOperationException("No model was given in this scenario.");

    private ModelLocation Output => _output ?? throw new InvalidOperationException("No conversion ran in this scenario.");

    private void PlaceFile(FixtureEntry entry)
    {
        var file = Workspace.File("models", Path.GetFileName(entry.RelativePath));
        Workspace.WriteBytes(file, FixtureCatalog.ReadBytes(entry));
        Place(file);
    }

    private void PlaceTree(IReadOnlyList<FixtureEntry> entries)
    {
        var tree = Workspace.MakeDirectory("models", "tree");
        foreach (var entry in entries)
            Workspace.WriteBytes(tree.File(entry.RelativePath[FixtureCatalog.Trees.AcmeDemoDirectory.Length..].Split('/')), FixtureCatalog.ReadBytes(entry));
        _input = tree;
    }

    private void Place(WorkspaceFile file) => _input = file;

    private static DriverLayout LayoutOf(string words) => words switch
    {
        "document tree" => DriverLayout.DocumentTree,
        "single file" => DriverLayout.SingleFile,
        _ => throw new ArgumentException($"'{words}' is not a layout: say document tree or single file.", nameof(words)),
    };

    private ModelLocation DefaultOutput(DriverLayout layout) =>
        layout == DriverLayout.DocumentTree ? Workspace.Directory("out", "converted") : Workspace.File("out", "converted.json");

    /// <summary>The records of <paramref name="name"/> events, each item read as a diagnostic of the report.</summary>
    private IReadOnlyList<ReportDiagnostic> Streamed(string name) =>
    [
        .. Result.Events()
            .Where(record => record.GetProperty("event").GetString() == name)
            .SelectMany(record => record.GetProperty(name).EnumerateArray())
            .Select(item => new ReportDiagnostic(
                item.GetProperty("code").GetString()!,
                item.GetProperty("message").GetString()!,
                item.TryGetProperty("severity", out var severity) ? severity.GetString() : null,
                item.TryGetProperty("location", out var location) ? location.GetString() : null)),
    ];

    /// <summary>The record as <c>event:stage</c>, for example <c>stageStarted:read</c>.</summary>
    private static string Describe(string line)
    {
        using var record = JsonDocument.Parse(line);
        var root = record.RootElement;
        return $"{root.GetProperty("event").GetString()}:{(root.TryGetProperty("stage", out var stage) ? stage.GetString() : "")}";
    }

    /// <summary>Each file at <paramref name="path"/> (a file, or every file below a directory) with its text.</summary>
    private static Dictionary<string, string> Snapshot(string path) =>
        File.Exists(path)
            ? new() { [""] = File.ReadAllText(path) }
            : Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .ToDictionary(file => Path.GetRelativePath(path, file), File.ReadAllText, StringComparer.Ordinal)
                : [];

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _driver.DisposeAsync();

    /// <summary>The paths of the report: the output joined with a path in the tree, with <c>/</c>.</summary>
    private static class ModelConvertPathsInTest
    {
        public static string Join(string output, string file) => output.TrimEnd('/', '\\') + "/" + file.Replace('\\', '/');
    }
}
