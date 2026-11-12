using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.Validate;

namespace Morphir.Tests.Hosts;

/// <summary>The shared composition registers the extension sources and the registry for both hosts.</summary>
[Category("PlatformSpecific")]
public sealed class HostRegistryTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void TheRegistryReadsTheBuiltInGeneratorsForEachHost(bool tool)
    {
        var host = tool ? Morphir.Tool.Program.Host : Morphir.Program.Host;
        using var app = Morphir.Program.CreateApp([], host);
        var registry = app.Services.GetRequiredService<ExtensionRegistry>();
        registry.TryRead(out var catalog, out var error).Should().BeTrue(error?.ToString());
        catalog!.Host.Should().Be(host);
        catalog.Entries.Select(entry => entry.Identity.Id.Value).Should().Equal("morphir-dotnet.fsharp", "morphir.csharp", "morphir.fsharp", "morphir.type-validator");
        catalog.Entries.Should().OnlyContain(entry => entry.Identity.Version.Equals(host.Version));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void TheTypeValidatorIsABuiltInValidatorForTheClassicVersionsInEachHost(bool tool)
    {
        var host = tool ? Morphir.Tool.Program.Host : Morphir.Program.Host;
        using var app = Morphir.Program.CreateApp([], host);
        app.Services.GetRequiredService<ExtensionRegistry>().TryRead(out var catalog, out _).Should().BeTrue();
        var entry = catalog!.Entries.Single(item => item.Identity.Id.Value == "morphir.type-validator");
        entry.Types.Select(type => type.WireValue).Should().Equal("validator");
        entry.Delivery.WireValue.Should().Be("built-in");
        entry.Hosts.WireValues.Should().Equal("morphir", "dotnet-morphir");
        entry.ValidatorClaimOrNull!.IrVersionTexts.Should().Equal("1", "2", "3");
    }

    /// <summary>The validator entries of the registry are exactly the analyzers that <c>morphir validate</c> runs.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void TheValidatorEntriesAreTheAnalyzersThatValidateRuns(bool tool)
    {
        var host = tool ? Morphir.Tool.Program.Host : Morphir.Program.Host;
        using var app = Morphir.Program.CreateApp([], host);
        app.Services.GetRequiredService<ExtensionRegistry>().TryRead(out var catalog, out _).Should().BeTrue();
        var validators = catalog!.Entries.Where(entry => entry.ValidatorClaimOrNull is not null).Select(entry => entry.Identity.Id.Value);
        var analyzers = app.Services.GetRequiredService<AnalyzerRegistry>().Analyzers.Select(analyzer => analyzer.Entry.Identity.Id.Value);
        validators.Should().Equal(analyzers);
    }
}
