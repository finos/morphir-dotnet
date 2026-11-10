using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Extensions;

public sealed class BackendExtensionSourceTests
{
    private static readonly HostIdentity Host = new(MorphirHost.DotnetTool, ReleaseVersion.Create("0.4.0-rc.1"));

    [Test]
    public void MakesOneBuiltInEntryForEachBackend()
    {
        var source = new BackendExtensionSource([new CSharpGenerationBackend(), new FSharpGenerationBackend()], Host);
        source.Entries.Select(entry => entry.Identity.Id.Value).Should().Equal("morphir.csharp", "morphir.fsharp");
        source.Entries.Should().OnlyContain(entry => entry.Delivery.Equals(ExtensionDelivery.BuiltIn) && entry.Hosts.Equals(HostAvailability.AllHosts));
    }

    [Test]
    public void TheEntryCarriesTheDescriptorIdentityAndTheHostVersion()
    {
        var backend = new CSharpGenerationBackend();
        var entry = new BackendExtensionSource([backend], Host).Entries.Single();
        entry.Identity.Id.Should().Be(backend.Descriptor.Id);
        entry.Identity.Name.Should().Be(backend.Descriptor.Name);
        entry.Identity.DescriptionOrNull.Should().Be(backend.Descriptor.Description);
        entry.Identity.Version.Should().Be(Host.Version);
    }

    [Test]
    public void TheBackendClaimHasTheTargetAndTheUnionOfDeclaredVersions()
    {
        var entry = new BackendExtensionSource([new CSharpGenerationBackend()], Host).Entries.Single();
        entry.Types.Select(type => type.WireValue).Should().Equal("backend");
        var claim = entry.BackendClaimOrNull!;
        claim.Targets.Select(target => target.Value).Should().Equal("csharp");
        claim.IrVersions.Select(version => version.Text).Should().Equal("1", "2", "3", "3.0.0", "3.1.0", "4", "4.0.0");
    }

    [Test]
    public void TheRegistryComposesEverySource()
    {
        var registry = new ExtensionRegistry([new BackendExtensionSource([new CSharpGenerationBackend()], Host), new BackendExtensionSource([new FSharpGenerationBackend()], Host)], Host, WasmRuntime.Process);
        registry.TryRead(out var catalog, out var error).Should().BeTrue(error?.ToString());
        catalog!.Entries.Should().HaveCount(2);
        catalog.Host.Should().Be(Host);
    }

    [Test]
    public void TheRegistryRefusesTheSameBackendTwice()
    {
        var registry = new ExtensionRegistry([new BackendExtensionSource([new CSharpGenerationBackend(), new CSharpGenerationBackend()], Host)], Host, WasmRuntime.Process);
        registry.TryRead(out _, out var error).Should().BeFalse();
        error!.Diagnostics.Single().Message.Should().Contain("morphir.csharp");
    }
}
