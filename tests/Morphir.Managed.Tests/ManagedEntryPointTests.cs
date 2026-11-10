// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tests.Hosts;
using Morphir.Tooling;
using ManagedProgram = Morphir.Managed.Program;
using ToolProgram = Morphir.Tool.Program;

namespace Morphir.Managed.Tests;

/// <summary>
/// DEP-1: the managed morphir runs the shared command layer with the managed composition, as dotnet-morphir does. It
/// registers the in-process F# frontend, has the services and command tree of dotnet-morphir, and its closure has the F#
/// compiler service. The real executable compiles F# in process.
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

    // Each entry point's real CreateApp, built with its own host. The only expected difference is the host value:
    // the managed morphir runs as the command morphir, dotnet-morphir as dotnet-morphir.

    [Test]
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
    public void TheManagedEntryPointHasTheCommandTreeOfTheDotnetTool() =>
        Managed().Commands.Should().Equal(Tool().Commands);

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

    // --version returns before the composition is built. These run the real executable from its own build output, with
    // its own deps.json and runtimeconfig, through the composition and the F# compiler service.

    [Test]
    public async Task TheManagedExecutableCompilesFSharpWithTheInProcessFrontend()
    {
        await using var sandbox = TestSandbox.Create("morphir-managed-compile-");
        Directory.CreateDirectory(Path.Combine(sandbox.Work, "src"));
        await File.WriteAllTextAsync(Path.Combine(sandbox.Work, "src", "Rates.fs"),
            "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n");

        var result = await RunManaged(sandbox, "compile", "--language", "fsharp", "--input", "src/Rates.fs", "--package-name", "acme/demo", "--json");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(result.StandardOutput);
        report.RootElement.GetProperty("frontend").GetString().Should().Be("morphir-dotnet.fsharp (in-process)");
    }

    [Test]
    public async Task TheManagedExecutableListsTheSharedBuiltIns()
    {
        await using var sandbox = TestSandbox.Create("morphir-managed-extensions-");
        var result = await RunManaged(sandbox, "extension", "list", "--json");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("host").GetString().Should().Be("morphir");
        report.RootElement.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Should().Equal("morphir.csharp", "morphir.fsharp", "morphir.type-validator");
    }

    private static CompositionSnapshot Managed() => CompositionSnapshot.Of(observe => ManagedProgram.CreateApp([], observe));

    private static CompositionSnapshot Tool() => CompositionSnapshot.Of(observe => ToolProgram.CreateApp([], observe));

    /// <summary>Runs the managed morphir apphost from <c>src/Morphir.Managed/bin</c>, in the sandbox.</summary>
    private static async Task<ChildProcessResult> RunManaged(TestSandbox sandbox, params string[] arguments)
    {
        var start = ChildProcess.StartInfo(ManagedExecutable(), arguments);
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start, new SandboxOptions { Boundary = sandbox.Root });
        return await ChildProcess.RunAsync(start, TimeSpan.FromSeconds(180));
    }

    /// <summary>The apphost in the managed project's own output folder, for the configuration of this test build.</summary>
    private static string ManagedExecutable()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetFramework = output.Name;
        var configuration = output.Parent!.Name;
        var root = output;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Morphir.slnx"))) root = root.Parent;
        root.Should().NotBeNull("the tests run inside the repository");
        var executable = Path.Combine(root!.FullName, "src", "Morphir.Managed", "bin", configuration, targetFramework,
            OperatingSystem.IsWindows() ? "morphir.exe" : "morphir");
        File.Exists(executable).Should().BeTrue($"{executable} is built with this test project");
        return executable;
    }

    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

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
