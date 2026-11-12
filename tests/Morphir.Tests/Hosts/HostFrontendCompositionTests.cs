// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.Tooling;
using Morphir.Tooling.Managed;

namespace Morphir.Tests.Hosts;

/// <summary>
/// CLI-3: the dotnet tool has its own composition, which adds the in-process F# frontend. The AOT executable
/// <c>morphir</c> has no F# frontend, and its assembly closure has no F# compiler service.
/// </summary>
[Category("PlatformSpecific")]
public sealed class HostFrontendCompositionTests
{
    [Test]
    public void CreateAppCallsTheExtensionAfterTheSharedComposition()
    {
        var called = false;
        using var app = Morphir.Program.CreateApp([], Morphir.Program.Host, builder =>
        {
            builder.Should().BeOfType<MorphirCliBuilder>();
            called = true;
        });
        called.Should().BeTrue();
    }

    [Test]
    public void TheToolCompositionRegistersTheInProcessFSharpFrontend()
    {
        using var app = Morphir.Tool.Program.CreateApp([]);
        var frontends = app.Services.GetServices<IFrontend>().ToArray();
        frontends.Select(frontend => frontend.Identity.Value).Should().Equal("morphir-dotnet.fsharp");
        frontends[0].Transport.Should().Be(FrontendTransport.InProcess);
    }

    [Test]
    public void TheNativeCompositionRegistersNoFrontend()
    {
        using var app = Morphir.Program.CreateApp([], Morphir.Program.Host, Morphir.Program.NativeComposition);
        app.Services.GetServices<IFrontend>().Should().BeEmpty();
    }

    [Test]
    public void TheAssemblyClosureOfMorphirHasNoFSharpCompilerService()
    {
        var closure = Closure(typeof(Morphir.Program).Assembly);
        closure.Should().Contain("Morphir.Compilation", "the compile command is in the shared composition");
        closure.Should().NotContain("FSharp.Compiler.Service");
        closure.Should().NotContain("Morphir.Frontend.FSharp");
    }

    [Test]
    public void TheAssemblyClosureOfTheToolHasTheFSharpFrontend()
    {
        var closure = Closure(typeof(Morphir.Tool.Program).Assembly);
        closure.Should().Contain("Morphir.Frontend.FSharp");
        closure.Should().Contain("FSharp.Compiler.Service");
    }

    // DEP-1: one shared command layer, and one managed composition that the managed entry points share.

    [Test]
    public void TheManagedCompositionRegistersTheInProcessFSharpFrontend()
    {
        using var app = MorphirApplication.CreateApp([], Morphir.Tool.Program.Host, ManagedComposition.Compose);
        var frontends = app.Services.GetServices<IFrontend>().ToArray();
        frontends.Select(frontend => frontend.Identity.Value).Should().Equal("morphir-dotnet.fsharp");
        frontends[0].Transport.Should().Be(FrontendTransport.InProcess);
    }

    // Each entry point's real composition, built with its own host. The AOT-compatible morphir and dotnet-morphir run
    // the same commands; the expected differences are the host value and the F# frontend, and nothing else.

    [Test]
    public void TheNativeEntryPointHasTheCommandTreeOfTheTool() =>
        Native().Commands.Should().Equal(Tool().Commands);

    [Test]
    public void TheNativeEntryPointDiffersFromTheToolOnlyInItsHostAndFrontend()
    {
        var native = Native();
        var tool = Tool();

        native.Host.Host.Should().Be(MorphirHost.Native);
        tool.Host.Host.Should().Be(MorphirHost.DotnetTool);
        native.Host.Version.Should().Be(tool.Host.Version);

        var nativeOnly = native.Services.Except(tool.Services).ToList();
        var toolOnly = tool.Services.Except(native.Services).ToList();
        nativeOnly.Should().ContainSingle().Which.Should().StartWith($"Singleton {typeof(Morphir.Tooling.Features.Compile.FrontendHint).FullName} -> instance",
            "the AOT-compatible composition adds only the hint that says where an F# frontend is");
        toolOnly.Should().ContainSingle().Which.Should().StartWith($"Singleton {typeof(IFrontend).FullName} -> instance",
            "the managed composition adds only the in-process F# frontend");
        native.Services.Where(line => !nativeOnly.Contains(line)).Should().Equal(tool.Services.Where(line => !toolOnly.Contains(line)),
            "the shared registrations come in the same order");
    }

    private static CompositionSnapshot Native() => CompositionSnapshot.Of(observe =>
        Morphir.Program.CreateApp([], Morphir.Program.Host, builder =>
        {
            Morphir.Program.NativeComposition(builder);
            observe(builder);
        }));

    private static CompositionSnapshot Tool() => CompositionSnapshot.Of(observe => Morphir.Tool.Program.CreateApp([], observe));
    [Test]
    public void TheAssemblyClosureOfTheManagedCompositionHasTheFSharpFrontend()
    {
        var closure = Closure(typeof(ManagedComposition).Assembly);
        closure.Should().Contain("Morphir.Frontend.FSharp");
        closure.Should().Contain("FSharp.Compiler.Service");
    }

    [Test]
    public void TheAssemblyClosureOfTheSharedLayerHasNoFSharpCompilerService()
    {
        var closure = Closure(typeof(MorphirApplication).Assembly);
        closure.Should().Contain("Morphir.Compilation", "the compile command is in the shared layer");
        closure.Should().NotContain("FSharp.Compiler.Service");
        closure.Should().NotContain("Morphir.Frontend.FSharp");
        closure.Should().NotContain("Morphir.Tooling.Managed");
    }

    [Test]
    public void TheAssemblyClosureOfMorphirHasNoManagedComposition() =>
        Closure(typeof(Morphir.Program).Assembly).Should().NotContain("Morphir.Tooling.Managed");

    /// <summary>The names of every assembly that <paramref name="root"/> references, directly or indirectly.</summary>
    private static HashSet<string> Closure(System.Reflection.Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<AssemblyName>();
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
