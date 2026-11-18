// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
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
    private readonly ModelConvertCommandDriver _driver = new();
    private string? _fileInTheWay;
    private (string? File, string Location)? _unknownMember;
    private string? _fileInTheWayBefore;
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

    /// <summary>
    /// The plain single file of IR <paramref name="ir"/> among the cases of the parent's compaction policy
    /// (<see cref="FixtureCatalog.Context"/>): a Library whose value uses one long FQName three times (v4) or twice in
    /// word arrays (v3), which the policy gives an alias.
    /// </summary>
    [Given("the plain v{int} single file of the compaction policy's cases")]
    public void ThePlainSingleFileOfThePolicyCases(int ir) => PlaceFile(FixtureCatalog.Context.Policy.Case($"v{ir}-single-file").Input);

    /// <summary>The parent's compacted single file of IR <paramref name="ir"/> (<see cref="FixtureCatalog.Context"/>).</summary>
    [Given("a compacted v{int} single-file model")]
    public void CompactedSingleFile(int ir) => PlaceFile(FixtureCatalog.Context.Example($"v{ir}-single-file").File);

    /// <summary>
    /// A compacted classic single file of the mother nested near the depth of a plain single file (4096 levels) or just
    /// past it: 2,000 or 2,060 tuple types of two levels each around its deepest type reference.
    /// </summary>
    [Given("a compacted v3 single-file model nested {string} the depth limit")]
    public void ADeeplyNestedCompactedSingleFile(string where)
    {
        var tuples = where switch
        {
            "near" => 2000,
            "just past" => 2060,
            _ => throw new ArgumentOutOfRangeException(nameof(where), where, "Use 'near' or 'just past'."),
        };
        var file = Workspace.File("models", "deep.json");
        Workspace.WriteBytes(file, Morphir.Models.TestSupport.NamePositionMother.deeplyNestedCompactedFile(tuples).Item1);
        Place(file);
    }

    /// <summary>
    /// The plain twin of the mother's deeply nested classic single file just past the depth of a plain single file
    /// (2,060 tuple types, about 4,140 levels).
    /// </summary>
    [Given("a plain v3 single-file model nested just past the depth limit")]
    public void ADeeplyNestedPlainSingleFile()
    {
        var file = Workspace.File("models", "deep.json");
        Workspace.WriteText(file, Morphir.Models.TestSupport.NamePositionMother.deeplyNestedCompactedFile(2060).Item2);
        Place(file);
    }

    /// <summary>
    /// The mother's compacted classic single file nested deeper than a read of a tree accepts (512 levels) and less
    /// deeply than the writers go (1,000): 300 tuple types, about 620 levels.
    /// </summary>
    [Given("a compacted v3 single-file model nested deeper than a tree reads")]
    public void ACompactedSingleFileDeeperThanATreeReads()
    {
        var file = Workspace.File("models", "deep.json");
        Workspace.WriteBytes(file, Morphir.Models.TestSupport.NamePositionMother.deeplyNestedCompactedFile(300).Item1);
        Place(file);
    }

    /// <summary>The parent's type file whose envelope schema URL no version of morphir declares, as a single file.</summary>
    [Given("a single-file model in an envelope whose schema this version does not declare")]
    public void SingleFileInAnUndeclaredEnvelope() => PlaceFile(FixtureCatalog.Context.Example("undeclared-schema").File);

    /// <summary>
    /// The mother's v3 tree with a member <c>metadata</c> in the definition of the value <c>default-currency</c>, where
    /// the classic model has no place for it, and where that member is.
    /// </summary>
    [Given("a v3 document tree with an unknown member in one value definition")]
    public void V3TreeWithAnUnknownMember()
    {
        PlaceTree(Morphir.Models.TestSupport.DocumentTreeMother.Invalid.v3ValueFileWithANestedUnknownMember());
        _unknownMember = (
            Morphir.Models.TestSupport.DocumentTreeMother.Invalid.v3DefaultCurrencyValueFile,
            Morphir.Models.TestSupport.DocumentTreeMother.Invalid.v3NestedUnknownValueMember);
    }

    /// <summary>The mother's v3 single file with the same member in the same value, and where that member is.</summary>
    [Given("a v3 single-file model with an unknown member in one value definition")]
    public void V3SingleFileWithAnUnknownMember()
    {
        var file = Workspace.File("models", "model.json");
        Workspace.WriteBytes(file, Morphir.Models.TestSupport.DistributionMother.SingleFiles.v3LibraryWithANestedUnknownMember);
        Place(file);
        _unknownMember = (null, Morphir.Models.TestSupport.DistributionMother.SingleFiles.v3NestedUnknownMember);
    }

    [Given("a single-file model that is not JSON")]
    public void SingleFileThatIsNotJson() => PlaceFile(FixtureCatalog.Models.Malformed);

    [Given("a compacted v4 document tree")]
    public void CompactedV4Tree() => PlaceTree(FixtureCatalog.Context.Tree(4, ContextTreeCompaction.EveryFile));

    /// <summary>The examples' v4 tree with a plain manifest, module file and type file, and a compacted value file.</summary>
    [Given("a v4 document tree with only its value file compacted")]
    public void V4TreeWithACompactedValueFile() => PlaceTree(FixtureCatalog.Context.Tree(4, ContextTreeCompaction.ValueFileOnly));

    /// <summary>The same tree without the type file that its module file lists, which the store refuses.</summary>
    [Given("a v4 document tree with only its value file compacted and no type file")]
    public void V4TreeWithACompactedValueFileAndNoTypeFile() =>
        PlaceTree(FixtureCatalog.Context.Tree(4, ContextTreeCompaction.ValueFileOnly)
            .Without(FixtureCatalog.Context.TreeLocation("type"), "the v4 tree of the name-context examples with no type file"));

    [Given("a v4 document tree whose type file references an alias that no table defines")]
    public void V4TreeWithAnUnknownAlias() => PlaceTreeWithTypeFile("unknown-alias");

    [Given("a v4 document tree with a type file in an envelope whose schema this version does not declare")]
    public void V4TreeWithATypeFileInAnUndeclaredEnvelope() => PlaceTreeWithTypeFile("undeclared-schema");

    /// <summary>The examples' plain v4 tree with its type file replaced by the parent's invalid example <paramref name="id"/>.</summary>
    private void PlaceTreeWithTypeFile(string id) =>
        PlaceTree(FixtureCatalog.Context.Tree(4, ContextTreeCompaction.NoFile)
            .WithFile(FixtureCatalog.Context.TreeLocation("type"), FixtureCatalog.Context.Example(id).File, $"the v4 tree of the name-context examples with the type file {id}"));

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

    [Given("the output's parent is an existing file")]
    public void OutputBelowAFile()
    {
        var file = Workspace.File("out", "in-the-way");
        Workspace.WriteText(file, "kept\n");
        RememberTheFileInTheWay(file.Path);
        _output = Workspace.File("out", "in-the-way", "converted");
    }

    [Given("the store holds each write until the test has read the stream")]
    public void TheStoreHoldsEachWrite() => _driver.Store.HoldWrites();

    [Given("reading the model fails with a disk error")]
    public void ReadingFails() => _driver.Store.FailReads(new IOException("The device is not ready."));

    // --- Conversions -----------------------------------------------------------------------------------------------

    /// <summary>To a <c>document tree</c> or a <c>single file</c>, the two words of a layout.</summary>
    [When("I convert the model to a {word} {word}")]
    public Task ConvertToALayout(string first, string second) => Convert(new ConversionOptions { Layout = LayoutOf($"{first} {second}") });

    /// <summary>To a document tree, with the layout after a space: <c>--layout document-tree</c>.</summary>
    [When("I convert the model to a document tree with the layout after a space")]
    public Task ConvertWithASeparatedLayout() =>
        Convert(new ConversionOptions { Layout = DriverLayout.DocumentTree, LayoutAfterASpace = true });

    [When("I convert the model without changing its layout")]
    public Task ConvertWithoutALayout() => Convert(new ConversionOptions());

    /// <summary>With a bare <c>--compact</c> and no layout: the layout of the input.</summary>
    [When("I convert the model with compaction")]
    public Task ConvertWithCompaction() => Convert(new ConversionOptions { Compact = CompactionArgument.Default });

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

    /// <summary>
    /// Holds the write, puts a file at the output (inside it when it is an empty directory), and releases the write,
    /// so the staged output cannot be moved into place.
    /// </summary>
    [When("I convert the model to a {word} {word} while a file appears at the output")]
    public async Task ConvertWhileAFileAppears(string first, string second)
    {
        var layout = LayoutOf($"{first} {second}");
        _output ??= DefaultOutput(layout);
        _driver.Store.HoldWrites();
        _running = _driver.Conversion.ConvertAsync(Input, _output, new ConversionOptions { Layout = layout });
        if (await Task.WhenAny(_driver.Store.WriteEntered, _running).WaitAsync(TimeSpan.FromMinutes(1)) != _driver.Store.WriteEntered)
            throw new InvalidOperationException($"The conversion ended before its write stage: {(await _running).StandardError}");
        var appeared = Directory.Exists(Output.Path) ? Path.Combine(Output.Path, "appeared.txt") : Output.Path;
        Directory.CreateDirectory(Path.GetDirectoryName(appeared)!);
        File.WriteAllText(appeared, "someone else's\n");
        RememberTheFileInTheWay(appeared);
        _driver.Store.ReleaseWrites();
        _result = await _running.WaitAsync(TimeSpan.FromMinutes(1));
    }

    /// <summary>The input is an empty path, which the command line accepts and no file system does.</summary>
    [When("I convert a model whose input path is empty")]
    public async Task ConvertAnEmptyInputPath()
    {
        _output = DefaultOutput(DriverLayout.SingleFile);
        _result = await _driver.RunAsync(["model", "convert", "--input", "", "--output", _output.Path, .. JsonOutputArgument.Stream.Words]);
    }

    private async Task Convert(ConversionOptions options)
    {
        // With no layout, the output has the layout of the input.
        _output ??= DefaultOutput(options.Layout ?? (Directory.Exists(Input.Path) ? DriverLayout.DocumentTree : DriverLayout.SingleFile));
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

    /// <summary>The report names the mode, and the parent's writer policy that the pinned policy cases are of.</summary>
    [Then("the report states the compaction mode {string} and the writer policy")]
    public void TheReportStatesTheCompactionModeAndThePolicy(string mode)
    {
        Report.CompactMode.Should().Be(mode);
        Report.CompactPolicy.Should().Be(FixtureCatalog.Context.Policy.Name);
    }

    [Then("the report has the notice {string}")]
    public void TheReportHasTheNotice(string code) => Report.ShouldHaveNotice(code);

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

    [Then("the report has the diagnostic {string}")]
    public void TheReportHasTheDiagnostic(string code) => Report.ShouldHaveDiagnostic(code);

    [Then("the report does not have the diagnostic {string}")]
    public void TheReportDoesNotHaveTheDiagnostic(string code) => Report.ShouldNotHaveDiagnostic(code);

    [Then("the report states the input encoding {string} and the output encoding {string}")]
    public void TheReportStatesTheEncodings(string input, string output)
    {
        Report.InputEncoding.Should().Be(input);
        Report.OutputEncoding.Should().Be(output);
    }

    /// <summary>The diagnostic with the code is at the type file of the input tree, and its message names the words.</summary>
    [Then("the diagnostic {string} is at the type file and names {string}")]
    public void TheDiagnosticIsAtTheTypeFile(string code, string words) =>
        Report.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == code)
            .Which.Should().Match<ReportDiagnostic>(diagnostic =>
                diagnostic.Location == ModelConvertPathsInTest.Join(Input.Path, FixtureCatalog.Context.TreeLocation("type"))
                && diagnostic.Message.Contains(words, StringComparison.Ordinal));

    /// <summary>The written single file is a plain document: it has no top-level <c>$schema</c>.</summary>
    /// <summary>
    /// The diagnostic is at the file of the member (the input itself for a single file), and its message starts with
    /// the member's JSON location and names it.
    /// </summary>
    [Then("the diagnostic {string} names the member that the classic model has no place for")]
    public void TheDiagnosticNamesTheUnknownMember(string code)
    {
        var (file, location) = _unknownMember ?? throw new InvalidOperationException("The scenario has no model with an unknown member.");
        var expected = file is null ? Input.Path : ModelConvertPathsInTest.Join(Input.Path, file);
        var diagnostic = Report.Diagnostics.Should().ContainSingle(found => found.Code == code).Which;
        diagnostic.Location.Should().Be(expected);
        diagnostic.Message.Should().StartWith(location + ":").And.Contain("'metadata'");
    }

    [Then("the single file is plain")]
    public void TheSingleFileIsPlain() => IsPlain(Output.Path);

    /// <summary>Each file of the written tree is a plain document.</summary>
    [Then("every file at the output is plain")]
    public void EveryFileAtTheOutputIsPlain()
    {
        var files = Directory.EnumerateFiles(Output.Path, "*.json", SearchOption.AllDirectories).ToArray();
        files.Should().NotBeEmpty();
        foreach (var file in files) IsPlain(file);
    }

    /// <summary>
    /// The written single file is one envelope of the schema that the policy's compacted cases name, and it is
    /// in the policy's written form: members sorted as RFC 8785 sorts them, no whitespace.
    /// </summary>
    [Then("the single file is compacted")]
    public void TheSingleFileIsCompacted()
    {
        File.Exists(Output.Path).Should().BeTrue("a single file stays one file");
        using var expected = JsonDocument.Parse(FixtureCatalog.ReadBytes(FixtureCatalog.Context.Policy.Case("v4-type").Expected));
        var bytes = File.ReadAllBytes(Output.Path);
        using var document = JsonDocument.Parse(bytes);
        document.RootElement.GetProperty("$schema").GetString().Should().Be(expected.RootElement.GetProperty("$schema").GetString());
        System.Text.Encoding.UTF8.GetString(bytes).Should().StartWith("{\"$schema\":").And.NotContain("\n");
    }

    private static void IsPlain(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { MaxDepth = 4096 });
        document.RootElement.TryGetProperty("$schema", out _).Should().BeFalse($"{file} is written plain");
    }

    /// <summary>The diagnostic with the code is at the output, or at a file below it.</summary>
    [Then("the diagnostic {string} is at the output")]
    public void TheDiagnosticIsAtTheOutput(string code) =>
        Report.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == code && diagnostic.Location != null
            && (diagnostic.Location == Output.Path || diagnostic.Location.StartsWith(ModelConvertPathsInTest.Join(Output.Path, ""), StringComparison.Ordinal)));

    [Then("the report states no input encoding")]
    public void TheReportStatesNoInputEncoding() => Report.InputEncoding.Should().BeNull();

    [Then("the stream ends with one completed record")]
    public void TheStreamEndsWithOneCompletedRecord()
    {
        var records = Result.Events().Select(record => Describe(record.GetRawText())).ToArray();
        records.Count(record => record.StartsWith("completed:", StringComparison.Ordinal)).Should().Be(1, Result.StandardOutput);
        records.Should().EndWith("completed:");
    }

    [Then("the read stage is completed as not passed")]
    public void TheReadStageIsCompletedAsNotPassed() =>
        Result.Events().Should().ContainSingle(record => record.GetProperty("event").GetString() == "stageCompleted"
            && record.GetProperty("stage").GetString() == "read" && !record.GetProperty("passed").GetBoolean());

    [Then("the file in the way is unchanged")]
    public void TheFileInTheWayIsUnchanged()
    {
        var file = _fileInTheWay ?? throw new InvalidOperationException("No file was put in the way in this scenario.");
        File.ReadAllText(file).Should().Be(_fileInTheWayBefore);
    }

    [Then("nothing is at the output")]
    public void NothingIsAtTheOutput()
    {
        var output = Path.TrimEndingDirectorySeparator(Output.Path);
        File.Exists(output).Should().BeFalse();
        Directory.Exists(output).Should().BeFalse();
        NoStagingIsLeft();
    }

    [Then("no staging file or directory is left beside the output")]
    public void NoStagingIsLeft()
    {
        var output = Path.TrimEndingDirectorySeparator(Output.Path);
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

    private void PlaceTree(FixtureTree tree) => _input = Workspace.WriteTree(tree, "models", "tree");

    /// <summary>Writes the files of a mother's tree below <c>models/tree</c>.</summary>
    private void PlaceTree(Morphir.Files.VFileTree files)
    {
        var tree = Workspace.MakeDirectory("models", "tree");
        foreach (var file in Morphir.Models.TestSupport.DocumentTreeStoreDriver.texts(files))
            Workspace.WriteText(tree.File(file.Key.Split('/')), file.Value);
        _input = tree;
    }

    private void Place(WorkspaceFile file) => _input = file;

    private void RememberTheFileInTheWay(string path)
    {
        _fileInTheWay = path;
        _fileInTheWayBefore = File.ReadAllText(path);
    }

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
