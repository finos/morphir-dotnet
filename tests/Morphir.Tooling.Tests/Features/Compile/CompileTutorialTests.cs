// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Keeps the compile guides true: the F# pricing tutorial and the quickstart. The tutorial's marked code blocks
/// (a first line such as <c>// src/Rates.fs</c> or <c># morphir.toml</c>) equal the files of
/// <c>examples/car-rental-pricing</c>, and the documented commands give the documented results with the real
/// <c>dotnet-morphir</c>.
/// </summary>
[NotInParallel(nameof(CompileTutorialTests))]
public sealed partial class CompileTutorialTests
{
    private static string Root => CompileDocumentationTests.RepositoryRoot();

    private static string Example => Path.Combine(Root, "examples", "car-rental-pricing");

    private static string Page(string relative) =>
        File.ReadAllText(Path.Combine(Root, "docs", "content", "docs", relative)).Replace("\r\n", "\n");

    [GeneratedRegex("```(?<language>[a-z]+)\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex("^(?:(?://|#) (?<file>[A-Za-z./]+)|<!-- (?<file>[A-Za-z./]+) -->)$")]
    private static partial Regex FileMarker();

    /// <summary>The code blocks whose first line names an example file, by that file.</summary>
    private static Dictionary<string, string> MarkedBlocks(string page) =>
        CodeBlock().Matches(page)
            .Select(match => match.Groups["body"].Value.Split('\n', 2))
            .Select(lines => (Marker: FileMarker().Match(lines[0]), Body: lines.Length > 1 ? lines[1] : ""))
            .Where(item => item.Marker.Success)
            .ToDictionary(item => item.Marker.Groups["file"].Value, item => item.Body);

    private static string ExampleFile(string relative) =>
        File.ReadAllText(Path.Combine(Example, relative)).Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>A copy of the example in a sandbox, so a test run writes nothing in the repository.</summary>
    private static string CopyExample(TestSandbox sandbox)
    {
        var project = sandbox.PathOf("acme");
        foreach (var file in Directory.GetFiles(Example, "*", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var target = Path.Combine(project, Path.GetRelativePath(Example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return project;
    }

    private static CliTestHelper Cli() =>
        new(Environment.GetEnvironmentVariable("MORPHIR_DOTNET_TOOL_CLI"), CliHost.DotnetTool);

    [Test]
    public void TheTutorialFilesEqualTheExample()
    {
        var blocks = MarkedBlocks(Page(Path.Combine("guides", "fsharp-pricing-tutorial.md")));
        blocks.Keys.Should().BeEquivalentTo("src/Rates.fs", "src/Quote.fs", "src/Discount.fs", "morphir.toml", "Acme.CarRental.fsproj");

        foreach (var file in new[] { "src/Rates.fs", "src/Quote.fs", "src/Discount.fs" })
            blocks[file].Should().Be(ExampleFile(file), file);

        // The toml block starts with its marker line; the rest is the file.
        blocks["morphir.toml"].Should().Be(ExampleFile("morphir.toml"));

        // Step 1 shows the project file before step 4 adds Discount.fs.
        var withDiscount = blocks["Acme.CarRental.fsproj"].Replace(
            "    <Compile Include=\"src/Quote.fs\" />\n",
            "    <Compile Include=\"src/Quote.fs\" />\n    <Compile Include=\"src/Discount.fs\" />\n");
        withDiscount.Should().Be(ExampleFile("Acme.CarRental.fsproj"));
    }

    [Test]
    public void TheQuickstartRuleEqualsTheExample()
    {
        var rule = CodeBlock().Matches(Page(Path.Combine("getting-started", "compile-fsharp.md")))
            .First(match => match.Groups["language"].Value == "fsharp").Groups["body"].Value;
        rule.Should().Be(ExampleFile("src/Rates.fs"));
    }

    [Test]
    public async Task TheTutorialCommandsGiveTheDocumentedResults()
    {
        await using var sandbox = TestSandbox.Create("morphir-compile-tutorial-");
        var project = CopyExample(sandbox);
        var cli = Cli();
        var timeout = TimeSpan.FromMinutes(3);

        // Step 6: the JSON report.
        var compiled = await cli.ExecuteCommandAsync($"compile --json --project \"{project}\"", timeout: timeout);
        compiled.ExitCode.Should().Be(0, compiled.CombinedOutput);
        using (var report = JsonDocument.Parse(compiled.StandardOutput))
        {
            report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            report.RootElement.GetProperty("modules").EnumerateArray().Select(item => item.GetString())
                .Should().Equal("Rates", "Quote", "Discount");
            report.RootElement.GetProperty("diagnostics").GetArrayLength().Should().Be(0);
        }

        // Step 5: generate from the most recent compile.
        var generatedDirectory = Path.Combine(project, "BookingApp", "Generated");
        var generated = await cli.ExecuteCommandAsync(
            $"generate --target csharp --output \"{generatedDirectory}\" --project \"{project}\"", timeout: timeout);
        generated.ExitCode.Should().Be(0, generated.CombinedOutput);
        Directory.GetFiles(generatedDirectory, "*.g.cs", SearchOption.AllDirectories).Select(Path.GetFileName)
            .Should().BeEquivalentTo("m_RatesModule.g.cs", "m_QuoteModule.g.cs", "m_DiscountModule.g.cs");

        // Step 6: classic IR version 3, installed with -o, and validated.
        var dist = Path.Combine(project, "dist");
        var classic = await cli.ExecuteCommandAsync($"compile --ir-version 3 -o \"{dist}\" --project \"{project}\"", timeout: timeout);
        classic.ExitCode.Should().Be(0, classic.CombinedOutput);
        var validated = await cli.ExecuteCommandAsync($"validate --input \"{Path.Combine(dist, "morphir-ir.json")}\"", timeout: timeout);
        validated.ExitCode.Should().Be(0, validated.CombinedOutput);
        validated.StandardOutput.Should().Contain("0 errors, 0 warnings, 0 info, 0 hints. Analyzers: 1 ran, 0 not run, 0 failed.");

        // Step 7: compile and generate in one run.
        var oneRun = await cli.ExecuteCommandAsync(
            $"compile --target csharp --generate-output \"{generatedDirectory}\" --project \"{project}\"", timeout: timeout);
        oneRun.ExitCode.Should().Be(0, oneRun.CombinedOutput);
        oneRun.StandardError.Should().Contain("generated 3 artifact(s) with morphir.csharp");
    }

    [Test]
    public async Task TheGuardedDiscountIsRefusedAsDocumented()
    {
        await using var sandbox = TestSandbox.Create("morphir-compile-tutorial-guard-");
        var project = CopyExample(sandbox);
        var guarded = CodeBlock().Matches(Page(Path.Combine("guides", "fsharp-pricing-tutorial.md")))
            .Select(match => match.Groups["body"].Value)
            .Single(body => body.Contains("| d when d >= 7"));
        File.WriteAllText(Path.Combine(project, "src", "Discount.fs"), guarded + "\n");

        var result = await Cli().ExecuteCommandAsync($"compile --project \"{project}\"", timeout: TimeSpan.FromMinutes(3));

        result.ExitCode.Should().Be(1, result.CombinedOutput);
        result.StandardError.Should().Contain(
            "src/Discount.fs:6:18: error fsharp.unsupported_pattern_guard: Pattern guards ('when') are not supported; a Morphir pattern has no condition. Match the case, then use 'if' in its body.");
        result.StandardError.Should().Contain("compile failed (source, exit code 1); 1 error(s): error fsharp.unsupported_pattern_guard");
    }
}
