// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Morphir.TestSupport.Fixtures;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of <c>model convert</c> and of its report, and the kit's document-tree cases as models of a scenario.
/// Each conversion writes to a new place in the work area (<c>converted-1</c>, <c>converted-2.json</c>, ...), and
/// "the written model" is the output of the last conversion.
/// </summary>
[Binding]
public sealed class ModelConversionSteps(IModelConversionDriver conversion, IModelVerificationDriver verification, IScenarioWorkspace workspace, ScenarioRun run)
{
    private int _conversions;
    private ModelLocation? _output;
    private WorkspaceFile? _writtenFile;
    private WorkspaceDirectory? _writtenTree;
    private WorkspaceFile? _previousWrittenFile;
    private WorkspaceDirectory? _previousWrittenTree;
    private WorkspaceDirectory? _presetOutput;
    private (WorkspaceFile File, byte[] Bytes)? _fileInTheOutput;
    private KitTreeCase? _kitCase;
    private KitSpellingTree? _spellingTree;
    private string? _rejectedFile;

    private ConversionReport Report => ConversionReport.Of(run.Result);

    private ModelLocation Output => _output ?? throw new InvalidOperationException("The scenario converted no model before this step.");

    private WorkspaceDirectory WrittenTree => _writtenTree ?? throw new InvalidOperationException("The last conversion of the scenario wrote no tree.");

    private WorkspaceFile WrittenFile => _writtenFile ?? throw new InvalidOperationException("The last conversion of the scenario wrote no single file.");

    // ---- The model of the scenario: a case of the kit ---------------------------------------------------------------

    /// <summary>The files of a kit tree case (<see cref="FixtureCatalog.Kit"/>), with the extension of the JSON profile, in the directory <c>model</c>.</summary>
    [Given("the tree of the kit case {string}")]
    public void TheTreeOfTheKitCase(string id)
    {
        _kitCase = FixtureCatalog.Kit.Case(id);
        if (_kitCase.Files.Count == 0) throw new InvalidOperationException($"The kit case {id} has no tree.");
        run.ModelTree = workspace.WriteKitTree(_kitCase.Files, "model");
    }

    /// <summary>
    /// The tree of the kit case <paramref name="tree"/> with its one type file replaced by the document that the kit case
    /// <paramref name="rejected"/> rejects. The kit decodes that document alone, having no grammar for a rejected tree;
    /// a tree reader checks each file of a tree the same way.
    /// </summary>
    [Given("the tree of the kit case {string} with the rejected document of the kit case {string} as its type file")]
    public void TheTreeWithTheRejectedDocument(string tree, string rejected)
    {
        _kitCase = FixtureCatalog.Kit.Case(rejected);
        var document = _kitCase.RejectedDocument ?? throw new InvalidOperationException($"The kit case {rejected} rejects no document.");
        var files = new Dictionary<string, string>(FixtureCatalog.Kit.Case(tree).Files, StringComparer.Ordinal);
        var types = files.Keys.Where(path => path.EndsWith(".type", StringComparison.Ordinal)).ToArray();
        if (types.Length != 1) throw new InvalidOperationException($"The kit case {tree} has {types.Length} type files, not one.");
        files[types[0]] = document;
        _rejectedFile = types[0] + KitSpellingTrees.JsonExtension;
        run.ModelTree = workspace.WriteKitTree(files, "model");
    }

    /// <summary>A tree that holds the file whose spelling the kit case pins, in its canonical spelling (<see cref="KitSpellingTrees"/>).</summary>
    [Given("a tree with the file of the kit case {string}")]
    public void ATreeWithTheFileOfTheKitCase(string id) => TheSpellingTree(KitSpellingTrees.Canonical(Spelling(id)));

    /// <summary>A tree that holds the file whose spelling the kit case pins, in the spelling that the case says a reader accepts.</summary>
    [Given("a tree with the accepted file spelling of the kit case {string}")]
    public void ATreeWithTheAcceptedFileSpelling(string id) => TheSpellingTree(KitSpellingTrees.Accepted(Spelling(id)));

    private void TheSpellingTree(KitSpellingTree tree)
    {
        _spellingTree = tree;
        run.ModelTree = workspace.WriteKitTree(tree.Files, "model");
    }

    private static KitFileSpelling Spelling(string id) =>
        FixtureCatalog.Kit.FileSpellings.FirstOrDefault(spelling => spelling.Id == id)
        ?? throw new KeyNotFoundException($"The pinned kit has no case '{id}' that pins a file's spelling.");

    /// <summary>
    /// The plain single file of IR <paramref name="ir"/> among the cases of the parent's compaction policy
    /// (<see cref="FixtureCatalog.Context"/>), in <c>model.json</c>: a Library whose one value uses one long FQName three
    /// times (v4) or twice as word arrays (v3), which the policy gives an alias, in a single file and in the value's
    /// file of a tree.
    /// </summary>
    [Given("the plain v{int} single file of the compaction policy's cases")]
    public void ThePlainSingleFileOfThePolicyCases(int ir) => ThePlainSingleFileOfThePolicyCasesInTheFile(ir, "model.json");

    /// <summary>
    /// The plain single file of the policy's cases in the file <paramref name="name"/> of the work area: for a
    /// documentation page that names its model file.
    /// </summary>
    [Given("the plain v{int} single file of the compaction policy's cases in the file {string}")]
    public void ThePlainSingleFileOfThePolicyCasesInTheFile(int ir, string name)
    {
        var model = workspace.File(name);
        workspace.WriteBytes(model, FixtureCatalog.ReadBytes(FixtureCatalog.Context.Policy.Case($"v{ir}-single-file").Input));
        run.Model = model;
    }

    // ---- The output -------------------------------------------------------------------------------------------------

    /// <summary>The next conversion writes into a directory that already holds a file.</summary>
    [Given("the output is a directory that holds a file")]
    public void TheOutputIsADirectoryThatHoldsAFile()
    {
        _presetOutput = workspace.MakeDirectory(NextName());
        var file = _presetOutput.File("notes.txt");
        workspace.WriteText(file, "Kept by the user.\n");
        _fileInTheOutput = (file, workspace.ReadBytes(file));
    }

    // ---- Converting -------------------------------------------------------------------------------------------------

    [When("I convert the model to a single file")]
    public Task ConvertTheModelToASingleFile() => Convert(run.ModelLocation, ModelLayout.SingleFile);

    [When("I convert the model to a document tree")]
    public Task ConvertTheModelToADocumentTree() => Convert(run.ModelLocation, ModelLayout.DocumentTree);

    /// <summary>Converts the model of the scenario with no layout and no compaction: the layout and the encoding of the input.</summary>
    [When("I convert the model without changing its layout or its encoding")]
    public Task ConvertTheModelWithoutChangingIt() => Convert(run.ModelLocation, layout: null);

    /// <summary>Converts the model of the scenario with a bare compaction option and no layout: it keeps its layout.</summary>
    [When("I compact the model")]
    public Task CompactTheModel() => Convert(run.ModelLocation, layout: null, CompactionArgument.Default);

    [When("I compact the model to a document tree")]
    public Task CompactTheModelToADocumentTree() => Convert(run.ModelLocation, ModelLayout.DocumentTree, CompactionArgument.Default);

    /// <summary>Converts the model of the scenario with the compaction mode given with an equals sign, and no layout.</summary>
    [When("I convert the model with the compaction mode {string}")]
    public Task ConvertWithTheCompactionMode(string mode) => Convert(run.ModelLocation, layout: null, CompactionArgument.WithMode(mode));

    /// <summary>Converts the model of the scenario with the compaction mode as the next argument, which the command refuses.</summary>
    [When("I convert the model with the compaction mode {string} after a space")]
    public Task ConvertWithTheCompactionModeAfterASpace(string mode) => Convert(run.ModelLocation, layout: null, CompactionArgument.Separated(mode));

    [When("I convert the written model to a single file")]
    public Task ConvertTheWrittenModelToASingleFile() => Convert(Output, ModelLayout.SingleFile);

    [When("I convert the written model to a document tree")]
    public Task ConvertTheWrittenModelToADocumentTree() => Convert(Output, ModelLayout.DocumentTree);

    /// <summary>Converts the plain twin of the compacted model of the scenario, which a step put beside it.</summary>
    [When("I convert the plain twin of the model to a single file")]
    public Task ConvertThePlainTwinToASingleFile() => Convert(PlainTwin, ModelLayout.SingleFile);

    [When("I convert the plain twin of the model to a document tree")]
    public Task ConvertThePlainTwinToADocumentTree() => Convert(PlainTwin, ModelLayout.DocumentTree);

    private ModelLocation PlainTwin => run.PlainTwin ?? throw new InvalidOperationException("The scenario has no compacted model with a plain twin before this step.");

    /// <summary>Verifies the tree that the last conversion wrote, as JSON; the tree is then the model of the scenario.</summary>
    [When("I verify the written tree as JSON")]
    public async Task VerifyTheWrittenTree()
    {
        run.ModelTree = WrittenTree;
        run.Result = await verification.VerifyAsync(WrittenTree, new VerificationOptions { Json = JsonOutputArgument.Stream });
    }

    /// <summary>Verifies the single file that the last conversion wrote, as JSON; the file is then the model of the scenario.</summary>
    [When("I verify the written single file as JSON")]
    public async Task VerifyTheWrittenSingleFile()
    {
        run.Model = WrittenFile;
        run.Result = await verification.VerifyAsync(WrittenFile, new VerificationOptions { Json = JsonOutputArgument.Stream });
    }

    private async Task Convert(ModelLocation input, ModelLayout? layout, CompactionArgument? compact = null)
    {
        // With no layout, the output has the layout of the input.
        var inputIsATree = (_writtenTree is { } written && input.Path == written.Path) || (run.ModelTree is { } model && input.Path == model.Path);
        var tree = layout == ModelLayout.DocumentTree || (layout is null && inputIsATree);
        if (_writtenFile is { } lastFile) _previousWrittenFile = lastFile;
        if (_writtenTree is { } lastTree) _previousWrittenTree = lastTree;
        if (_presetOutput is { } preset)
        {
            _output = preset;
            _writtenTree = preset;
            _writtenFile = null;
            _presetOutput = null;
        }
        else if (tree)
        {
            _writtenTree = workspace.Directory(NextName());
            _writtenFile = null;
            _output = _writtenTree;
        }
        else
        {
            _writtenFile = workspace.File(NextName() + ".json");
            _writtenTree = null;
            _output = _writtenFile;
        }

        run.Result = await conversion.ConvertAsync(input, _output, new ConversionOptions { Layout = layout, Compact = compact });
    }

    private string NextName() => "converted-" + (++_conversions).ToString(CultureInfo.InvariantCulture);

    // ---- The report -------------------------------------------------------------------------------------------------

    [Then("the conversion succeeds")]
    public void TheConversionSucceeds()
    {
        run.Result.ShouldExitWith(0);
        Report.ShouldSucceed();
    }

    [Then("the conversion fails with the exit code {int} and the diagnostic {string}")]
    public void TheConversionFails(int exitCode, string code)
    {
        run.Result.ShouldExitWith(exitCode);
        var report = Report;
        report.Check(!report.Success, "The conversion report says that the conversion succeeded.");
        report.ShouldHaveDiagnostic(code);
    }

    [Then("the conversion report states the layouts {string} and {string}")]
    public void TheReportStatesTheLayouts(string input, string output)
    {
        var report = Report;
        report.Check(report.InputLayout == input && report.OutputLayout == output,
            $"The conversion report states the layouts {report.InputLayout ?? "null"} and {report.OutputLayout ?? "null"}, not {input} and {output}.");
    }

    [Then("the conversion report states the encodings {string} and {string}")]
    public void TheReportStatesTheEncodings(string input, string output)
    {
        var report = Report;
        report.Check(report.InputEncoding == input && report.OutputEncoding == output,
            $"The conversion report states the encodings {report.InputEncoding ?? "null"} and {report.OutputEncoding ?? "null"}, not {input} and {output}.");
    }

    /// <summary>The report does not say whether the input is plain or compacted, as when its only envelopes are undeclared.</summary>
    [Then("the conversion report states no input encoding")]
    public void TheReportStatesNoInputEncoding()
    {
        var report = Report;
        report.Check(report.InputEncoding is null, $"The conversion report states the input encoding {report.InputEncoding}.");
    }

    [Then("the conversion report states the IR version {string}")]
    public void TheReportStatesTheIrVersion(string version)
    {
        var report = Report;
        report.Check(report.IrVersion == version, $"The conversion report states the IR version {report.IrVersion ?? "null"}, not {version}.");
    }

    /// <summary>The report names the compaction mode, and the parent's writer policy whose fixed cases the catalog pins.</summary>
    [Then("the conversion report names the compaction mode {string} and the writer policy of the pinned cases")]
    public void TheReportNamesTheModeAndThePolicy(string mode)
    {
        var report = Report;
        var policy = FixtureCatalog.Context.Policy.Name;
        report.Check(report.CompactMode == mode && report.CompactPolicy == policy,
            $"The conversion report names the compaction mode {report.CompactMode ?? "null"} and the policy {report.CompactPolicy ?? "null"}, not {mode} and {policy}.");
    }

    /// <summary>The diagnostic with the code suggests the words in its message.</summary>
    [Then("the diagnostic {string} suggests {string}")]
    public void TheDiagnosticSuggests(string code, string words)
    {
        var diagnostic = Diagnostic(code);
        Report.Check(diagnostic.Message.Contains(words, StringComparison.Ordinal), $"The diagnostic {code} does not suggest '{words}': {diagnostic.Message}");
    }

    /// <summary>The diagnostic suggests the compaction mode given with an equals sign, the one form the option takes.</summary>
    [Then("the diagnostic {string} suggests the compaction mode {string} with an equals sign")]
    public void TheDiagnosticSuggestsTheModeWithAnEqualsSign(string code, string mode) =>
        TheDiagnosticSuggests(code, string.Join(" ", CompactionArgument.WithMode(mode).Words));

    [Then("the conversion report has the notice {string}")]
    public void TheReportHasTheNotice(string code) => Report.ShouldHaveNotice(code);

    /// <summary>The diagnostic with the code names the input in its message, with these words.</summary>
    [Then("the diagnostic {string} names the input as {string}")]
    public void TheDiagnosticNamesTheInputAs(string code, string words)
    {
        var diagnostic = Diagnostic(code);
        Report.Check(diagnostic.Message.Contains(words, StringComparison.Ordinal), $"The diagnostic {code} does not name the input as '{words}': {diagnostic.Message}");
    }

    /// <summary>
    /// The diagnostic with the code is at the fault of the model of the scenario: at its file of the tree, or at the
    /// tree when the fault is in no one file, and its message names each word of the fault.
    /// </summary>
    [Then("the diagnostic {string} names the place of the fault")]
    public void TheDiagnosticNamesThePlaceOfTheFault(string code)
    {
        var fault = run.Fault ?? throw new InvalidOperationException("The scenario has no faulty model before this step.");
        var diagnostic = Diagnostic(code);
        var location = Slashes(diagnostic.Location);
        var expected = fault.File is { } file ? Slashes(run.ModelLocation.Path) + "/" + file : Slashes(run.ModelLocation.Path);
        Report.Check(location == expected, $"The diagnostic {code} is at {diagnostic.Location ?? "no location"}, not at {expected}.");
        foreach (var name in fault.Names)
            Report.Check(diagnostic.Message.Contains(name, StringComparison.Ordinal), $"The diagnostic {code} does not name {name}: {diagnostic.Message}");
    }

    [Then("the diagnostic {string} names the output")]
    public void TheDiagnosticNamesTheOutput(string code)
    {
        var diagnostic = Diagnostic(code);
        Report.Check(Slashes(diagnostic.Location) == Slashes(Output.Path), $"The diagnostic {code} is at {diagnostic.Location ?? "no location"}, not at the output {Output.Path}.");
    }

    [Then("the file in the output is unchanged")]
    public void TheFileInTheOutputIsUnchanged()
    {
        var (file, bytes) = _fileInTheOutput ?? throw new InvalidOperationException("The scenario put no file in the output before this step.");
        Report.Check(workspace.Exists(file) && workspace.ReadBytes(file).AsSpan().SequenceEqual(bytes), $"The file {file.Name} in the output changed.");
        var files = workspace.FilesIn(WrittenTree);
        Report.Check(files.Count == 1, $"The output holds more than the file that was there: {string.Join(", ", files)}.");
    }

    /// <summary>Nothing is at the output, and the work area has no staging place of the command beside it.</summary>
    [Then("nothing is at the output")]
    public void NothingIsAtTheOutput()
    {
        var there = _writtenFile is { } file ? workspace.Exists(file) : workspace.Exists(WrittenTree);
        Report.Check(!there, $"The output {Output.Path} is there.");
        var name = Output.Path.Replace('\\', '/')[(Output.Path.Replace('\\', '/').LastIndexOf('/') + 1)..];
        var staging = workspace.EntriesIn(workspace.Work).Where(entry => entry.StartsWith($".{name}.", StringComparison.Ordinal)).ToArray();
        Report.Check(staging.Length == 0, $"The work area holds what the command staged: {string.Join(", ", staging)}.");
    }

    /// <summary>The report lists exactly the files below the written tree, each as the output joined with its path in the tree.</summary>
    [Then("the conversion report lists every file of the written tree")]
    public void TheReportListsEveryFile()
    {
        var report = Report;
        var prefix = Slashes(Output.Path) + "/";
        var listed = report.FilesWritten.Select(Slashes).ToArray();
        report.Check(listed.All(path => path.StartsWith(prefix, StringComparison.Ordinal)), $"A file of the report is not below the output {Output.Path}: {string.Join(", ", listed)}.");
        var files = workspace.FilesIn(WrittenTree);
        report.Check(listed.Select(path => path[prefix.Length..]).Order(StringComparer.Ordinal).SequenceEqual(files),
            $"The report lists {string.Join(", ", listed)}, and the tree has {string.Join(", ", files)}.");
    }

    [Then("the stream has one fileWritten record for each written file")]
    public void TheStreamHasOneFileWrittenRecordForEachFile()
    {
        var report = Report;
        var events = report.FileWrittenEvents();
        report.Check(events.SequenceEqual(report.FilesWritten), $"The fileWritten records are {string.Join(", ", events)}, and the report lists {string.Join(", ", report.FilesWritten)}.");
    }

    [Then("the conversion report has {int} notices")]
    public void TheReportHasNotices(int count)
    {
        var report = Report;
        report.Check(report.Notices.Count == count, $"The conversion report has {report.Notices.Count} notices, not {count}.");
    }

    /// <summary>The <c>notices</c> records of the stream hold the notices of the report, in its order.</summary>
    [Then("the stream has the notices of the conversion report")]
    public void TheStreamHasTheNoticesOfTheReport()
    {
        var report = Report;
        var streamed = run.Result.Events(JsonCommandReport.MaxDepth)
            .Where(record => record.TryGetProperty(JsonOutputAssertions.EventMember, out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "notices")
            .SelectMany(record => record.GetProperty("notices").EnumerateArray().Select(notice => notice.GetRawText()))
            .ToArray();
        var reported = report.Required("notices").EnumerateArray().Select(notice => notice.GetRawText()).ToArray();
        report.Check(streamed.SequenceEqual(reported), $"The stream has the notices {string.Join(", ", streamed)}, and the report has {string.Join(", ", reported)}.");
    }

    /// <summary>A notice with the code names the member, in quotes, in its message.</summary>
    [Then("the conversion report has a notice {string} that names {string}")]
    public void TheReportHasANoticeThatNames(string code, string name)
    {
        var report = Report;
        report.ShouldHaveNotice(code);
        report.Check(report.Notices.Any(notice => notice.Code == code && notice.Message.Contains($"'{name}'", StringComparison.Ordinal)),
            $"No notice {code} of the conversion report names '{name}'.");
    }

    // ---- The written files ------------------------------------------------------------------------------------------

    [Then("the written single file says the format version {int}")]
    public void TheWrittenFileSaysTheFormatVersion(int version)
    {
        var formatVersion = JsonNode.Parse(workspace.ReadText(WrittenFile))!["formatVersion"];
        Report.Check(formatVersion is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.GetValue<int>() == version,
            $"The written file says the format version {formatVersion?.ToJsonString() ?? "nothing"}, not {version}.");
    }

    [Then("the written single file says the format version {string}")]
    public void TheWrittenFileSaysTheReleaseFormatVersion(string version)
    {
        var formatVersion = JsonNode.Parse(workspace.ReadText(WrittenFile))!["formatVersion"];
        Report.Check(formatVersion is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() == version,
            $"The written file says the format version {formatVersion?.ToJsonString() ?? "nothing"}, not \"{version}\".");
    }

    [Then("every file of the written tree says the format version {string}")]
    public void EveryFileOfTheWrittenTreeSaysTheFormatVersion(string version)
    {
        foreach (var path in workspace.FilesIn(WrittenTree))
        {
            var formatVersion = JsonNode.Parse(workspace.ReadText(WrittenTree.File(path.Split('/'))))!["formatVersion"];
            Report.Check(formatVersion is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() == version,
                $"The tree file {path} says the format version {formatVersion?.ToJsonString() ?? "nothing"}, not \"{version}\".");
        }
    }

    /// <summary>The written single file is a plain document: its top level has no <c>$schema</c>, the mark of a compaction envelope.</summary>
    [Then("the written single file is plain")]
    public void TheWrittenFileIsPlain()
    {
        var root = JsonNode.Parse(workspace.ReadText(WrittenFile))!.AsObject();
        Report.Check(!root.ContainsKey("$schema"), $"The written file {WrittenFile.Name} is in a compaction envelope.");
    }

    /// <summary>
    /// The written single file is one file in a compaction envelope of the schema that the policy's compacted cases
    /// name, in the policy's written form (members sorted as RFC 8785 sorts them, no whitespace): <c>$schema</c>
    /// first, on one line.
    /// </summary>
    [Then("the written single file is compacted")]
    public void TheWrittenFileIsCompacted()
    {
        var text = workspace.ReadText(WrittenFile);
        var schema = JsonNode.Parse(text)!.AsObject()["$schema"]?.GetValue<string>();
        Report.Check(schema == PolicySchema, $"The written file {WrittenFile.Name} is not in an envelope of {PolicySchema}: its $schema is {schema ?? "missing"}.");
        Report.Check(text.StartsWith("{\"$schema\":", StringComparison.Ordinal) && !text.Contains('\n', StringComparison.Ordinal),
            $"The written file {WrittenFile.Name} is not in the policy's written form: it does not start with its $schema on one line.");
    }

    /// <summary>
    /// At least one file of the written tree is in a compaction envelope; the scenario's compacted files are then the
    /// written tree's, as paths from its root, for a verification of the tree.
    /// </summary>
    [Then("the written tree has a compacted file")]
    public void TheWrittenTreeHasACompactedFile()
    {
        var compacted = workspace.FilesIn(WrittenTree)
            .Where(path => JsonNode.Parse(workspace.ReadText(WrittenTree.File(path.Split('/'))))!.AsObject()["$schema"]?.GetValue<string>() == PolicySchema)
            .ToArray();
        Report.Check(compacted.Length > 0, $"No file of the written tree is in an envelope of {PolicySchema}.");
        run.CompactedFiles = compacted;
    }

    /// <summary>The envelope schema that the compacted cases of the parent's policy name.</summary>
    private static string PolicySchema =>
        JsonNode.Parse(FixtureCatalog.ReadText(FixtureCatalog.Context.Policy.All.First(policyCase => policyCase.Compacted).Expected))!["$schema"]!.GetValue<string>();

    /// <summary>The single files that the last two conversions wrote are the same JSON.</summary>
    [Then("the last two written single files are the same")]
    public void TheLastTwoWrittenFilesAreTheSame()
    {
        var previous = _previousWrittenFile ?? throw new InvalidOperationException("The conversion before the last one of the scenario wrote no single file.");
        SameJson(workspace.ReadText(WrittenFile), workspace.ReadText(previous), $"The written single file {WrittenFile.Name} is not the file {previous.Name}");
    }

    /// <summary>The trees that the last two conversions wrote have the same files, each the same JSON.</summary>
    [Then("the last two written trees are the same")]
    public void TheLastTwoWrittenTreesAreTheSame()
    {
        var previous = _previousWrittenTree ?? throw new InvalidOperationException("The conversion before the last one of the scenario wrote no tree.");
        var files = workspace.FilesIn(WrittenTree);
        var previousFiles = workspace.FilesIn(previous);
        Report.Check(files.Count > 0 && files.SequenceEqual(previousFiles),
            $"The written tree has the files {string.Join(", ", files)}, and the tree before it {string.Join(", ", previousFiles)}.");
        foreach (var path in files)
            SameJson(workspace.ReadText(WrittenTree.File(path.Split('/'))), workspace.ReadText(previous.File(path.Split('/'))),
                $"The written file {path} is not the file of the tree before it");
    }

    /// <summary>The written single file is, as JSON, the canonical single file of the kit case.</summary>
    [Then("the written single file is the canonical spelling of the kit case")]
    public void TheWrittenFileIsTheCanonicalSpelling()
    {
        var kitCase = KitCase;
        var canonical = kitCase.CanonicalSingleFile ?? throw new InvalidOperationException($"The kit case {kitCase.Id} has no canonical single file.");
        SameJson(workspace.ReadText(WrittenFile), canonical, $"The written single file is not the canonical spelling of {kitCase.Id}");
    }

    /// <summary>The written tree has the files of the kit case, at their places with the extension of the JSON profile, each the same JSON.</summary>
    [Then("the written tree has the files of the kit case")]
    public void TheWrittenTreeHasTheFilesOfTheKitCase()
    {
        var kitCase = KitCase;
        var files = workspace.FilesIn(WrittenTree);
        var expected = kitCase.Files.Keys.Select(path => path + KitSpellingTrees.JsonExtension).Order(StringComparer.Ordinal).ToArray();
        Report.Check(files.SequenceEqual(expected), $"The written tree has the files {string.Join(", ", files)}, and {kitCase.Id} has {string.Join(", ", expected)}.");
        foreach (var (path, json) in kitCase.Files)
            SameJson(workspace.ReadText(WrittenTree.File((path + KitSpellingTrees.JsonExtension).Split('/'))), json, $"The written file {path} is not the file of {kitCase.Id}");
    }

    /// <summary>The written tree has the file whose spelling the kit case pins, at its place, in the canonical spelling.</summary>
    [Then("the written tree has the file of the kit case in its canonical spelling")]
    public void TheWrittenTreeHasTheFileInItsCanonicalSpelling()
    {
        var tree = _spellingTree ?? throw new InvalidOperationException("The scenario has no tree of a kit file spelling before this step.");
        var file = WrittenTree.File((tree.SpelledFile + KitSpellingTrees.JsonExtension).Split('/'));
        Report.Check(workspace.Exists(file), $"The written tree has no file {tree.SpelledFile}{KitSpellingTrees.JsonExtension}.");
        SameJson(workspace.ReadText(file), tree.Spelling.CanonicalJson, $"The written file {tree.SpelledFile} is not the canonical spelling of {tree.Spelling.Id}");
    }

    /// <summary>The conversion exits with 1 and has the kit case's diagnostic, at the file that holds the rejected document.</summary>
    [Then("the conversion fails with the diagnostic of the kit case at the rejected file")]
    public void TheConversionFailsWithTheDiagnosticOfTheKitCase()
    {
        var kitCase = KitCase;
        var code = kitCase.Diagnostic ?? throw new InvalidOperationException($"The kit case {kitCase.Id} has no diagnostic.");
        TheConversionFails(1, code);
        var expected = Slashes(run.ModelLocation.Path) + "/" + (_rejectedFile ?? throw new InvalidOperationException("The scenario has no rejected file."));
        Report.Check(Report.Diagnostics.Any(diagnostic => diagnostic.Code == code && Slashes(diagnostic.Location) == expected),
            $"No diagnostic {code} of the conversion report is at {expected}.");
    }

    private KitTreeCase KitCase => _kitCase ?? throw new InvalidOperationException("The scenario has no kit case before this step.");

    private ReportDiagnostic Diagnostic(string code) =>
        Report.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Code == code)
        ?? throw new CommandAssertionException($"The conversion report has no diagnostic {code}.", run.Result);

    private void SameJson(string actual, string expected, string reason) =>
        Report.Check(JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)),
            $"{reason}.\nWritten:  {JsonNode.Parse(actual)!.ToJsonString()}\nExpected: {JsonNode.Parse(expected)!.ToJsonString()}");

    private static string Slashes(string? path) => (path ?? "").Replace('\\', '/');
}
