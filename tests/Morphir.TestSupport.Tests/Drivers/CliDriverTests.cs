// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The CLI driver on the development build (design DRV-2 of the change consolidate-black-box-tests). These tests
/// start the built hosts, which the build of this project builds first (Morphir.CliArtifacts.targets).
/// </summary>
[Category("PlatformSpecific")]
public class CliDriverTests
{
    private static readonly TestTarget Development = new TestTarget.DevelopmentBuild();

    private static ScenarioWorkspace Workspace() =>
        ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static CliArtifacts Artifacts() => CliArtifacts.OfDevelopmentBuild(typeof(CliDriverTests).Assembly);

    private static CliDriver Driver(IScenarioWorkspace workspace, MorphirHost host) =>
        new(Development, host, Artifacts().PathOf(host), workspace);

    [Test]
    public void TheTestAssemblyHasThePathOfEachHostInTheConfigurationOfItsBuild()
    {
        var artifacts = Artifacts();
        var configuration = typeof(CliDriverTests).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;

        artifacts.Configuration.Should().Be(configuration);
        foreach (var host in MorphirHosts.All)
        {
            var parts = artifacts.PathOf(host).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            parts.Should().Contain(configuration, $"the {host.Name()} host is the one of this build configuration");
            File.Exists(artifacts.PathOf(host)).Should().BeTrue();
        }

        Path.GetFileName(artifacts.PathOf(MorphirHost.Native)).Should().Be("morphir.dll");
        Path.GetFileName(artifacts.PathOf(MorphirHost.Managed)).Should().Be("morphir.dll");
        Path.GetFileName(artifacts.PathOf(MorphirHost.DotnetTool)).Should().Be("dotnet-morphir.dll");
    }

    [Test]
    public void TheManagedHostAndTheNativeHostAreTwoDifferentFiles()
    {
        var artifacts = Artifacts();

        artifacts.PathOf(MorphirHost.Managed).Should().NotBe(artifacts.PathOf(MorphirHost.Native));
        artifacts.PathOf(MorphirHost.Managed).Should().Contain("Morphir.Managed");
    }

    [Test]
    public void NoAssemblyOfAnEntryPointIsVisibleToTheTestProject()
    {
        var referenced = typeof(CliDriverTests).Assembly.GetReferencedAssemblies().Select(name => name.Name);

        referenced.Should().NotContain(["morphir", "dotnet-morphir", "Morphir.Tooling", "Morphir.Tooling.Managed"]);
        File.Exists(Path.Combine(AppContext.BaseDirectory, "morphir.dll")).Should().BeFalse("a build-order reference copies no file");
        File.Exists(Path.Combine(AppContext.BaseDirectory, "dotnet-morphir.dll")).Should().BeFalse();
    }

    [Test]
    [Arguments(MorphirHost.Native)]
    [Arguments(MorphirHost.Managed)]
    [Arguments(MorphirHost.DotnetTool)]
    public async Task TheVersionCommandExitsWithZeroOnEachHost(MorphirHost host)
    {
        await using var workspace = Workspace();
        var driver = Driver(workspace, host);

        var result = await driver.RunAsync(["--version"]);

        result.ShouldExitWith(0);
        result.StandardOutput.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+");
        result.CommandLine.Should().Contain(Artifacts().PathOf(host)).And.EndWith("--version");
        result.Duration.Should().BePositive();
        driver.Host.Should().Be(host);
        driver.Target.Should().Be(Development);
    }

    [Test]
    public async Task ACommandLineAsOneStringRunsTheSameCommand()
    {
        await using var workspace = Workspace();

        var result = await Driver(workspace, MorphirHost.Native).RunCommandLineAsync("--version");

        result.ShouldExitWith(0);
        result.StandardOutput.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Test]
    public async Task APathWithASpaceIsOneArgument()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/valid-ir-v3.json", "my models/valid ir.json");

        var result = await Driver(workspace, MorphirHost.Native).RunAsync(["ir", "verify", model.Path]);

        result.ShouldExitWith(0).OutputShouldContain("VALID");
    }

    [Test]
    public async Task APathWithASpaceInACommandLineStringNeedsQuotes()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/valid-ir-v3.json", "my models/valid ir.json");

        var result = await Driver(workspace, MorphirHost.Native).RunCommandLineAsync($"ir verify \"{model.Path}\"");

        result.ShouldExitWith(0).OutputShouldContain("VALID");
    }

    [Test]
    public async Task TheCommandStartsInTheWorkAreaAndTheResultHasTheFilesThatItWrote()
    {
        await using var workspace = Workspace();
        workspace.WriteText(workspace.File("before.txt"), "before");
        var shell = Shell(workspace, "echo text> written.txt");

        var result = await shell.Driver.RunAsync(shell.Arguments);

        result.ShouldExitWith(0).ShouldHaveWritten("written.txt");
        result.WrittenFiles.Should().Equal("written.txt");
    }

    [Test]
    public async Task TheProductGetsTheHomeOfTheWorkspaceAndTheVariablesOfTheCall()
    {
        await using var workspace = Workspace();
        var shell = Shell(workspace, OperatingSystem.IsWindows() ? "echo %USERPROFILE% %MORPHIR_EXTRA%" : "echo $HOME $MORPHIR_EXTRA");

        var result = await shell.Driver.RunAsync(shell.Arguments, new CliRunOptions
        {
            Environment = new Dictionary<string, string?> { ["MORPHIR_EXTRA"] = "from-the-call" },
        });

        result.StandardOutput.Trim().Should().Be($"{workspace.Home.Path} from-the-call");
    }

    [Test]
    public async Task StandardInputReachesTheProduct()
    {
        await using var workspace = Workspace();
        var shell = Shell(workspace, OperatingSystem.IsWindows() ? "findstr x" : "cat");

        var result = await shell.Driver.RunAsync(shell.Arguments, new CliRunOptions { StandardInput = "x from standard input\n" });

        result.StandardOutput.Trim().Should().Be("x from standard input");
    }

    [Test]
    public async Task ACommandThatExceedsItsLimitFailsWithATimeOutMessageAndLeavesNoProcess()
    {
        await using var workspace = Workspace();
        var shell = Shell(workspace, OperatingSystem.IsWindows() ? "ping -n 31 127.0.0.1 >nul" : "sleep 30; echo done");

        var run = () => shell.Driver.RunAsync(shell.Arguments, new CliRunOptions { Timeout = TimeSpan.FromSeconds(1) });

        var failure = await run.Should().ThrowAsync<ChildProcessTimeoutException>();
        failure.Which.Message.Should().Contain("did not finish within 1s");
        failure.Which.ProcessExited.Should().BeTrue("the process tree is stopped before the error surfaces");
    }

    [Test]
    public async Task ACommandWaitsForAFreePlaceOfTheProcessGate()
    {
        await using var workspace = Workspace();
        var gate = new ProcessGate(1);
        var driver = new CliDriver(Development, MorphirHost.Native, Artifacts().PathOf(MorphirHost.Native), workspace, gate);
        var held = await gate.EnterAsync();

        var run = driver.RunAsync(["--version"]);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var completedWhileThePlaceWasHeld = run.IsCompleted;
        held.Dispose();
        var result = await run;

        completedWhileThePlaceWasHeld.Should().BeFalse();
        result.ShouldExitWith(0);
    }

    [Test]
    public void AMissingArtifactGivesTheBuildCommandAndUsesNoOtherFile()
    {
        var metadata = new Dictionary<string, string>
        {
            ["Morphir.TestSupport.Cli.Native"] = "/repo/src/Morphir/bin/Debug/net11.0/morphir.dll",
            ["Morphir.TestSupport.Cli.Managed"] = "/repo/src/Morphir.Managed/bin/Debug/net11.0/morphir.dll",
            ["Morphir.TestSupport.Cli.Tool"] = "/repo/src/Morphir.Tool/bin/Debug/net11.0/dotnet-morphir.dll",
            ["Morphir.TestSupport.Cli.Configuration"] = "Debug",
        };
        var asked = new List<string>();

        // Only the managed host is built. The Release build of the native host is there, and must not be used.
        var resolve = () => CliArtifacts.OfDevelopmentBuild(metadata, "A.Tests", path =>
        {
            asked.Add(path);
            return path.Contains("Morphir.Managed", StringComparison.Ordinal);
        });

        var message = resolve.Should().Throw<TestSessionException>().Which.Message;
        message.Should().Contain("/repo/src/Morphir/bin/Debug/net11.0/morphir.dll");
        message.Should().Contain("dotnet build src/Morphir/Morphir.csproj -c Debug");
        message.Should().Contain("/repo/src/Morphir.Tool/bin/Debug/net11.0/dotnet-morphir.dll");
        message.Should().Contain("dotnet build src/Morphir.Tool/Morphir.Tool.csproj -c Debug");
        message.Should().NotContain("Morphir.Managed", "that host is built");
        asked.Should().OnlyContain(path => metadata.ContainsValue(path), "no other file is a candidate");
    }

    [Test]
    public void ATestAssemblyWithNoMetadataNamesTheTargetsFile()
    {
        var resolve = () => CliArtifacts.OfDevelopmentBuild(new Dictionary<string, string>(), "A.Tests", _ => true);

        resolve.Should().Throw<TestSessionException>()
            .WithMessage("*A.Tests*").WithMessage("*Morphir.CliArtifacts.targets*");
    }

    /// <summary>A driver on the shell of the operating system, for the behavior of the driver that needs no product.</summary>
    private static (CliDriver Driver, string[] Arguments) Shell(IScenarioWorkspace workspace, string script)
    {
        var path = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
            : "/bin/sh";
        var target = new TestTarget.Executable(path, MorphirHost.Native);
        return (new CliDriver(target, MorphirHost.Native, path, workspace), [OperatingSystem.IsWindows() ? "/c" : "-c", script]);
    }
}
