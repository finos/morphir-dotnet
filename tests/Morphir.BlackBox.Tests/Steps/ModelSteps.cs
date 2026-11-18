// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using Morphir.TestSupport.Fixtures;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>The steps that put the model of a scenario into its workspace, and that change it.</summary>
[Binding]
public sealed class ModelSteps(IScenarioWorkspace workspace, ScenarioRun run)
{
    [Given("the model fixture {string}")]
    public void TheModelFixture(string fixture) => run.Model = workspace.CopyFixture(fixture);

    [Given("the model fixture {string} as {string}")]
    public void TheModelFixtureAs(string fixture, string name) => run.Model = workspace.CopyFixture(fixture, name);

    [Given("the model tree fixture {string}")]
    public void TheModelTreeFixture(string fixture) => run.ModelTree = workspace.CopyFixtureDirectory(fixture);

    [Given("no model file {string}")]
    public void NoModelFile(string name) => run.Model = workspace.File(name);

    /// <summary>A valid single-file model of a classic format version, from <see cref="FixtureCatalog.Models"/>.</summary>
    [Given("a valid IR v{int} model")]
    public void AValidModel(int version) => TheCatalogModel(version switch
    {
        1 => FixtureCatalog.Models.V1EmptyLibrary,
        2 => FixtureCatalog.Models.V2EmptyLibrary,
        3 => FixtureCatalog.Models.V3DailyRate,
        4 => FixtureCatalog.Models.V4DailyRate,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "The catalog has a valid model of the versions 1 to 4."),
    });

    /// <summary>
    /// A valid single-file model, as <see cref="AValidModel"/> gives it, in the file <paramref name="name"/> of the work
    /// area: for a documentation page that names its model file.
    /// </summary>
    [Given("a valid IR v{int} model in the file {string}")]
    public void AValidModelInTheFile(int version, string name)
    {
        AValidModel(version);
        var model = workspace.File(name);
        workspace.WriteBytes(model, workspace.ReadBytes(run.Model));
        run.Model = model;
    }

    /// <summary>A single-file v4 Library with two modules, <see cref="FixtureCatalog.Models.V4ConstantFolding"/>.</summary>
    [Given("a valid IR v4 model with two modules")]
    public void AValidV4ModelWithTwoModules() => TheCatalogModel(FixtureCatalog.Models.V4ConstantFolding);

    /// <summary>A v4 tree whose manifest has <c>version</c> and <c>created</c>, <see cref="FixtureCatalog.Trees.AcmeDemoTree"/>.</summary>
    [Given("a v4 tree whose manifest has a version and a created date")]
    public void AV4TreeWithAVersionAndACreatedDate() => TheCatalogTree(FixtureCatalog.Trees.AcmeDemoTree);

    /// <summary>A single-file v4 model with one type node whose tag no type node has, and where that node is.</summary>
    [Given("an IR v4 model with an unknown tag in one type node")]
    public void AV4ModelWithAnUnknownTypeTag()
    {
        TheCatalogModel(FixtureCatalog.Models.V4WithAnUnknownTypeTag.Entry);
        run.Fault = FixtureCatalog.Models.V4WithAnUnknownTypeTag.Fault;
    }

    /// <summary>
    /// The document tree that the document-tree store writes from a mother case of a Library with a dependency, pinned
    /// in <see cref="FixtureCatalog.Trees"/>, in the directory <c>model</c> of the workspace. <c>model convert</c>
    /// writes through the same store; <c>ModelConversion.feature</c> verifies the trees that it wrote itself.
    /// </summary>
    [Given("the tree that the store writes from a v{int} Library with a dependency")]
    public void TheWrittenTree(int version) => TheCatalogTree(version switch
    {
        3 => FixtureCatalog.Trees.WrittenV3LibraryWithDependency,
        4 => FixtureCatalog.Trees.WrittenV4LibraryWithDependency,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "The catalog has a written tree of the versions 3 and 4."),
    });

    /// <summary>The written v3 or v4 tree with one fault, and where the fault is.</summary>
    [Given("the tree that the store writes from a v{int} Library with a dependency, with {string}")]
    public void TheWrittenTreeWithAFault(int version, string fault)
    {
        var faulty = (version, fault) switch
        {
            (4, "an unknown tag in one type node") => FixtureCatalog.Trees.WrittenV4WithAnUnknownTypeTag,
            (4, "no file for one value") => FixtureCatalog.Trees.WrittenV4WithAMissingValueFile,
            (4, "a second manifest in the YAML profile") => FixtureCatalog.Trees.WrittenV4WithASecondManifestInTheYamlProfile,
            (3, "an unknown member in one value definition") => FixtureCatalog.Trees.WrittenV3WithANestedUnknownMember,
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault,
                "Use v4 with 'an unknown tag in one type node', 'no file for one value' or 'a second manifest in the YAML profile', or v3 with 'an unknown member in one value definition'."),
        };
        TheCatalogTree(faulty.Tree);
        run.Fault = faulty.Fault;
    }

    /// <summary>
    /// The parent's compacted single file of IR <paramref name="ir"/> (<see cref="FixtureCatalog.Context"/>), with its
    /// plain twin beside it.
    /// </summary>
    [Given("the compacted v{int} single file of the name-context examples")]
    public void TheCompactedSingleFile(int ir)
    {
        var example = FixtureCatalog.Context.Example($"v{ir}-single-file");
        TheCatalogModel(example.File);
        var twin = workspace.File("plain-twin.json");
        workspace.WriteBytes(twin, FixtureCatalog.ReadBytes(example.Expanded!));
        run.PlainTwin = twin;
    }

    /// <summary>
    /// The parent's compacted single file of IR <paramref name="ir"/> in the file <paramref name="name"/> of the work
    /// area: for a documentation page that names its model file.
    /// </summary>
    [Given("the compacted v{int} single file of the name-context examples in the file {string}")]
    public void TheCompactedSingleFileInTheFile(int ir, string name)
    {
        var model = workspace.File(name);
        workspace.WriteBytes(model, FixtureCatalog.ReadBytes(FixtureCatalog.Context.Example($"v{ir}-single-file").File));
        run.Model = model;
    }

    /// <summary>
    /// The examples' tree of IR <paramref name="ir"/> (<see cref="FixtureCatalog.Context.Tree"/>) with <c>every file</c>
    /// or <c>only its value file</c> compacted, with its plain twin beside it.
    /// </summary>
    [Given("the v{int} tree of the name-context examples, with {string} compacted")]
    public void TheCompactedTree(int ir, string which)
    {
        var compaction = which switch
        {
            "every file" => ContextTreeCompaction.EveryFile,
            "only its value file" => ContextTreeCompaction.ValueFileOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "Use 'every file' or 'only its value file'."),
        };
        var tree = FixtureCatalog.Context.Tree(ir, compaction);
        TheCatalogTree(tree);
        run.PlainTwin = workspace.WriteTree(FixtureCatalog.Context.Tree(ir, ContextTreeCompaction.NoFile), "plain-twin");
        run.CompactedFiles = [.. tree.Files.Where(file => FixtureCatalog.Context.Examples.Any(example => example.File == file.Entry)).Select(file => file.Location)];
    }

    /// <summary>
    /// The examples' plain v4 tree with its type file replaced by one of the parent's invalid examples, and where the
    /// fault is: the type file, and the words that a refusal names.
    /// </summary>
    [Given("the v4 tree of the name-context examples, with {string} in its type file")]
    public void TheTreeWithAnInvalidTypeFile(string fault)
    {
        var id = fault switch
        {
            "a reference to an alias that no table defines" => "unknown-alias",
            "a document before its context" => "context-after-document",
            "an envelope schema that this version does not declare" => "undeclared-schema",
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault,
                "Use 'a reference to an alias that no table defines', 'a document before its context' or 'an envelope schema that this version does not declare'."),
        };
        var example = FixtureCatalog.Context.Example(id);
        var location = FixtureCatalog.Context.TreeLocation("type");
        TheCatalogTree(FixtureCatalog.Context.Tree(4, ContextTreeCompaction.NoFile)
            .WithFile(location, example.File, $"the v4 tree of the name-context examples with the type file {id}"));
        // What a refusal names: the alias that the file references and no table defines, the rule of the order, or the
        // URL of the envelope's schema.
        string[] names = id switch
        {
            "unknown-alias" => ["'zz'"],
            "context-after-document" => ["the context must come first"],
            _ => [JsonNode.Parse(FixtureCatalog.ReadText(example.File))!["$schema"]!.GetValue<string>()],
        };
        run.Fault = new FixtureFault(location, null, names);
    }

    /// <summary>Writes a catalog tree to the directory <c>model</c> of the workspace, as the model of the scenario.</summary>
    private void TheCatalogTree(FixtureTree tree) => run.ModelTree = workspace.WriteTree(tree, "model");

    [Given("an IR model with no format version")]
    public void AModelWithNoFormatVersion() => TheCatalogModel(FixtureCatalog.Models.NoFormatVersion);

    [Given("an IR model whose format version is a string")]
    public void AModelWhoseFormatVersionIsAString() => TheCatalogModel(FixtureCatalog.Models.StringFormatVersion);

    [Given("a model file that is not JSON")]
    public void AModelFileThatIsNotJson() => TheCatalogModel(FixtureCatalog.Models.Malformed);

    /// <summary>Writes the bytes of a catalog entry to <c>model.json</c> of the workspace, as the model of the scenario.</summary>
    private void TheCatalogModel(FixtureEntry entry)
    {
        var model = workspace.File("model.json");
        workspace.WriteBytes(model, FixtureCatalog.ReadBytes(entry));
        run.Model = model;
    }

    [When("I replace {string} with {string} in the model")]
    public void ReplaceInTheModel(string text, string replacement)
    {
        var model = workspace.ReadText(run.Model);
        if (!model.Contains(text, StringComparison.Ordinal))
            throw new InvalidOperationException($"The model {run.Model.Name} does not contain '{text}'.");
        workspace.WriteText(run.Model, model.Replace(text, replacement, StringComparison.Ordinal));
    }

    /// <summary>Removes one module of a classic (format version 1 to 3) library model: <c>distribution[3].modules</c>.</summary>
    [When("I remove the module {string} from the model")]
    public void RemoveTheModuleFromTheModel(string module)
    {
        var model = JsonNode.Parse(workspace.ReadText(run.Model))!;
        var modules = model["distribution"]![3]!["modules"]!.AsArray();
        var matches = modules.Where(entry => entry![0]!.ToJsonString().Contains(module, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"The model {run.Model.Name} has {matches.Length} modules with '{module}' in the name, not one.");
        modules.Remove(matches[0]);
        workspace.WriteText(run.Model, model.ToJsonString());
    }

    /// <summary>
    /// <c>malformed</c>: the model file is not JSON. <c>document-literal</c>: the whole number 5000 of the daily rate
    /// model is a document literal, which a backend cannot generate.
    /// </summary>
    [Given("the model has the problem {string}")]
    public void TheModelHasTheProblem(string problem)
    {
        switch (problem)
        {
            case "malformed":
                workspace.WriteText(run.Model, "{ broken");
                break;
            case "document-literal":
                ReplaceInTheModel("\"WholeNumberLiteral\": 5000", "\"DocumentLiteral\": {\"extra\":true}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(problem), problem, "Use malformed or document-literal.");
        }
    }
}
