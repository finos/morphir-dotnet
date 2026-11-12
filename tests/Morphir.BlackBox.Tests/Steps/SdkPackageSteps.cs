// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Morphir.BlackBox.Tests.Steps;

/// <summary>
/// The steps of the F# SDK package migration. The canonical package <c>Morphir.SDK.FSharp</c> owns the runtime
/// assembly <c>Morphir.SDK.dll</c>; the compatibility package <c>Morphir.SDK</c> has only a dependency on it. A package
/// graph is the set of packages that a consumer installs: <c>canonical</c>, <c>legacy</c> or <c>combined</c>.
/// </summary>
[Binding]
public sealed class SdkPackageSteps(IPackageDriver packages, IScenarioWorkspace workspace, ScenarioRun run, PackageRun state)
{
    private const string Canonical = "Morphir.SDK.FSharp";
    private const string Legacy = "Morphir.SDK";

    /// <summary>The SHA-256 of the precompiled consumer that the legacy package built before the migration (Fixtures/SdkPackages/README.md).</summary>
    private const string PrecompiledHash = "41afc3e57b4ebf350467d0d812d0fdb63eda1878eecd9099d30c992d60768228";

    // The consumers target net10.0. The .NET 11 SDK has no .NET 10 targeting or host packs, so they come from nuget.org.
    private static readonly PackageSourcePattern[] Mapping =
    [
        new(FromFeed: true, "Morphir.SDK*"),
        new(FromFeed: false, "FSharp.Core"),
        new(FromFeed: false, "Microsoft.NETCore.App.*"),
        new(FromFeed: false, "Microsoft.AspNetCore.App.*"),
        new(FromFeed: false, "Microsoft.WindowsDesktop.App.*"),
    ];

    private SdkConsumer? _consumer;

    private SdkConsumer Consumer => _consumer ?? throw new InvalidOperationException("The scenario has no SDK consumer before this step.");

    // ---- The packages -------------------------------------------------------------------------------------------

    [Given("the SDK packages built and packed in the workspace with the version of the changelog")]
    public Task TheSdkPackagesWithTheChangelogVersion() => BuildAndPack(version: null);

    /// <summary>The build takes the version from a changelog with that version, as a release build does.</summary>
    [Given("the SDK packages built and packed in the workspace at the version {string}")]
    public Task TheSdkPackagesAtTheVersion(string version) => BuildAndPack(version);

    private async Task BuildAndPack(string? version)
    {
        foreach (var project in new[] { PackageProject.SdkFSharp, PackageProject.Sdk })
        {
            var package = await packages.BuildAndPackAsync(project, new BuildPackOptions { ChangelogVersion = version });
            state.InFeed[package.Id] = package;
            state.Package = package;
        }
    }

    [Then("the build of the package {string} in the workspace made no assembly")]
    public void TheBuildMadeNoAssembly(string id)
    {
        // The build in the workspace (--artifacts-path) has a folder for each project below bin and obj.
        var found = workspace.FilesIn(packages.Artifacts)
            .Where(file => (file.StartsWith($"bin/{id}/", StringComparison.Ordinal) || file.StartsWith($"obj/{id}/", StringComparison.Ordinal))
                && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (found.Length > 0) throw new InvalidOperationException($"The build of {id} made the assemblies {string.Join(", ", found)}.");
    }

    [Then("the precompiled legacy consumer has its recorded SHA-256")]
    public void ThePrecompiledConsumerHasItsHash()
    {
        var hash = Hash(workspace.CopyFixture("SdkPackages/Baseline/LegacyConsumer.dll"));
        if (hash != PrecompiledHash) throw new InvalidOperationException($"The precompiled baseline binary changed: its SHA-256 is {hash}, not {PrecompiledHash}.");
    }

    // ---- A consumer of a package graph --------------------------------------------------------------------------

    /// <summary>
    /// A consumer with unchanged source restores the packages of the graph into a new package folder, builds, and
    /// runs; it writes the public declarations of the runtime that it uses. Then the precompiled consumer of the
    /// legacy package runs, unchanged, on the runtime of this build, with a trace of the host.
    /// </summary>
    [When("a consumer of the package graph {string} restores, builds and runs")]
    public async Task AConsumerOfTheGraph(string graph)
    {
        string[] ids = graph switch
        {
            "canonical" => [Canonical],
            "legacy" => [Legacy],
            "combined" => [Canonical, Legacy],
            _ => throw new ArgumentException($"The package graph '{graph}' is not canonical, legacy or combined."),
        };
        var version = state.VersionOf(Canonical);
        var folder = workspace.Directory(graph);
        var consumer = new SdkConsumer(graph, folder, folder.Directory("packages"), version);
        _consumer = consumer;

        var references = string.Concat(ids.Select(id => $"""<PackageReference Include="{id}" Version="{version}" />"""));
        workspace.WriteText(folder.File("LegacyConsumer.fsproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><GenerateProgramFile>false</GenerateProgramFile><FSharpCoreImplicitPackageVersion>11.0.100</FSharpCoreImplicitPackageVersion><WarningsAsErrors>NU1605</WarningsAsErrors></PropertyGroup>
              <ItemGroup>{references}</ItemGroup>
              <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
            </Project>
            """);
        workspace.WriteText(folder.File("Program.fs"), workspace.ReadText(workspace.CopyFixture("SdkPackages/LegacyConsumer.fs")));
        workspace.WriteText(folder.File("global.json"), workspace.ReadText(workspace.CopyFixture("Repository/global.json")));
        var config = packages.WriteNuGetConfig(folder, includeNuGetOrg: true, Mapping);

        await Dotnet(folder, "restore", "LegacyConsumer.fsproj", "--packages", consumer.Packages.Path, "--configfile", config.Path, "--disable-parallel", "-m:1", "-nodeReuse:false");
        await Dotnet(folder, "build", "LegacyConsumer.fsproj", "--no-restore", "-c", "Release", "-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false");
        await Dotnet(folder, "exec", consumer.Output.File("LegacyConsumer.dll").Path, consumer.PublicApi.Path);

        // The precompiled consumer runs with the files of this build, and only its own assembly is the old one.
        foreach (var file in workspace.FilesIn(consumer.Output).Where(file => !file.Contains('/')))
            workspace.WriteBytes(consumer.Precompiled.File(file), workspace.ReadBytes(consumer.Output.File(file)));
        workspace.WriteBytes(consumer.Precompiled.File("LegacyConsumer.dll"), workspace.ReadBytes(workspace.CopyFixture("SdkPackages/Baseline/LegacyConsumer.dll")));
        await Dotnet(consumer.Precompiled, new PackageCommandOptions
        {
            Environment = new Dictionary<string, string?> { ["DOTNET_HOST_TRACE"] = "1", ["DOTNET_HOST_TRACEFILE"] = consumer.HostTrace.Path },
        }, "exec", consumer.Precompiled.File("LegacyConsumer.dll").Path);
    }

    [Then("the consumer resolves one runtime assembly, from the package {string}")]
    public void TheConsumerResolvesOneRuntime(string id)
    {
        var owners = Target().Select(entry => (Package: entry.Key, Assets: entry.Value?["runtime"]?.AsObject()))
            .SelectMany(entry => entry.Assets?.Select(asset => asset.Key).Where(path => path.EndsWith("/Morphir.SDK.dll", StringComparison.Ordinal))
                .Select(path => $"{entry.Package}: {path}") ?? [])
            .ToArray();
        if (owners.Length != 1 || !owners[0].StartsWith($"{id}/{Consumer.Version}:", StringComparison.Ordinal))
            throw new InvalidOperationException($"The graph {Consumer.Graph} resolves the runtime assemblies [{string.Join("; ", owners)}], not one of {id}/{Consumer.Version}.");
    }

    [Then("the consumer resolves only the packages of its graph")]
    public void TheConsumerResolvesOnlyItsPackages()
    {
        var allowed = new List<string> { $"{Canonical}/{Consumer.Version}", "FSharp.Core/11.0.100" };
        if (Consumer.Graph != "canonical") allowed.Add($"{Legacy}/{Consumer.Version}");
        var unexpected = Target().Select(entry => entry.Key).Where(package => !allowed.Contains(package)).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidOperationException($"The graph {Consumer.Graph} resolves the packages {string.Join(", ", unexpected)}, which it must not.");
    }

    [Then("the runtime assembly in the output of the consumer is the one of the package {string}, with its identity")]
    public void TheRuntimeIsTheOneOfThePackage(string id)
    {
        var runtime = Consumer.Output.File("Morphir.SDK.dll");
        var packaged = Consumer.PackagedRuntime(id);
        if (Hash(runtime) != Hash(packaged))
            throw new InvalidOperationException($"The runtime {runtime} of the graph {Consumer.Graph} differs from the runtime of the package {id}.");

        var identity = AssemblyIdentity.Of(workspace.ReadBytes(runtime));
        var expected = AssemblyIdentity.Of(workspace.ReadBytes(packaged));
        if (identity.Name != expected.Name || identity.Version != expected.Version || identity.Culture != expected.Culture || !identity.PublicKey.SequenceEqual(expected.PublicKey))
            throw new InvalidOperationException($"The identity {identity} of the runtime differs from the identity {expected} of the package.");
        if (expected.Name != "Morphir.SDK" || expected.Culture.Length != 0 || !expected.PublicKey.IsEmpty)
            throw new InvalidOperationException($"The name, the culture or the signing of the runtime changed: {expected}, with a public key of {expected.PublicKey.Length} bytes.");
    }

    [Then("the consumer keeps each public declaration of the legacy runtime")]
    public void TheConsumerKeepsEachPublicDeclaration()
    {
        var now = Lines(workspace.ReadText(Consumer.PublicApi)).ToHashSet(StringComparer.Ordinal);
        var missing = Lines(workspace.ReadText(workspace.CopyFixture("SdkPackages/Baseline/public-api.txt"))).Where(line => !now.Contains(line)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"These old public declarations are missing: {string.Join("; ", missing)}.");
    }

    [Then("the precompiled legacy consumer ran unchanged on the runtime of the package {string}")]
    public void ThePrecompiledConsumerRanOnTheRuntime(string id)
    {
        var loaded = Consumer.Precompiled.File("Morphir.SDK.dll");
        if (Hash(loaded) != Hash(Consumer.PackagedRuntime(id)))
            throw new InvalidOperationException($"The runtime {loaded} of the precompiled consumer is not the runtime of the package {id}.");
        if (Hash(Consumer.Precompiled.File("LegacyConsumer.dll")) != PrecompiledHash)
            throw new InvalidOperationException("The precompiled consumer was compiled again or changed.");
        if (!workspace.ReadText(Consumer.HostTrace).Contains(loaded.Path, StringComparison.Ordinal))
            throw new InvalidOperationException($"The host trace {Consumer.HostTrace} does not show that the host resolved the runtime {loaded}.");
    }

    // ---- A consumer of the generated source ---------------------------------------------------------------------

    /// <summary>
    /// The F# source that the generator gives for the Ordering example (in the representation classic-3 or
    /// v4-4.0.0, the same bytes) compiles against the package and runs four checks of the Order type.
    /// </summary>
    [When("a consumer of the generated Ordering source of {string} builds and runs against the package {string}")]
    public async Task AConsumerOfTheGeneratedSource(string representation, string id)
    {
        var version = state.VersionOf(id);
        var folder = workspace.Directory("generated-" + representation);
        var consumer = new SdkConsumer(representation, folder, folder.Directory("packages"), version);
        _consumer = consumer;

        var source = workspace.CopyFixture("SdkPackages/Generated/Ordering.fs");
        workspace.WriteBytes(folder.File("Ordering.fs"), workspace.ReadBytes(source));
        if (Hash(folder.File("Ordering.fs")) != Hash(source)) throw new InvalidOperationException("The generated source bytes changed.");
        workspace.WriteText(folder.File("GeneratedConsumer.fsproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><GenerateProgramFile>false</GenerateProgramFile><FSharpCoreImplicitPackageVersion>11.0.100</FSharpCoreImplicitPackageVersion><WarningsAsErrors>NU1605</WarningsAsErrors></PropertyGroup><ItemGroup><PackageReference Include="{id}" Version="[{version}]"/><Compile Include="Ordering.fs"/><Compile Include="Program.fs"/></ItemGroup></Project>
            """);
        workspace.WriteText(folder.File("Program.fs"), """
            module GeneratedConsumer
            open Morphir.SDK
            [<EntryPoint>]
            let main _ =
                if Car.Rental.Ordering.less <> LT || Car.Rental.Ordering.compare 1 2 <> LT || Car.Rental.Ordering.compare 2 2 <> EQ || Car.Rental.Ordering.compare 3 2 <> GT then failwith "Generated Order behavior changed"
                printfn "Generated Order: 4 assertions passed"
                0

            """);
        workspace.WriteText(folder.File("global.json"), workspace.ReadText(workspace.CopyFixture("Repository/global.json")));
        var config = packages.WriteNuGetConfig(folder, includeNuGetOrg: true, Mapping);

        await Dotnet(folder, "restore", "GeneratedConsumer.fsproj", "--packages", consumer.Packages.Path, "--configfile", config.Path, "--disable-parallel", "-m:1", "-nodeReuse:false");
        await Dotnet(folder, "build", "GeneratedConsumer.fsproj", "--no-restore", "-c", "Release", "-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false");
        await Dotnet(folder, "exec", consumer.Output.File("GeneratedConsumer.dll").Path);
        state.ConsumerOutput = run.Result;
    }

    [Then("the runtime assembly in the output of the consumer is the runtime of the package {string}")]
    public void TheRuntimeOfTheGeneratedConsumer(string id)
    {
        if (Hash(Consumer.Output.File("Morphir.SDK.dll")) != Hash(Consumer.PackagedRuntime(id)))
            throw new InvalidOperationException($"The consumer {Consumer.Graph} did not load the runtime of the package {id}.");
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private Task Dotnet(WorkspaceDirectory folder, params string[] arguments) => Dotnet(folder, options: null, arguments);

    private async Task Dotnet(WorkspaceDirectory folder, PackageCommandOptions? options, params string[] arguments) =>
        run.Result = (await packages.DotnetAsync(folder, arguments, options)).ShouldExitWith(0);

    private JsonObject Target()
    {
        var assets = JsonNode.Parse(workspace.ReadText(Consumer.Folder.File("obj", "project.assets.json")))!;
        return assets["targets"]?["net10.0"]?.AsObject()
            ?? throw new InvalidOperationException($"The assets file of the graph {Consumer.Graph} has no target net10.0.");
    }

    private string Hash(WorkspaceFile file) => Convert.ToHexStringLower(SHA256.HashData(workspace.ReadBytes(file)));

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Where(line => line.Length > 0);

    /// <summary>The folders and files of one consumer.</summary>
    private sealed record SdkConsumer(string Graph, WorkspaceDirectory Folder, WorkspaceDirectory Packages, string Version)
    {
        public WorkspaceDirectory Output => Folder.Directory("bin", "Release", "net10.0");

        public WorkspaceDirectory Precompiled => Folder.Directory("precompiled-run");

        public WorkspaceFile PublicApi => Folder.File("public-api.txt");

        public WorkspaceFile HostTrace => Folder.File("precompiled-host-trace.txt");

        /// <summary>The runtime assembly in the package folder of the consumer.</summary>
        public WorkspaceFile PackagedRuntime(string id) =>
            Packages.File(id.ToLowerInvariant(), Version.ToLowerInvariant(), "lib", "net10.0", "Morphir.SDK.dll");
    }
}
