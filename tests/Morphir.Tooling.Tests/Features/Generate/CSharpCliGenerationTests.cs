using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Lang.CSharp.Backend;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Tests.Integration.CLI;

using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// Compares the output of the CLI with the result of the generator in this process, so it needs the generator
/// assembly and stays here. The cases that need only the CLI are black-box scenarios in
/// tests/Morphir.BlackBox.Tests/Integration/Generation.feature.
/// </summary>
[Category("PlatformSpecific")]
public sealed class CSharpCliGenerationTests : IDisposable
{
    public void Dispose() => TolerantDirectory.TryDelete(_root);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "morphir-csharp-cli-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(_root, "output");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private async Task<CliExecutionResult> Invoke(string input, bool json = true, string? output = null, params string[] options)
    {
        var arguments = new List<string> { "generate", "--input", input, "--target", "csharp", "--output", output ?? Output };
        if (json) arguments.Add("--json=document");
        foreach (var option in options) { arguments.Add("--option"); arguments.Add(option); }
        return await CliTestHelper.ForTarget()
            .ExecuteCommandAsync(string.Join(" ", arguments.Select(Quote)), timeout: TimeSpan.FromSeconds(60));
    }

    [Test]
    [Arguments("1", "json")]
    [Arguments("2", "json")]
    [Arguments("3", "json")]
    [Arguments("3.0.0", "json")]
    [Arguments("3.1.0", "json")]
    [Arguments("4", "json")]
    [Arguments("4.0.0", "json")]
    [Arguments("3.0.0", "text")]
    [Arguments("3.0.0", "binary")]
    [Arguments("3.0.0", "tree")]
    [Arguments("3.1.0", "text")]
    [Arguments("3.1.0", "binary")]
    [Arguments("3.1.0", "tree")]
    [Arguments("4.0.0", "text")]
    [Arguments("4.0.0", "binary")]
    [Arguments("4.0.0", "tree")]
    public async Task EveryAdmittedWireFormUsesTheRealProviderAndMatchesCoreBytes(string version, string format)
    {
        var input = GenerationInputFixtures.DailyRate(_root, version, format);
        var result = await Invoke(input, options: ["namespace=Pricing.Generated"]);
        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var report = JsonDocument.Parse(result.StandardOutput);
        report.RootElement.GetProperty("backend").GetString().Should().Be("morphir.csharp");
        report.RootElement.GetProperty("sourceVersion").GetString().Should().Be(version);
        var loaded = ((Outcome<LoadedModel>.Success)await new GenerationModelLoader(new BoundedModelCapture()).LoadAsync(new(input), ModelReadLimits.Default, default)).Value;
        var direct = Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(loaded.Model, GenerationOptions.Default, NamespaceOverride.TryCreate("Pricing.Generated").ResultValue, default);
        direct.Status.IsSuccess.Should().BeTrue();
        report.RootElement.GetProperty("artifacts").EnumerateArray().Select(x => x.GetString()).Should().Equal(direct.Sources.Select(x => x.RelativePath));
        foreach (var source in direct.Sources)
            File.ReadAllBytes(Path.Combine(Output, source.RelativePath)).Should().Equal(Encoding.UTF8.GetBytes(source.Text));
    }

    /// <summary>
    /// The CLI and the pipeline function of the source generator and the MSBuild task give the same paths and bytes
    /// (csharp-build-generation, "Same bytes"). The black-box scenario of the package compares the CLI with the target and
    /// the generator in a user build.
    /// </summary>
    [Test]
    [Arguments("2")]
    [Arguments("3")]
    [Arguments("3.1.0")]
    [Arguments("4")]
    [Arguments("4.0.0")]
    public async Task TheCliWritesThePathsAndBytesOfThePipelineFunction(string version)
    {
        var input = GenerationInputFixtures.DailyRate(_root, version, "json");
        var result = await Invoke(input, options: ["namespace=Pricing.Generated"]);
        result.ExitCode.Should().Be(0, result.CombinedOutput);

        var request = Morphir.Lang.CSharp.CSharpPipelineRequest.Create(File.ReadAllBytes(input))
            .WithNamespace(NamespaceOverride.TryCreate("Pricing.Generated").ResultValue);
        var piped = Morphir.Lang.CSharp.CSharpPipeline.Generate(request, default);
        piped.Succeeded.Should().BeTrue(string.Join("; ", piped.Diagnostics));

        var written = Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Output, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != GenerationDefaults.OwnershipManifest)
            .Order(StringComparer.Ordinal);
        written.Should().Equal(piped.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
        foreach (var file in piped.Files)
            File.ReadAllBytes(Path.Combine(Output, file.RelativePath)).Should().Equal(Encoding.UTF8.GetBytes(file.Text), file.RelativePath);
    }
}
