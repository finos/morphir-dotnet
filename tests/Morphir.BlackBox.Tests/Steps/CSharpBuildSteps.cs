// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of a C# consumer of the package <c>Morphir.Lang.CSharp</c>: a <c>net10.0</c> program with the model of the
/// scenario as the additional file <c>car-rental.morphir.json</c>. The source generator of the package adds the code of
/// the model to its compilation, or the MSBuild target of the package writes it into the intermediate output folder.
/// The build steps are not the steps of <see cref="ConsumerSteps"/>: <see cref="ConsumerProject"/> is an F# project.
/// The run of the consumer sets <see cref="PackageRun.ConsumerOutput"/>, so <c>Then the consumer prints</c> checks it.
/// </summary>
[Binding]
public sealed partial class CSharpBuildSteps(IPackageDriver packages, IScenarioWorkspace workspace, ScenarioRun run, PackageRun state)
{
    private const string ProjectFile = "Consumer.csproj";

    /// <summary>The packages that a C# consumer restores from the feed: the package and its Morphir dependencies, and the SDK.</summary>
    private static readonly string[] Packages = ["Morphir.Lang.CSharp", "Morphir.Models", "Morphir.Compilation", "Morphir.MiddleEnd", "Morphir.SDK.CSharp"];

    // The folders of the generated files below the consumer: EmitCompilerGeneratedFiles of the source generator, and the
    // output folder of the MSBuild target.
    private const string GeneratorFolder = "obj/Release/net10.0/generated/Morphir.Lang.CSharp.Generator/Morphir.Lang.CSharp.Generator.MorphirCSharpGenerator/";
    private const string TargetFolder = "obj/Release/net10.0/morphir/csharp/";

    private WorkspaceDirectory Consumer => workspace.Directory("csharp consumer");

    // The result of the last build of the consumer.
    private CommandResult? _build;

    [Given("the packed packages of C# generation in the feed of the workspace")]
    public void ThePackedPackagesInTheFeed()
    {
        foreach (var id in Packages)
            state.InFeed[id] = packages.AddToFeed(packages.Packed.Find(id));
    }

    [Given("a C# consumer of the model")]
    public void AConsumerOfTheModel() => Write(targetWritesFiles: false);

    [Given("a C# consumer of the model that writes the generated files with the MSBuild target")]
    public void AConsumerThatWritesTheFiles() => Write(targetWritesFiles: true);

    [When("I build and run the C# consumer")]
    public async Task BuildAndRun()
    {
        await Build(succeeds: true);
        run.Result = state.ConsumerOutput = (await packages.DotnetAsync(Consumer, ["run", "--project", ProjectFile, "-c", "Release", "--no-build"])).ShouldExitWith(0);
    }

    [When("I build the C# consumer and it fails")]
    public Task BuildAndItFails() => Build(succeeds: false);

    [Then("the files of the source generator in the C# consumer are the files of the output")]
    public void TheGeneratorFilesAreTheOutput() => ShouldBeTheOutput(GeneratorFolder);

    [Then("the files of the MSBuild target in the C# consumer are the files of the output")]
    public void TheTargetFilesAreTheOutput() => ShouldBeTheOutput(TargetFolder);

    [Then("the source generator wrote no file in the C# consumer")]
    public void TheGeneratorWroteNoFile()
    {
        var files = FilesBelow(GeneratorFolder);
        if (files.Count > 0)
            throw new CommandAssertionException($"The source generator wrote {string.Join(", ", files.Keys)}.", run.Result);
    }

    private async Task Build(bool succeeds)
    {
        var result = await packages.DotnetAsync(Consumer, ["build", ProjectFile, "-c", "Release", "--nologo"]);
        run.Result = _build = result;
        if (succeeds && result.ExitCode != 0) throw new CommandAssertionException("The build of the C# consumer failed.", result);
        if (!succeeds && result.ExitCode == 0) throw new CommandAssertionException("The build of the C# consumer did not fail.", result);
    }

    /// <summary>The paths and bytes below a folder of the consumer, relative to the folder.</summary>
    private Dictionary<string, byte[]> FilesBelow(string folder) =>
        workspace.FilesIn(Consumer)
            .Where(path => path.StartsWith(folder, StringComparison.Ordinal))
            .ToDictionary(path => path[folder.Length..], path => workspace.ReadBytes(Consumer.File(path.Split('/'))), StringComparer.Ordinal);

    // The output of the generate command of the scenario, without the ownership manifest of the CLI.
    private void ShouldBeTheOutput(string folder)
    {
        var output = workspace.Directory("output");
        var expected = workspace.FilesIn(output).Where(path => path != ".morphir-generated.json").ToList();
        var actual = FilesBelow(folder);
        if (!expected.SequenceEqual(actual.Keys.Order(StringComparer.Ordinal)))
            throw new CommandAssertionException(
                $"The consumer has the files [{string.Join(", ", actual.Keys.Order(StringComparer.Ordinal))}] in {folder}, and the CLI wrote [{string.Join(", ", expected)}].", run.Result);
        foreach (var path in expected)
        {
            if (!workspace.ReadBytes(output.File(path.Split('/'))).AsSpan().SequenceEqual(actual[path]))
                throw new CommandAssertionException($"The bytes of '{path}' in {folder} are not the bytes that the CLI wrote.", run.Result);
        }
    }

    private void Write(bool targetWritesFiles) =>
        WriteProject(Consumer, ProjectFile, "Exe", targetWritesFiles ? "<MorphirGenerateCSharpFiles>true</MorphirGenerateCSharpFiles>" : "", "", run.Model,
            """System.Console.WriteLine($"daily rate: {Car.Rental.Inventory.DailyRate()}");""" + "\n");

    private void WriteProject(WorkspaceDirectory folder, string projectFile, string outputType, string properties, string items, WorkspaceFile model, string program)
    {
        var references = string.Concat(Packages.Where(id => id is "Morphir.Lang.CSharp" or "Morphir.SDK.CSharp")
            .Select(id => $"""<PackageReference Include="{id}" Version="[{state.VersionOf(id)}]" />"""));
        workspace.WriteText(folder.File(projectFile), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                {properties}
              </PropertyGroup>
              <ItemGroup>
                {references}
                {items}
                <AdditionalFiles Include="car-rental.morphir.json" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteBytes(folder.File("car-rental.morphir.json"), workspace.ReadBytes(model));
        if (program.Length > 0) workspace.WriteText(folder.File("Program.cs"), program);
        packages.WriteNuGetConfig(folder, includeNuGetOrg: true);
    }

    // ---- A library and an application, each with its own polyfill file ----------------------------------------

    private WorkspaceDirectory Library => workspace.Directory("csharp library");

    /// <summary>The arms of the match of the application, by case: the union Shape and the closed record Size.</summary>
    private readonly List<(string Case, string Arm)> _arms =
    [
        ("Circle", "Circle circle => $\"circle {circle.Radius}\""),
        ("Rect", "Rect rect => $\"rect {rect.Width}x{rect.Height}\""),
        ("Small", "Small => \"small\""),
        ("Large", "Large => \"large\""),
    ];

    /// <summary>
    /// The library: the model of the scenario (the shapes model), with its own polyfill file. The application (the C#
    /// consumer): the daily rate model, with its own polyfill file, a reference to the library, and a switch with no
    /// discard arm on the union Shape and on the closed record Size of the library.
    /// </summary>
    [Given("a C# library of the model and a C# consumer that matches on the types of the library")]
    public void ALibraryAndAnApplication()
    {
        WriteProject(Library, "Library.csproj", "Library", "", "", run.Model, "");
        WriteProject(Consumer, ProjectFile, "Exe", "", """<ProjectReference Include="../csharp library/Library.csproj" />""",
            workspace.CopyFixture("Models/daily-rate-v4.json", "daily-rate.json"), "");
        WriteMatch();
    }

    [Given("the match of the C# consumer has no arm for the case {string}")]
    public void TheMatchHasNoArm(string name)
    {
        if (_arms.RemoveAll(arm => arm.Case == name) != 1) throw new InvalidOperationException($"The match has no arm for the case {name}.");
        WriteMatch();
    }

    [Then("the last build of the C# consumer has no warning")]
    public void TheBuildHasNoWarning()
    {
        var build = _build ?? throw new InvalidOperationException("The scenario did not build the C# consumer before this step.");
        // A warning line of MSBuild and of the compiler has "warning" and its code in each logger and UI language.
        build.OutputShouldNotContain(": warning ");
    }

    [Then("the C# library and the C# consumer each have the polyfill file")]
    public void EachHasThePolyfillFile()
    {
        foreach (var folder in new[] { Library, Consumer })
        {
            if (!workspace.Exists(folder.File([.. GeneratorFolder.TrimEnd('/').Split('/'), "Morphir.Polyfill.g.cs"])))
                throw new CommandAssertionException($"{folder} has no file {GeneratorFolder}Morphir.Polyfill.g.cs.", run.Result);
        }
    }

    private void WriteMatch()
    {
        string Arms(params string[] cases) =>
            string.Join(",\n", _arms.Where(arm => cases.Contains(arm.Case)).Select(arm => "    " + arm.Arm));
        workspace.WriteText(Consumer.File("Program.cs"), $$"""
            using Acme.Shapes.Shapes;

            Shape shape = new Circle(2);
            Size size = new Small();
            var text = shape switch
            {
            {{Arms("Circle", "Rect")}}
            };
            var name = size switch
            {
            {{Arms("Small", "Large")}}
            };
            System.Console.WriteLine($"{text}, {name}, daily rate {Car.Rental.Inventory.DailyRate()}");

            """);
    }

    // ---- The commands of the page of the build integration -----------------------------------------------------

    [GeneratedRegex("```(?<kind>bash|xml|csharp)\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex Block();

    /// <summary>
    /// Follows the page in the order of its blocks, in the work folder "docs": each <c>$ dotnet</c> line of a bash block
    /// runs, and its documented output lines must be its standard output; <c>$ cd</c> changes the folder. The xml block of
    /// an item group goes into the project file of the folder, with the model of the scenario as
    /// <c>car-rental.morphir.json</c>, and the csharp block is <c>Program.cs</c>. The Morphir packages come from the feed
    /// of the workspace only.
    /// </summary>
    [When("I follow the documentation page {string} with the packed packages")]
    public async Task FollowThePage(string page)
    {
        var root = workspace.Directory("docs");
        packages.WriteNuGetConfig(root, includeNuGetOrg: true, [new PackageSourcePattern(true, "Morphir.*"), new PackageSourcePattern(false, "*")]);
        var folder = root;
        // A Windows checkout has CRLF in the page (text=auto), and the blocks are matched with \n.
        var text = workspace.ReadText(workspace.CopyFixture("Docs/" + page)).Replace("\r\n", "\n");
        var commands = 0;
        foreach (Match block in Block().Matches(text))
        {
            var body = block.Groups["body"].Value;
            switch (block.Groups["kind"].Value)
            {
                case "csharp":
                    workspace.WriteText(folder.File("Program.cs"), body + "\n");
                    break;
                case "xml" when body.StartsWith("<ItemGroup>", StringComparison.Ordinal):
                    var project = folder.File(folder.Path.Split('/', '\\')[^1] + ".csproj");
                    workspace.WriteText(project, workspace.ReadText(project).Replace("</Project>", body + "\n</Project>", StringComparison.Ordinal));
                    workspace.WriteBytes(folder.File("car-rental.morphir.json"), workspace.ReadBytes(run.Model));
                    break;
                case "bash":
                    var lines = body.Split('\n');
                    for (var index = 0; index < lines.Length; index++)
                    {
                        if (!lines[index].StartsWith("$ ", StringComparison.Ordinal)) continue;
                        var words = lines[index][2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var output = lines.Skip(index + 1).TakeWhile(line => !line.StartsWith("$ ", StringComparison.Ordinal)).ToArray();
                        if (words[0] == "cd")
                        {
                            folder = folder.Directory(words[1]);
                            continue;
                        }

                        if (words[0] != "dotnet") throw new InvalidOperationException($"The page {page} has a command that is not dotnet or cd: {lines[index]}");
                        var result = (await packages.DotnetAsync(folder, words[1..])).ShouldExitWith(0);
                        run.Result = result;
                        commands++;
                        var actual = result.StandardOutput.Trim().Replace("\r\n", "\n");
                        if (output.Length > 0 && actual != string.Join('\n', output))
                            throw new CommandAssertionException($"The output of '{lines[index]}' is not the documented output.", result);
                    }

                    break;
            }
        }

        if (commands == 0) throw new InvalidOperationException($"The page {page} has no dotnet command in a bash block.");
    }
}
