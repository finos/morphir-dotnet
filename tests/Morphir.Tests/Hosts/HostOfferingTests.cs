using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tests.Hosts;

/// <summary>
/// Task 2.3: each entry point states the offerings it can be published as and the profile that built it; the registry's
/// frontend entries agree with the frontends that <c>compile</c> selects.
/// </summary>
public sealed class HostOfferingTests
{
    [Test]
    public void TheAotEntryPointIsPublishedOnlyAsNativeAot()
    {
        var host = Morphir.Program.Host;
        host.Host.Should().Be(MorphirHost.Native);
        host.EntryPoint.Should().Equal(Deployment.NativeAot);
    }

    [Test]
    public void TheToolEntryPointIsPublishedOnlyAsTheDotnetTool()
    {
        var host = Morphir.Tool.Program.Host;
        host.Host.Should().Be(MorphirHost.DotnetTool);
        host.EntryPoint.Should().Equal(Deployment.DotnetTool);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void ATestBuildStatesNoDeploymentAndRunsJit(bool tool)
    {
        // No named release profile builds the test host's references, so they state no deployment.
        var host = tool ? Morphir.Tool.Program.Host : Morphir.Program.Host;
        host.DeploymentOrNull.Should().BeNull();
        host.PlatformOrNull.Should().BeNull();
        host.Execution.Should().Be(ExecutionMode.Jit);
        host.ExecutionMatchesDeployment.Should().BeTrue();
    }

    /// <summary>The available frontend entries are exactly the in-process frontends that compile can select.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void TheAvailableFrontendEntriesAreTheFrontendsThatCompileSelects(bool tool)
    {
        using var app = tool ? Morphir.Tool.Program.CreateApp([]) : Morphir.Program.CreateApp([], Morphir.Program.Host, Morphir.Program.NativeComposition);
        app.Services.GetRequiredService<ExtensionRegistry>().TryRead(out var catalog, out var error).Should().BeTrue(error?.ToString());
        var available = catalog!.Entries
            .Where(entry => entry.FrontendClaimOrNull is not null && entry.IsAvailableIn(catalog.Host))
            .Select(entry => entry.Identity.Id.Value);
        var selectable = app.Services.GetServices<IFrontend>()
            .Where(frontend => frontend.Transport.Equals(FrontendTransport.InProcess))
            .Select(frontend => frontend.Identity.Value);
        available.Should().BeEquivalentTo(selectable);
    }

    /// <summary>The declared F# frontend entry claims what the registered F# frontend advertises.</summary>
    [Test]
    public void TheFSharpFrontendEntryClaimsWhatTheFrontendAdvertises()
    {
        using var app = Morphir.Tool.Program.CreateApp([]);
        var frontend = app.Services.GetServices<IFrontend>().Single();
        frontend.Identity.Value.Should().Be(FrontendExtensionSource.FSharpId.Value);
        var claim = FrontendExtensionSource.FSharpClaim;
        claim.Languages.Select(language => language.Language.Value).Should().Equal(frontend.Capability.Languages.Select(language => language.Id.Value));
        claim.Languages.Single().FileExtensions.Should().Equal(frontend.Capability.Languages.Single().FileExtensions);
        claim.IrVersionTexts.Should().BeEquivalentTo(frontend.Capability.Outputs.Select(target => Morphir.IR.Versioning.version(target.Version)).Distinct());
    }

    [Test]
    public void TheAotCompileRefusalAndTheRegistryReasonComeFromTheSameSource()
    {
        using var app = Morphir.Program.CreateApp([], Morphir.Program.Host, Morphir.Program.NativeComposition);
        var hint = app.Services.GetServices<FrontendHint>().Single();
        hint.Language.Should().Be(FrontendExtensionSource.FSharpLanguage);
        hint.Message.Should().Be(FrontendExtensionSource.FSharpCompileHint);
        app.Services.GetRequiredService<ExtensionRegistry>().TryRead(out var catalog, out _).Should().BeTrue();
        var entry = catalog!.Entries.Single(item => item.Identity.Id.Equals(FrontendExtensionSource.FSharpId));
        entry.ReasonOrNullIn(catalog.Host)!.Value.Should().Be(FrontendExtensionSource.FSharpUnavailable.Value).And.Contain("morphir-fsharp-frontend");
    }
}
