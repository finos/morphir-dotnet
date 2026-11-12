using System.Text.RegularExpressions;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Validate;

/// <summary>
/// The configuration example of docs/content/docs/cli/validate.md gives its documented output, with the validation
/// library in the test process. <c>model.json</c> in the page is the classic fixture with a type mismatch. The commands
/// of the page run through the CLI in tests/Morphir.BlackBox.Tests/Integration/ValidationDocumentation.feature.
/// </summary>
[Category("PlatformSpecific")]
public sealed class ValidateDocumentationTests
{
    private static readonly string Model = ModelValidationTests.Fixture("daily-rate-mismatch-v3.json");

    private static string Page()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Morphir.slnx")))
            root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "docs", "content", "docs", "cli", "validate.md")).Replace("\r\n", "\n");
    }

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
}
