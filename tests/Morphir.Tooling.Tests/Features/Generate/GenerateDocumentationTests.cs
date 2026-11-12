// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Tests.Features.Compile;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// Task 5.5: the two-step example of docs/content/docs/cli/generate.md ("Input from a prior compile") runs with
/// the real <c>dotnet-morphir</c> on the sources of the MCK corpus case <c>frontend-fsharp-0116</c>. Each CLI
/// run has its own sandbox directory, so the test gives each command <c>--project</c> and an absolute output.
/// </summary>
[Category("PlatformSpecific")]
public sealed partial class GenerateDocumentationTests
{
    private static string Section()
    {
        var page = File.ReadAllText(Path.Combine(CompileDocumentationTests.RepositoryRoot(), "docs", "content", "docs", "cli", "generate.md"))
            .Replace("\r\n", "\n");
        var start = page.IndexOf("## Input from a prior compile\n", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return page[start..end];
    }

    /// <summary>Each <c>$ </c> line of the section's bash block and the output lines after it.</summary>
    private static IReadOnlyList<(string Command, string[] Output)> Commands()
    {
        var body = BashBlock().Match(Section()).Groups["body"].Value;
        var commands = new List<(string, List<string>)>();
        foreach (var line in body.Split('\n'))
            if (line.StartsWith("$ ", StringComparison.Ordinal)) commands.Add((line[2..], []));
            else commands[^1].Item2.Add(line);
        return [.. commands.Select(command => (command.Item1, command.Item2.ToArray()))];
    }

    [GeneratedRegex("```bash\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex BashBlock();

    [GeneratedRegex("```toml\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex TomlBlock();

    [Test]
    public void TheSectionDocumentsCompileThenGenerate() =>
        Commands().Select(command => command.Command).Should().Equal(
            "dotnet-morphir compile", "dotnet-morphir generate --target fsharp --output gen");

    [Test]
    public async Task TheDocumentedTwoStepExampleRuns()
    {
        await using var sandbox = TestSandbox.Create("morphir-generate-docs-");
        var project = sandbox.PathOf("demo");
        Directory.CreateDirectory(Path.Combine(project, "src"));
        File.WriteAllText(Path.Combine(project, "morphir.toml"), TomlBlock().Match(Section()).Groups["body"].Value + "\n");
        foreach (var document in CompileDocumentationTests.CaseDocuments("frontend-fsharp-0116"))
            File.WriteAllText(Path.Combine(project, "src", document.Uri), document.Text);

        var cli = new CliTestHelper(Environment.GetEnvironmentVariable("MORPHIR_DOTNET_TOOL_CLI"), CliHost.DotnetTool);
        var (compile, compileOutput) = Commands()[0];
        var compiled = await cli.ExecuteCommandAsync($"{compile.Split(' ', 2)[1]} --project \"{project}\"", timeout: TimeSpan.FromSeconds(120));
        compiled.ExitCode.Should().Be(0, compiled.CombinedOutput);
        var documentedIr = compileOutput.Single();
        var publishedIr = Path.Combine(project, ".morphir", "out", "compile.dest", "morphir-ir.json");
        compiled.StandardOutput.Should().Be(publishedIr);
        publishedIr.Replace('\\', '/').Should().EndWith(documentedIr["/work/demo".Length..]);

        var (generate, generateOutput) = Commands()[1];
        var arguments = generate.Split(' ', 2)[1].Replace("--output gen", $"--output \"{Path.Combine(project, "gen")}\"");
        var generated = await cli.ExecuteCommandAsync($"{arguments} --project \"{project}\"", timeout: TimeSpan.FromSeconds(120));
        generated.ExitCode.Should().Be(0, generated.CombinedOutput);
        generated.StandardOutput.Replace("\r\n", "\n").Split('\n').Should().Equal(generateOutput);
        foreach (var file in generateOutput) File.Exists(Path.Combine(project, "gen", file)).Should().BeTrue(file);
    }
}
