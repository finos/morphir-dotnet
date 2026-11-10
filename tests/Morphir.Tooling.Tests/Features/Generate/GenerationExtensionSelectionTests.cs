// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using FluentAssertions;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Frontend.FSharp;
using Morphir.IR.Pipeline;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Scenarios;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Features.TaskRecords;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// Backend selection for <c>generate</c>, as <c>compile</c> selects a frontend: <c>--extension</c>, else
/// <c>[codegen.&lt;target&gt;].extension</c> of the project configuration; with neither, the registered built-in
/// backend of the target runs and the report keeps its members.
/// </summary>
public sealed class GenerationExtensionSelectionTests
{
    private static RecordGenerationSettingsResolver Resolver() =>
        new(new CliGenerationSettingsResolver(), new FileSystemCompileConfigurationLoader(),
            new TaskStoreOptions { Diagnostics = TextWriter.Null, PollInterval = TimeSpan.FromMilliseconds(10) });

    private static InvocationContext Invocation(CompileWorkspace workspace) =>
        new(workspace.Root) { Environment = workspace.Context().Environment };

    private static CompileWorkspace Workspace(string? configuration = null)
    {
        var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"car/rental\"\n" + (configuration ?? ""));
        File.Copy(Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirIR", "daily-rate-v4.json"), workspace.PathOf("model.json"));
        return workspace;
    }

    private static GenerateOverrides Explicit(CompileWorkspace workspace, string? extension = null) =>
        new(workspace.PathOf("model.json"), "fsharp", workspace.PathOf("gen")) { Extension = extension, Project = workspace.Root };

    private static async Task<GenerationSettings> Settings(CompileWorkspace workspace, GenerateOverrides overrides)
    {
        var resolved = await Resolver().ResolveAsync(overrides, Invocation(workspace), default);
        resolved.Should().BeOfType<Outcome<GenerationSettings>.Success>(resolved.ToString());
        return ((Outcome<GenerationSettings>.Success)resolved).Value;
    }

    [Test]
    public async Task WithoutASelectionTheSettingsNameNoExtension()
    {
        using var workspace = Workspace();
        (await Settings(workspace, Explicit(workspace))).Extension.Should().BeNull();
    }

    [Test]
    public async Task TheExtensionOptionSelectsAnExtensionFromTheCommandLine()
    {
        using var workspace = Workspace();
        var extension = (await Settings(workspace, Explicit(workspace, "backend-guest"))).Extension;
        extension.Should().NotBeNull();
        extension!.Value.Should().Be("backend-guest");
        extension.Origin.Should().Be(new SettingOrigin.CommandLine("--extension"));
    }

    [Test]
    public async Task TheConfigurationKeyOfTheTargetSelectsAnExtension()
    {
        using var workspace = Workspace("\n[codegen.fsharp]\nextension = \"backend-guest\"\n");
        var extension = (await Settings(workspace, Explicit(workspace))).Extension;
        extension.Should().NotBeNull();
        extension!.Value.Should().Be("backend-guest");
        extension.Origin.Should().BeOfType<SettingOrigin.Configuration>();
        ((SettingOrigin.Configuration)extension.Origin).Key.Should().Be("codegen.fsharp.extension");
    }

    [Test]
    public async Task TheExtensionOptionOverridesTheConfigurationKey()
    {
        using var workspace = Workspace("\n[codegen.fsharp]\nextension = \"configured-guest\"\n");
        var extension = (await Settings(workspace, Explicit(workspace, "command-line-guest"))).Extension;
        extension!.Value.Should().Be("command-line-guest");
        extension.Origin.Should().Be(new SettingOrigin.CommandLine("--extension"));
    }

    [Test]
    public async Task TheKeyOfAnotherTargetIsNotRead()
    {
        using var workspace = Workspace("\n[codegen.csharp]\nextension = \"csharp-guest\"\n");
        (await Settings(workspace, Explicit(workspace))).Extension.Should().BeNull();
    }

    [Test]
    public async Task AnEmptyExtensionIsAnInvocationFailure()
    {
        using var workspace = Workspace();
        var resolved = await Resolver().ResolveAsync(Explicit(workspace, " "), Invocation(workspace), default);
        var failure = resolved.Should().BeOfType<Outcome<GenerationSettings>.Failure>().Subject.Error;
        failure.Kind.Should().Be(GenerationFailureKind.Invocation);
        failure.Diagnostics.Single().Code.Should().Be("invalid-extension");
        failure.Diagnostics.Single().Message.Should().Contain("--extension");
    }

    [Test]
    public async Task WithoutAnInputTheKeyIsReadFromTheProjectOfTheCompileRecord()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("morphir.toml", "[project]\nname = \"acme/car-rental\"\n\n[frontend]\nlanguage = \"fsharp\"\n\n[codegen.fsharp]\nextension = \"backend-guest\"\n");
        workspace.Write("src/Rates.fs", CompileRunnerTests.Rates);
        workspace.Write("src/Pricing.fs", CompileRunnerTests.Pricing);
        var compiled = CompileHandler.Fold(await CompileRunners.Events(CompileRunners.Create([FSharpFrontend.Registration]),
            new CompileOverrides { Inputs = ["src/Rates.fs", "src/Pricing.fs"] }, workspace.Context()));
        compiled.ExitCode.Should().Be(0);

        var resolved = await Resolver().ResolveAsync(new GenerateOverrides(Target: "fsharp", Output: "gen"), Invocation(workspace), default);
        var settings = resolved.Should().BeOfType<Outcome<GenerationSettings>.Success>().Subject.Value;
        using var lease = settings.InputLease;
        settings.Extension!.Value.Should().Be("backend-guest");
    }

    /// <summary>A connected external backend that records the request and returns one file.</summary>
    private sealed class StubExternal(string id = "org.example.stub-backend") : IExternalGenerationBackend
    {
        public bool Generated { get; private set; }
        public bool Disposed { get; private set; }
        public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create(id), ExtensionName.Create("Stub backend"), TargetId.Create("fsharp"),
            [new(Morphir.IR.Model.ModelFamily.V4, Morphir.Extensions.SourceFormatVersions.Create("4", "4.0.0"), Morphir.IR.Model.DistributionKind.Library)]);
        public string ExtensionVersion => "9.9.9";
        public string Transport => "mep";
        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new Validated());
        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            Generated = true;
            var tree = VFileTreeModule.fromFileMap(Microsoft.FSharp.Collections.MapModule.OfSeq([Tuple.Create("Stub.fs", VFileModule.ofText("Stub.fs", "module Stub"))]));
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new(tree, [])));
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
        private sealed record Validated : ValidatedBackendOptions;
    }

    /// <summary>A connector that records its requests and returns the stub, or a failure.</summary>
    private sealed class StubConnector(Outcome<IExternalGenerationBackend> outcome) : IBackendExtensionConnector
    {
        public List<BackendExtensionRequest> Requests { get; } = [];
        public Task<Outcome<IExternalGenerationBackend>> ConnectAsync(BackendExtensionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(outcome);
        }
    }

    private static Task<Outcome<GenerationSuccess>> Handle(CompileWorkspace workspace, GenerateOverrides overrides, IBackendExtensionConnector connector) =>
        GenerateHandler.Handle(new GenerateRequest(overrides, Invocation(workspace)), Resolver(), new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), connector, [new FSharpGenerationBackend()], default);

    [Test]
    public async Task ASelectedExtensionRunsInsteadOfTheBuiltInAndTheResultNamesIt()
    {
        using var workspace = Workspace();
        var stub = new StubExternal();
        var connector = new StubConnector(new Outcome<IExternalGenerationBackend>.Success(stub));

        var outcome = await Handle(workspace, Explicit(workspace, "stub-guest"), connector);
        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Subject.Value;
        success.Backend.Should().Be("org.example.stub-backend");
        success.BackendTransport.Should().Be("mep");
        success.BackendVersion.Should().Be("9.9.9");
        success.Publication.Artifacts.Should().Equal("Stub.fs");
        stub.Generated.Should().BeTrue();
        stub.Disposed.Should().BeTrue("the run ends the extension's session");
        connector.Requests.Single().Extension.Value.Should().Be("stub-guest");
        connector.Requests.Single().Target.Value.Should().Be("fsharp");
        connector.Requests.Single().WorkingDirectory.Should().Be(workspace.Root);

        var report = GenerationReport.From(outcome, Explicit(workspace, "stub-guest"));
        report.BackendTransport.Should().Be("mep");
        report.ToJson().Should().Contain("\"backendTransport\": \"mep\"").And.Contain("\"backendVersion\": \"9.9.9\"");
    }

    [Test]
    public async Task WithoutASelectionTheBuiltInRunsTheConnectorIsNotCalledAndTheReportKeepsItsMembers()
    {
        using var workspace = Workspace();
        var connector = new StubConnector(new Outcome<IExternalGenerationBackend>.Success(new StubExternal()));

        var outcome = await Handle(workspace, Explicit(workspace), connector);
        var success = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Subject.Value;
        success.Backend.Should().Be("morphir.fsharp");
        success.BackendTransport.Should().BeNull();
        connector.Requests.Should().BeEmpty();

        using var json = System.Text.Json.JsonDocument.Parse(GenerationReport.From(outcome, Explicit(workspace)).ToJson());
        json.RootElement.EnumerateObject().Select(member => member.Name).Should().Equal(
            "success", "exitCode", "target", "backend", "input", "output", "sourceVersion", "family", "distributionKind",
            "representation", "artifacts", "written", "unchanged", "removed", "diagnostics", "failureKind");
    }

    [Test]
    public async Task AConnectionFailureIsTheGenerationFailureAndPublishesNothing()
    {
        using var workspace = Workspace();
        var failure = GenerationFailure.One(GenerationFailureKind.Operational, "extension.unavailable", "Extension no-such-guest did not start.");
        var connector = new StubConnector(new Outcome<IExternalGenerationBackend>.Failure(failure));

        var outcome = await Handle(workspace, Explicit(workspace, "no-such-guest"), connector);
        var error = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Subject.Error;
        error.Diagnostics.Single().Code.Should().Be("extension.unavailable");
        GenerationReport.From(outcome, Explicit(workspace, "no-such-guest")).ExitCode.Should().Be(2);
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
    }

    [Test]
    public async Task AnExtensionThatGivesTheIdOfABuiltInIsRefusedAndNothingIsGenerated()
    {
        using var workspace = Workspace();
        var stub = new StubExternal("morphir.fsharp");
        var connector = new StubConnector(new Outcome<IExternalGenerationBackend>.Success(stub));

        var outcome = await Handle(workspace, Explicit(workspace, "impostor-guest"), connector);
        var error = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>(outcome.ToString()).Subject.Error;
        error.Diagnostics.Single().Code.Should().Be("extension.unavailable");
        error.Diagnostics.Single().Message.Should().Contain("morphir.fsharp").And.Contain("built-in");
        stub.Generated.Should().BeFalse();
        stub.Disposed.Should().BeTrue("the refused extension's session ends");
        Directory.Exists(workspace.PathOf("gen")).Should().BeFalse();
    }

    [Test]
    public async Task TheMediatorRunsTheHandlerWithTheRegisteredBuiltInsSoAnImpostorIsRefused()
    {
        using var workspace = Workspace();
        var stub = new StubExternal("morphir.fsharp");
        using var app = MorphirCli.CreateBuilder([])
            .ConfigureServices(services => services.AddSingleton<IBackendExtensionConnector>(
                new StubConnector(new Outcome<IExternalGenerationBackend>.Success(stub))))
            .AddScenario<GenerationScenario>()
            .Build();
        var mediator = app.Services.GetRequiredService<IMediator>();

        var outcome = await mediator.InvokeAsync<Outcome<GenerationSuccess>>(new GenerateRequest(Explicit(workspace, "impostor-guest"), Invocation(workspace)));
        var error = outcome.Should().BeOfType<Outcome<GenerationSuccess>.Failure>(outcome.ToString()).Subject.Error;
        error.Diagnostics.Single().Message.Should().Contain("built-in");
        stub.Generated.Should().BeFalse();
    }
}
