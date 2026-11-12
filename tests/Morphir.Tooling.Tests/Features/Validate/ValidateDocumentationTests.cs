using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;
using Morphir.Tooling.Tests.Integration.CLI;

namespace Morphir.Tooling.Tests.Features.Validate;

/// <summary>
/// Every command in docs/content/docs/cli/validate.md runs against the test fixtures, and its documented output is the
/// real output. <c>model.json</c> in the page is the classic fixture with a type mismatch.
/// </summary>
[Category("PlatformSpecific")]
public sealed partial class ValidateDocumentationTests
{
    private static readonly string Model = ModelValidationTests.Fixture("daily-rate-mismatch-v3.json");

    /// <summary>The <c>$ morphir ...</c> commands of the bash blocks, with the lines that follow them as the documented output.</summary>
    private static string Page()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Morphir.slnx")))
            root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "docs", "content", "docs", "cli", "validate.md")).Replace("\r\n", "\n");
    }

    private static IReadOnlyList<(string Command, string Output)> DocumentedCommands()
    {
        return BashBlock().Matches(Page())
            .Select(match => match.Groups["body"].Value.Split('\n', 2))
            .Where(lines => lines[0].StartsWith("$ morphir ", StringComparison.Ordinal))
            .Select(lines => (lines[0]["$ morphir ".Length..], lines.Length > 1 ? lines[1] : ""))
            .ToArray();
    }

    [GeneratedRegex("```bash\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex BashBlock();

    /// <summary>The configuration example: its TOML and the output that the page shows for it.</summary>
    [Test]
    public async Task TheConfigurationExampleTurnsOffTheTypeValidatorWithTheDocumentedOutput()
    {
        var page = Page();
        var toml = Regex.Match(page, "```toml\n(?<body>.*?)\n```", RegexOptions.Singleline).Groups["body"].Value;
        var start = page.IndexOf("With this `morphir.toml`", StringComparison.Ordinal);
        var documented = Regex.Match(page[start..], "```text\n(?<body>.*?)\n```", RegexOptions.Singleline).Groups["body"].Value;
        await using var sandbox = TestSandbox.Create("morphir-validate-docs-");
        await File.WriteAllTextAsync(Path.Combine(sandbox.Work, "morphir.toml"), toml + "\n");
        File.Copy(Model, Path.Combine(sandbox.Work, "model.json"));
        var host = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));
        var validation = new ModelValidation(new GenerationModelLoader(new BoundedModelCapture()), new AnalyzerRegistry(host),
            new LayeredAnalyzerConfiguration(sandbox.EnvironmentSnapshot(Containment.CurrentEnvironment())), host);
        var report = await validation.RunAsync(new ValidateOverrides("model.json"), new InvocationContext(sandbox.Work), default);
        report.ToText(TerminalProfile.Plain).Replace("\r\n", "\n").Should().Be(documented);
    }

    [Test]
    public void ThePageDocumentsTheTextAndJsonCommands() =>
        DocumentedCommands().Select(item => item.Command).Should().Equal(
            "validate --input model.json", "validate --input model.json --json");

    [Test]
    public async Task EachDocumentedCommandGivesItsDocumentedOutput()
    {
        foreach (var (command, documented) in DocumentedCommands())
        {
            var result = await new CliTestHelper(Environment.GetEnvironmentVariable("MORPHIR_GENERATION_CLI"))
                .ExecuteCommandAsync(command.Replace("model.json", $"\"{Model}\""), timeout: TimeSpan.FromSeconds(60));
            result.ExitCode.Should().Be(1, result.CombinedOutput);
            var actual = result.StandardOutput.Replace("\r\n", "\n");
            if (documented.StartsWith('{'))
            {
                // The documented input is an example path.
                var expected = JsonNode.Parse(documented)!.AsObject();
                var written = JsonNode.Parse(actual)!.AsObject();
                written["input"]!.GetValue<string>().Should().Be(Model);
                expected.Remove("input");
                written.Remove("input");
                written.ToJsonString().Should().Be(expected.ToJsonString());
            }
            else
                actual.Should().Be(documented);
        }
    }
}
