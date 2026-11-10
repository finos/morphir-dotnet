// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Tests.Features.Generate;

/// <summary>
/// The F# backend option <c>format</c>: true (the default) formats with Fantomas when the host can, false writes the
/// emitter's own text with LF line endings and one final LF. The report says what happened, and a host that cannot
/// format under the default says so on stderr, never on stdout.
/// </summary>
public class GenerateFormattingTests
{
    private static string Model => Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirIR", "daily-rate-v4.json");

    private static BackendOptions Options(params (string Key, string Json)[] values) =>
        new(values.Select(value => KeyValuePair.Create(value.Key, JsonDocument.Parse(value.Json).RootElement.Clone())));

    private static async Task<GenerationSuccess> Generate(string output, params string[] options)
    {
        var outcome = await GenerateHandler.Handle(
            new GenerateRequest(new GenerateOverrides(Model, "fsharp", output, null, options), new InvocationContext(Path.GetDirectoryName(output)!)),
            new CliGenerationSettingsResolver(), new GenerationModelLoader(new BoundedModelCapture()),
            new RegisteredBackendResolver([new FSharpGenerationBackend()]), new GenerationPublisher(), default);
        return outcome.Should().BeOfType<Outcome<GenerationSuccess>.Success>(outcome.ToString()).Subject.Value;
    }

    [Test]
    [Arguments("true")]
    [Arguments("false")]
    public async Task TheFSharpBackendAcceptsTheBooleanOptionFormat(string value)
    {
        new FSharpGenerationBackend().ValidateOptions(Options(("format", value)))
            .Should().BeOfType<Outcome<ValidatedBackendOptions>.Success>();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ANonBooleanFormatIsRefusedAndTheMessageNamesBothOptions()
    {
        var refused = new FSharpGenerationBackend().ValidateOptions(Options(("format", "\"off\"")));
        var failure = refused.Should().BeOfType<Outcome<ValidatedBackendOptions>.Failure>().Subject.Error;
        failure.Diagnostics.Single().Message.Should().Contain("optimize").And.Contain("format");
        await Task.CompletedTask;
    }

    [Test]
    public async Task FormatFalseWritesLfTextWithOneFinalLfAndTheReportSaysOff()
    {
        using var sandbox = TestSandbox.Create("morphir-format-");
        var success = await Generate(sandbox.PathOf("off"), "format=false");

        success.Formatting.Should().Be("off");
        foreach (var path in success.Publication.Artifacts)
        {
            var text = await File.ReadAllTextAsync(Path.Combine(success.OutputDirectory, path));
            text.Should().NotContain("\r", path).And.EndWith("\n", path);
            text.Should().NotEndWith("\n\n", path);
        }

        var report = JsonNode.Parse(GenerationReport.From(new Outcome<GenerationSuccess>.Success(success), new()).ToJson())!;
        report["formatting"]!.GetValue<string>().Should().Be("off");
    }

    [Test]
    public async Task TheDefaultFormatsUnderJitAndTheReportSaysApplied()
    {
        using var sandbox = TestSandbox.Create("morphir-format-");
        var success = await Generate(sandbox.PathOf("default"));
        success.Formatting.Should().Be("applied");
    }

    [Test]
    public async Task TheFormattingWarningIsForAHostThatCannotFormatOnly()
    {
        GenerateCommand.Warnings(Report("unavailable")).Should().ContainSingle()
            .Which.Should().StartWith("warning:").And.Contain("format=false");
        GenerateCommand.Warnings(Report("applied")).Should().BeEmpty();
        GenerateCommand.Warnings(Report("off")).Should().BeEmpty();
        GenerateCommand.Warnings(Report(null)).Should().BeEmpty();
        await Task.CompletedTask;

        static GenerationReport Report(string? formatting) => GenerationReport.From(
            new Outcome<GenerationSuccess>.Success(new GenerationSuccess("fsharp", "morphir.fsharp", "model.json", "gen", "4", "v4", "library", "json",
                new GenerationPublication([], [], [], []), [])
            { Formatting = formatting }), new());
    }

    /// <summary>
    /// Runs the JIT <c>morphir</c> with the runtime's dynamic-code feature switch off, the switch that is false
    /// under Native AOT, so the F# backend cannot run Fantomas as on the Native AOT host.
    /// </summary>
    private static async Task<ChildProcessResult> RunWithoutDynamicCode(TestSandbox sandbox, params string[] arguments)
    {
        var configuration = typeof(GenerateFormattingTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var bin = Path.Combine(GenerateSteps.RepositoryRoot(), "src", "Morphir", "bin", configuration, "net10.0");
        var runtimeConfig = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(bin, "morphir.runtimeconfig.json")))!;
        var runtimeOptions = runtimeConfig["runtimeOptions"]!.AsObject();
        if (runtimeOptions["configProperties"] is not JsonObject properties)
            runtimeOptions["configProperties"] = properties = new JsonObject();
        properties["System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"] = false;
        var custom = sandbox.PathOf("no-dynamic-code.runtimeconfig.json");
        await File.WriteAllTextAsync(custom, runtimeConfig.ToJsonString());

        var start = ChildProcess.StartInfo("dotnet", ["exec", "--runtimeconfig", custom, Path.Combine(bin, "morphir.dll"), .. arguments]);
        start.WorkingDirectory = sandbox.Work;
        sandbox.Apply(start);
        return await ChildProcess.RunAsync(start, TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task OnAHostThatCannotFormatTheDefaultWarnsOnStderrOnlyAndTheJsonReportSaysUnavailable()
    {
        using var sandbox = TestSandbox.Create("morphir-format-host-");
        var run = await RunWithoutDynamicCode(sandbox, "generate", "--input", Model, "--target", "fsharp", "--output", sandbox.PathOf("gen"), "--json");

        run.ExitCode.Should().Be(0, run.CombinedOutput);
        run.StandardError.Should().Contain("warning:").And.Contain("format=false");
        run.StandardOutput.Should().NotContain("warning", "warnings never go to stdout");
        JsonNode.Parse(run.StandardOutput)!["formatting"]!.GetValue<string>().Should().Be("unavailable");
    }

    [Test]
    public async Task OnAHostThatCannotFormatHumanModeKeepsStdoutToTheArtifactPaths()
    {
        using var sandbox = TestSandbox.Create("morphir-format-host-");
        var run = await RunWithoutDynamicCode(sandbox, "generate", "--input", Model, "--target", "fsharp", "--output", sandbox.PathOf("gen"));

        run.ExitCode.Should().Be(0, run.CombinedOutput);
        run.StandardError.Should().Contain("warning:");
        run.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Should().OnlyContain(line => line.EndsWith(".fs", StringComparison.Ordinal), "stdout lists only the artifacts");
    }

    [Test]
    public async Task WithFormatFalseAHostThatCannotFormatWritesTheSameBytesAsJitAndDoesNotWarn()
    {
        using var sandbox = TestSandbox.Create("morphir-format-host-");
        var run = await RunWithoutDynamicCode(sandbox, "generate", "--input", Model, "--target", "fsharp", "--output", sandbox.PathOf("nodyn"),
            "--option", "format=false", "--json");
        run.ExitCode.Should().Be(0, run.CombinedOutput);
        run.StandardError.Should().NotContain("warning");
        JsonNode.Parse(run.StandardOutput)!["formatting"]!.GetValue<string>().Should().Be("off");

        var jit = await Generate(sandbox.PathOf("jit"), "format=false");
        foreach (var path in jit.Publication.Artifacts)
            (await File.ReadAllBytesAsync(sandbox.PathOf("nodyn", path)))
                .Should().Equal(await File.ReadAllBytesAsync(Path.Combine(jit.OutputDirectory, path)), path);
    }
}
