// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Tooling;

namespace Morphir.Tests.Hosts;

/// <summary>
/// CLI-3: the dotnet tool has its own composition, which adds the in-process F# frontend. The AOT executable
/// <c>morphir</c> has no F# frontend, and its assembly closure has no F# compiler service.
/// </summary>
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
