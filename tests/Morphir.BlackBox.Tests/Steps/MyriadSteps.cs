// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;
using Reqnroll.UnitTestProvider;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of a consumer of the Myriad plugin package (<c>Morphir.Myriad</c>): an F# project that compiles the
/// business rules of a Morphir JSON model (<c>MorphirModel</c> items) through the plugin, and the changes of its
/// model. A model with modules is the classic daily rate model with one module for each row.
/// </summary>
[Binding]
public sealed class MyriadSteps(IPackageDriver packages, IScenarioWorkspace workspace, PackageRun state, IUnitTestRuntimeProvider runtime)
{
    private const string Plugin = "Morphir.Myriad";
    private const string DailyRateModel = "Models/daily-rate-v3.json";

    private ConsumerProject Consumer => state.Consumer;

    // ---- The consumer -------------------------------------------------------------------------------------------

    [Given("a Myriad consumer of the model fixture {string}")]
    public void AConsumerOfTheModelFixture(string fixture)
    {
        NewConsumer(new ConsumerModel("model.json"));
        workspace.WriteText(Consumer.File("model.json"), workspace.ReadText(workspace.CopyFixture(fixture)));
    }

    /// <summary>A model file <c>model.json</c> with a module and a daily rate for each row (columns <c>module</c> and <c>daily rate</c>).</summary>
    [Given("a Myriad consumer of a model with the modules:")]
    public void AConsumerOfAModelWithTheModules(Table modules)
    {
        NewConsumer(new ConsumerModel("model.json"));
        workspace.WriteText(Consumer.File("model.json"), Model(modules.Rows.Select(Module)));
    }

    /// <summary>A model file for each value of the column <c>file</c>, with the modules of its rows.</summary>
    [Given("a Myriad consumer of the models:")]
    public void AConsumerOfTheModels(Table models)
    {
        var files = models.Rows.GroupBy(row => row["file"]).ToArray();
        NewConsumer([.. files.Select(file => new ConsumerModel(file.Key))]);
        foreach (var file in files) workspace.WriteText(Consumer.File(file.Key), Model(file.Select(Module)));
    }

    [Given("a Myriad consumer with no model file")]
    public void AConsumerWithNoModelFile() => NewConsumer(new ConsumerModel("model.json"));

    [Given("the consumer program is the car rental example")]
    public void TheProgramIsTheCarRentalExample() =>
        workspace.WriteText(Consumer.File("Program.fs"), workspace.ReadText(workspace.CopyFixture("Myriad/car-rental-program.fs")));

    /// <summary>The program prints the daily rate of one module, or the sum for <c>A + B</c>.</summary>
    [Given("the consumer program prints the daily rate of {string}")]
    public void TheProgramPrintsTheDailyRateOf(string modules) => workspace.WriteText(Consumer.File("Program.fs"), PrintDailyRate(modules));

    [When("the consumer program prints the daily rate of {string}")]
    public void TheProgramNowPrintsTheDailyRateOf(string modules) => TheProgramPrintsTheDailyRateOf(modules);

    /// <summary>A consumer of the System.Text.Json plugin that also compiles a model with the Myriad plugin.</summary>
    [Given("the consumer also uses the Myriad plugin with the model fixture {string}")]
    public void TheConsumerAlsoUsesTheMyriadPlugin(string fixture)
    {
        Consumer.Packages.Insert(0, (Plugin, state.VersionOf(Plugin), true));
        Consumer.Models.Add(new ConsumerModel("model.json"));
        workspace.WriteText(Consumer.File("model.json"), workspace.ReadText(workspace.CopyFixture(fixture)));
        Save();
    }

    [Given("the consumer program also prints the daily rate of {string}")]
    public void TheProgramAlsoPrintsTheDailyRateOf(string modules)
    {
        var program = Consumer.File("Program.fs");
        workspace.WriteText(program, workspace.ReadText(program) + PrintDailyRate(modules));
    }

    // ---- The model items ----------------------------------------------------------------------------------------

    [Given("the model {string} of the consumer has the output directory {string}")]
    public void TheModelHasTheOutputDirectory(string file, string directory) => SetOutputDirectory(file, directory);

    [Given("the model {string} of the consumer has the output directory {string} with the separator of the operating system at its end")]
    public void TheModelHasTheOutputDirectoryWithASeparator(string file, string directory) =>
        SetOutputDirectory(file, directory + Path.DirectorySeparatorChar);

    [When("the model {string} of the consumer has the full path of the output directory {string} with the separator of the operating system at its end")]
    public void TheModelHasTheFullPathOfTheOutputDirectory(string file, string directory) =>
        SetOutputDirectory(file, Consumer.Folder.Directory(directory).Path + Path.DirectorySeparatorChar);

    /// <summary>
    /// The consumer folder <paramref name="link"/> is a symbolic link to its folder <paramref name="target"/>. A
    /// symbolic link of a directory needs a privilege on Windows, so there the scenario is skipped with that reason.
    /// </summary>
    [Given("the consumer folder {string} is a symbolic link to its folder {string}")]
    public void TheFolderIsASymbolicLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            runtime.TestIgnore("A symbolic link of a directory needs a privilege on Windows.");
            return;
        }

        var folder = Consumer.Folder.Directory(target);
        workspace.CreateDirectory(folder);
        workspace.CreateDirectoryLink(Consumer.Folder.Directory(link), folder);
    }

    // ---- Changes of the model -----------------------------------------------------------------------------------

    [When("the model of the consumer changes to the modules:")]
    public void TheModelChangesToTheModules(Table modules) => workspace.WriteText(Consumer.File("model.json"), Model(modules.Rows.Select(Module)));

    [When("the model of the consumer is the fixture {string}")]
    public void TheModelIsTheFixture(string fixture) =>
        workspace.WriteText(Consumer.File("model.json"), workspace.ReadText(workspace.CopyFixture(fixture)));

    [Given("the daily rate of the module {string} of the model is the string {string}")]
    public void TheDailyRateIsAString(string module, string text)
    {
        var model = JsonNode.Parse(workspace.ReadText(Consumer.File("model.json")))!;
        var body = DailyRateBody(ModuleOf(model, module.Split(' ')));
        body[0] = "StringLiteral";
        body[1] = text;
        workspace.WriteText(Consumer.File("model.json"), model.ToJsonString());
    }

    /// <summary>Changes the daily rate of the module <c>inventory</c> of the car rental model.</summary>
    [When("the daily rate of the car rental model changes to {int} cents")]
    public void TheDailyRateOfTheCarRentalModelChanges(int cents)
    {
        var model = JsonNode.Parse(workspace.ReadText(Consumer.File("model.json")))!;
        var inventory = ModuleOf(model, ["inventory"]);
        var value = inventory[1]!["value"]!["values"]!.AsArray()
            .Single(entry => entry![0]!.AsArray().Select(word => word!.GetValue<string>()).SequenceEqual(["daily", "rate"]))!;
        value[1]!["value"]!["value"]!["body"]![2]![1] = cents;
        workspace.WriteText(Consumer.File("model.json"), model.ToJsonString());
    }

    /// <summary>An input that the plugin refuses, by its kind: <c>malformed</c> text, or a change of the JSON model.</summary>
    [When("the model of the consumer changes to the invalid input {string}")]
    public void TheModelChangesToTheInvalidInput(string input)
    {
        if (input == "malformed")
        {
            TheModelIsTheFixture("Models/malformed-model.txt");
            return;
        }

        var model = JsonNode.Parse(workspace.ReadText(Consumer.File("model.json")))!.AsObject();
        switch (input)
        {
            case "missing": model.Remove("formatVersion"); break;
            case "string": model["formatVersion"] = "3"; break;
            case "fractional": model["formatVersion"] = 3.5; break;
            case "zero": model["formatVersion"] = 0; break;
            case "future": model["formatVersion"] = 4; break;
            case "non-library": model["distribution"]![0] = "DocumentTree"; break;
            default: throw new ArgumentException($"The invalid input '{input}' is not known.");
        }

        workspace.WriteText(Consumer.File("model.json"), model.ToJsonString());
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private void NewConsumer(params ConsumerModel[] models)
    {
        state.Consumer = new ConsumerProject(workspace.Directory("consumer"));
        Consumer.Packages.Add((Plugin, state.VersionOf(Plugin), false));
        Consumer.Models.AddRange(models);
        packages.WriteNuGetConfig(Consumer.Folder, includeNuGetOrg: true);
        workspace.WriteText(Consumer.File("Program.fs"), PrintDailyRate("Inventory"));
        Save();
    }

    private void Save() => workspace.WriteText(Consumer.File(ConsumerProject.ProjectFile), Consumer.Render());

    private void SetOutputDirectory(string file, string directory)
    {
        var index = Consumer.Models.FindIndex(model => model.File == file);
        if (index < 0) throw new InvalidOperationException($"The consumer has no model {file}.");
        Consumer.Models[index] = Consumer.Models[index] with { OutputDirectory = directory };
        Save();
    }

    private static string PrintDailyRate(string modules)
    {
        var rates = modules.Split('+', StringSplitOptions.TrimEntries).Select(module => $"Car.Rental.{module}.dailyRate").ToArray();
        return $"printfn \"%d\" {(rates.Length == 1 ? rates[0] : "(" + string.Join(" + ", rates) + ")")}\n";
    }

    /// <summary>The module of a row: the words of its name (column <c>module</c>, separated by a space) and its daily rate.</summary>
    private JsonNode Module(DataTableRow row)
    {
        var module = DailyRateFixture()["distribution"]![3]!["modules"]![0]!.DeepClone();
        module[0] = JsonSerializer.SerializeToNode(new[] { row["module"].Split(' ') });
        DailyRateBody(module)[1] = int.Parse(row["daily rate"], System.Globalization.CultureInfo.InvariantCulture);
        return module;
    }

    private string Model(IEnumerable<JsonNode> modules)
    {
        var model = DailyRateFixture();
        var list = model["distribution"]![3]!["modules"]!.AsArray();
        list.Clear();
        foreach (var module in modules) list.Add(module);
        return model.ToJsonString();
    }

    private JsonNode DailyRateFixture() => JsonNode.Parse(workspace.ReadText(workspace.CopyFixture(DailyRateModel)))!;

    // The body of the first value of a module of the daily rate model: [kind, attributes, literal].
    private static JsonArray DailyRateBody(JsonNode module) => module[1]!["value"]!["values"]![0]![1]!["value"]!["body"]![2]!.AsArray();

    private static JsonNode ModuleOf(JsonNode model, string[] words) =>
        model["distribution"]![3]!["modules"]!.AsArray()
            .Single(module => module![0]![0]!.AsArray().Select(word => word!.GetValue<string>()).SequenceEqual(words))!;
}
