using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tests.Hosts;

/// <summary>The shared composition registers the extension sources and the registry for both hosts.</summary>
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
        catalog.Entries.Select(entry => entry.Identity.Id.Value).Should().Equal("morphir.csharp", "morphir.fsharp");
        catalog.Entries.Should().OnlyContain(entry => entry.Identity.Version.Equals(host.Version));
    }
}
