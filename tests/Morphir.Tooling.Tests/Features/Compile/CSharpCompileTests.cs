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

    private const string Totals =
        "using System.Collections.Generic;\n\nnamespace Acme.Demo.Rates;\n\npublic static class Pricing\n{\n    public static int Total(IReadOnlyList<int> xs)\n    {\n        var total = 0;\n        foreach (var x in xs) { total += x; }\n        return total;\n    }\n}\n";

    private static string ValueKinds(CompileWorkspace workspace)
    {
        var ir = File.ReadAllText(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json"));
        return ir.Contains("\"LetRecursion\"", StringComparison.Ordinal) ? "LetRecursion" : "no LetRecursion";
    }

    [Test]
    public async Task TheLoopKeyOfTheCSharpFrontendTableGivesTheIrOfTheAttribute()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Totals.cs", Totals);
        var plain = await Compile(workspace, "--json --input Totals.cs --package-name acme/demo");
        plain.Exit.Should().Be(0, plain.Text);
        ValueKinds(workspace).Should().Be("no LetRecursion", "a fold is List.foldl by default");

        workspace.Write("morphir.toml", "[frontend.csharp]\nloop_lowering = \"recursion\"\n");
        var byKey = await Compile(workspace, "--json --input Totals.cs --package-name acme/demo");
        byKey.Exit.Should().Be(0, byKey.Text);
        var keyIr = File.ReadAllText(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json"));
        ValueKinds(workspace).Should().Be("LetRecursion", "the key lowers the loop to recursion");

        // The assembly attribute, with no key, gives the same IR. It is at the end of the first line, so no location moves.
        File.Delete(workspace.PathOf("morphir.toml"));
        workspace.Write("Totals.cs", Totals.Replace("using System.Collections.Generic;", "using System.Collections.Generic; [assembly: Morphir.MorphirLoop(Morphir.LoopLowering.Recursion)]", StringComparison.Ordinal));
        var byAttribute = await Compile(workspace, "--json --input Totals.cs --package-name acme/demo");
        byAttribute.Exit.Should().Be(0, byAttribute.Text);
        File.ReadAllText(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().Be(keyIr, "the key and the attribute have the same effect");
    }

    [Test]
    public async Task AnAttributeOnTheMethodWinsOverTheLoopKey()
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Totals.cs", Totals.Replace("    public static int Total", "    [Morphir.MorphirLoop(Morphir.LoopLowering.Traversal)]\n    public static int Total", StringComparison.Ordinal));
        workspace.Write("morphir.toml", "[frontend.csharp]\nloop_lowering = \"recursion\"\n");
        var run = await Compile(workspace, "--json --input Totals.cs --package-name acme/demo");
        run.Exit.Should().Be(0, run.Text);
        ValueKinds(workspace).Should().Be("no LetRecursion", "the attribute on the member wins over morphir.toml");
    }

    [Test]
    [Arguments("loop_lowering = \"sideways\"")]
    [Arguments("loop_lowering = true")]
    [Arguments("traversal = \"functional\"")]
    public async Task AMalformedKeyOfTheCSharpFrontendTableFailsBeforeCompilationWithExitCode2(string line)
    {
        using var workspace = new CompileWorkspace();
        workspace.Write("Totals.cs", Totals);
        workspace.Write("morphir.toml", "[frontend.csharp]\n" + line + "\n");
        var run = await Compile(workspace, "--json --input Totals.cs --package-name acme/demo");
        run.Exit.Should().Be(2, run.Text);
        using var report = JsonDocument.Parse(run.Text);
        report.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        run.Text.Should().Contain("frontend.csharp");
        File.Exists(workspace.PathOf(".morphir/out/compile.dest/morphir-ir.json")).Should().BeFalse();
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
