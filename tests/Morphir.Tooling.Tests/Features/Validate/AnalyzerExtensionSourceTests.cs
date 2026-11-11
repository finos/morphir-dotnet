using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Validate;

namespace Morphir.Tooling.Tests.Features.Validate;

/// <summary>The registry entries of the built-in analyzers come from the analyzers themselves.</summary>
public sealed class AnalyzerExtensionSourceTests
{
    private static readonly HostIdentity Native = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    [Test]
    public void TheSourceHasOneEntryForEachBuiltInAnalyzer()
    {
        var registry = new AnalyzerRegistry(Native);
        var source = new AnalyzerExtensionSource(registry);
        source.Entries.Select(entry => entry.Identity.Id.Value).Should().Equal("morphir.type-validator");
        source.Entries.Should().Equal(registry.Analyzers.Select(analyzer => analyzer.Entry));
    }

    [Test]
    public void TheTypeValidatorEntryHasTheHostVersionAndTheClassicClaim()
    {
        var entry = new AnalyzerExtensionSource(new AnalyzerRegistry(Native)).Entries.Single();
        entry.Identity.Version.Should().Be(Native.Version);
        entry.Types.Select(type => type.WireValue).Should().Equal("validator");
        entry.BackendClaimOrNull.Should().BeNull();
        entry.ValidatorClaimOrNull!.IrVersionTexts.Should().Equal("1", "2", "3");
        entry.CapabilitySummary.Should().Be("validator (1, 2, 3)");
    }
}
