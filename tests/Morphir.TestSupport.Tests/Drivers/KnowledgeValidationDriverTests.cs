// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

[Category("PlatformSpecific")]
public sealed class KnowledgeValidationDriverTests
{
    [Test]
    public async Task KnowledgeValidationPassesTheBundleAsOneArgument()
    {
        await using var workspace = ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        var cli = new RecordingCli();
        var bundle = workspace.Directory("shared knowledge");

        await new KnowledgeValidationDriver(cli).ValidateAsync(bundle,
            new KnowledgeValidationOptions { Version = "0.2", MorphirPolicy = true, Json = JsonOutputArgument.Document });

        cli.Arguments.Should().Equal("knowledge", "validate", bundle.Path, "--okf-version", "0.2",
            "--policy", "morphir", "--json=document");
    }

    [Test]
    public async Task ARealKnowledgeReportIdentifiesTheCheckedVersion()
    {
        await using var workspace = ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        var bundle = KnowledgeBundleMother.Write(workspace, KnowledgeBundleMother.ProjectDocumentation);
        var host = MorphirHost.Native;
        var artifact = CliArtifacts.OfDevelopmentBuild(typeof(KnowledgeValidationDriverTests).Assembly).PathOf(host);
        var cli = new CliDriver(new TestTarget.DevelopmentBuild(), host, artifact, workspace);
        var result = await new KnowledgeValidationDriver(cli).ValidateAsync(bundle,
            new KnowledgeValidationOptions { Version = "0.2", Json = JsonOutputArgument.Stream });
        var report = KnowledgeValidationReport.Of(result.ShouldExitWith(1));

        report.Version.Should().Be("0.2");
        report.Conformance.Should().Be("non-conformant");
        report.ShouldHaveDiagnostic("okf.frontmatter-required");
    }

    private sealed class RecordingCli : ICliDriver
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public TestTarget Target { get; } = new TestTarget.DevelopmentBuild();
        public MorphirHost Host => MorphirHost.Native;

        public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null,
            CancellationToken cancellation = default)
        {
            Arguments = arguments;
            return Task.FromResult(new CommandResult("morphir", 0, "", "", TimeSpan.Zero, []));
        }

        public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null,
            CancellationToken cancellation = default) => throw new NotSupportedException();
    }
}
