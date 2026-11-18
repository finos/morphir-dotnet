// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Knowledge.Okf;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Knowledge;

/// <summary>Checks a directory as a knowledge bundle.</summary>
internal sealed class KnowledgeValidateCommand : Command, IMorphirParseErrorHandler
{
    private readonly Argument<string> _bundle = new("bundle") { Description = "Path to the knowledge bundle directory" };
    private readonly Option<string?> _version = new("--okf-version") { Description = "Override the OKF version with 0.2 or 0.3" };
    private readonly Option<string?> _policy = new("--policy") { Description = "Use the morphir authoring policy in addition to OKF conformance" };
    private readonly Option<string> _json = JsonOutputOption.Create();

    internal KnowledgeValidateCommand() : base("validate", "Validate an Open Knowledge Format bundle")
    {
        Arguments.Add(_bundle);
        Options.Add(_version);
        Options.Add(_policy);
        Options.Add(_json);
        SetAction(Validate);
    }

    private Task<int> Validate(ParseResult result, CancellationToken cancellation)
    {
        var path = result.GetRequiredValue(_bundle);
        var mode = JsonOutputOption.ModeOf(result);
        OkfVersion? version = null;
        if (result.GetValue(_version) is { } requestedVersion && !OkfVersion.TryParse(requestedVersion, out version, out var error))
            return Task.FromResult(Render(KnowledgeCommandReport.Usage(path, [error.ToString()]), mode));

        var policy = BundlePolicy.Standard;
        if (result.GetValue(_policy) is { } requestedPolicy)
        {
            if (requestedPolicy != "morphir")
                return Task.FromResult(Render(KnowledgeCommandReport.Usage(path, ["The knowledge policy must be 'morphir'."]), mode));
            policy = BundlePolicy.Morphir;
        }

        var report = BundleValidation.ValidateDirectory(path, version, policy, cancellation);
        return Task.FromResult(Render(KnowledgeCommandReport.From(path, report), mode));
    }

    public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages) =>
        Render(KnowledgeCommandReport.Usage(null, messages),
            JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]));

    private static int Render(KnowledgeCommandReport report, JsonOutputMode mode)
    {
        if (mode != JsonOutputMode.None)
            JsonReportOutput.Write(ConsoleOutputHelper.Stdout, mode, report, KnowledgeReportJsonContext.Default.KnowledgeCommandReport);
        else
        {
            var output = report.ExitCode == 2 ? Console.Error : ConsoleOutputHelper.Stdout;
            foreach (var finding in report.Diagnostics)
                output.WriteLine(finding.ToText());
            if (report.ExitCode != 2)
                output.WriteLine($"OKF {report.Version} {report.Conformance}; policy {report.Policy}.");
        }

        return report.ExitCode;
    }
}

internal sealed record KnowledgeCommandReport(string? Bundle, string? Version, string? DeclaredVersion, string Policy,
    string Conformance, int ExitCode, KnowledgeCommandFinding[] Diagnostics)
{
    internal static KnowledgeCommandReport From(string bundle, BundleValidationReport report) =>
        new(bundle, report.VersionOrNull?.WireValue, report.DeclaredVersionOrNull, report.Policy.WireValue,
            report.Conformance.WireValue, report.ExitCode, report.Findings.Select(KnowledgeCommandFinding.From).ToArray());

    internal static KnowledgeCommandReport Usage(string? path, IReadOnlyList<string> messages) =>
        new(path, null, null, "standard", "not-checked", 2,
            messages.Select(message => new KnowledgeCommandFinding("okf.invalid-arguments", "error", "input", message, null, null)).ToArray());
}

internal sealed record KnowledgeCommandFinding(string Code, string Severity, string Origin, string Message, string? Path,
    KnowledgeCommandRange? Range)
{
    internal static KnowledgeCommandFinding From(BundleFinding finding) =>
        new(finding.Code, finding.Severity.WireValue, finding.Origin.WireValue, finding.Message, finding.PathOrNull,
            finding.RangeOrNull is { } range
                ? new(new(range.Start.Line, range.Start.Character), new(range.End.Line, range.End.Character))
                : null);

    internal string ToText()
    {
        var path = Path is null ? "" : Range is { } range
            ? $"{Path}:{range.Start.Line + 1}:{range.Start.Character + 1} "
            : $"{Path} ";
        return $"{path}{Severity} {Code} [{Origin}]: {Message}";
    }
}

internal sealed record KnowledgeCommandRange(KnowledgeCommandPosition Start, KnowledgeCommandPosition End);
internal sealed record KnowledgeCommandPosition(int Line, int Character);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KnowledgeCommandReport))]
internal sealed partial class KnowledgeReportJsonContext : JsonSerializerContext;
