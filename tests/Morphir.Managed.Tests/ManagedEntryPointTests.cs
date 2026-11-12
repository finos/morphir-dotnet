// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Tests.Hosts;
using Morphir.Tooling;
using ManagedProgram = Morphir.Managed.Program;
using ToolProgram = Morphir.Tool.Program;

namespace Morphir.Managed.Tests;

/// <summary>
/// DEP-1: the managed morphir runs the shared command layer with the managed composition, as dotnet-morphir does. It
/// registers the in-process F# frontend, has the services and command tree of dotnet-morphir, and its closure has the F#
/// compiler service. The tests of the built executable (its version, its compile of F# in process and its list of
/// extensions) are in tests/Morphir.BlackBox.Tests with the tag @host-managed.
/// </summary>
public sealed class ManagedEntryPointTests
{
    [Test]
    public void TheManagedEntryPointIsPublishedAsSelfContainedOrFrameworkDependentAndATestBuildStatesNeither()
    {
        var host = ManagedProgram.Host;
        host.Host.Should().Be(MorphirHost.Native, "the managed morphir runs as the command morphir");
        host.EntryPoint.Should().Equal(Deployment.SelfContained, Deployment.FrameworkDependent);
        host.DeploymentOrNull.Should().BeNull("no named release profile built the test host's reference");
        host.ExecutionMatchesDeployment.Should().BeTrue();
    }

    /// <summary>The managed morphir lists the in-process F# frontend as available, like dotnet-morphir.</summary>
    [Test]
    [Category("PlatformSpecific")]
    public void TheManagedEntryPointListsTheFSharpFrontendAsAvailable()
    {
        using var app = ManagedProgram.CreateApp([]);
        app.Services.GetRequiredService<Morphir.Tooling.Features.Extensions.ExtensionRegistry>().TryRead(out var catalog, out var error).Should().BeTrue(error?.ToString());
        var entry = catalog!.Entries.Single(item => item.Identity.Id.Equals(Morphir.Tooling.Features.Extensions.FrontendExtensionSource.FSharpId));
        entry.IsAvailableIn(catalog.Host).Should().BeTrue();
    }
    [Test]
    [Category("PlatformSpecific")]
    public void TheManagedEntryPointRegistersTheInProcessFSharpFrontend()
    {
        using var app = ManagedProgram.CreateApp([]);
        var frontends = app.Services.GetServices<IFrontend>().ToArray();
        frontends.Select(frontend => frontend.Identity.Value).Should().Equal("morphir-dotnet.fsharp");
        frontends[0].Transport.Should().Be(FrontendTransport.InProcess);
    }

    [Test]
    [Category("PlatformSpecific")]
    public void TheManagedEntryPointHasTheFrontendsOfTheDotnetTool()
    {
        using var managed = ManagedProgram.CreateApp([]);
        using var tool = ToolProgram.CreateApp([]);
        Frontends(managed).Should().Equal(Frontends(tool));
    }

    // Each entry point's real CreateApp, built with its own host. The only expected difference is the host value:
    // the managed morphir runs as the command morphir, dotnet-morphir as dotnet-morphir.

    [Test]
    [Category("PlatformSpecific")]
    public void TheManagedEntryPointHasTheServicesOfTheDotnetTool()
    {
        var managed = Managed();
        var tool = Tool();
        managed.Services.Should().Equal(tool.Services);

        managed.Host.Host.Should().Be(MorphirHost.Native, "the managed morphir runs as the command morphir");
        tool.Host.Host.Should().Be(MorphirHost.DotnetTool);
        managed.Host.Version.Should().Be(tool.Host.Version);
    }

    [Test]
    [Category("PlatformSpecific")]
    public void TheManagedEntryPointHasTheCommandTreeOfTheDotnetTool() =>
        Managed().Commands.Should().Equal(Tool().Commands);

    [Test]
    [Category("PlatformSpecific")]
    public void TheAssemblyClosureOfTheManagedEntryPointHasTheFSharpFrontend()
    {
        var closure = Closure(typeof(ManagedProgram).Assembly);
        closure.Should().Contain("Morphir.Frontend.FSharp");
        closure.Should().Contain("FSharp.Compiler.Service");
    }

    [Test]
    public void TheManagedEntryPointRunsAsMorphirWithTheVersionOfThisBuild()
    {
        typeof(ManagedProgram).Assembly.GetName().Name.Should().Be("morphir");
        ManagedProgram.Host.Host.Should().Be(MorphirHost.Native);
        ManagedProgram.Host.Version.Should().Be(ToolProgram.Host.Version);
    }

    private static CompositionSnapshot Managed() => CompositionSnapshot.Of(observe => ManagedProgram.CreateApp([], observe));

    private static CompositionSnapshot Tool() => CompositionSnapshot.Of(observe => ToolProgram.CreateApp([], observe));

    private static string[] Frontends(MorphirCliApp app) =>
        [.. app.Services.GetServices<IFrontend>().Select(frontend => $"{frontend.Identity.Value} {frontend.Transport}")];

    /// <summary>The names of every assembly that <paramref name="root"/> references, directly or indirectly.</summary>
    private static HashSet<string> Closure(System.Reflection.Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<System.Reflection.AssemblyName>();
        pending.Push(root.GetName());
        while (pending.TryPop(out var name))
        {
            if (name.Name is null || !seen.Add(name.Name)) continue;
            System.Reflection.Assembly assembly;
            try { assembly = System.Reflection.Assembly.Load(name); }
            catch (FileNotFoundException) { continue; }
            foreach (var reference in assembly.GetReferencedAssemblies()) pending.Push(reference);
        }
        return seen;
    }
}
