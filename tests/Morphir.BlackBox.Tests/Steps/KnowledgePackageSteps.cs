// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;

namespace Morphir.BlackBox.Tests.Steps;

[Binding]
public sealed class KnowledgePackageSteps(IPackageDriver packages, IScenarioWorkspace workspace)
{
    private const string Core = "Morphir.Knowledge";
    private const string Okf = "Morphir.Knowledge.Okf";
    private const string Rdf = "Morphir.Knowledge.Rdf";
    private const string Files = "Morphir.Files";

    private static readonly PackageSourcePattern[] Mapping =
    [
        new(FromFeed: true, "Morphir.*"),
        new(FromFeed: false, "*"),
    ];

    private readonly List<(CommandResult Result, string[] Dependencies)> _consumers = [];
    private string[] _expectedPackages = [];

    [When("fresh F# and C# consumers use the {string} knowledge package on {string}")]
    public async Task RunConsumers(string capability, string framework)
    {
        var example = KnowledgePackageExamples.For(capability);
        _expectedPackages = example.Package switch
        {
            Core => [Core],
            Okf => [Core, Files, Okf],
            Rdf => [Core, Rdf],
            _ => throw new ArgumentOutOfRangeException(nameof(capability)),
        };

        foreach (var id in _expectedPackages)
            packages.AddToFeed(packages.Packed.Find(id));

        var version = packages.Packed.Find(example.Package).Version;
        foreach (var (extension, source) in new[] { ("fs", example.FSharp), ("cs", example.CSharp) })
        {
            var folder = workspace.Directory("knowledge " + extension);
            var program = $"Program.{extension}";
            var project = $"Consumer.{extension}proj";
            var compile = extension == "fs" ? $"""<Compile Include="{program}" />""" : "";
            workspace.WriteText(folder.File(program), source);
            workspace.WriteText(folder.File(project), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{framework}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{example.Package}" Version="[{version}]" />
                    {compile}
                  </ItemGroup>
                </Project>
                """);

            var config = packages.WriteNuGetConfig(folder, includeNuGetOrg: true, Mapping);
            var restore = await packages.DotnetAsync(folder,
                ["restore", project, "--packages", folder.Directory("packages").Path, "--configfile", config.Path, "--disable-parallel", "-m:1", "-nodeReuse:false"]);
            restore.ShouldExitWith(0);
            var assets = JsonNode.Parse(workspace.ReadText(folder.File("obj", "project.assets.json")))!
                ["targets"]![framework]!.AsObject();
            var ids = assets.Select(entry => entry.Key.Split('/')[0]).ToArray();

            string[] args = ["run", "--project", project, "--no-restore", "-c", "Release"];
            if (example.Concept is { } concept)
            {
                var bundle = folder.Directory("bundle");
                workspace.WriteText(bundle.File("revenue.md"), concept);
                args = [.. args, "--", bundle.Path];
            }

            _consumers.Add((await packages.DotnetAsync(folder, args), ids));
        }
    }

    [Then("both knowledge consumers complete the example with their expected dependencies")]
    public void CheckConsumers()
    {
        if (_consumers.Count != 2)
            throw new InvalidOperationException("Expected both a C# consumer and an F# consumer.");

        foreach (var (result, dependencies) in _consumers)
        {
            result.ShouldExitWith(0).OutputShouldContain("knowledge example passed");
            var morphir = dependencies.Where(id => id.StartsWith("Morphir.", StringComparison.OrdinalIgnoreCase));
            if (!morphir.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(_expectedPackages))
                throw new CommandAssertionException($"Unexpected Morphir package graph: {string.Join(", ", morphir)}.", result);

            var hasOkf = _expectedPackages.Contains(Okf, StringComparer.Ordinal);
            foreach (var id in dependencies)
            {
                if (_expectedPackages.Contains(id, StringComparer.OrdinalIgnoreCase) ||
                    id is "FSharp.Core" || id.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                    (hasOkf && id is "Markdig" or "YamlDotNet"))
                    continue;

                throw new CommandAssertionException($"Unexpected dependency {id} in the knowledge package graph.", result);
            }
        }
    }
}
