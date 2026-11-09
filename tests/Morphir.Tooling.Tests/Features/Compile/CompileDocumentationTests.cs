// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.FSharp.Collections;
using Morphir.Conformance;
using Morphir.TestSupport;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Compile;

/// <summary>
/// Task 4.8: every command in docs/content/docs/cli/compile.md runs against the MCK corpus fixtures, and gives
/// its documented output. <c>output</c>, <c>sources.root</c> and <c>generation.output</c> show example paths, so
/// they are not compared. Task 6.2 adds the <c>--target</c> example.
/// </summary>
public sealed partial class CompileDocumentationTests
{
    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Morphir.slnx"))) root = root.Parent;
        return root!.FullName;
    }

    private static IReadOnlyList<(string Host, string Command, string Output)> DocumentedCommands()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "content", "docs", "cli", "compile.md")).Replace("\r\n", "\n");
        return BashBlock().Matches(page)
            .Select(match => match.Groups["body"].Value.Split('\n', 2))
            .Where(lines => lines[0].StartsWith("$ ", StringComparison.Ordinal))
            .Select(lines => (Host: lines[0][2..].Split(' ')[0], Command: lines[0][2..].Split(' ', 2)[1], Output: lines.Length > 1 ? lines[1] : ""))
            .ToArray();
    }

    [GeneratedRegex("```bash\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex BashBlock();

    /// <summary>The documents of one corpus case, by its id prefix.</summary>
    private static IEnumerable<Morphir.Conformance.SourceDocument> CaseDocuments(string id)
    {
        var root = Path.Combine(RepositoryRoot(), "conformance");
        var artifacts = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => new Morphir.Conformance.Artifact(Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllBytes(file)));
        var read = Morphir.Conformance.Corpus.read(
            new CorpusOptions("frontend-fsharp", "1.1", ListModule.OfSeq(["1", "2", "3", "4"]), ListModule.OfSeq(["files", "directory", "project file"])), ListModule.OfSeq(artifacts));
        return read.ResultValue.Cases.First(c => c.Id.StartsWith(id, StringComparison.Ordinal)).Input.Documents;
    }

    [Test]
    public void ThePageDocumentsTheExpectedCommands() =>
        DocumentedCommands().Select(item => $"{item.Host} {item.Command.Split(' ').Last()}").Should().Equal(
            "dotnet-morphir --json", "dotnet-morphir --json", "dotnet-morphir --json", "dotnet-morphir --json", "morphir --json",
            "dotnet-morphir --json-lines");

    [Test]
    public async Task EachDocumentedCommandGivesItsDocumentedOutput()
    {
        await using var sandbox = TestSandbox.Create("morphir-compile-docs-");
        foreach (var document in CaseDocuments("frontend-fsharp-0116")) Write(sandbox.PathOf("src", document.Uri), document.Text);
        foreach (var document in CaseDocuments("frontend-fsharp-0001")) Write(sandbox.PathOf("broken", document.Uri), document.Text);

        foreach (var (host, command, documented) in DocumentedCommands())
        {
            var cli = new CliTestHelper(host == "morphir" ? Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI") : Environment.GetEnvironmentVariable("MORPHIR_DOTNET_TOOL_CLI"),
                host == "morphir" ? CliHost.Morphir : CliHost.DotnetTool);
            var arguments = Regex.Replace(command, @"(?<= )(src|broken)/", match => sandbox.PathOf(match.Groups[1].Value) + "/");
            var result = await cli.ExecuteCommandAsync(arguments, timeout: TimeSpan.FromSeconds(120));
            var actual = result.StandardOutput.Replace("\r\n", "\n").Split('\n');
            var expected = documented.Split('\n');
            actual.Length.Should().Be(expected.Length, $"{command}\n{result.CombinedOutput}");
            if (command.EndsWith("--json", StringComparison.Ordinal))
                Normalize(JsonNode.Parse(result.StandardOutput)!).Should().Be(Normalize(JsonNode.Parse(documented)!), command);
            else
                for (var line = 0; line < expected.Length; line++)
                    Normalize(JsonNode.Parse(actual[line])!).Should().Be(Normalize(JsonNode.Parse(expected[line])!), command);
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>The JSON text without the example paths.</summary>
    private static string Normalize(JsonNode node)
    {
        var report = node["report"]?.AsObject() ?? (node is JsonObject root && root.ContainsKey("success") ? root : null);
        if (report is not null)
        {
            report.Remove("output");
            report["sources"]?.AsObject().Remove("root");
            report["generation"]?.AsObject().Remove("output");
        }
        return node.ToJsonString();
    }
}
