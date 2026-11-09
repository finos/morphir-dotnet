using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Scenarios;
using Morphir.Tooling.Tests.Integration.CLI;
using Morphir.Extensions;
using Morphir.TestSupport;

namespace Morphir.Tooling.Tests.Features.Generate;

public sealed class GenerationReportingTests
{
    [Test]
    [Arguments(GenerationFailureKind.Semantic, 1)]
    [Arguments(GenerationFailureKind.Invocation, 2)]
    [Arguments(GenerationFailureKind.Operational, 2)]
    [Arguments(GenerationFailureKind.Cancelled, 130)]
    public void SourceGeneratedFailureReportsHaveTypedExitCodes(GenerationFailureKind kind, int code)
    {
        var report = GenerationReport.From(new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(kind, "example-rule", "A structured failure.", "model.ion", "$[1]")), new(Input: "model.ion"));
        report.ExitCode.Should().Be(code);
        using var json = JsonDocument.Parse(report.ToJson());
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(code);
        json.RootElement.GetProperty("diagnostics")[0].GetProperty("location").GetString().Should().Be("$[1]");
        json.RootElement.GetProperty("artifacts").GetArrayLength().Should().Be(0);
    }

    [Test]
    public void ADiagnosticWithoutARangeKeepsTheExistingReportShape()
    {
        var report = GenerationReport.From(new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Semantic,
            "example-rule", "A structured failure.", "model.json", "$")), new(Input: "model.json"));
        using var json = JsonDocument.Parse(report.ToJson());
        json.RootElement.GetProperty("diagnostics")[0].EnumerateObject().Select(member => member.Name)
            .Should().Equal("code", "message", "path", "location", "severity");
    }

    [Test]
    public void ADiagnosticRangeIsAnAdditiveOptionalMember()
    {
        var range = new DiagnosticRange(new DiagnosticPosition(11, 8), new DiagnosticPosition(11, 15));
        var failure = new GenerationFailure(GenerationFailureKind.Semantic,
            [new GenerationDiagnostic("fsharp.unsupported_mutable", "Mutable bindings are not supported.", "Pricing.fs", Range: range)]);
        var report = GenerationReport.From(new Outcome<GenerationSuccess>.Failure(failure), new(Input: "model.json"));
        using var json = JsonDocument.Parse(report.ToJson());
        var diagnostic = json.RootElement.GetProperty("diagnostics")[0];
        diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(11);
        diagnostic.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32().Should().Be(15);
    }

    [Test]
    public async Task OperationalCliFailureReturnsTwoAndOneJsonDocument() =>
        await AssertOperationalFailureAsync(new CliTestHelper(), TimeSpan.FromSeconds(30));

    // Test runners run many CLI children through the shared process gate, so a command can wait for a slot
    // longer than its own budget. The budget is for the command, so the wait must not use it.
    [Test]
    public async Task OperationalCliFailureBudgetStartsAfterTheProcessSlotIsGranted()
    {
        var gate = new ProcessGate(1);
        var budget = TimeSpan.FromSeconds(15);
        var busy = ChildProcess.RunAsync(Sleep(budget + TimeSpan.FromSeconds(3)), TimeSpan.FromSeconds(60), gate: gate);
        await Task.Delay(TimeSpan.FromMilliseconds(500)); // The busy child takes the only slot first.

        await AssertOperationalFailureAsync(new CliTestHelper(gate: gate), budget);
        await busy;
    }

    private static async Task AssertOperationalFailureAsync(CliTestHelper cli, TimeSpan budget)
    {
        var missing = Path.Combine(Path.GetTempPath(), "missing-model-" + Guid.NewGuid().ToString("N") + ".json");
        var output = Path.Combine(Path.GetTempPath(), "unused-output-" + Guid.NewGuid().ToString("N"));
        var result = await cli.ExecuteCommandAsync($"generate --input \"{missing}\" --target fsharp --output \"{output}\" --json", timeout: budget);
        result.ExitCode.Should().Be(2);
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("failureKind").GetString().Should().Be("operational");
        Directory.Exists(output).Should().BeFalse();
    }

    private static ProcessStartInfo Sleep(TimeSpan duration) =>
        OperatingSystem.IsWindows()
            ? ChildProcess.StartInfo("cmd.exe", "/c", $"ping -n {(int)duration.TotalSeconds + 1} 127.0.0.1 >nul")
            : ChildProcess.StartInfo("/bin/sh", "-c", $"sleep {(int)duration.TotalSeconds}");

    [Test]
    public async Task HostCancellationReachesTheHandlerAndReturns130BeforePublication()
    {
        var root = Path.Combine(Path.GetTempPath(), "morphir-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = GenerationInputFixtures.DailyRate(root, "3", "json");
            var output = Path.Combine(root, "output");
            var slow = new SlowBackend();
            using var app = MorphirCli.CreateBuilder(["generate", "--input", input, "--target", "slow", "--output", output, "--json"])
                .AddScenario<GenerationScenario>().ConfigureServices(services => services.AddSingleton<IGenerationBackend>(slow)).Build();
            using var cancellation = new CancellationTokenSource();
            var running = app.RunAsync(cancellation.Token);
            await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            (await running.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(130);
            Directory.Exists(output).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed record Options : ValidatedBackendOptions;
    private sealed class SlowBackend : IGenerationBackend
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("test.slow"), ExtensionName.Create("Slow test backend"), TargetId.Create("slow"), [new(ModelFamily.Classic, SourceFormatVersions.Create("3"), DistributionKind.Library)]);
        public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) => new Outcome<ValidatedBackendOptions>.Success(new Options());
        public async ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
        {
            Entered.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return new Outcome<GenerationArtifacts>.Failure(GenerationFailure.One(GenerationFailureKind.Operational, "token-not-propagated", "The host did not propagate cancellation."));
        }
    }
}
