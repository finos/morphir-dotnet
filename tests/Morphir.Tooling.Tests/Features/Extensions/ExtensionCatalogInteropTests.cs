using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>Builds and reads a catalog from C# with no switch on an F# union.</summary>
public sealed class ExtensionCatalogInteropTests
{
    private static readonly HostIdentity Host = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    private static ExtensionEntry Backend(string id) => ExtensionEntry.BuiltInBackend(
        ExtensionIdentity.Create(ExtensionId.Create(id), ExtensionName.Create(id + " generator"), Host.Version, null),
        new BackendClaim(NonEmptySet<TargetId>.Create(TargetId.Create("csharp")), SourceFormatVersions.Create("4.0.0")));

    [Test]
    public void TryCreateBuildsAnOrderedCatalog()
    {
        if (!ExtensionCatalog.TryCreate(Host, [Backend("morphir.fsharp"), Backend("morphir.csharp")], out var catalog, out var error))
            throw new InvalidOperationException(error.ToString());
        catalog.Entries.Select(entry => entry.Identity.Id.Value).Should().Equal("morphir.csharp", "morphir.fsharp");
        catalog.Host.Should().Be(Host);
    }

    [Test]
    public void TryCreateReportsDuplicateIdsAsDiagnostics()
    {
        ExtensionCatalog.TryCreate(Host, [Backend("morphir.csharp"), Backend("morphir.csharp")], out _, out var error).Should().BeFalse();
        error!.Diagnostics.Should().ContainSingle()
            .Which.Should().Be(new Diagnostic(DiagnosticCode.DuplicateExtensionId, "Extension id 'morphir.csharp' is registered more than once."));
    }

    [Test]
    public void TheOutputEdgeReadsWireValuesWithoutBranching()
    {
        var entry = Backend("morphir.csharp");
        entry.Types.Select(type => type.WireValue).Should().Equal("backend");
        entry.Delivery.WireValue.Should().Be("built-in");
        entry.Hosts.WireValues.Should().Equal("morphir", "dotnet-morphir");
        entry.IsAvailableIn(TestHosts.Unprofiled(MorphirHost.DotnetTool, Host.Version)).Should().BeTrue();
        entry.BackendClaimOrNull!.Targets.Select(target => target.Value).Should().Equal("csharp");
        entry.Identity.DescriptionOrNull.Should().BeNull();
    }

    [Test]
    public void AnEntryForOneHostIsNotAvailableInTheOther()
    {
        var entry = Backend("acme.frontend").WithHosts(HostAvailability.OnlyIn(MorphirHost.DotnetTool));
        entry.IsAvailableIn(TestHosts.Unprofiled(MorphirHost.Native, Host.Version)).Should().BeFalse();
        entry.IsAvailableIn(TestHosts.Unprofiled(MorphirHost.DotnetTool, Host.Version)).Should().BeTrue();
    }
}
