using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Features.Validate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Features.Validate;

/// <summary><c>morphir validate</c> below the command line: loading, configuration, the runner and the report.</summary>
public sealed class ModelValidationTests
{
    private static readonly HostIdentity Native = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));

    internal static string Fixture(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Morphir.slnx")))
            root = root.Parent;
        return Path.Combine(root!.FullName, "tests", "Fixtures", "MorphirIR", name);
    }

    /// <summary>Turns off the given ids and records whether it was read.</summary>
    private sealed class FixedConfiguration(params string[] turnedOff) : IAnalyzerConfiguration
    {
        public int Reads { get; private set; }

        public Outcome<IReadOnlyList<ExtensionId>> TurnedOff(string workingDirectory, IReadOnlyList<ExtensionId> analyzers, CancellationToken ct)
        {
            Reads++;
            return new Outcome<IReadOnlyList<ExtensionId>>.Success(turnedOff.Select(ExtensionId.Create).ToArray());
        }
    }

    private sealed class BrokenConfiguration : IAnalyzerConfiguration
    {
        public Outcome<IReadOnlyList<ExtensionId>> TurnedOff(string workingDirectory, IReadOnlyList<ExtensionId> analyzers, CancellationToken ct) =>
            new Outcome<IReadOnlyList<ExtensionId>>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation, "config.load", "The configuration could not be read."));
    }

    /// <summary>A classic analyzer with a fixed result, that counts its runs.</summary>
    private sealed class FakeAnalyzer(string id, Func<IReadOnlyList<Morphir.Mep.Diagnostic>> result) : IModelAnalyzer
    {
        public int Runs { get; private set; }

        public ExtensionEntry Entry { get; } = ExtensionEntry.BuiltInValidator(
            ExtensionIdentity.Create(ExtensionId.Create(id), ExtensionName.Create(id), Native.Version, null),
            ClassicThree());

        private static ValidatorClaim ClassicThree() =>
            ValidatorClaim.TryParse(["3"], out var claim, out var failure) ? claim : throw new InvalidOperationException(failure.ToString());

        public Task<IReadOnlyList<Morphir.Mep.Diagnostic>> AnalyzeAsync(DistributionModel model, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(result());
        }
    }

    private static Morphir.Mep.Diagnostic Warning(string message) =>
        MepRecords.CreateDiagnostic(Morphir.Mep.DiagnosticSeverity.Warning, message, "style", null);

    private static ModelValidation Validation(AnalyzerRegistry? analyzers = null, IAnalyzerConfiguration? configuration = null) =>
        new(new GenerationModelLoader(new BoundedModelCapture()), analyzers ?? new AnalyzerRegistry(Native), configuration ?? new FixedConfiguration(), Native);

    private static Task<ValidationReport> Run(string? input, AnalyzerRegistry? analyzers = null, IAnalyzerConfiguration? configuration = null,
        string? format = null, CancellationToken ct = default) =>
        Validation(analyzers, configuration).RunAsync(new ValidateOverrides(input, format), new InvocationContext(Path.GetTempPath()), ct);

    [Test]
    public async Task AClassicModelWithATypeMismatchGivesExitOneAndOneTypeMismatchError()
    {
        var report = await Run(Fixture("daily-rate-mismatch-v3.json"));
        report.ExitCode.Should().Be(1);
        report.Success.Should().BeFalse();
        report.Family.Should().Be("Classic");
        report.FormatVersion.Should().Be("3");
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be("error");
        diagnostic.Code.Should().Be("type-mismatch");
        diagnostic.Extension.Should().Be("morphir.type-validator");
        diagnostic.Message.Should().Contain("String").And.Contain("Int");
        diagnostic.Location.Should().BeNull();
        report.Analyzers.Should().Equal(new ValidationAnalyzer("morphir.type-validator", "ran", null, null));
    }

    [Test]
    public async Task ACleanModelGivesExitZero()
    {
        var report = await Run(Fixture("daily-rate-v3.json"));
        report.ExitCode.Should().Be(0);
        report.Success.Should().BeTrue();
        report.Diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task AMissingInputGivesExitTwoNamesThePathAndRunsNoAnalyzer()
    {
        var analyzer = new FakeAnalyzer("acme.lint", () => []);
        var configuration = new FixedConfiguration();
        var missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".json");
        var report = await Run(missing, new AnalyzerRegistry([analyzer]), configuration);
        report.ExitCode.Should().Be(2);
        report.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain(missing);
        report.Diagnostics[0].Extension.Should().BeNull();
        analyzer.Runs.Should().Be(0);
        report.Analyzers.Should().BeEmpty();
    }

    [Test]
    public async Task AV4ModelGivesOneInfoDiagnosticThatNamesTheFamilyAndExitZero()
    {
        var report = await Run(Fixture("daily-rate-v4.json"));
        report.ExitCode.Should().Be(0);
        report.Family.Should().Be("V4");
        var diagnostic = report.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be("info");
        diagnostic.Code.Should().Be("no-analyzer");
        diagnostic.Message.Should().Contain("V4");
        diagnostic.Extension.Should().BeNull();
        report.Analyzers.Should().Equal(new ValidationAnalyzer("morphir.type-validator", "not-run", "unsupported-model", null));
    }

    [Test]
    [Arguments(null, null, "missing-input")]
    [Arguments("model.json", "yaml", "invalid-input-format")]
    public async Task AnInvalidInvocationGivesExitTwo(string? input, string? format, string code)
    {
        var report = await Run(input, format: format);
        report.ExitCode.Should().Be(2);
        report.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(code);
    }

    [Test]
    public async Task WarningsOnlyGiveExitZeroAndAreInTheResult()
    {
        var report = await Run(Fixture("daily-rate-v3.json"), new AnalyzerRegistry([new FakeAnalyzer("acme.lint", () => [Warning("w")])]));
        report.ExitCode.Should().Be(0);
        report.Diagnostics.Should().ContainSingle().Which.Severity.Should().Be("warning");
    }

    [Test]
    public async Task AFailingAnalyzerGivesExitTwoNamesItAndKeepsTheOtherDiagnostics()
    {
        var failing = new FakeAnalyzer("acme.broken", () => throw new InvalidOperationException("boom"));
        var working = new FakeAnalyzer("acme.lint", () => [Warning("w")]);
        var report = await Run(Fixture("daily-rate-v3.json"), new AnalyzerRegistry([failing, working]));
        report.ExitCode.Should().Be(2);
        report.Analyzers.Select(analyzer => (analyzer.Id, analyzer.Status)).Should().Equal(("acme.broken", "failed"), ("acme.lint", "ran"));
        report.Analyzers[0].Detail.Should().Contain("boom");
        report.Diagnostics.Should().ContainSingle().Which.Extension.Should().Be("acme.lint");
    }

    [Test]
    public async Task ACancelledRunGivesExit130()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var report = await Run(Fixture("daily-rate-v3.json"), ct: source.Token);
        report.ExitCode.Should().Be(130);
    }

    [Test]
    public async Task ATurnedOffAnalyzerDoesNotRunAndIsListedAsTurnedOff()
    {
        var report = await Run(Fixture("daily-rate-mismatch-v3.json"), configuration: new FixedConfiguration("morphir.type-validator"));
        report.Diagnostics.Should().NotContain(diagnostic => diagnostic.Extension == "morphir.type-validator");
        report.Analyzers.Should().Equal(new ValidationAnalyzer("morphir.type-validator", "not-run", "turned-off", null));
        report.ExitCode.Should().Be(0);
    }

    [Test]
    public async Task AConfigurationThatCannotBeReadGivesExitTwoAndRunsNoAnalyzer()
    {
        var analyzer = new FakeAnalyzer("acme.lint", () => []);
        var report = await Run(Fixture("daily-rate-v3.json"), new AnalyzerRegistry([analyzer]), new BrokenConfiguration());
        report.ExitCode.Should().Be(2);
        report.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("config.load");
        analyzer.Runs.Should().Be(0);
    }

    [Test]
    public async Task TheJsonReportHasTheSpecifiedMembers()
    {
        var report = await Run(Fixture("daily-rate-mismatch-v3.json"));
        using var document = JsonDocument.Parse(report.ToJson());
        var root = document.RootElement;
        root.GetProperty("host").GetString().Should().Be("morphir");
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("exitCode").GetInt32().Should().Be(1);
        root.GetProperty("input").GetString().Should().Be(Fixture("daily-rate-mismatch-v3.json"));
        root.GetProperty("family").GetString().Should().Be("Classic");
        root.GetProperty("formatVersion").GetString().Should().Be("3");
        var analyzer = root.GetProperty("analyzers")[0];
        analyzer.GetProperty("id").GetString().Should().Be("morphir.type-validator");
        analyzer.GetProperty("status").GetString().Should().Be("ran");
        analyzer.TryGetProperty("reason", out _).Should().BeFalse("an absent reason is left out");
        var diagnostic = root.GetProperty("diagnostics")[0];
        diagnostic.GetProperty("severity").GetString().Should().Be("error");
        diagnostic.GetProperty("code").GetString().Should().Be("type-mismatch");
        diagnostic.GetProperty("extension").GetString().Should().Be("morphir.type-validator");
        diagnostic.TryGetProperty("location", out _).Should().BeFalse("an absent location is left out");
    }

    [Test]
    public async Task TheTextOutputIsAPlainTableAndASummaryLine()
    {
        var report = await Run(Fixture("daily-rate-mismatch-v3.json"));
        var lines = report.ToText(TerminalProfile.Plain).Replace("\r\n", "\n").Split('\n');
        lines.Should().Contain(line => line.StartsWith("│ SEVERITY ") && line.Contains("│ CODE ") && line.Contains("│ LOCATION ")
            && line.Contains("│ MESSAGE ") && line.Contains("│ EXTENSION "));
        lines.Should().Contain(line => line.StartsWith("│ error ") && line.Contains("type-mismatch") && line.Contains("morphir.type-validator"));
        lines[^1].Should().Be("1 error, 0 warnings, 0 info, 0 hints. Analyzers: 1 ran, 0 not run, 0 failed.");
        string.Join('\n', lines).Should().NotContain("\u001b[");
    }

    [Test]
    public async Task TheTextOutputNamesAnAnalyzerThatDidNotRun()
    {
        var report = await Run(Fixture("daily-rate-mismatch-v3.json"), configuration: new FixedConfiguration("morphir.type-validator"));
        var text = report.ToText(TerminalProfile.Plain);
        text.Should().Contain("morphir.type-validator: not run (turned-off)");
        text.Should().EndWith("0 errors, 0 warnings, 1 info, 0 hints. Analyzers: 0 ran, 1 not run, 0 failed.");
    }

    [Test]
    public async Task ARunLeavesTheModelFileAndTheWorkingDirectoryUnchanged()
    {
        await using var sandbox = Morphir.TestSupport.TestSandbox.Create("morphir-validate-");
        var model = Path.Combine(sandbox.Work, "model.json");
        File.Copy(Fixture("daily-rate-mismatch-v3.json"), model);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(model));
        var entries = Directory.GetFileSystemEntries(sandbox.Work, "*", SearchOption.AllDirectories);
        var report = await Validation().RunAsync(new ValidateOverrides("model.json"), new InvocationContext(sandbox.Work), default);
        report.ExitCode.Should().Be(1);
        SHA256.HashData(await File.ReadAllBytesAsync(model)).Should().Equal(before);
        Directory.GetFileSystemEntries(sandbox.Work, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(entries);
    }
}
