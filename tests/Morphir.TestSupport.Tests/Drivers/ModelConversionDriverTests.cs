// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The model-conversion capability (add-model-convert, test-driver-contract): the driver of <c>model convert</c>, its
/// report type and its capability tag. The command line tests use a recording CLI driver. The command does not exist
/// yet, so the report tests read reports in the form that cli-model-conversion gives them.
/// </summary>
public class ModelConversionDriverTests
{
    private const string Converted =
        "{\"success\":true,\"exitCode\":0,\"input\":\"in/morphir-ir.json\",\"output\":\"out/tree\"," +
        "\"inputLayout\":\"single-file\",\"outputLayout\":\"document-tree\",\"inputEncoding\":\"plain\",\"outputEncoding\":\"compacted\"," +
        "\"irVersion\":\"4\",\"compactMode\":\"names\",\"compactPolicy\":\"writer-policy-1\"," +
        "\"filesWritten\":[\"out/tree/manifest.json\",\"out/tree/Acme/module.json\"]," +
        "\"diagnostics\":[]," +
        "\"notices\":[{\"code\":\"tree.manifest_member_ignored\",\"message\":\"The manifest member created is not kept.\",\"severity\":\"info\"}]}";

    private const string Refused =
        "{\"success\":false,\"exitCode\":1,\"input\":\"in/tree\",\"output\":\"out/morphir-ir.json\"," +
        "\"inputLayout\":\"document-tree\",\"outputLayout\":\"single-file\",\"inputEncoding\":\"plain\",\"outputEncoding\":\"plain\"," +
        "\"irVersion\":\"4\",\"filesWritten\":[]," +
        "\"diagnostics\":[{\"code\":\"tree.missing_member\",\"message\":\"The file Acme/rate.type.json is missing.\",\"severity\":\"error\"}],\"notices\":[]}";

    private static CommandResult Result(int exitCode, string output) =>
        new("morphir model convert", exitCode, output, "", TimeSpan.Zero, []);

    private static CommandResult Stream(int exitCode, string report, params string[] events) =>
        Result(exitCode, string.Join("\n", [.. events, "{\"event\":\"completed\",\"report\":" + report + "}"]) + "\n");

    private static ScenarioWorkspace Workspace() =>
        ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

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

    // Scenario: A conversion report

    [Test]
    public void AConversionReport()
    {
        var result = Stream(0, Converted, "{\"event\":\"fileWritten\",\"path\":\"out/tree/manifest.json\"}", "{\"event\":\"fileWritten\",\"path\":\"out/tree/Acme/module.json\"}");

        var report = ConversionReport.Of(result).ShouldSucceed();

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        report.FilesWritten.Should().Equal("out/tree/manifest.json", "out/tree/Acme/module.json");
        report.FileWrittenEvents().Should().Equal(report.FilesWritten);
        report.Input.Should().Be("in/morphir-ir.json");
        report.Output.Should().Be("out/tree");
        report.InputLayout.Should().Be("single-file");
        report.OutputLayout.Should().Be(ModelLayout.DocumentTree.Name()).And.Be("document-tree");
        report.InputEncoding.Should().Be("plain");
        report.OutputEncoding.Should().Be("compacted");
        report.IrVersion.Should().Be("4");
        report.CompactMode.Should().Be("names");
        report.CompactPolicy.Should().Be("writer-policy-1");
        report.Diagnostics.Should().BeEmpty();
        report.Notices.Should().Equal(new ReportDiagnostic("tree.manifest_member_ignored", "The manifest member created is not kept.", "info", null));
        report.ShouldHaveNotice("tree.manifest_member_ignored");
    }

    [Test]
    public void AConversionReportIsReadFromTheDocumentAsFromTheStream()
    {
        var report = ConversionReport.Of(Result(0, Converted));

        report.Success.Should().BeTrue();
        report.FilesWritten.Should().HaveCount(2);
        var events = () => report.FileWrittenEvents();
        events.Should().Throw<CommandAssertionException>("a document is not a stream of events");
    }

    [Test]
    public void AConversionReportOfARefusalReadsItsDiagnosticAndNoCompaction()
    {
        var report = ConversionReport.Of(Stream(1, Refused));

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.FilesWritten.Should().BeEmpty();
        report.CompactMode.Should().BeNull();
        report.CompactPolicy.Should().BeNull();
        report.ShouldHaveDiagnostic("tree.missing_member");
        var succeed = () => report.ShouldSucceed();
        succeed.Should().Throw<CommandAssertionException>().WithMessage("*success is false*tree.missing_member*");
        var notice = () => report.ShouldHaveNotice("tree.manifest_member_ignored");
        notice.Should().Throw<CommandAssertionException>().WithMessage("*no notice tree.manifest_member_ignored*");
    }

    [Test]
    [Arguments(1, "success is true, but the exit code is 1")]
    [Arguments(2, "success is true, but the exit code is 2")]
    public void AConversionReportWhoseSuccessDisagreesWithTheExitCodeFails(int exitCode, string reason)
    {
        var report = Converted.Replace("\"exitCode\":0", $"\"exitCode\":{exitCode}", StringComparison.Ordinal);

        var read = () => ConversionReport.Of(Stream(exitCode, report));

        read.Should().Throw<CommandAssertionException>().WithMessage($"*{reason}*");
    }

    [Test]
    public void AConversionReportWithNoSuccessIsAReportOfAUsageErrorOnly()
    {
        const string usage = "{\"exitCode\":2,\"diagnostics\":[{\"code\":\"cli.usage\",\"message\":\"Use --compact=names.\"}]}";

        var report = ConversionReport.Of(Stream(2, usage));
        var failure = () => ConversionReport.Of(Stream(1, usage.Replace("\"exitCode\":2", "\"exitCode\":1", StringComparison.Ordinal)));

        report.Success.Should().BeFalse();
        report.FilesWritten.Should().BeEmpty();
        report.ShouldHaveDiagnosticThatNames("--compact=names");
        failure.Should().Throw<CommandAssertionException>().WithMessage("*no boolean field success*");
    }

    [Test]
    [Arguments("\"exitCode\":0,", "", "no field exitCode")]
    [Arguments("\"filesWritten\":[\"out/tree/manifest.json\",\"out/tree/Acme/module.json\"],", "", "no list filesWritten")]
    [Arguments("\"notices\":[", "\"notices\":7,\"other\":[", "no list notices")]
    public void AConversionReportWithoutAFieldOfItsCommandFails(string field, string replacement, string reason)
    {
        var read = () => ConversionReport.Of(Stream(0, Converted.Replace(field, replacement, StringComparison.Ordinal)));

        read.Should().Throw<CommandAssertionException>().WithMessage($"*{reason}*");
    }

    // Scenario: A feature file states intents only

    [Test]
    [Category("PlatformSpecific")]
    public async Task AFeatureFileStatesIntentsOnly()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var driver = new ModelConversionDriver(cli);
        var model = workspace.File("my models", "daily rate.json");
        var tree = workspace.Directory("my trees", "daily rate");

        await driver.ConvertAsync(model, tree, new ConversionOptions { Layout = ModelLayout.DocumentTree, Compact = CompactionArgument.Default });
        await driver.ConvertAsync(tree, model, new ConversionOptions { Layout = ModelLayout.SingleFile, Json = JsonOutputArgument.Document });
        await driver.ConvertAsync(model, model, new ConversionOptions { Compact = CompactionArgument.WithMode("names") });
        await driver.ConvertAsync(tree, tree);
        await driver.ConvertAsync(model, tree, new ConversionOptions { Json = null });

        cli.Commands.Should().BeEquivalentTo(
            new IReadOnlyList<string>[]
            {
                ["model", "convert", "--input", model.Path, "--output", tree.Path, "--layout=document-tree", "--compact", "--json"],
                ["model", "convert", "--input", tree.Path, "--output", model.Path, "--layout=single-file", "--json=document"],
                ["model", "convert", "--input", model.Path, "--output", model.Path, "--compact=names", "--json"],
                ["model", "convert", "--input", tree.Path, "--output", tree.Path, "--json"],
                ["model", "convert", "--input", model.Path, "--output", tree.Path],
            },
            options => options.WithStrictOrdering());
        model.Path.Should().Contain(" ");
        tree.Path.Should().Contain(" ");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task TheDriverGivesTheFormsThatATestOfTheConventionNeeds()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var driver = new ModelConversionDriver(cli);
        var model = workspace.File("model.json");
        var tree = workspace.Directory("tree");

        await driver.ConvertAsync(model, tree, new ConversionOptions { Compact = CompactionArgument.WithMode("bogus") });
        await driver.ConvertAsync(model, tree, new ConversionOptions { Compact = CompactionArgument.Separated("names") });
        await driver.ConvertAsync(model, tree, new ConversionOptions { Arguments = ["--no-such-option"], Json = JsonOutputArgument.Stream.Then("--last") });

        cli.Commands.Should().BeEquivalentTo(
            new IReadOnlyList<string>[]
            {
                ["model", "convert", "--input", model.Path, "--output", tree.Path, "--compact=bogus", "--json"],
                // A mode after a space, which the command refuses with a hint.
                ["model", "convert", "--input", model.Path, "--output", tree.Path, "--compact", "names", "--json"],
                ["model", "convert", "--input", model.Path, "--output", tree.Path, "--no-such-option", "--json", "--last"],
            },
            options => options.WithStrictOrdering());
        CompactionArgument.Separated("names").ToString().Should().Be("--compact names");
    }

    [Test]
    public void EachLayoutHasTheNameOfTheCommand()
    {
        ModelLayout.SingleFile.Name().Should().Be("single-file");
        ModelLayout.DocumentTree.Name().Should().Be("document-tree");
    }

    // Scenario: A target without the capability skips the test

    [Test]
    public void ATargetWithoutTheCapabilitySkipsTheTest()
    {
        var target = new TestTarget.DevelopmentBuild();
        var needs = TestNeeds.FromTags(["integration", "needs-model-conversion"]);
        var without = new RunProfile(TestLane.None, target, MorphirHost.Native,
            host => DriverCapabilities.Of(target, host).Remove(DriverCapability.ModelConversion));

        var skipped = TestEligibility.Decide(needs, without).Should().BeOfType<Eligibility.Skipped>().Subject;

        needs.Capabilities.Should().Equal(DriverCapability.ModelConversion);
        skipped.Kind.Should().Be(SkipKind.Capability);
        skipped.Reason.Should().Be("capability: ModelConversion on target dev, host native");
        TestEligibility.Decide(needs, RunProfile.Of(TestRunSettings.Parse(_ => null, _ => false)))
            .Should().BeOfType<Eligibility.Eligible>("the development build supports model conversion");
    }

    [Test]
    public void EachHostOfEachTargetThatRunsCommandsSupportsModelConversion()
    {
        DriverCapabilities.TagOf(DriverCapability.ModelConversion).Should().Be("needs-model-conversion");
        foreach (var target in new TestTarget[] { new TestTarget.DevelopmentBuild(), new TestTarget.NativeAot(), new TestTarget.Packaged(PackagedOffering.SelfContained) })
        {
            foreach (var host in target.Hosts)
                DriverCapabilities.Of(target, host).Should().Contain([DriverCapability.ModelConversion, DriverCapability.ModelVerification]);
        }
    }
}
