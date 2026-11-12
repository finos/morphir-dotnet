using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;

namespace Morphir.Tests.Hosts;

/// <summary>Each entry point states its host, and the shared composition registers it.</summary>
public sealed class HostCompositionTests
{
    [Test]
    public void TheNativeEntryPointStatesTheNativeHost() =>
        Morphir.Program.Host.Host.Should().Be(MorphirHost.Native);

    [Test]
    public void TheToolEntryPointStatesTheDotnetToolHost() =>
        Morphir.Tool.Program.Host.Host.Should().Be(MorphirHost.DotnetTool);

    [Test]
    public void BothHostsHaveTheVersionOfThisBuild() =>
        Morphir.Tool.Program.Host.Version.Should().Be(Morphir.Program.Host.Version);

    [Test]
    [Category("PlatformSpecific")]
    [Arguments(true)]
    [Arguments(false)]
    public void TheCompositionRegistersTheHostItIsGiven(bool tool)
    {
        var host = tool ? Morphir.Tool.Program.Host : Morphir.Program.Host;
        using var app = Morphir.Program.CreateApp([], host);
        app.Services.GetRequiredService<HostIdentity>().Should().Be(host);
    }
}
