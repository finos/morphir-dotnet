// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The package driver (design DRV-1 and GRP-1 of the change consolidate-black-box-tests): the packed packages of the
/// packages lane, the reading of a package, and a pack of a project of the development build into the workspace.
/// </summary>
[Category("PlatformSpecific")]
public class PackageDriverTests
{
    private static readonly System.Reflection.Assembly ThisAssembly = typeof(PackageDriverTests).Assembly;

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static ScenarioWorkspace Workspace() => ScenarioWorkspace.Create(Fixtures);

    /// <summary>Writes a package file with a manifest, the given entries and, optionally, one assembly.</summary>
    private static string WritePackage(
        string folder, string id, string version, string? dependencies = null, (string Entry, byte[] Bytes)? assembly = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{id}.{version}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry($"{id}.nuspec").Open()))
        {
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{id}</id>
                    <version>{version}</version>
                    <authors>FINOS</authors>
                    <license type="expression">Apache-2.0</license>
                    <projectUrl>https://github.com/finos/morphir-dotnet</projectUrl>
                    <releaseNotes>### Added</releaseNotes>
                    <packageTypes><packageType name="DotnetTool" /></packageTypes>
                    <dependencies><group targetFramework="net10.0">{dependencies}</group></dependencies>
                  </metadata>
                </package>
                """);
        }

        using (var writer = new StreamWriter(archive.CreateEntry("README.md").Open())) writer.Write("readme");
        if (assembly is { } file)
        {
            using var stream = archive.CreateEntry(file.Entry).Open();
            stream.Write(file.Bytes);
        }

        return path;
    }

    [Test]
    public void ThePackagesLaneReadsOnlyTheFolderThatTheBuildGives()
    {
        using var sandbox = TestSandbox.Create("packed-packages-");
        var given = sandbox.PathOf("given");
        WritePackage(given, "Morphir.One", "1.0.0");
        WritePackage(sandbox.PathOf("other"), "Morphir.Two", "1.0.0");

        var packed = PackedPackages.Of(given, "Release");

        packed.Folder.Should().Be(Path.GetFullPath(given));
        packed.All.Select(package => package.Id).Should().Equal("Morphir.One");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public void APackagesRunWithNoFolderStopsWithThePackCommand(string? folder)
    {
        var start = () => PackedPackages.Of(folder, "Release");

        start.Should().Throw<TestSessionException>()
            .WithMessage($"*{PackedPackages.Variable}*./build.sh --target PackAll --configuration Release*");
    }

    [Test]
    public void APackagesRunWithAFolderWithNoPackageStopsWithThePackCommand()
    {
        using var sandbox = TestSandbox.Create("packed-packages-");
        var empty = Directory.CreateDirectory(sandbox.PathOf("empty")).FullName;

        var start = () => PackedPackages.Of(empty, "Debug");

        start.Should().Throw<TestSessionException>().WithMessage($"*{empty}*has no package*./build.sh --target PackAll --configuration Debug*");
        var missing = () => PackedPackages.Of(sandbox.PathOf("not there"), "Debug");
        missing.Should().Throw<TestSessionException>().WithMessage("*has no package*PackAll*");
    }

    [Test]
    public void APackagesRunWithNoPackageStopsBeforeTheFirstTest()
    {
        using var sandbox = TestSandbox.Create("packed-packages-");
        var empty = Directory.CreateDirectory(sandbox.PathOf("empty")).FullName;
        var variables = new Dictionary<string, string> { ["MORPHIR_TEST_LANE"] = "packages", [PackedPackages.Variable] = empty };

        var start = () => BlackBoxSession.Start(ThisAssembly, variables.GetValueOrDefault, File.Exists, Fixtures);

        start.Should().Throw<TestSessionException>().WithMessage("*has no package*PackAll*");
    }

    [Test]
    public async Task ATestOfThePackagesLaneGetsThePackagesOfTheFolder()
    {
        using var sandbox = TestSandbox.Create("packed-packages-");
        var given = sandbox.PathOf("given");
        WritePackage(given, "Morphir.One", "1.0.0");
        var variables = new Dictionary<string, string> { ["MORPHIR_TEST_LANE"] = "packages", [PackedPackages.Variable] = given };
        var session = BlackBoxSession.Start(ThisAssembly, variables.GetValueOrDefault, File.Exists, Fixtures);

        await using var drivers = session.BeginTest(MorphirHost.Native);

        drivers.Packages.Packed.Find("Morphir.One").Version.Should().Be("1.0.0");
    }

    [Test]
    public async Task ARunOfAnotherLaneDoesNotReadThePackagesFolder()
    {
        // The variable names a folder with no package. A run of another lane does not read it, so it starts.
        var variables = new Dictionary<string, string> { [PackedPackages.Variable] = Path.Combine(Fixtures, "not there") };
        var session = BlackBoxSession.Start(ThisAssembly, variables.GetValueOrDefault, File.Exists, Fixtures);

        await using var drivers = session.BeginTest(MorphirHost.Native);

        var packed = () => drivers.Packages.Packed;
        packed.Should().Throw<TestSessionException>().WithMessage("*only in the packages lane*");
    }

    [Test]
    public void TwoPackagesWithOneIdInTheFolderAreAnError()
    {
        using var sandbox = TestSandbox.Create("packed-packages-");
        var given = sandbox.PathOf("given");
        WritePackage(given, "Morphir.One", "1.0.0");
        WritePackage(given, "Morphir.One", "2.0.0");

        var find = () => PackedPackages.Of(given, "Release").Find("Morphir.One");

        find.Should().Throw<TestSessionException>().WithMessage("*2 packages Morphir.One*Morphir.One.1.0.0.nupkg*Morphir.One.2.0.0.nupkg*");
        var none = () => PackedPackages.Of(given, "Release").Find("Morphir.Two");
        none.Should().Throw<TestSessionException>().WithMessage("*no package Morphir.Two*");
    }

    [Test]
    public void APackageGivesItsManifestItsEntriesAndItsAssemblies()
    {
        using var sandbox = TestSandbox.Create("packed-packages-");
        var bytes = File.ReadAllBytes(typeof(PackedPackage).Assembly.Location);
        var path = WritePackage(
            sandbox.PathOf("feed"), "Morphir.One", "1.2.3-rc.1",
            """<dependency id="Morphir.Two" version="[1.2.3-rc.1]" /><dependency id="FSharp.Core" version="11.0.100" />""",
            ("tools/net11.0/any/Morphir.TestSupport.dll", bytes));

        var package = PackedPackage.Read(path);

        package.Id.Should().Be("Morphir.One");
        package.Version.Should().Be("1.2.3-rc.1");
        package.FileName.Should().Be("Morphir.One.1.2.3-rc.1.nupkg");
        package.Manifest.Authors.Should().Be("FINOS");
        package.Manifest.License.Should().Be("Apache-2.0");
        package.Manifest.ProjectUrl.Should().Be("https://github.com/finos/morphir-dotnet");
        package.Manifest.ReleaseNotes.Should().Be("### Added");
        package.Manifest.PackageTypes.Should().Equal("DotnetTool");
        package.Manifest.Dependencies.Should().Equal(new PackageDependency("Morphir.Two", "[1.2.3-rc.1]"), new PackageDependency("FSharp.Core", "11.0.100"));
        package.Manifest.Dependencies[0].IsExactly("1.2.3-rc.1").Should().BeTrue();
        package.Manifest.Dependencies[1].IsExactly("11.0.101").Should().BeFalse();
        package.Entries.Should().Contain(["Morphir.One.nuspec", "README.md", "tools/net11.0/any/Morphir.TestSupport.dll"]);
        package.ReadEntry("README.md").Should().Be("readme");

        var assembly = package.Assemblies(entry => entry.StartsWith("tools/", StringComparison.Ordinal)).Should().ContainSingle().Subject;
        assembly.Entry.Should().Be("tools/net11.0/any/Morphir.TestSupport.dll");
        assembly.Identity.Name.Should().Be("Morphir.TestSupport");
        assembly.Identity.Version.Should().Be(typeof(PackedPackage).Assembly.GetName().Version);
        assembly.Identity.References.Select(reference => reference.Name).Should().Contain("System.Runtime");
        package.Assemblies(entry => entry.StartsWith("lib/", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Test]
    [Arguments("[1.0.0]", true, true)]
    [Arguments("[1.0.0, 1.0.0]", true, true)]
    [Arguments("1.0.0", false, true)]
    [Arguments("[1.0.0, )", false, false)]
    [Arguments("(1.0.0, 1.0.0]", false, false)]
    [Arguments("[1.0.0, 2.0.0]", false, false)]
    [Arguments("[1.0.1]", false, false)]
    public void ADependencyRangeIsExactOnlyWithBothBoundsIncluded(string range, bool exactly, bool names)
    {
        var dependency = new PackageDependency("Morphir.Two", range);

        dependency.IsExactly("1.0.0").Should().Be(exactly);
        dependency.NamesVersion("1.0.0").Should().Be(names);
    }

    [Test]
    public void TheTestAssemblyHasThePackageSourcesOfTheConfigurationOfItsBuild()
    {
        var sources = PackageSources.OfDevelopmentBuild(ThisAssembly);
        var configuration = ThisAssembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;

        sources.Should().NotBeNull("the project imports Morphir.PackageSources.targets");
        sources!.Configuration.Should().Be(configuration);
        foreach (var project in PackageProjects.All)
        {
            File.Exists(sources.ProjectOf(project)).Should().BeTrue($"{PackageProjects.IdOf(project)} has a project file");
            Path.GetFileNameWithoutExtension(sources.ProjectOf(project)).Should().Be(PackageProjects.IdOf(project));
        }

        sources.TargetPathOf(PackageProject.MyriadSystemTextJsonAnnotations).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Should().Contain(configuration);
        var sdk = () => sources.TargetPathOf(PackageProject.SdkFSharp);
        sdk.Should().Throw<TestSessionException>().WithMessage("*not built first*");
    }

    [Test]
    public void APackageIdThatTheDriverDoesNotPackIsAnErrorWithTheIds()
    {
        PackageProjects.Parse("Morphir.Myriad").Should().Be(PackageProject.Myriad);

        var parse = () => PackageProjects.Parse("Morphir.Unknown");

        parse.Should().Throw<TestSessionException>().WithMessage("*Morphir.Unknown*Morphir.Myriad*Morphir.SDK.FSharp*");
    }

    [Test]
    public void OnlyTheDevelopmentBuildPacksItsProjects()
    {
        foreach (var host in MorphirHosts.All)
            DriverCapabilities.Of(new TestTarget.DevelopmentBuild(), host).Should().Contain(DriverCapability.DevelopmentPackages);

        var executable = new TestTarget.Executable(typeof(PackageDriverTests).Assembly.Location, MorphirHost.Native);
        DriverCapabilities.Of(executable, MorphirHost.Native).Should().NotContain(DriverCapability.DevelopmentPackages);
        DriverCapabilities.TagOf(DriverCapability.DevelopmentPackages).Should().Be("needs-development-packages");
    }

    [Test]
    public async Task TheNuGetConfigurationOfAConsumerHasTheFeedOfTheWorkspace()
    {
        await using var workspace = Workspace();
        var driver = new PackageDriver(workspace, PackageSources.OfDevelopmentBuild(ThisAssembly), packed: null);
        var consumer = workspace.Directory("consumer with a space");

        var feedOnly = workspace.ReadText(driver.WriteNuGetConfig(consumer, includeNuGetOrg: false));
        feedOnly.Should().Contain("<clear />").And.Contain(driver.Feed.Path).And.NotContain("api.nuget.org");

        var mapped = workspace.ReadText(driver.WriteNuGetConfig(
            consumer, includeNuGetOrg: true, [new PackageSourcePattern(FromFeed: true, "Morphir.SDK*"), new PackageSourcePattern(FromFeed: false, "FSharp.Core")]));
        mapped.Should().Contain("https://api.nuget.org/v3/index.json")
            .And.Contain("""<packageSource key="feed"><package pattern="Morphir.SDK*" /></packageSource>""")
            .And.Contain("""<packageSource key="nuget"><package pattern="FSharp.Core" /></packageSource>""");
    }

    /// <summary>The length and the time of each file below the bin and obj folders of each project of the package sources.</summary>
    private static Dictionary<string, (long, DateTime)> BuildOutputs(PackageSources sources) =>
        PackageProjects.All
            .Select(project => Path.GetDirectoryName(sources.ProjectOf(project))!)
            .SelectMany(directory => new[] { Path.Combine(directory, "bin"), Path.Combine(directory, "obj") })
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .ToDictionary(file => file, file => (new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)));

    [Test]
    public async Task APackWithNoBuildAndAConsumerOfItChangeNoFileOfTheSolutionBuild()
    {
        var sources = PackageSources.OfDevelopmentBuild(ThisAssembly)!;
        var before = BuildOutputs(sources);
        await using var workspace = Workspace();
        var driver = new PackageDriver(workspace, sources, packed: null);

        var package = await driver.PackAsync(PackageProject.MyriadSystemTextJsonAnnotations, new PackOptions { PackageVersion = "0.1.0-driver.1" });

        package.Id.Should().Be("Morphir.Myriad.SystemTextJson.Annotations");
        package.Version.Should().Be("0.1.0-driver.1");
        package.Path.Should().StartWith(driver.Feed.Path);
        package.Entries.Should().Contain("lib/netstandard2.0/Morphir.Myriad.SystemTextJson.Annotations.dll");

        // A consumer in a folder with a space restores the package from the feed of the workspace and builds.
        var consumer = workspace.Directory("consumer with a space");
        driver.WriteNuGetConfig(consumer, includeNuGetOrg: false);
        workspace.WriteText(consumer.File("Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Morphir.Myriad.SystemTextJson.Annotations" Version="[0.1.0-driver.1]" /></ItemGroup>
            </Project>
            """);
        workspace.WriteText(consumer.File("Uses.cs"), "public static class Uses { public static System.Type Type => typeof(Morphir.Myriad.SystemTextJson.JsonUnionAttribute); }");

        var build = await driver.DotnetAsync(consumer, ["build", "Consumer.csproj", "-nologo"]);

        build.ShouldExitWith(0);
        Directory.Exists(Path.Combine(driver.Packages.Path, "morphir.myriad.systemtextjson.annotations", "0.1.0-driver.1"))
            .Should().BeTrue("the consumer restores into the packages folder of the workspace");
        BuildOutputs(sources).Should().Equal(before, "a pack with no build and a consumer of the package write no file below the bin and obj folders of the repository, so a solution build after them compiles nothing");
    }

    // A product tool writes UTF-8 and reads a framed message as UTF-8 with no byte order mark. On Windows the default
    // encoding of a redirected stream is the code page of the console.
    [Test]
    public async Task ACommandReadsAndWritesItsStreamsAsUtf8()
    {
        await using var workspace = Workspace();
        var driver = new PackageDriver(workspace, PackageSources.OfDevelopmentBuild(ThisAssembly), packed: null);
        var consumer = workspace.Directory("echo consumer");
        driver.WriteNuGetConfig(consumer, includeNuGetOrg: false);
        workspace.WriteText(consumer.File("Echo.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        // The program writes the code of each character of its input, and then the input with a check mark.
        workspace.WriteText(consumer.File("Program.cs"), """
            System.Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            System.Console.InputEncoding = new System.Text.UTF8Encoding(false);
            var input = System.Console.In.ReadToEnd();
            System.Console.WriteLine(string.Join(" ", System.Linq.Enumerable.Select(input, character => ((int)character).ToString("x4"))));
            System.Console.WriteLine("✓ " + input);
            """);
        // The CI jobs set CONFIGURATION=Release, and MSBuild reads an environment variable as a property. So the build
        // names its configuration and its output folder, and the test does not depend on the environment of the job.
        var job = new PackageCommandOptions { Environment = new Dictionary<string, string?> { ["CONFIGURATION"] = "Release" } };
        var output = workspace.Directory("echo output");
        (await driver.DotnetAsync(consumer, ["build", "Echo.csproj", "-c", "Debug", "-o", output.Path, "-nologo"], job)).ShouldExitWith(0);

        var result = await driver.DotnetAsync(consumer, ["exec", output.File("Echo.dll").Path], new PackageCommandOptions { StandardInput = "é│" });

        result.ShouldExitWith(0);
        result.StandardOutput.Split('\n')[0].Trim().Should().Be("00e9 2502", "the input has no byte order mark, and its characters are UTF-8");
        result.OutputShouldContain("✓ é│");
    }

    // On Windows the restore writes the package folder of a project as $(UserProfile)\.nuget\packages\ in its
    // obj/*.nuget.g.props, and the workspace has its own home. So a command on a project of the repository gets the
    // package folder of the solution restore, and it does not look in the home of the workspace.
    [Test]
    public async Task ACommandOnAProjectOfTheRepositoryUsesThePackageFolderOfTheSolutionRestore()
    {
        await using var workspace = Workspace();
        var driver = new PackageDriver(workspace, PackageSources.OfDevelopmentBuild(ThisAssembly), packed: null);
        var project = workspace.Directory("project like windows");
        workspace.WriteText(project.File("Fixture.proj"), """
            <Project>
              <PropertyGroup>
                <NuGetPackageRoot Condition=" '$(NuGetPackageRoot)' == '' ">$(UserProfile)\.nuget\packages\</NuGetPackageRoot>
                <NuGetPackageFolders Condition=" '$(NuGetPackageFolders)' == '' ">$(UserProfile)\.nuget\packages\</NuGetPackageFolders>
              </PropertyGroup>
            </Project>
            """);

        var result = await driver.DotnetOnSourcesAsync(project, ["msbuild", "Fixture.proj", "-getProperty:NuGetPackageRoot", "-getProperty:NuGetPackageFolders", "-nologo"]);

        var folder = Path.TrimEndingDirectorySeparator(TestSandbox.RealNuGetPackagesFolder) + Path.DirectorySeparatorChar;
        using var properties = System.Text.Json.JsonDocument.Parse(result.ShouldExitWith(0).StandardOutput);
        properties.RootElement.GetProperty("Properties").GetProperty("NuGetPackageRoot").GetString().Should().Be(folder);
        properties.RootElement.GetProperty("Properties").GetProperty("NuGetPackageFolders").GetString().Should().Be(folder);
    }

    // On Linux a command starts in a session of its own (setsid), which finds the program with the PATH of the
    // command. The dotnet host of the test process starts each dotnet command, so a PATH with no dotnet works.
    [Test]
    public async Task ACommandStartsTheDotnetHostOfTheTestProcess()
    {
        await using var workspace = Workspace();
        var driver = new PackageDriver(workspace, PackageSources.OfDevelopmentBuild(ThisAssembly), packed: null);
        var noDotnet = new PackageCommandOptions { Environment = new Dictionary<string, string?> { ["PATH"] = workspace.Temp.Path } };

        var result = await driver.DotnetAsync(workspace.Work, ["--version"], noDotnet);

        result.ShouldExitWith(0).CommandLine.Should().StartWith(DotnetHost.Path.Contains(' ') ? "\"" + DotnetHost.Path : DotnetHost.Path);
    }

    [Test]
    public async Task APackOfAProjectThatIsNotBuiltFirstIsRefused()
    {
        await using var workspace = Workspace();
        var driver = new PackageDriver(workspace, PackageSources.OfDevelopmentBuild(ThisAssembly), packed: null);

        var pack = () => driver.PackAsync(PackageProject.SdkFSharp);

        await pack.Should().ThrowAsync<TestSessionException>().WithMessage("*Morphir.SDK.FSharp*not built first*BuildAndPackAsync*");
    }
}
