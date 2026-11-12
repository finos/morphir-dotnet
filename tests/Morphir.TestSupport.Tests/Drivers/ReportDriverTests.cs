// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The drivers and report types that pull request 2 of the change consolidate-black-box-tests adds: the reports of
/// <c>extension list</c> and <c>extension runtimes</c>, the build
/// identity of <c>extension worker</c>, the host copies with no engine library, and the target of a test outside the
/// black-box project. The report tests run the real commands of the development build.
/// </summary>
[Category("PlatformSpecific")]
public class ReportDriverTests
{
    private static readonly System.Reflection.Assembly ThisAssembly = typeof(ReportDriverTests).Assembly;

    private static ScenarioWorkspace Workspace() =>
        ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static CliDriver Cli(IScenarioWorkspace workspace, MorphirHost host) =>
        new(new TestTarget.DevelopmentBuild(), host, CliArtifacts.OfDevelopmentBuild(ThisAssembly).PathOf(host), workspace);

    private static Func<string, string?> Variables(params (string Name, string Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return environment.GetValueOrDefault;
    }

    [Test]
    public async Task TheExtensionListReportReadsARealReportOfEachHost()
    {
        await using var workspace = Workspace();

        var native = ExtensionListReport.Of(await Cli(workspace, MorphirHost.Native).RunAsync(ExtensionListReport.Arguments));
        var tool = ExtensionListReport.Of(await Cli(workspace, MorphirHost.DotnetTool).RunAsync(ExtensionListReport.Arguments));

        native.Host.Should().Be(ExtensionListReport.HostName(MorphirHost.Native)).And.Be("morphir");
        tool.Host.Should().Be(ExtensionListReport.HostName(MorphirHost.DotnetTool)).And.Be("dotnet-morphir");
        native.HasDeployment.Should().BeFalse("no named release profile built the development build");
        native.Execution.Should().Be("jit");
        native.Extension("morphir-dotnet.fsharp").Available.Should().BeFalse();
        native.Extension("morphir-dotnet.fsharp").HasReason.Should().BeTrue();
        tool.Extension("morphir-dotnet.fsharp").Available.Should().BeTrue();
        tool.Extension("morphir-dotnet.fsharp").HasReason.Should().BeFalse();
        native.Extension("morphir.type-validator").ValidatorIrVersions.Should().Equal("1", "2", "3");
        var missing = () => native.Extension("no.such-extension");
        missing.Should().Throw<CommandAssertionException>().WithMessage("*no entry 'no.such-extension'*");
    }

    [Test]
    [Arguments("dev", null, "jit")]
    [Arguments("trimmed", null, "jit")]
    [Arguments("aot", "native-aot", "native-aot")]
    [Arguments("native-aot", "native-aot", "native-aot")]
    [Arguments("self-contained", "self-contained", "jit")]
    [Arguments("dotnet-tool", "dotnet-tool", "jit")]
    public void TheExpectedDeploymentIsTheReleaseProfileOfTheTarget(string name, string? deployment, string execution)
    {
        var target = TestRunSettings.Parse(Variables(("MORPHIR_TEST_TARGET", name)), _ => false).Target;

        ExtensionListReport.ExpectedDeploymentOf(target).Should().Be((deployment, execution));
    }

    [Test]
    public async Task TheRuntimeReportReadsARealReport()
    {
        await using var workspace = Workspace();

        var report = ExtensionRuntimesReport.Of(await Cli(workspace, MorphirHost.Native).RunAsync(ExtensionRuntimesReport.Arguments));

        report.Host.Should().Be("morphir");
        report.Platform.Should().Be(RuntimeIdentifiers.Current);
        report.Wasm.Kind.Should().Be("wasm");
        report.Wasm.EngineName.Should().Be("wasmtime");
        report.Wasm.EngineVersion.Should().Be(PinnedPackages.VersionOf(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Directory.Packages.props")), "Wasmtime"));
        report.Wasm.Available.Should().Be(!report.IsWindowsX64);
    }

    [Test]
    public async Task TheBuildIdentityOfAWorkerIsReadFromItsHelp()
    {
        await using var workspace = Workspace();
        var fake = Path.Combine(AppContext.BaseDirectory, "Morphir.Conformance.FakeWorker.dll");
        var sessions = new ProcessSessionDriver(new TestTarget.DevelopmentBuild(), MorphirHost.Native, fake, workspace);

        var identity = await ExtensionWorker.BuildIdentityAsync(sessions);

        identity.Should().Be(new string('a', 64), "the fake worker gives that identity with no script");
        ExtensionWorker.OtherIdentity(identity).Should().Be(new string('a', 63) + "0");
        ExtensionWorker.ServeArguments("/my modules/guest.wasm", identity, "--verbose")
            .Should().Equal("extension", "worker", "--abi", "extism-kernel-v1", "--module", "/my modules/guest.wasm", "--build-identity", identity, "--json", "--verbose");
    }

    [Test]
    public void AHostCopyDeletesEachEngineLibraryAndLeavesTheSourceAsItWas()
    {
        using var sandbox = TestSandbox.Create("morphir host ");
        var host = Directory.CreateDirectory(sandbox.PathOf("host")).FullName;
        File.WriteAllText(Path.Combine(host, "morphir.dll"), "host");
        var native = Directory.CreateDirectory(Path.Combine(host, "runtimes", "linux-x64", "native")).FullName;
        File.WriteAllText(Path.Combine(native, "libwasmtime.so"), "engine");
        File.WriteAllText(Path.Combine(host, "wasmtime.dll"), "engine");
        using var copies = new HostCopies();

        var copy = copies.WithoutEngineLibraries(Path.Combine(host, "morphir.dll"));

        copy.Deleted.Should().BeEquivalentTo("runtimes/linux-x64/native/libwasmtime.so", "wasmtime.dll");
        File.ReadAllText(copy.Artifact).Should().Be("host");
        File.Exists(Path.Combine(native, "libwasmtime.so")).Should().BeTrue("the source is not changed");
        copies.WithoutEngineLibraries(Path.Combine(host, "morphir.dll")).Should().BeSameAs(copy, "each host is copied once");
        copies.Dispose();
        File.Exists(copy.Artifact).Should().BeFalse();
    }

    [Test]
    public void TheTargetOfATestOutsideTheBlackBoxProjectHasItsDefaultHost()
    {
        var development = CliArtifacts.OfDevelopmentBuild(ThisAssembly);

        var tool = TargetCli.OfTestRun(ThisAssembly, MorphirHost.DotnetTool, Variables(), File.Exists);
        var native = TargetCli.OfTestRun(ThisAssembly, MorphirHost.DotnetTool, Variables(("MORPHIR_TEST_HOST", "native")), File.Exists);
        var path = TargetCli.OfTestRun(ThisAssembly, MorphirHost.DotnetTool,
            Variables(("MORPHIR_TEST_TARGET", "executable"), ("MORPHIR_TEST_EXECUTABLE", "/opt/morphir"), ("MORPHIR_TEST_HOST", "native")), _ => true);

        tool.Host.Should().Be(MorphirHost.DotnetTool);
        tool.Artifact.Should().Be(development.PathOf(MorphirHost.DotnetTool));
        tool.FileName.Should().Be(DotnetHost.Path, "a test that changes PATH must still find the dotnet host");
        tool.PrefixArguments.Should().Equal("exec", tool.Artifact);
        native.Host.Should().Be(MorphirHost.Native, "MORPHIR_TEST_HOST names the host of the run");
        path.Host.Should().Be(MorphirHost.Native, "the target has one host, so the default host of the test is not used");
        path.FileName.Should().Be("/opt/morphir");
        path.PrefixArguments.Should().BeEmpty();
    }

    [Test]
    public void TheTargetOfATestOutsideTheBlackBoxProjectStopsWithThePublishCommand()
    {
        var resolve = () => TargetCli.OfTestRun(ThisAssembly, MorphirHost.Native, Variables(("MORPHIR_TEST_TARGET", "aot")), path => path.EndsWith(".dll", StringComparison.Ordinal));

        resolve.Should().Throw<TestSessionException>().WithMessage("*PublishExecutable*");
    }
}
