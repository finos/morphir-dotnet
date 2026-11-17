// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The report of <c>model convert --json</c>, in either form (cli-model-conversion). It states success in the field
/// <c>success</c>, which must agree with the exit code of the command, as its field <c>exitCode</c> must. A successful
/// report lists each file that the command wrote in <c>filesWritten</c>.
/// </summary>
public sealed class ConversionReport : JsonCommandReport
{
    /// <summary>The <c>event</c> of the record that the stream writes for each written file.</summary>
    public const string FileWrittenEvent = "fileWritten";

    private ConversionReport(CommandResult result) : base(result, "conversion")
    {
        if (Member("exitCode") is not { ValueKind: JsonValueKind.Number } exitCode)
            throw new CommandAssertionException("The conversion report has no field exitCode.", result);
        ExitCode = exitCode.GetInt32();

        if (Member("success") is { ValueKind: JsonValueKind.True or JsonValueKind.False } success)
        {
            Success = success.GetBoolean();
            if (Success != (result.ExitCode == 0))
                throw new CommandAssertionException($"The conversion report disagrees with the command: success is {(Success ? "true" : "false")}, but the exit code is {result.ExitCode}.", result);
        }
        else if (result.ExitCode != 2)
        {
            // Only the report of a usage error (exit code 2) has no field success.
            throw new CommandAssertionException("The conversion report has no boolean field success.", result);
        }

        FilesWritten = Member("filesWritten") switch
        {
            { ValueKind: JsonValueKind.Array } list => [.. list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new CommandAssertionException("An item of the list filesWritten of the conversion report is not a text.", result))],
            null when !Success => [],
            _ => throw new CommandAssertionException("The conversion report has no list filesWritten.", result),
        };
        Notices = Member("notices") switch
        {
            { ValueKind: JsonValueKind.Array } list => [.. list.EnumerateArray().Select(Diagnostic)],
            null => [],
            _ => throw new CommandAssertionException("The conversion report has no list notices.", result),
        };
    }

    /// <summary>False also for a report of a usage error, which has no field <c>success</c>.</summary>
    public bool Success { get; }

    /// <summary>The exit code that the report states (<c>exitCode</c>); it agrees with the exit code of the command.</summary>
    public int ExitCode { get; }

    /// <summary>The path of the input, as the command line gave it (<c>input</c>).</summary>
    public string? Input => Text("input");

    /// <summary>The path of the output, as the command line gave it (<c>output</c>).</summary>
    public string? Output => Text("output");

    /// <summary>The layout that the command detected (<c>inputLayout</c>): a name of <see cref="ModelLayouts.Name"/>.</summary>
    public string? InputLayout => Text("inputLayout");

    /// <summary>The layout of the output (<c>outputLayout</c>): a name of <see cref="ModelLayouts.Name"/>.</summary>
    public string? OutputLayout => Text("outputLayout");

    /// <summary>The encoding that the command detected (<c>inputEncoding</c>): <c>plain</c> or <c>compacted</c>.</summary>
    public string? InputEncoding => Text("inputEncoding");

    /// <summary>The encoding of the output (<c>outputEncoding</c>): <c>plain</c> or <c>compacted</c>.</summary>
    public string? OutputEncoding => Text("outputEncoding");

    /// <summary>The IR version of the input and the output (<c>irVersion</c>), a text as in the compilation report.</summary>
    public string? IrVersion => Text("irVersion");

    /// <summary>The compaction mode (<c>compactMode</c>), for example <c>names</c>; null when the command did not compact.</summary>
    public string? CompactMode => Text("compactMode");

    /// <summary>The policy that wrote the compacted files (<c>compactPolicy</c>); null when the command did not compact.</summary>
    public string? CompactPolicy => Text("compactPolicy");

    /// <summary>Each file that the command wrote (<c>filesWritten</c>), in the order of the report. Empty for a refusal.</summary>
    public IReadOnlyList<string> FilesWritten { get; }

    /// <summary>The notices of the report (<c>notices</c>), in the form of its diagnostics.</summary>
    public IReadOnlyList<ReportDiagnostic> Notices { get; }

    /// <summary>Reads the report from standard output, in either form of <see cref="JsonOutputAssertions"/>.</summary>
    public static ConversionReport Of(CommandResult result) => new(result);

    /// <summary>
    /// The <c>path</c> of each <c>fileWritten</c> record of the stream, in order. Output that is not a stream (a
    /// <c>--json=document</c> run) fails the check.
    /// </summary>
    public IReadOnlyList<string> FileWrittenEvents() =>
        [
            .. Result.Events(MaxDepth)
                .Where(record => record.TryGetProperty(JsonOutputAssertions.EventMember, out var name) && name.ValueKind == JsonValueKind.String
                    && name.GetString() == FileWrittenEvent)
                .Select(record => record.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                    ? path.GetString()!
                    : throw new CommandAssertionException("A fileWritten record of the stream has no text path.", Result)),
        ];

    public ConversionReport ShouldSucceed()
    {
        Check(Success, $"The conversion report says that the conversion failed: success is false. Diagnostics: {DescribeDiagnostics()}");
        return this;
    }

    /// <summary>The report has a notice with <paramref name="code"/>.</summary>
    public ConversionReport ShouldHaveNotice(string code)
    {
        Check(Notices.Any(notice => notice.Code == code),
            $"The conversion report has no notice {code}. It has: {(Notices.Count == 0 ? "no notice" : string.Join("; ", Notices.Select(notice => $"{notice.Code}: {notice.Message}")))}.");
        return this;
    }
}
