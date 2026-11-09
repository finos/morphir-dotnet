// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Compilation;
using Morphir.Frontend.FSharp;
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Task 9.1: <c>--extension</c> runs the frontend as a MEP 0.2.0-draft.1 guest process through the one extension
/// connector. The guest is <c>morphir-fsharp-frontend</c>, started on the dotnet host from the test output.
/// </summary>
[NotInParallel(nameof(MepExtensionCompileTests))]
public sealed class MepExtensionCompileTests
{
    private static readonly string Guest = Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-frontend.dll");

    private const string Source = "module Acme.Demo.Pricing\n\nlet total (seats: int) (days: int) : int = seats * days\n";

    private static async Task<(int Exit, JsonDocument Report)> Compile(CompileWorkspace workspace, IFrontend? inProcess, string arguments)
    {
        var output = new CompileCommandTests.CapturedOutput();
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<CompilationScenario>()
            .ConfigureServices(services =>
            {
                if (inProcess is not null) services.AddSingleton(inProcess);
                services.AddSingleton<ICompileOutput>(output);
                services.AddSingleton(new CompileContextSource(() => workspace.Context()));
            })
            .Build();
        var exit = await app.RunAsync();
        return (exit, JsonDocument.Parse(output.Text));
    }

    [Test]
    [Arguments("4")]
    [Arguments("3")]
    [Arguments("2")]
    [Arguments("1")]
    public async Task TheGuestGivesTheSameIrAsTheInProcessFrontend(string version)
    {
        using var mep = new CompileWorkspace();
        mep.Write("Pricing.fs", Source);
        using var local = new CompileWorkspace();
        local.Write("Pricing.fs", Source);
        const string common = "--json --language fsharp --input Pricing.fs --package-name acme/demo --ir-version ";

        var (exit, report) = await Compile(mep, null, common + version + " --extension " + Guest);
        using var _ = report;
        exit.Should().Be(0, report.RootElement.GetRawText());
        report.RootElement.GetProperty("frontend").GetString().Should().Contain("mep");

        var (localExit, localReport) = await Compile(local, FSharpFrontend.Registration, common + version);
        using var __ = localReport;
        localExit.Should().Be(0, localReport.RootElement.GetRawText());

        var remoteIr = File.ReadAllBytes(report.RootElement.GetProperty("output").GetString()!);
        var localIr = File.ReadAllBytes(localReport.RootElement.GetProperty("output").GetString()!);
        remoteIr.Should().Equal(localIr, "the IR from the guest equals the in-process IR (classic 1 and 2 come from 3.0.0, MEP-3)");
    }

    [Test]
    public async Task AnExtensionThatDoesNotStartFailsWithExitCode2AndNamesTheExtension()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Pricing.fs", Source);
        var missing = Path.Combine(workspace.Root, "no-such-guest");
        var (exit, report) = await Compile(workspace, null,
            "--json --language fsharp --input Pricing.fs --package-name acme/demo --extension " + missing);
        using var _ = report;
        exit.Should().Be(2, report.RootElement.GetRawText());
        report.RootElement.GetRawText().Should().Contain("extension.unavailable").And.Contain("no-such-guest");
    }

    [Test]
    public void AnExtensionSettingResolvesToAProcessWithoutAShell()
    {
        var directory = Path.GetTempPath();
        MepFrontendExtensionConnector.Resolve("morphir-fsharp-frontend", directory)
            .Should().Be(("morphir-fsharp-frontend", Array.Empty<string>()), "a name without a directory is looked up on PATH");

        var relative = MepFrontendExtensionConnector.Resolve(Path.Combine("tools", "guest"), directory);
        relative.Executable.Should().Be(Path.GetFullPath(Path.Combine(directory, "tools", "guest")));

        var dll = MepFrontendExtensionConnector.Resolve(Guest, directory);
        dll.Arguments.Should().Equal(Guest);
    }
}
