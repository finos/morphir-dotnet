// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Compilation;
using Morphir.Lang.CSharp;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// The compile command with the in-process C# frontend, as both hosts register it. The tests use the real frontend and
/// the shared command layer, with no process and no project file.
/// </summary>
[NotInParallel(nameof(CSharpCompileTests))]
public sealed class CSharpCompileTests
{
    private const string Rates =
        "namespace Acme.Demo.Rates;\n\npublic static class Pricing\n{\n    public static int DailyRate(int days) => days * 40;\n}\n";

    [Test]
    public async Task TheCompileCommandCompilesCSharpWithTheInProcessFrontend()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Rates.cs", Rates);
        var run = await Compile(workspace, "--json --input Rates.cs --package-name acme/demo");
        run.Exit.Should().Be(0, run.Text);
        using var report = JsonDocument.Parse(run.Text);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("language").GetString().Should().Be("csharp");
        report.RootElement.GetProperty("frontend").GetString().Should().Be("morphir-dotnet.csharp (in-process)");
        report.RootElement.GetProperty("irVersion").GetString().Should().Be("4");
        report.RootElement.GetProperty("modules").EnumerateArray().Select(module => module.GetString()).Should().Equal("Rates.Pricing");
    }

    [Test]
    public async Task ARefusalOfTheSubsetIsAReportedDiagnosticWithItsCodeAndDocument()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Loop.cs",
            "namespace Acme.Demo.Rates;\n\npublic static class Pricing\n{\n    public static int Total(int[] xs)\n    {\n        foreach (var x in xs) { }\n        return 0;\n    }\n}\n");
        var run = await Compile(workspace, "--json --input Loop.cs --package-name acme/demo");
        run.Exit.Should().Be(1, run.Text);
        using var report = JsonDocument.Parse(run.Text);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        var loop = report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Single(diagnostic => diagnostic.GetProperty("code").GetString() == "csharp.unsupported_loop");
        loop.GetProperty("location").GetProperty("uri").GetString().Should().Be("Loop.cs");
    }

    [Test]
    public async Task ACompilerErrorIsAReportedCSharpCompilerDiagnostic()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Bad.cs", "namespace Acme.Demo.Rates;\n\npublic static class Pricing\n{\n    public static int Bad() => \"text\";\n}\n");
        var run = await Compile(workspace, "--json --input Bad.cs --package-name acme/demo");
        run.Exit.Should().Be(1, run.Text);
        using var report = JsonDocument.Parse(run.Text);
        report.RootElement.GetProperty("diagnostics").EnumerateArray().Select(diagnostic => diagnostic.GetProperty("code").GetString())
            .Should().Contain("csharp.compiler");
    }

    private static async Task<(int Exit, string Text)> Compile(CompileWorkspace workspace, string arguments)
    {
        var output = new CompileCommandTests.CapturedOutput();
        var args = ("compile " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await using var app = MorphirCli.CreateBuilder(args)
            .AddScenario<CompilationScenario>()
            .AddScenario<CSharpFrontendScenario>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<Morphir.Tooling.Features.Compile.ICompileOutput>(output);
                services.AddSingleton(new Morphir.Tooling.Features.Compile.CompileContextSource(() => workspace.Context()));
            })
            .Build();
        var exit = await app.RunAsync(CancellationToken.None);
        return (exit, output.Text);
    }
}
