// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tooling;
using ManagedProgram = Morphir.Managed.Program;
using ToolProgram = Morphir.Tool.Program;

namespace Morphir.Managed.Tests;

/// <summary>
/// DEP-1: the managed morphir runs the shared command layer with the managed composition, as dotnet-morphir does. It
/// registers the in-process F# frontend, has the command tree of dotnet-morphir, and its closure has the F# compiler
/// service.
/// </summary>
public sealed class ManagedEntryPointTests
{
    [Test]
    public void TheManagedEntryPointRegistersTheInProcessFSharpFrontend()
    {
        using var app = ManagedProgram.CreateApp([]);
        var frontends = app.Services.GetServices<IFrontend>().ToArray();
        frontends.Select(frontend => frontend.Identity.Value).Should().Equal("morphir-dotnet.fsharp");
        frontends[0].Transport.Should().Be(FrontendTransport.InProcess);
    }

    [Test]
    public void TheManagedEntryPointHasTheFrontendsOfTheDotnetTool()
    {
        using var managed = ManagedProgram.CreateApp([]);
        using var tool = ToolProgram.CreateApp([]);
        Frontends(managed).Should().Equal(Frontends(tool));
    }

    [Test]
    public void TheManagedEntryPointHasTheCommandTreeOfTheDotnetTool() =>
        CommandTree(ManagedProgram.Host, ManagedProgram.Compose).Should().Equal(CommandTree(ToolProgram.Host, ToolProgram.Compose));

    [Test]
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

    [Test]
    public async Task VersionOutputParsesAsAReleaseVersion()
    {
        var start = ChildProcess.StartInfo(DotnetHost(), Path.Combine(AppContext.BaseDirectory, "morphir.dll"), "--version");
        await using var sandbox = TestSandbox.Create("morphir-managed-version-");
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start);
        var result = await ChildProcess.RunAsync(start, TimeSpan.FromSeconds(60));

        result.ExitCode.Should().Be(0, result.StandardError);
        var output = result.StandardOutput.Trim();
        ReleaseVersion.TryParse(output, out var version, out var failure).Should().BeTrue(failure?.ToString());
        version!.ToString().Should().Be(output);
    }

    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    private static string[] Frontends(MorphirCliApp app) =>
        [.. app.Services.GetServices<IFrontend>().Select(frontend => $"{frontend.Identity.Value} {frontend.Transport}")];

    /// <summary>Every command path with its options and arguments, as the entry point's composition builds it.</summary>
    private static List<string> CommandTree(HostIdentity host, Action<MorphirCliBuilder> compose)
    {
        RootCommand? root = null;
        using var app = MorphirApplication.CreateApp([], host, builder =>
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
