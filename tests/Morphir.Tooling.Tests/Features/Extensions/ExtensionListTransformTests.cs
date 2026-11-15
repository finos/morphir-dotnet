// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.MiddleEnd;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Transform;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// A built-in pass is a <c>transform</c> extension entry with its stage. The stage is a morphir-dotnet report member:
/// the MEP extension record of the entry has no stage.
/// </summary>
public sealed class ExtensionListTransformTests
{
    private static readonly HostIdentity Native = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    private sealed class Pass(string id, PassStage stage) : IModelTransform
    {
        public string Id => id;
        public string Name => id + " pass";
        public string Description => "A test pass.";
        public PassStage Stage => stage;
        public FSharpSet<IrFeature> Removes => SetModule.Empty<IrFeature>();
        public Checked<PassResult> Run(ModuleUnit unit, IPackageView view) =>
            Checked<PassResult>.Pass(new PassResult(unit.Definition, FSharpList<PassApplication>.Empty));
    }

    private static ExtensionListReport Report(params IModelTransform[] passes)
    {
        var source = new TransformExtensionSource(Native, passes);
        ExtensionCatalog.TryCreate(Native, source.Entries, out var catalog, out var error).Should().BeTrue();
        error.Should().BeNull();
        return ExtensionListReport.From(catalog!, () => WasmRuntimeAvailability.Available);
    }

    [Test]
    public void EachPassIsATransformEntryWithItsStageAndItsTarget()
    {
        var report = Report(new Pass("morphir.csharp.pass", PassStage.NewBeforeBackend("csharp")), new Pass("morphir.prune", PassStage.AfterFrontend));
        using var json = JsonDocument.Parse(report.ToJson());
        var entries = json.RootElement.GetProperty("extensions").EnumerateArray().ToDictionary(entry => entry.GetProperty("id").GetString()!);

        var target = entries["morphir.csharp.pass"];
        target.GetProperty("types").EnumerateArray().Select(type => type.GetString()).Should().Equal("transform");
        target.GetProperty("delivery").GetString().Should().Be("built-in");
        var claim = target.GetProperty("capabilities").GetProperty("transform");
        claim.GetProperty("stage").GetString().Should().Be("before-backend");
        claim.GetProperty("target").GetString().Should().Be("csharp");

        var frontend = entries["morphir.prune"].GetProperty("capabilities").GetProperty("transform");
        frontend.GetProperty("stage").GetString().Should().Be("after-frontend");
        frontend.TryGetProperty("target", out _).Should().BeFalse("a stage after a frontend has no target");
    }

    [Test]
    public void ATransformEntryHasNoStageInTheMepRecord()
    {
        var source = new TransformExtensionSource(Native, [new Pass("morphir.csharp.pass", PassStage.NewBeforeBackend("csharp"))]);
        var info = source.Entries.Single().ToMep();
        info.Types.Select(type => type.WireValue).Should().Equal("transform");

        var text = JsonSerializer.Serialize(info, Morphir.Mep.MepJson.Default.ExtensionInfo);
        text.Should().NotContain("stage").And.NotContain("before-backend");
        var capabilities = JsonSerializer.Serialize(source.Entries.Single().ToMepCapabilities(), Morphir.Mep.MepJson.Default.ExtensionCapabilities);
        capabilities.Should().NotContain("transform").And.NotContain("stage");
    }

    [Test]
    public void ThePassesOfThisReleaseAreBuiltInTransformEntries()
    {
        var source = new TransformExtensionSource(Native);
        source.Entries.Select(entry => entry.Identity.Id.Value).Should().Equal(BuiltInTransforms.All.Select(pass => pass.Id));
        source.Entries.Should().OnlyContain(entry => entry.TransformClaimOrNull != null && entry.Delivery == ExtensionDelivery.BuiltIn);
    }
}
