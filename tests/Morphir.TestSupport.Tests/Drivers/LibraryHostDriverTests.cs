// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The programs that a black-box test runs or publishes, and the library host driver (design DRV-1 of the change
/// consolidate-black-box-tests). The real-process tests use the host <c>Morphir.Compilation.Smoke</c>, which the
/// build of this project builds first (Morphir.TestPrograms.targets).
/// </summary>
[Category("PlatformSpecific")]
public class LibraryHostDriverTests
{
    private const string Program = "compilation-smoke";

    private static ScenarioWorkspace Workspace() => ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static TestPrograms Programs() => TestPrograms.OfTestAssembly(typeof(LibraryHostDriverTests).Assembly);

    private static LibraryHostDriver Driver(IScenarioWorkspace workspace) => new(Programs(), workspace);

    private static readonly Dictionary<string, string> Metadata = new()
    {
        ["Morphir.TestSupport.Program.a-host"] = "/repo/tests/A.Host/bin/Debug/net11.0/A.Host.dll",
        ["Morphir.TestSupport.Program.a-host.Project"] = "/repo/tests/A.Host/A.Host.csproj",
        ["Morphir.TestSupport.Program.b-host"] = "/repo/tests/B.Host/bin/Debug/net11.0/B.Host.dll",
        ["Morphir.TestSupport.Program.b-host.Project"] = "/repo/tests/B.Host/B.Host.fsproj",
        ["Morphir.TestSupport.Cli.Native"] = "/repo/src/Morphir/bin/Debug/net11.0/morphir.dll",
    };

    [Test]
    public void TheTestAssemblyHasThePathAndTheProjectOfEachProgram()
    {
        var program = Programs().Of(Program);

        program.Name.Should().Be(Program);
        Path.GetFileName(program.Assembly).Should().Be("Morphir.Compilation.Smoke.dll");
        File.Exists(program.Assembly).Should().BeTrue();
        Path.GetFileName(program.Project).Should().Be("Morphir.Compilation.Smoke.fsproj");
        program.OutputDirectory.Should().Be(Path.GetDirectoryName(program.Assembly));
    }

    [Test]
    public void TheProgramsAreReadFromTheMetadataOfTheTargetsFile()
    {
        var programs = TestPrograms.Of(Metadata, "A.Tests", _ => true);

        programs.Names.Should().Equal("a-host", "b-host");
        programs.Of("b-host").Project.Should().Be("/repo/tests/B.Host/B.Host.fsproj");
    }

    [Test]
    public void AProgramWithNoFileGivesTheBuildCommand()
    {
        var resolve = () => TestPrograms.Of(Metadata, "A.Tests", path => !path.Contains("B.Host", StringComparison.Ordinal));

        resolve.Should().Throw<TestSessionException>()
            .WithMessage("*b-host*").WithMessage("*/repo/tests/B.Host/bin/Debug/net11.0/B.Host.dll*").WithMessage("*dotnet build /repo/tests/B.Host/B.Host.fsproj -c Debug*");
    }

    [Test]
    public void AProgramThatTheTestAssemblyDoesNotNameIsAnError()
    {
        var programs = TestPrograms.Of(Metadata, "A.Tests", _ => true);

        var program = () => programs.Of("c-host");

        program.Should().Throw<TestSessionException>().WithMessage("*c-host*").WithMessage("*a-host, b-host*").WithMessage("*MorphirTestProgram*");
    }

    [Test]
    public void ATestAssemblyWithNoProgramHasNone() =>
        TestPrograms.Of(new Dictionary<string, string>(), "A.Tests", _ => true).Names.Should().BeEmpty();

    [Test]
    public async Task TheBuiltProgramRunsInTheWorkspace()
    {
        await using var workspace = Workspace();

        var result = await Driver(workspace).RunBuiltAsync(Program, []);

        result.ShouldExitWith(0).OutputShouldContain("ok   diagnostic text");
        result.CommandLine.Should().Contain("Morphir.Compilation.Smoke.dll");
    }

    [Test]
    public async Task AManagedPublishGoesToTheWorkspaceGivesTheSameOutputAndReplacesNoAssemblyOfTheSolutionBuild()
    {
        await using var workspace = Workspace();
        var program = Programs().Of(Program);
        var before = SolutionOutputs(program);
        var lockFile = LockFile(program);
        var driver = Driver(workspace);

        var built = await driver.RunBuiltAsync(Program, []);
        var published = await driver.PublishAsync(Program, ProgramForm.Managed);
        var run = await driver.RunAsync(published, []);

        published.Publish.ShouldExitWith(0);
        published.Executable.Should().StartWith(workspace.Root);
        published.Form.Should().Be(ProgramForm.Managed);
        run.ShouldExitWith(0);
        run.StandardOutput.Should().Be(built.StandardOutput);
        SolutionOutputs(program).Should().Equal(before, "a publish writes its outputs to the workspace, not to the solution build");
        LockFile(program).Should().Be(lockFile, "the publish changes no committed lock file");
    }

    [Test]
    public void ThePublishCommandOfEachFormSendsEachOutputToTheWorkspace()
    {
        var managed = LibraryHostDriver.PublishArguments("/repo/A.csproj", ProgramForm.Managed, "Release", "osx-arm64", "/w/artifacts", "/w/out");
        var trimmed = LibraryHostDriver.PublishArguments("/repo/A.csproj", ProgramForm.Trimmed, "Release", "osx-arm64", "/w/artifacts", "/w/out");
        var aot = LibraryHostDriver.PublishArguments("/repo/A.csproj", ProgramForm.NativeAot, "Release", "osx-arm64", "/w/artifacts", "/w/out");

        managed.Should().StartWith(["publish", "/repo/A.csproj", "-c", "Release"]).And.Contain(["--artifacts-path", "/w/artifacts", "-o", "/w/out"]).And.NotContain("-r");
        trimmed.Should().Contain(["-r", "osx-arm64", "-p:SelfContained=true", "-p:PublishTrimmed=true", "-p:MorphirRidSpecificPublish=true"]);
        aot.Should().Contain(["-r", "osx-arm64", "-p:PublishAot=true", "-p:MorphirProbeAot=true", "-p:MorphirRidSpecificPublish=true"]);
        foreach (var arguments in new[] { trimmed, aot })
        {
            // A restore for the runtime identifier writes its lock files below obj/, not into the committed ones.
            arguments.Should().Contain("-p:NuGetLockFilePath=obj/publish-lock/osx-arm64/packages.lock.json");
        }

        managed.Should().NotContain(argument => argument.StartsWith("-p:NuGetLockFilePath", StringComparison.Ordinal));
        foreach (var arguments in new[] { managed, trimmed, aot })
        {
            // A warning is a result of the publish: the test reads it, and the publish does not fail on it.
            arguments.Should().Contain("-p:ILLinkTreatWarningsAsErrors=false");
        }
    }

    private const string Log = """
          Determining projects to restore...
        /_/src/FSharp.Core/prim-types.fs(1): warning IL2104: Assembly 'FSharp.Core' produced trim warnings. For more information see https://aka.ms/il2104 [/repo/A.fsproj]
        /_/src/FSharp.Core/prim-types.fs(1): warning IL3053: Assembly 'FSharp.Core' produced AOT analysis warnings. [/repo/A.fsproj]
        /_/src/FSharp.Core/prim-types.fs(1): warning IL3053: Assembly 'FSharp.Core' produced AOT analysis warnings. [/repo/A.fsproj]
        /repo/src/Lib/Code.cs(12,5): warning IL2026: Using member 'System.Text.Json.JsonSerializer.Serialize' which has 'RequiresUnreferencedCodeAttribute' can break functionality. [/repo/A.fsproj]
        ILC: error IL3050: Lib.Code.Run(): Using member 'Activator.CreateInstance' which has 'RequiresDynamicCodeAttribute'. [/repo/A.fsproj]
        /repo/src/Lib/Code.cs(3,1): warning CS0168: The variable 'e' is declared but never used [/repo/A.fsproj]
        """;

    [Test]
    public void TheTrimAndAotWarningsOfAPublishAreReadOnce()
    {
        var warnings = PublishWarning.Parse(Log);

        warnings.Select(warning => warning.Code).Should().Equal("IL2104", "IL3053", "IL2026", "IL3050");
        warnings[0].Assembly.Should().Be("FSharp.Core");
        warnings[2].Assembly.Should().BeNull();
        warnings[3].IsError.Should().BeTrue();
    }

    [Test]
    public void AWarningInTheAllowedListPasses()
    {
        var warnings = PublishWarning.Parse(Log).Where(warning => warning.Assembly == "FSharp.Core").ToArray();

        PublishWarning.Unexpected(warnings, [new AllowedWarning("IL2104", "FSharp.Core"), new AllowedWarning("IL3053", "FSharp.Core")]).Should().BeEmpty();
    }

    [Test]
    public void AWarningOutsideTheAllowedListFailsWithTheWarning()
    {
        var published = new PublishedProgram(
            "a-host", ProgramForm.NativeAot, "/w/out/a-host", PublishWarning.Parse(Log),
            new CommandResult("dotnet publish /repo/A.csproj", 0, Log, "", TimeSpan.Zero, []));

        var check = () => published.ShouldHaveOnlyTheWarnings([new AllowedWarning("IL2104", "FSharp.Core"), new AllowedWarning("IL3053", "FSharp.Core")]);

        check.Should().Throw<CommandAssertionException>()
            .WithMessage("*The publish of a-host as NativeAot has 2 trim or AOT warnings that are not allowed*")
            .WithMessage("*IL2026*").WithMessage("*IL3050*").WithMessage("*command: dotnet publish*");
    }

    [Test]
    public void AnAllowedWarningOfOneAssemblyDoesNotAllowTheSameCodeOfAnotherAssembly() =>
        PublishWarning.Unexpected(
            PublishWarning.Parse("x: warning IL2104: Assembly 'CsToml' produced trim warnings. [/repo/A.fsproj]"),
            [new AllowedWarning("IL2104", "FSharp.Core")]).Should().ContainSingle();

    [Test]
    public void AFailedPublishIsAnErrorWithItsOutput()
    {
        var published = new PublishedProgram("a-host", ProgramForm.NativeAot, "/w/out/a-host", [],
            new CommandResult("dotnet publish /repo/A.csproj", 1, "error NU1101", "", TimeSpan.Zero, []));

        var run = () => published.ShouldHaveSucceeded();

        run.Should().Throw<CommandAssertionException>().WithMessage("*The publish of a-host as NativeAot failed*").WithMessage("*error NU1101*");
    }

    private static string LockFile(TestProgram program) =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(program.Project)!, "packages.lock.json"));

    // The assemblies of the solution build of a program and of its references, with the time of each.
    private static Dictionary<string, DateTime> SolutionOutputs(TestProgram program) =>
        Directory.EnumerateFiles(program.OutputDirectory, "*.dll")
            .ToDictionary(path => Path.GetFileName(path), File.GetLastWriteTimeUtc);
}
