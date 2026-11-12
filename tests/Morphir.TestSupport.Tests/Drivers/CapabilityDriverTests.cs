// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The drivers of the <c>ir verify</c> and <c>generate</c> commands and their report types (design DRV-1 and
/// ASRT-1 of the change consolidate-black-box-tests). The command line tests use a recording CLI driver; the report
/// tests run the real commands of the development build.
/// </summary>
[Category("PlatformSpecific")]
public class CapabilityDriverTests
{
    private static ScenarioWorkspace Workspace() =>
        ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static CliDriver Cli(IScenarioWorkspace workspace) =>
        new(new TestTarget.DevelopmentBuild(), MorphirHost.Native,
            CliArtifacts.OfDevelopmentBuild(typeof(CapabilityDriverTests).Assembly).PathOf(MorphirHost.Native), workspace);

    private static CommandResult Result(int exitCode, string output) =>
        new("morphir", exitCode, output, "", TimeSpan.Zero, []);

    private sealed class RecordingCli : ICliDriver
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public TestTarget Target { get; } = new TestTarget.DevelopmentBuild();

        public MorphirHost Host => MorphirHost.Native;

        public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
        {
            Commands.Add(arguments);
            return Task.FromResult(Result(0, ""));
        }

        public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default) =>
            throw new NotSupportedException("A capability driver gives each argument apart.");
    }

    [Test]
    public async Task VerifyBuildsTheCommandLineWithThePathAsOneArgument()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var model = workspace.File("my models", "valid ir.json");

        await new ModelVerificationDriver(cli).VerifyAsync(model);
        await new ModelVerificationDriver(cli).VerifyAsync(model, new VerificationOptions { SchemaVersion = 3, Json = true });
        await new ModelVerificationDriver(cli).VerifyAsync(model, new VerificationOptions { Quiet = true });

        cli.Commands[0].Should().Equal("ir", "verify", model.Path);
        cli.Commands[1].Should().Equal("ir", "verify", model.Path, "--schema-version", "3", "--json");
        cli.Commands[2].Should().Equal("ir", "verify", model.Path, "--quiet");
        model.Path.Should().Contain(" ");
    }

    [Test]
    public async Task GenerateBuildsTheCommandLineWithEachPathAndEachOptionAsOneArgument()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var model = workspace.File("my models", "car rental.json");
        var output = workspace.Directory("generated code");

        await new GenerationDriver(cli).GenerateAsync(model, "csharp", output);
        await new GenerationDriver(cli).GenerateAsync(model, "csharp", output, new GenerationOptions
        {
            Json = false,
            InputFormat = "json",
            Options = ["namespace=Pricing.Generated", "language-version=14"],
        });

        await new GenerationDriver(cli).GenerateAsync(workspace.Directory("my models", "a tree"), "csharp", output);

        cli.Commands[2].Should().Equal("generate", "--input", workspace.Directory("my models", "a tree").Path, "--target", "csharp", "--output", output.Path, "--json");
        cli.Commands[0].Should().Equal("generate", "--input", model.Path, "--target", "csharp", "--output", output.Path, "--json");
        cli.Commands[1].Should().Equal(
            "generate", "--input", model.Path, "--target", "csharp", "--output", output.Path,
            "--input-format", "json", "--option", "namespace=Pricing.Generated", "--option", "language-version=14");
    }

    [Test]
    public async Task ARealVerificationReportOfAValidModelReadsIsValid()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/valid-ir-v3.json", "my models/valid ir.json");

        var result = await new ModelVerificationDriver(Cli(workspace)).VerifyAsync(model, new VerificationOptions { Json = true });
        var report = VerificationReport.Of(result);

        result.ShouldExitWith(0);
        report.IsValid.Should().BeTrue();
        report.SchemaVersion.Should().Be("3");
        report.DetectionMethod.Should().Be("auto");
        report.Errors.Should().BeEmpty();
        report.ShouldBeValid();
        ((Action)(() => report.ShouldBeInvalid())).Should().Throw<CommandAssertionException>().WithMessage("*IsValid is true*");
    }

    [Test]
    public async Task ARealVerificationReportOfAnInvalidModelHasItsErrors()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/invalid-missing-formatversion.json");

        var result = await new ModelVerificationDriver(Cli(workspace)).VerifyAsync(model, new VerificationOptions { Json = true });
        var report = VerificationReport.Of(result);

        result.ShouldExitWith(1);
        report.IsValid.Should().BeFalse();
        report.Errors.Should().ContainSingle().Which.Message.Should().Contain("formatVersion");
        report.ShouldBeInvalid();
        ((Action)(() => report.ShouldBeValid())).Should().Throw<CommandAssertionException>()
            .WithMessage("*IsValid is false*").WithMessage("*formatVersion*").WithMessage("*command: *");
    }

    [Test]
    public void AVerificationReportWithNoIsValidFieldFails()
    {
        var read = () => VerificationReport.Of(Result(0, "{ \"success\": true }"));

        read.Should().Throw<CommandAssertionException>().WithMessage("*IsValid*");
    }

    [Test]
    public async Task ARealGenerationReportReadsSuccessAndTheFilesOfTheRun()
    {
        await using var workspace = Workspace();
        var model = workspace.CopyFixture("Cli/car-rental-v3.json", "my models/car rental.json");
        var output = workspace.Directory("generated code");

        var result = await new GenerationDriver(Cli(workspace)).GenerateAsync(model, "csharp", output);
        var report = GenerationReport.Of(result);

        report.Success.Should().BeTrue();
        report.Backend.Should().Be("morphir.csharp");
        report.SourceVersion.Should().Be("3");
        report.FailureKind.Should().BeNull();
        report.Artifacts.Should().Contain("p_Car.Rental/m_BillingModule.g.cs");
        report.Written.Should().Equal(report.Artifacts);
        report.Unchanged.Should().BeEmpty();
        report.Removed.Should().BeEmpty();
        report.Diagnostics.Should().BeEmpty();
        report.ShouldSucceed();
        result.ShouldHaveWritten("generated code/p_Car.Rental/m_BillingModule.g.cs");
        ((Action)(() => report.ShouldFail("semantic"))).Should().Throw<CommandAssertionException>().WithMessage("*success is true*");
    }

    [Test]
    public async Task ARealGenerationReportOfAMissingInputHasItsFailureKindAndItsDiagnostics()
    {
        await using var workspace = Workspace();

        var result = await new GenerationDriver(Cli(workspace)).GenerateAsync(workspace.File("none.json"), "csharp", workspace.Directory("out"));
        var report = GenerationReport.Of(result);

        result.ShouldExitWith(2);
        report.Success.Should().BeFalse();
        report.FailureKind.Should().Be("operational");
        report.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("input-read");
        report.ShouldFail("operational");
        ((Action)(() => report.ShouldSucceed())).Should().Throw<CommandAssertionException>()
            .WithMessage("*success is false*").WithMessage("*input-read*");
        ((Action)(() => report.ShouldFail("semantic"))).Should().Throw<CommandAssertionException>().WithMessage("*operational*");
    }

    [Test]
    [Arguments(1, "{ \"success\": true, \"exitCode\": 0 }", "success is true, but the exit code is 1")]
    [Arguments(0, "{ \"success\": false, \"exitCode\": 0 }", "success is false, but the exit code is 0")]
    [Arguments(2, "{ \"success\": false, \"exitCode\": 1 }", "exitCode is 1, but the exit code is 2")]
    public void AGenerationReportThatDisagreesWithTheExitCodeFails(int exitCode, string output, string reason)
    {
        var read = () => GenerationReport.Of(Result(exitCode, output));

        read.Should().Throw<CommandAssertionException>().WithMessage($"*{reason}*");
    }

    [Test]
    public void AGenerationReportWithNoSuccessFieldFails()
    {
        var read = () => GenerationReport.Of(Result(0, "{ \"IsValid\": true }"));

        read.Should().Throw<CommandAssertionException>().WithMessage("*success*");
    }

    [Test]
    public void AReportIsReadOnlyFromOneJsonDocument()
    {
        var read = () => GenerationReport.Of(Result(0, "[12:00 INF] log\n{ \"success\": true, \"exitCode\": 0 }"));

        read.Should().Throw<CommandAssertionException>().WithMessage("*not exactly one JSON document*");
    }
}
