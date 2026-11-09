// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Tooling;
using Morphir.Tooling.Managed;

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

    // DEP-1: one shared command layer, and one managed composition that the managed entry points share.

    [Test]
    public void TheManagedCompositionRegistersTheInProcessFSharpFrontend()
    {
        using var app = MorphirApplication.CreateApp([], Morphir.Tool.Program.Host, ManagedComposition.Compose);
        var frontends = app.Services.GetServices<IFrontend>().ToArray();
        frontends.Select(frontend => frontend.Identity.Value).Should().Equal("morphir-dotnet.fsharp");
        frontends[0].Transport.Should().Be(FrontendTransport.InProcess);
    }

    [Test]
    public void TheToolCompositionIsTheManagedComposition()
    {
        using var tool = Morphir.Tool.Program.CreateApp([]);
        using var managed = MorphirApplication.CreateApp([], Morphir.Tool.Program.Host, ManagedComposition.Compose);
        Frontends(tool).Should().Equal(Frontends(managed));
        CommandTree(Morphir.Tool.Program.Compose).Should().Equal(CommandTree(ManagedComposition.Compose));
    }

    [Test]
    public void TheNativeCompositionHasTheCommandTreeOfTheTool() =>
        CommandTree(Morphir.Program.NativeComposition).Should().Equal(CommandTree(Morphir.Tool.Program.Compose));

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

    private static string[] Frontends(MorphirCliApp app) =>
        [.. app.Services.GetServices<IFrontend>().Select(frontend => $"{frontend.Identity.Value} {frontend.Transport}")];

    /// <summary>Every command path with its options and arguments, as a composition builds it.</summary>
    private static List<string> CommandTree(Action<MorphirCliBuilder> compose)
    {
        RootCommand? root = null;
        using var app = MorphirApplication.CreateApp([], Morphir.Program.Host, builder =>
        {
            compose(builder);
            root = builder.RootCommand;
        });
        var lines = new List<string>();
        Describe(root!, "morphir", lines);
        return lines;
    }

    private static void Describe(Command command, string path, List<string> lines)
    {
        var options = command.Options.Select(option => option.Name).Order(StringComparer.Ordinal);
        var arguments = command.Arguments.Select(argument => argument.Name);
        lines.Add($"{path} [{string.Join(' ', options)}] ({string.Join(' ', arguments)})");
        foreach (var subcommand in command.Subcommands.OrderBy(subcommand => subcommand.Name, StringComparer.Ordinal))
            Describe(subcommand, $"{path} {subcommand.Name}", lines);
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
