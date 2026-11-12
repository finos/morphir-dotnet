// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps that build, run and clean the consumer project of a scenario, and that check what it prints and the
/// files that its build writes. A generated file is a file below the <c>obj</c> folder of the consumer.
/// </summary>
[Binding]
public sealed class ConsumerSteps(IPackageDriver packages, IScenarioWorkspace workspace, ScenarioRun run, PackageRun state)
{
    private ConsumerProject Consumer => state.Consumer;

    // ---- Build, run, clean --------------------------------------------------------------------------------------

    [When("I build the consumer")]
    public Task BuildTheConsumer() => Build(noRestore: false, succeeds: true);

    [When("I build the consumer with no restore")]
    public Task BuildTheConsumerWithNoRestore() => Build(noRestore: true, succeeds: true);

    [When("I build the consumer and it fails")]
    public Task BuildTheConsumerAndItFails() => Build(noRestore: false, succeeds: false);

    [When("I build the consumer with no restore and it fails")]
    public Task BuildTheConsumerWithNoRestoreAndItFails() => Build(noRestore: true, succeeds: false);

    [When("I build the consumer in the configurations {string} and {string} at the same time with no restore")]
    public async Task BuildInTwoConfigurations(string first, string second)
    {
        var builds = await Task.WhenAll(new[] { first, second }.Select(configuration =>
            packages.DotnetAsync(Consumer.Folder, ["build", ConsumerProject.ProjectFile, "-c", configuration, "--no-restore", "--nologo"])));
        foreach (var build in builds) run.Result = build.ShouldExitWith(0);
    }

    [When("I run the consumer")]
    public async Task RunTheConsumer()
    {
        var result = await packages.DotnetAsync(Consumer.Folder, ["run", "--project", ConsumerProject.ProjectFile, "-c", "Release", "--no-build"]);
        run.Result = state.ConsumerOutput = result.ShouldExitWith(0);
    }

    [When("I build and run the consumer")]
    public async Task BuildAndRunTheConsumer()
    {
        await BuildTheConsumer();
        await RunTheConsumer();
    }

    [When("I build and run the consumer with no restore")]
    public async Task BuildAndRunTheConsumerWithNoRestore()
    {
        await BuildTheConsumerWithNoRestore();
        await RunTheConsumer();
    }

    [When("I clean the consumer")]
    public async Task CleanTheConsumer() =>
        run.Result = (await packages.DotnetAsync(Consumer.Folder, ["clean", ConsumerProject.ProjectFile, "-c", "Release", "--nologo"])).ShouldExitWith(0);

    private async Task Build(bool noRestore, bool succeeds)
    {
        string[] restore = noRestore ? ["--no-restore"] : [];
        var result = await packages.DotnetAsync(Consumer.Folder, ["build", ConsumerProject.ProjectFile, "-c", "Release", .. restore, "--nologo"]);
        run.Result = result;
        if (succeeds && result.ExitCode != 0) throw new CommandAssertionException("The build of the consumer failed.", result);
        if (!succeeds && result.ExitCode == 0) throw new CommandAssertionException("The build of the consumer did not fail.", result);
    }

    // ---- The sources of the consumer ----------------------------------------------------------------------------

    [Given("the consumer program is:")]
    public void TheConsumerProgramIs(string program) => workspace.WriteText(Consumer.File("Program.fs"), program + "\n");

    [Given("the consumer program is the fixture {string}")]
    public void TheConsumerProgramIsTheFixture(string fixture) =>
        workspace.WriteText(Consumer.File("Program.fs"), workspace.ReadText(workspace.CopyFixture(fixture)));

    [Given("the consumer types are:")]
    public void TheConsumerTypesAre(string types) => workspace.WriteText(Consumer.File("Types.fs"), types + "\n");

    [Given("the consumer types are the fixture {string}")]
    public void TheConsumerTypesAreTheFixture(string fixture) =>
        workspace.WriteText(Consumer.File("Types.fs"), workspace.ReadText(workspace.CopyFixture(fixture)));

    [Given("the consumer has the property {string} with the value {string}")]
    public void TheConsumerHasTheProperty(string name, string value)
    {
        Consumer.Properties.Add((name, value));
        workspace.WriteText(Consumer.File(ConsumerProject.ProjectFile), Consumer.Render());
    }

    // ---- What the consumer prints -------------------------------------------------------------------------------

    [Then("the consumer prints {string}")]
    public void TheConsumerPrints(string text)
    {
        if (state.ConsumerOutput.StandardOutput.Trim() != text)
            throw new CommandAssertionException($"The consumer printed something other than '{text}'.", state.ConsumerOutput);
    }

    [Then("the consumer prints:")]
    public void TheConsumerPrintsTheLines(string text)
    {
        if (Lines(state.ConsumerOutput.StandardOutput.Trim()) != Lines(text.Trim()))
            throw new CommandAssertionException($"The consumer printed something other than:\n{text}", state.ConsumerOutput);
    }

    [Then("the first line that the consumer prints is:")]
    public void TheFirstLineIs(string line)
    {
        if (Lines(state.ConsumerOutput.StandardOutput.Trim()).Split('\n')[0] != line.Trim())
            throw new CommandAssertionException($"The first line that the consumer printed is not '{line}'.", state.ConsumerOutput);
    }

    [Then("the consumer prints {int} lines")]
    public void TheConsumerPrintsLines(int count)
    {
        var lines = Lines(state.ConsumerOutput.StandardOutput.Trim()).Split('\n').Length;
        if (lines != count) throw new CommandAssertionException($"The consumer printed {lines} lines, not {count}.", state.ConsumerOutput);
    }

    [Then("the consumer reports a complete rental costing {int} cents")]
    public void TheConsumerReportsACompleteRental(int cents)
    {
        foreach (var expected in new[] { "inventory=Available", "reservation=Reserved", "pickup=Rented", "return=Available", $"invoice={cents}" })
        {
            if (!state.ConsumerOutput.StandardOutput.Contains(expected, StringComparison.Ordinal))
                throw new CommandAssertionException($"The consumer did not print '{expected}'.", state.ConsumerOutput);
        }
    }

    [Then("the consumer rejects invalid rental operations")]
    public void TheConsumerRejectsInvalidOperations()
    {
        if (!state.ConsumerOutput.StandardOutput.Contains("rejections=7", StringComparison.Ordinal))
            throw new CommandAssertionException("The consumer did not print seven verified business-rule rejections ('rejections=7').", state.ConsumerOutput);
    }

    // ---- The files of the consumer ------------------------------------------------------------------------------

    [Then("the consumer has the file {string}")]
    public void TheConsumerHasTheFile(string file) => Expect(workspace.Exists(Consumer.File(file)), $"the consumer has no file {file}");

    [Then("the consumer has no file {string}")]
    public void TheConsumerHasNoFile(string file) => Expect(!workspace.Exists(Consumer.File(file)), $"the consumer has the file {file}");

    [Then("the consumer has no directory {string}")]
    public void TheConsumerHasNoDirectory(string directory) =>
        Expect(!workspace.Exists(Consumer.Folder.Directory(directory.Split('/'))), $"the consumer has the directory {directory}");

    [Then("the consumer directory {string} has no file")]
    public void TheConsumerDirectoryHasNoFile(string directory)
    {
        var files = workspace.FilesIn(Consumer.Folder.Directory(directory.Split('/')));
        Expect(files.Count == 0, $"the directory {directory} has the files {string.Join(", ", files)}");
    }

    [Then("the consumer has {int} generated file(s) {string}")]
    public void TheConsumerHasGeneratedFiles(int count, string pattern)
    {
        var found = Generated(pattern);
        Expect(found.Length == count, $"the consumer has {found.Length} generated files {pattern}, not {count}: {string.Join(", ", found)}");
    }

    [Then("the consumer has no generated file {string} in a folder {string}")]
    public void TheConsumerHasNoGeneratedFileInAFolder(string pattern, string folder)
    {
        var found = Generated(pattern).Where(file => file.Split('/').Contains(folder)).ToArray();
        Expect(found.Length == 0, $"the consumer has the generated files {string.Join(", ", found)} in a folder {folder}");
    }

    /// <summary>Remembers the text and the time of each generated F# source file and each manifest.</summary>
    [Given("I remember the generated files of the consumer")]
    public void RememberTheGeneratedFiles()
    {
        state.Remembered.Clear();
        foreach (var file in Generated("*.fs").Concat(Generated("manifest.json")))
        {
            var path = Consumer.File(file);
            state.Remembered[file] = (workspace.ReadText(path), workspace.LastWriteTimeUtc(path));
        }

        Expect(state.Remembered.Count > 0, "the consumer has no generated file to remember");
    }

    [Then("the generated files of the consumer are the same as I remembered")]
    public void TheGeneratedFilesAreTheSame()
    {
        var now = Generated("*.fs").Concat(Generated("manifest.json")).Order(StringComparer.Ordinal).ToArray();
        Expect(now.SequenceEqual(state.Remembered.Keys.Order(StringComparer.Ordinal)),
            $"the generated files are [{string.Join(", ", now)}], not the remembered [{string.Join(", ", state.Remembered.Keys)}]");
        foreach (var (file, (text, time)) in state.Remembered)
        {
            var path = Consumer.File(file);
            Expect(workspace.ReadText(path) == text, $"the text of {file} changed");
            Expect(workspace.LastWriteTimeUtc(path) == time, $"the build wrote {file} again ({time:O} before, {workspace.LastWriteTimeUtc(path):O} now)");
        }
    }

    [Given("I remember the text of the generated file {string}")]
    public void RememberTheTextOfTheGeneratedFile(string name)
    {
        var file = OneGenerated(name);
        state.Remembered[file] = (workspace.ReadText(Consumer.File(file)), default);
    }

    [Then("the text of the generated file {string} is the same as I remembered")]
    public void TheTextOfTheGeneratedFileIsTheSame(string name)
    {
        var file = OneGenerated(name);
        Expect(state.Remembered.TryGetValue(file, out var remembered) && workspace.ReadText(Consumer.File(file)) == remembered.Text, $"the text of {file} changed");
    }

    [Given("I put the file {string} with the text {string} beside the generated file {string}")]
    public void PutAFileBesideTheGeneratedFile(string name, string text, string generated)
    {
        var file = OneGenerated(generated);
        var folder = file[..(file.LastIndexOf('/') + 1)];
        state.Placed[name] = folder + name;
        workspace.WriteText(Consumer.File(folder + name), text);
    }

    [Then("the file {string} beside the generated files has the text {string}")]
    public void TheFileBesideTheGeneratedFilesHasTheText(string name, string text)
    {
        var file = state.Placed.TryGetValue(name, out var placed)
            ? placed
            : throw new InvalidOperationException($"No step put the file {name} beside the generated files.");
        Expect(workspace.Exists(Consumer.File(file)) && workspace.ReadText(Consumer.File(file)) == text, $"the file {file} does not have the text '{text}'");
    }

    [Given("I wait {int} milliseconds")]
    public Task Wait(int milliseconds) => Task.Delay(milliseconds);

    /// <summary>The generated files (below <c>obj</c>) whose name matches <paramref name="pattern"/> (<c>*</c> is any text), relative to the consumer.</summary>
    private string[] Generated(string pattern)
    {
        var obj = Consumer.Folder.Directory("obj");
        if (!workspace.Exists(obj)) return [];
        var name = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
        return [.. workspace.FilesIn(obj).Where(file => name.IsMatch(file.Split('/')[^1])).Select(file => "obj/" + file)];
    }

    private string OneGenerated(string name)
    {
        var found = Generated(name);
        Expect(found.Length == 1, $"the consumer has {found.Length} generated files {name}, not one: {string.Join(", ", found)}");
        return found[0];
    }

    private static string Lines(string text) => text.Replace("\r\n", "\n");

    private void Expect(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException($"The consumer {Consumer.Folder.Path}: {failure}.");
    }
}
