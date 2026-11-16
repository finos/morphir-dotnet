// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.IR.V4;
using Morphir.MiddleEnd;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Transform;
using Diagnostic = Morphir.Compilation.Diagnostic;
using DiagnosticCode = Morphir.Compilation.DiagnosticCode;
using SourceFormatVersions = Morphir.Extensions.SourceFormatVersions;
using Path = System.IO.Path;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// <c>generate</c> runs the passes of the stage before the backend of its target, on the current IR, and stops before the
/// backend when a pass reports an error or a feature that the backend cannot take remains.
/// </summary>
[Category("PlatformSpecific")]
public sealed class ModelPassesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-passes-" + Guid.NewGuid().ToString("N"));
    public ModelPassesTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private sealed record Options : ValidatedBackendOptions;

    private sealed class RecordingBackend(string target, List<string> log, params BackendCapability[] capabilities) : IGenerationBackend
    {
        public BackendDescriptor Descriptor { get; } =
            new(ExtensionId.Create("test-" + target), ExtensionName.Create("Recording test backend"), TargetId.Create(target), capabilities);

        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new Options());

        public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            log.Add("backend");
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new GenerationArtifacts(VFileTreeModule.empty, [])));
        }
    }

    /// <summary>A pass written in C#: it records its module paths and gives one application when asked.</summary>
    private sealed class RecordingPass(string id, PassStage stage, List<string> log, Func<ModuleUnit, IPackageView, Checked<PassResult>>? run = null) : IModelTransform
    {
        public RecordingPass(string id, PassStage stage, List<string> log, Func<ModuleUnit, Checked<PassResult>> run)
            : this(id, stage, log, (unit, _) => run(unit)) { }

        public string Id => id;
        public string Name => id;
        public string Description => "A test pass.";
        public PassStage Stage => stage;
        public FSharpSet<IrFeature> Removes => SetModule.Empty<IrFeature>();

        public Checked<PassResult> Run(ModuleUnit unit, IPackageView view)
        {
            log.Add($"{id}:{Morphir.IR.CanonicalNames.pathToString(unit.Path)}");
            return run?.Invoke(unit, view) ?? Checked<PassResult>.Pass(new PassResult(unit.Definition, FSharpList<PassApplication>.Empty));
        }
    }

    private static BackendCapability Current() =>
        new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), DistributionKind.Library);

    private static BackendCapability Classic() =>
        new(ModelFamily.Classic, SourceFormatVersions.Create("1", "2", "3", "3.0.0", "3.1.0"), DistributionKind.Library);

    /// <summary>A current model with a type alias of an extensible record, which the C# backend cannot take.</summary>
    private const string OpenRecordModel = """
        {"formatVersion":4,"distribution":{"Library":{"packageName":"car/rental","dependencies":{},"def":{"modules":{
          "inventory":{"access":"Public",
            "types":{"row":{"access":"Public","TypeAliasDefinition":{"typeParams":["r"],
              "typeExp":{"ExtensibleRecord":{"variable":"r","fields":{"price":"morphir/SDK:basics#int"}}}}}},
            "values":{"daily-rate":{"access":"Public","ExpressionBody":{"inputTypes":{},"outputType":"morphir/SDK:basics#int","body":{"Literal":{"WholeNumberLiteral": 5000}}}}}}
        }}}}}
        """;

    private async Task<Outcome<GenerationSuccess>> Run(string version, string target, IGenerationBackend backend, ModelPasses passes, string? json = null)
    {
        var input = GenerationInputFixtures.DailyRate(Path.Combine(_root, Guid.NewGuid().ToString("N")), version, "json");
        if (json is not null) File.WriteAllText(input, json);
        var loader = new GenerationModelLoader(new BoundedModelCapture());
        var resolution = await new CliGenerationSettingsResolver().ResolveAsync(new(input, target, Path.Combine(_root, "out-" + Guid.NewGuid().ToString("N"))), new(_root), default);
        var settings = ((Outcome<GenerationSettings>.Success)resolution).Value;
        var loaded = ((Outcome<LoadedModel>.Success)await loader.LoadAsync(settings.Input, settings.Limits, default)).Value;
        return await GenerationStages.RunFrom(loaded, settings, new RegisteredBackendResolver([backend]), new GenerationPublisher(), default, null, passes);
    }

    private static ModelPasses Passes(params IModelTransform[] transforms) =>
        new(transforms, new Dictionary<string, ImmutableArray<IrFeature>>());

    [Test]
    public async Task ThePassesOfTheTargetRunBeforeTheBackendAndThePassesOfAnotherTargetDoNot()
    {
        var log = new List<string>();
        var pass = new RecordingPass("csharp-pass", PassStage.NewBeforeBackend("current-only"), log);
        var other = new RecordingPass("other-pass", PassStage.NewBeforeBackend("fsharp"), log);
        var result = await Run("4.0.0", "current-only", new RecordingBackend("current-only", log, Current()), Passes(pass, other));

        result.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        log.Should().NotBeEmpty();
        log.Last().Should().Be("backend");
        log.Where(entry => entry != "backend").Should().OnlyContain(entry => entry.StartsWith("csharp-pass:"));
        ((Outcome<GenerationSuccess>.Success)result).Value.Passes!.Ran.Should().Equal("csharp-pass");
    }

    [Test]
    public async Task ThePassesRunOnTheUpgradedModelOfAClassicInput()
    {
        var log = new List<string>();
        var pass = new RecordingPass("csharp-pass", PassStage.NewBeforeBackend("current-only"), log);
        var result = await Run("3.0.0", "current-only", new RecordingBackend("current-only", log, Current()), Passes(pass));

        result.Should().BeOfType<Outcome<GenerationSuccess>.Success>();
        log.Should().Contain(entry => entry.StartsWith("csharp-pass:"));
    }

    [Test]
    public async Task APassErrorStopsBeforeTheBackendAndPublishesNothing()
    {
        var log = new List<string>();
        var failing = new RecordingPass("failing", PassStage.NewBeforeBackend("current-only"), log,
            _ => Checked<PassResult>.Fail([Diagnostic.Create(DiagnosticSeverity.Error, DiagnosticCode.Create("test.failed"), "The pass failed.", null)]));
        var result = await Run("4.0.0", "current-only", new RecordingBackend("current-only", log, Current()), Passes(failing));

        var failure = result.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Subject;
        failure.Error.Kind.Should().Be(GenerationFailureKind.Semantic);
        failure.Error.Diagnostics.Select(d => d.Code).Should().Contain("test.failed");
        log.Should().NotContain("backend");
    }

    [Test]
    public async Task ThePassesOfAClassicModelAreSkippedWithOneInfoDiagnostic()
    {
        var log = new List<string>();
        var pass = new RecordingPass("classic-pass", PassStage.NewBeforeBackend("classic"), log);
        var result = await Run("3.0.0", "classic", new RecordingBackend("classic", log, Classic()), Passes(pass));

        var success = ((Outcome<GenerationSuccess>.Success)result).Value;
        log.Should().Equal("backend");
        var notes = success.Diagnostics.Where(d => d.Code == "transform.skipped").ToArray();
        notes.Should().ContainSingle();
        notes[0].Severity.Should().Be("info");
        notes[0].Message.Should().Contain("Classic");
        success.Passes.Should().BeNull();
    }

    [Test]
    public async Task NoPassForTheStageGivesNoDiagnosticAndNoReport()
    {
        var log = new List<string>();
        var result = await Run("3.0.0", "classic", new RecordingBackend("classic", log, Classic()), Passes());

        var success = ((Outcome<GenerationSuccess>.Success)result).Value;
        success.Diagnostics.Should().NotContain(d => d.Code == "transform.skipped");
        success.Passes.Should().BeNull();
    }

    [Test]
    public async Task AFeatureThatTheBackendCannotTakeAndNoPassRemovedFailsBeforeTheBackendAndNamesIt()
    {
        var log = new List<string>();
        var profiles = new Dictionary<string, ImmutableArray<IrFeature>>
        {
            ["current-only"] = [IrFeature.NewValue(ValueKind.Literal)],
        };
        var result = await Run("4.0.0", "current-only", new RecordingBackend("current-only", log, Current()), new ModelPasses([], profiles));

        var failure = result.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Subject;
        failure.Error.Diagnostics.Should().ContainSingle(d => d.Code == "transform.feature_remains"
            && d.Message.Contains("value.literal") && d.Message.Contains("current-only"));
        log.Should().NotContain("backend");
    }

    [Test]
    public async Task TheBuiltInProfileOfTheCSharpTargetRefusesAnOpenRecordBeforeTheBackend()
    {
        var log = new List<string>();
        var result = await Run("4", "csharp", new RecordingBackend("csharp", log, Current()), new ModelPasses([], BuiltInTransforms.CannotTake), OpenRecordModel);

        var failure = result.Should().BeOfType<Outcome<GenerationSuccess>.Failure>().Subject;
        failure.Error.Diagnostics.Should().ContainSingle(d => d.Code == "transform.feature_remains"
            && d.Message.Contains("type.extensible-record") && d.Message.Contains("csharp") && d.Message.Contains("inventory"));
        log.Should().NotContain("backend");
    }

    [Test]
    public void TheBuiltInProfileOfTheCSharpTargetNamesTheFeaturesThatTheBackendRefuses()
    {
        BuiltInTransforms.CannotTake["csharp"].Select(feature => feature.Text).Should().BeEquivalentTo(
            "type.extensible-record", "value.hole", "body.native", "body.external", "body.incomplete");
    }

    [Test]
    public async Task TheJsonReportListsThePassesTheApplicationsAndTheLookups()
    {
        var log = new List<string>();
        var rule = RuleId.Create("https://example.org/rules/touch");

        var touching = new RecordingPass("touching", PassStage.NewBeforeBackend("current-only"), log, (unit, view) =>
        {
            // A read of another module: the package view records it, also when the package has no such module.
            view.TryValue(Morphir.IR.CanonicalNames.parsePath("orders/tax").ResultValue, Morphir.IR.CanonicalNames.parseName("rate").ResultValue);
            var values = OrderedMap.toList(unit.Definition.Values);
            if (values.IsEmpty) return Checked<PassResult>.Pass(new PassResult(unit.Definition, FSharpList<PassApplication>.Empty));
            var value = values.Head.Item1;
            var place = new NodePosition(NodeRoot.NewValue(NodeOwner.OwnPackage, unit.Path, value), ListModule.OfSeq([NodeStep.Body]));
            var origin = FSharpOption<SourceLocation>.Some(new SourceLocation(3, 4, 3, 9));
            return Checked<PassResult>.Pass(new PassResult(unit.Definition, ListModule.OfSeq([new PassApplication(rule, place, place, origin)])));
        });

        var result = await Run("4.0.0", "current-only", new RecordingBackend("current-only", log, Current()), Passes(touching));
        var report = GenerationReport.From(result, new GenerateOverrides(Target: "current-only")).ToJson();

        using var document = System.Text.Json.JsonDocument.Parse(report);
        var passes = document.RootElement.GetProperty("passes");
        passes.GetProperty("ran")[0].GetString().Should().Be("touching");
        var application = passes.GetProperty("applications")[0];
        application.GetProperty("rule").GetString().Should().Be("https://example.org/rules/touch");
        application.GetProperty("before").GetString().Should().StartWith("morphir://ir/").And.Contain("rev=sha256:");
        application.GetProperty("after").GetString().Should().StartWith("morphir://ir/").And.Contain("rev=sha256:");
        application.GetProperty("origin").GetProperty("startLine").GetInt32().Should().Be(3);
        var lookup = passes.GetProperty("lookups").EnumerateArray().Should().ContainSingle().Subject;
        lookup.GetProperty("pass").GetString().Should().Be("touching");
        lookup.GetProperty("from").GetString().Should().Be("inventory");
        lookup.GetProperty("module").GetString().Should().Be("orders/tax");
        lookup.GetProperty("value").GetString().Should().Be("rate");
    }
}
