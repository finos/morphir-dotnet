// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of a C# consumer of the package <c>Morphir.Lang.CSharp</c>: a <c>net10.0</c> program with the model of the
/// scenario as the additional file <c>car-rental.morphir.json</c>. The source generator of the package adds the code of
/// the model to its compilation, or the MSBuild target of the package writes it into the intermediate output folder.
/// </summary>
[Binding]
public sealed class CSharpBuildSteps(IPackageDriver packages, IScenarioWorkspace workspace, ScenarioRun run, PackageRun state)
{
    private const string ProjectFile = "Consumer.csproj";

    /// <summary>The packages that a C# consumer restores from the feed: the package and its Morphir dependencies, and the SDK.</summary>
    private static readonly string[] Packages = ["Morphir.Lang.CSharp", "Morphir.Models", "Morphir.Compilation", "Morphir.MiddleEnd", "Morphir.SDK.CSharp"];

    // The folders of the generated files below the consumer: EmitCompilerGeneratedFiles of the source generator, and the
    // output folder of the MSBuild target.
    private const string GeneratorFolder = "obj/Release/net10.0/generated/Morphir.Lang.CSharp.Generator/Morphir.Lang.CSharp.Generator.MorphirCSharpGenerator/";
    private const string TargetFolder = "obj/Release/net10.0/morphir/csharp/";

    private WorkspaceDirectory Consumer => workspace.Directory("csharp consumer");

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

    [Then("the C# consumer prints {string}")]
    public void ThePrintedText(string text)
    {
        if (state.ConsumerOutput.StandardOutput.Trim() != text)
            throw new CommandAssertionException($"The C# consumer did not print '{text}'.", state.ConsumerOutput);
    }

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
        run.Result = result;
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

    private void Write(bool targetWritesFiles)
    {
        var files = targetWritesFiles ? "<MorphirGenerateCSharpFiles>true</MorphirGenerateCSharpFiles>" : "";
        var references = string.Concat(Packages.Where(id => id is "Morphir.Lang.CSharp" or "Morphir.SDK.CSharp")
            .Select(id => $"""<PackageReference Include="{id}" Version="[{state.VersionOf(id)}]" />"""));
        workspace.WriteText(Consumer.File(ProjectFile), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                {files}
              </PropertyGroup>
              <ItemGroup>
                {references}
                <AdditionalFiles Include="car-rental.morphir.json" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteBytes(Consumer.File("car-rental.morphir.json"), workspace.ReadBytes(run.Model));
        workspace.WriteText(Consumer.File("Program.cs"), """System.Console.WriteLine($"daily rate: {Car.Rental.Inventory.DailyRate()}");""" + "\n");
        packages.WriteNuGetConfig(Consumer, includeNuGetOrg: true);
    }
}
