// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The drivers of the command families of pull request 3 of the change consolidate-black-box-tests: compile, config,
/// the two migrations, extension and validate, and their report types. The command line tests use a recording CLI
/// driver; the report tests run the real commands of the development build.
/// </summary>
[Category("PlatformSpecific")]
public class CommandFamilyDriverTests
{
    private static ScenarioWorkspace Workspace() =>
        ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static CliDriver Cli(IScenarioWorkspace workspace, MorphirHost? host = null) =>
        new(new TestTarget.DevelopmentBuild(), host ?? MorphirHost.Native,
            CliArtifacts.OfDevelopmentBuild(typeof(CommandFamilyDriverTests).Assembly).PathOf(host ?? MorphirHost.Native), workspace);

    private static CommandResult Result(int exitCode, string output) => new("morphir", exitCode, output, "", TimeSpan.Zero, []);

    private sealed class RecordingCli : ICliDriver
    {
        public List<(IReadOnlyList<string> Arguments, CliRunOptions? Options)> Commands { get; } = [];

        public TestTarget Target { get; } = new TestTarget.DevelopmentBuild();

        public MorphirHost Host => MorphirHost.Native;

        public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
        {
            Commands.Add((arguments, options));
            return Task.FromResult(Result(0, ""));
        }

        public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default) =>
            throw new NotSupportedException("A capability driver gives each argument apart.");
    }

    // --- The command lines ----------------------------------------------------------------------------------------

    [Test]
    public async Task CompileBuildsTheCommandLineWithEachPathAsOneArgument()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var project = workspace.Directory("my project");
        var rates = workspace.File("my project", "src", "Rates.fs");
        var pricing = workspace.File("my project", "src", "Pricing.fs");

        await new CompilationDriver(cli).CompileAsync(new CompilationOptions { Project = project, Inputs = [rates, pricing], IrVersion = "4" });
        await new CompilationDriver(cli).CompileAsync(new CompilationOptions
        {
            Output = CompilationOutput.Text,
            Project = project,
            ProjectFile = workspace.File("my project", "Acme Car.fsproj"),
            Language = "fsharp",
            PackageName = "acme/demo",
            Install = workspace.Directory("my project", "dist"),
            OutDir = workspace.Directory("out dir"),
            Target = "csharp",
            GenerateOutput = workspace.Directory("gen code"),
            ExtensionId = "org.finos.morphir.fsharp-frontend",
            Arguments = ["--target", "fsharp"],
        });
        await new CompilationDriver(cli).CompileAsync(new CompilationOptions { Output = CompilationOutput.JsonLines });

        cli.Commands[0].Arguments.Should().Equal("compile", "--json", "--project", project.Path, "--input", rates.Path, pricing.Path, "--ir-version", "4");
        cli.Commands[1].Arguments.Should().Equal(
            "compile", "--project", project.Path, "--project-file", workspace.File("my project", "Acme Car.fsproj").Path, "--language", "fsharp",
            "--package-name", "acme/demo", "--output", workspace.Directory("my project", "dist").Path, "--out-dir", workspace.Directory("out dir").Path,
            "--target", "csharp", "--generate-output", workspace.Directory("gen code").Path, "--extension-id", "org.finos.morphir.fsharp-frontend",
            "--target", "fsharp");
        cli.Commands[2].Arguments.Should().Equal("compile", "--json-lines");
        cli.Commands[0].Options!.Timeout.Should().Be(CompilationDriver.Timeout);
        project.Path.Should().Contain(" ");
    }

    [Test]
    public async Task ConfigurationAndMigrationRunInTheMachineOfTheScenario()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var machine = new ScenarioMachine(workspace.Directory("my app", "src"))
        {
            Home = workspace.Directory("my home"),
            Variables = new Dictionary<string, string> { ["MORPHIR_ISOLATED"] = "1", ["MORPHIR_PROFILES"] = "ci" },
        };

        await new ConfigurationDriver(cli).ConfigureAsync(["get", "custom.winner", "--json"], machine);
        await new MigrationDriver(cli).MigrateProjectAsync(["--dry-run", "--json"], machine);
        await new MigrationDriver(cli).MigrateWorkspaceAsync(["--dry-run"], new ScenarioMachine(workspace.Work));

        cli.Commands[0].Arguments.Should().Equal("config", "get", "custom.winner", "--json");
        cli.Commands[1].Arguments.Should().Equal("project", "migrate", "--dry-run", "--json");
        cli.Commands[2].Arguments.Should().Equal("workspace", "migrate", "--dry-run");

        var options = cli.Commands[0].Options!;
        options.WorkingDirectory.Should().Be(workspace.Directory("my app", "src").Path);
        options.Timeout.Should().Be(ScenarioMachine.Timeout);
        options.Environment.Should().Contain(new KeyValuePair<string, string?>("MORPHIR_HOME", null))
            .And.Contain(new KeyValuePair<string, string?>("MORPHIR_DISCOVERY_BOUNDARY", null))
            .And.Contain(new KeyValuePair<string, string?>("MORPHIR_ISOLATED", "1"))
            .And.Contain(new KeyValuePair<string, string?>("MORPHIR_PROFILES", "ci"));
        foreach (var name in ScenarioMachine.HomeVariables)
            options.Environment.Should().Contain(new KeyValuePair<string, string?>(name, workspace.Directory("my home").Path));

        // With no home of its own, the scenario keeps the home of the workspace, and only the controlled variables go.
        var plain = cli.Commands[2].Options!.Environment!;
        plain.Keys.Should().BeEquivalentTo(ScenarioMachine.ControlledVariables);
        plain.Values.Should().OnlyContain(value => value == null);
    }

    [Test]
    public async Task ExtensionAndValidationBuildTheirCommandLines()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var model = workspace.File("my models", "daily rate.json");

        await new ExtensionDriver(cli).ListAsync();
        await new ExtensionDriver(cli).ListAsync(new ExtensionListOptions { Json = false, Local = true, Platform = "linux-x64" });
        await new ExtensionDriver(cli).RuntimesAsync();
        await new ExtensionDriver(cli).RuntimesAsync(json: false);
        await new ModelValidationDriver(cli).ValidateAsync(model);
        await new ModelValidationDriver(cli).ValidateAsync(model, new ValidationOptions { Json = true, Arguments = ["--no-such-option"] });

        cli.Commands.Select(command => command.Arguments).Should().BeEquivalentTo(
            new IReadOnlyList<string>[]
            {
                ["extension", "list", "--json"],
                ["extension", "list", "--local", "--platform", "linux-x64"],
                ["extension", "runtimes", "--json"],
                ["extension", "runtimes"],
                ["validate", "--input", model.Path],
                ["validate", "--input", model.Path, "--no-such-option", "--json"],
            },
            options => options.WithStrictOrdering());
    }

    [Test]
    public async Task GenerateLeavesOutEachPartThatTheRequestDoesNotHave()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var project = workspace.Directory("my project");
        var output = workspace.Directory("generated code");

        await new GenerationDriver(cli).GenerateAsync(new GenerationRequest
        {
            Target = "fsharp",
            Output = output,
            Options = new GenerationOptions { Project = project, Extension = "a guest.dll", Options = ["format=false"] },
        });
        await new GenerationDriver(cli).GenerateAsync(GenerationRequest.Of(workspace.File("model.json")) with { Options = new GenerationOptions { Arguments = ["--unavailable"] } });

        cli.Commands[0].Arguments.Should().Equal(
            "generate", "--target", "fsharp", "--output", output.Path, "--json", "--option", "format=false", "--project", project.Path, "--extension", "a guest.dll");
        cli.Commands[1].Arguments.Should().Equal("generate", "--input", workspace.File("model.json").Path, "--json", "--unavailable");
    }

    // --- The reports of the real commands -------------------------------------------------------------------------

    [Test]
    public async Task ARealCompilationReportReadsSuccessTheVersionAndTheDocuments()
    {
        await using var workspace = Workspace();
        var rates = workspace.File("acme demo", "src", "Rates.fs");
        workspace.WriteText(rates, "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n");

        var result = await new CompilationDriver(Cli(workspace, MorphirHost.DotnetTool)).CompileAsync(new CompilationOptions
        {
            Inputs = [rates],
            Language = "fsharp",
            PackageName = "acme/demo",
            IrVersion = "4",
        });
        var report = CompilationReport.Of(result);

        result.ShouldExitWith(0);
        report.Success.Should().BeTrue();
        report.IrVersion.Should().Be("4");
        report.Frontend.Should().Be("morphir-dotnet.fsharp (in-process)");
        report.SourceDocuments.Should().ContainSingle().Which.Should().EndWith("Rates.fs");
        report.Diagnostics.Should().BeEmpty();
        report.ShouldSucceed();
        ((Action)(() => report.ShouldHaveDiagnostic("compile.unsupported_ir_version"))).Should().Throw<CommandAssertionException>()
            .WithMessage("*no diagnostic compile.unsupported_ir_version*").WithMessage("*command: *");
    }

    [Test]
    public async Task ARealCompilationReportOfARefusedVersionHasItsDiagnostic()
    {
        await using var workspace = Workspace();
        var rates = workspace.File("src", "Rates.fs");
        workspace.WriteText(rates, "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n");

        var result = await new CompilationDriver(Cli(workspace, MorphirHost.DotnetTool)).CompileAsync(new CompilationOptions
        {
            Inputs = [rates],
            Language = "fsharp",
            PackageName = "acme/demo",
            IrVersion = "4.1.0-draft.1",
        });
        var report = CompilationReport.Of(result);

        result.ShouldExitWith(2);
        report.Success.Should().BeFalse();
        report.ShouldHaveDiagnostic("compile.unsupported_ir_version");
        ((Action)(() => report.ShouldSucceed())).Should().Throw<CommandAssertionException>().WithMessage("*compile.unsupported_ir_version*");
    }

    [Test]
    [Arguments(0, "{ \"success\": false, \"diagnostics\": [] }", "success is false, but the exit code is 0")]
    [Arguments(1, "{ \"success\": true, \"diagnostics\": [] }", "success is true, but the exit code is 1")]
    [Arguments(2, "{ \"success\": false, \"exitCode\": 1, \"diagnostics\": [] }", "exitCode is 1, but the exit code is 2")]
    [Arguments(0, "{ \"diagnostics\": [] }", "no boolean field success")]
    [Arguments(0, "{ \"success\": true }", "no list diagnostics")]
    public void ACompilationReportThatDisagreesWithTheCommandFails(int exitCode, string report, string reason) =>
        ((Action)(() => CompilationReport.Of(Result(exitCode, report)))).Should().Throw<CommandAssertionException>().WithMessage($"*{reason}*");

    [Test]
    public async Task ARealConfigurationReportReadsItsOperationItsSourcesAndItsDiagnostics()
    {
        await using var workspace = Workspace();
        workspace.WriteText(workspace.File("app", "morphir.toml"), "[project]\nname='App'\n");
        var machine = new ScenarioMachine(workspace.Directory("app")) { Home = workspace.Work };
        var driver = new ConfigurationDriver(Cli(workspace));

        var inspected = await driver.ConfigureAsync(["path", "--json", "--isolated", "--discovery-boundary", workspace.Work.Path], machine);
        var report = ConfigurationReport.Of(inspected);
        var refused = ConfigurationReport.Of(await driver.ConfigureAsync(["get", "project.name", "--profiles", "INVALID", "--json"], machine));

        inspected.ShouldExitWith(0);
        report.Operation.Should().Be("path");
        report.Sources.Select(source => source.Path).Should().Contain(ReportPath.Join(workspace.Work.Path, "app/morphir.toml"));
        report.Sources.Select(source => source.Role).Should().NotContain(["System", "Global", "UserOverride"]);
        refused.Result.ExitCode.Should().Be(2);
        refused.ShouldHaveDiagnostic("config.usage");
        ((Action)(() => report.ShouldHaveOutcome("written"))).Should().Throw<CommandAssertionException>().WithMessage("*outcome*null*");
    }

    [Test]
    public async Task ARealEditAndARealMigrationReportReadTheirOutcome()
    {
        await using var workspace = Workspace();
        workspace.WriteText(workspace.File("morphir.json"), "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order']}\n");
        var machine = new ScenarioMachine(workspace.Work);

        var migration = MigrationReport.Of(await new MigrationDriver(Cli(workspace)).MigrateProjectAsync(
            ["--dry-run", "--json", "--discovery-boundary", workspace.Work.Path], machine));
        var edit = ConfigurationReport.Of(await new ConfigurationDriver(Cli(workspace)).ConfigureAsync(
            ["set", "custom.value", "after", "--dry-run", "--json", "--discovery-boundary", workspace.Work.Path], machine));

        migration.Result.ShouldExitWith(0);
        migration.ShouldHaveOutcome("preview");
        migration.Outcome.Should().Be("preview");
        workspace.Exists(workspace.File("morphir.toml")).Should().BeFalse("a dry run writes nothing");
        edit.ShouldHaveOutcome("preview");
        ((Action)(() => migration.ShouldHaveOutcome("written"))).Should().Throw<CommandAssertionException>().WithMessage("*preview, not written*");
    }

    [Test]
    public void AReportWhoseExitCodeDisagreesWithTheCommandFails()
    {
        ((Action)(() => MigrationReport.Of(Result(0, "{ \"exitCode\": 2, \"outcome\": \"blocked\" }")))).Should().Throw<CommandAssertionException>()
            .WithMessage("*exitCode is 2, but the exit code is 0*");
        ((Action)(() => ConfigurationReport.Of(Result(0, "[]")))).Should().Throw<CommandAssertionException>().WithMessage("*not a JSON object*");
    }

    [Test]
    public void AReportCanNestMoreThanSixtyFourLevels()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 130)) + "1" + new string('}', 130);

        ConfigurationReport.Of(Result(0, "{ \"exitCode\": 0, \"value\": " + deep + " }")).Member("value", "a", "a").Should().NotBeNull();
    }

    [Test]
    public async Task RealExtensionReportsReadTheHostTheExtensionsAndTheRuntimes()
    {
        await using var workspace = Workspace();
        var driver = new ExtensionDriver(Cli(workspace));

        var list = ExtensionListReport.Of(await driver.ListAsync());
        var runtimes = ExtensionRuntimesReport.Of(await driver.RuntimesAsync());

        list.Host.Should().Be("morphir");
        list.Execution.Should().Be("jit");
        list.HasDeployment.Should().BeFalse();
        list.Extensions.Select(extension => extension.Id).Should().Contain(["morphir.csharp", "morphir.fsharp"]);
        list.Extensions.Single(extension => extension.Id == "morphir-dotnet.fsharp").Available.Should().BeFalse("the native host has no in-process F# frontend");
        runtimes.Host.Should().Be("morphir");
        runtimes.Platform.Should().NotBeNullOrEmpty();
        runtimes.Runtimes.Should().ContainSingle().Which.Kind.Should().Be("wasm");
        runtimes.Runtimes[0].EngineName.Should().Be("wasmtime");
    }

    [Test]
    public async Task RealValidationReportsReadSuccessAndTheHost()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/valid-ir-v3.json", "my models/valid ir.json");
        var driver = new ModelValidationDriver(Cli(workspace));

        var valid = ValidationReport.Of(await driver.ValidateAsync(model, new ValidationOptions { Json = true }));
        var usage = ValidationReport.Of(await driver.ValidateAsync(model, new ValidationOptions { Json = true, Arguments = ["--no-such-option"] }));

        valid.Result.ShouldExitWith(0);
        valid.Success.Should().BeTrue();
        valid.Host.Should().Be("morphir");
        valid.Input.Should().Be(model.Path);
        usage.Result.ShouldExitWith(2);
        usage.Success.Should().BeFalse();
    }

    [Test]
    public void TheBuiltFSharpBackendGuestIsTheFileOfTheDevelopmentBuild()
    {
        var path = ExtensionArtifacts.OfTestAssembly(typeof(CommandFamilyDriverTests).Assembly).PathOf(BuiltExtension.FSharpBackend);

        path.Should().EndWith("morphir-fsharp-backend.dll");
        File.Exists(path).Should().BeTrue();
        path.Should().Contain(Path.Combine("src", "Morphir.CodeGeneration.FSharp.Extension", "bin"));
    }

    [Test]
    public void AMissingBuiltGuestStopsWithTheTargetsFileOrTheBuildCommand()
    {
        var noMetadata = () => ExtensionArtifacts.Of(new Dictionary<string, string>(), _ => true).PathOf(BuiltExtension.FSharpBackend);
        var noFile = () => ExtensionArtifacts.Of(
            new Dictionary<string, string> { ["Morphir.TestSupport.Extension.FSharpBackend"] = "/no/such/morphir-fsharp-backend.dll" }, _ => false)
            .PathOf(BuiltExtension.FSharpBackend);

        noMetadata.Should().Throw<TestSessionException>().WithMessage("*Morphir.ExtensionArtifacts.targets*");
        noFile.Should().Throw<TestSessionException>().WithMessage("*dotnet build src/Morphir.CodeGeneration.FSharp.Extension/*");
        ((Action)(() => new ExtensionDriver(new RecordingCli()).BuiltExtension(BuiltExtension.FSharpBackend))).Should().Throw<TestSessionException>();
    }

    [Test]
    public void OnlyTheDevelopmentBuildHasTheBuiltGuests()
    {
        foreach (var host in MorphirHosts.All)
            DriverCapabilities.Of(new TestTarget.DevelopmentBuild(), host).Should().Contain(DriverCapability.BuiltExtensions);
        DriverCapabilities.Of(new TestTarget.Executable("/a/morphir", MorphirHost.Native), MorphirHost.Native).Should().NotContain(DriverCapability.BuiltExtensions);
    }

    [Test]
    public async Task AHostWithoutDynamicCodeRunsWithTheSwitchOffAndCannotFormat()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/car-rental-v3.json", "my models/car rental.json");
        var driver = new GenerationDriver(Cli(workspace));

        var withoutDynamicCode = await driver.GenerateAsync(model, "fsharp", workspace.Directory("no dynamic code"), new GenerationOptions { WithoutDynamicCode = true });
        var jit = await driver.GenerateAsync(model, "fsharp", workspace.Directory("jit"));

        withoutDynamicCode.CommandLine.Should().Contain("exec --runtimeconfig");
        GenerationReport.Of(withoutDynamicCode).ShouldSucceed().Document.GetProperty("formatting").GetString().Should().Be("unavailable");
        GenerationReport.Of(jit).ShouldSucceed().Document.GetProperty("formatting").GetString().Should().Be("applied");
        workspace.FilesIn(workspace.Temp).Should().Contain("morphir.no-dynamic-code.runtimeconfig.json", "the copy is in the workspace, not in the build output");
        DriverCapabilities.Of(new TestTarget.Executable("/a/morphir", MorphirHost.Native), MorphirHost.Native).Should().NotContain(DriverCapability.HostWithoutDynamicCode);
    }

    [Test]
    public async Task ThePlatformOfThisMachineIsThePlatformOfTheRuntimesReport()
    {
        await using var workspace = Workspace();

        var report = ExtensionRuntimesReport.Of(await new ExtensionDriver(Cli(workspace)).RuntimesAsync());

        report.Platform.Should().Be(ExtensionPlatform.OfThisMachine);
        ExtensionPlatform.Of("linux", System.Runtime.InteropServices.Architecture.X64, "linux-musl-x64").Should().Be("linux-musl-x64");
        ExtensionPlatform.Of("linux", System.Runtime.InteropServices.Architecture.Arm64, "ubuntu.24.04-arm64").Should().Be("linux-arm64");
        ExtensionPlatform.Of("win", System.Runtime.InteropServices.Architecture.X64, "win-x64").Should().Be(ExtensionPlatform.WithoutWasm);
    }

    [Test]
    public async Task TheRoutingDriverRunsTheHostAndTheStubWithTheSearchPathOfTheScenario()
    {
        await using var workspace = Workspace();
        var routing = new ToolRoutingDriver(Cli(workspace), workspace, ExtensionArtifacts.OfTestAssembly(typeof(CommandFamilyDriverTests).Assembly));
        var stub = routing.InstallStub(workspace.Directory("my tools"));
        var options = new ToolRoutingOptions { Path = [workspace.Directory("my tools")], Variables = new Dictionary<string, string> { ["MCK_STUB_EXIT"] = "3" } };

        var routed = await routing.RunAsync(["mck", "run", "a b"], options);
        var alone = await routing.RunStubAsync(stub, ["run", "a b"], options);

        stub.Name.Should().Be(routing.ExecutableName("morphir-mck"));
        routed.ShouldExitWith(3);
        alone.ShouldExitWith(3);
        StubReport.Of(routed).Arguments.Should().Equal("run", "a b");
        routed.StandardOutput.Should().Be(alone.StandardOutput);
        StubReport.Of(alone).WorkingDirectory.Should().EndWith("work");
        (await routing.RunAsync(["mck", "run"], new ToolRoutingOptions())).ShouldExitWith(127);
    }

    [Test]
    public async Task TheReportsRefuseTheFormsThatTheOldChecksRefused()
    {
        await using var workspace = Workspace();

        ((Action)(() => _ = ConfigurationReport.Of(Result(0, "{ \"exitCode\": 0 }")).Sources)).Should().Throw<CommandAssertionException>().WithMessage("*no list sources*");
        ((Action)(() => ValidationReport.Of(Result(1, "{ \"exitCode\": 1 }")))).Should().Throw<CommandAssertionException>().WithMessage("*no boolean field success*");
        ValidationReport.Of(Result(2, "{ \"exitCode\": 2 }")).Success.Should().BeFalse("a usage error has no field success");
        ExtensionPlatform.Of("linux", System.Runtime.InteropServices.Architecture.X64, "alpine.3.20-x64").Should().Be("linux-musl-x64");
        var oneString = () => Cli(workspace).RunCommandLineAsync("--version", new CliRunOptions { WithoutDynamicCode = true });
        await oneString.Should().ThrowAsync<NotSupportedException>();
    }

    [Test]
    public void ReportPathsHaveOneFormOnEachPlatform()
    {
        ReportPath.Normalize(@"C:\work\app\..\morphir.toml").Should().Be("C:/work/morphir.toml");
        ReportPath.Normalize("/work/./app//src/").Should().Be("/work/app/src");
        ReportPath.Join("/work/", "app/morphir.toml").Should().Be("/work/app/morphir.toml");
        ReportPath.Contains("/work/app", "/work/app/src/a.toml").Should().BeTrue();
        ReportPath.Contains("/work/app", "/work/application/a.toml").Should().BeFalse();
        ReportPath.Contains("/work/app", "/work/app").Should().BeTrue();
    }
}
