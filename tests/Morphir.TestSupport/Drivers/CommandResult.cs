// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The result of one product command, in the one form that each driver returns.
/// <paramref name="WrittenFiles"/> are the files of the work area that the command made or changed, as paths
/// relative to the work area with <c>/</c>.
/// </summary>
public sealed record CommandResult(
    string CommandLine,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    IReadOnlyList<string> WrittenFiles)
{
    /// <summary>The command line, the exit code and both streams, for a failure message.</summary>
    public string Describe() =>
        $"command: {CommandLine}{Environment.NewLine}" +
        $"exit code: {ExitCode}{Environment.NewLine}" +
        $"standard output:{Environment.NewLine}{StandardOutput}{Environment.NewLine}" +
        $"standard error:{Environment.NewLine}{StandardError}";
}

/// <summary>A check of a command result failed. The message has the command line, the exit code and both streams.</summary>
public sealed class CommandAssertionException(string reason, CommandResult result)
    : Exception(reason + Environment.NewLine + result.Describe())
{
    public CommandResult Result { get; } = result;
}

/// <summary>
/// The general checks of a command result. They know no field of a report: the status of a report belongs to the
/// capability of its command (for example <see cref="VerificationReport"/> and <see cref="GenerationReport"/>).
/// </summary>
public static class CommandResultAssertions
{
    public static CommandResult ShouldExitWith(this CommandResult result, int exitCode) =>
        result.ExitCode == exitCode
            ? result
            : throw new CommandAssertionException($"The exit code is {result.ExitCode}, not {exitCode}.", result);

    /// <summary>
    /// Standard output is exactly one JSON document and nothing else: a command with a machine-readable format
    /// writes its logs to standard error. Returns the document.
    /// </summary>
    public static JsonElement ShouldWriteOneJsonDocument(this CommandResult result)
    {
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new CommandAssertionException("Standard output is empty, not one JSON document.", result);

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new CommandAssertionException($"Standard output is not exactly one JSON document: {error.Message}", result);
        }
    }

    /// <summary>The command made or changed the file. <paramref name="relativePath"/> is relative to the work area, with <c>/</c>.</summary>
    public static CommandResult ShouldHaveWritten(this CommandResult result, string relativePath) =>
        result.WrittenFiles.Contains(relativePath, StringComparer.Ordinal)
            ? result
            : throw new CommandAssertionException(
                $"The command did not write '{relativePath}'. It wrote: {(result.WrittenFiles.Count == 0 ? "no file" : string.Join(", ", result.WrittenFiles))}.", result);

    public static CommandResult ShouldHaveWrittenNoFile(this CommandResult result) =>
        result.WrittenFiles.Count == 0
            ? result
            : throw new CommandAssertionException($"The command wrote: {string.Join(", ", result.WrittenFiles)}.", result);

    public static CommandResult StandardErrorShouldContain(this CommandResult result, string text) =>
        result.StandardError.Contains(text, StringComparison.Ordinal)
            ? result
            : throw new CommandAssertionException($"Standard error does not contain '{text}'.", result);

    /// <summary>The text is on standard output or on standard error, with the same case.</summary>
    public static CommandResult OutputShouldContain(this CommandResult result, string text) =>
        Contains(result, text) ? result : throw new CommandAssertionException($"The output does not contain '{text}'.", result);

    /// <summary>One of the texts is on standard output or on standard error, with the same case.</summary>
    public static CommandResult OutputShouldContainOneOf(this CommandResult result, params string[] texts) =>
        texts.Any(text => Contains(result, text))
            ? result
            : throw new CommandAssertionException($"The output contains none of: {string.Join(", ", texts.Select(text => $"'{text}'"))}.", result);

    /// <summary>The command wrote nothing to standard output and nothing to standard error.</summary>
    public static CommandResult OutputShouldBeEmpty(this CommandResult result) =>
        string.IsNullOrWhiteSpace(result.StandardOutput) && string.IsNullOrWhiteSpace(result.StandardError)
            ? result
            : throw new CommandAssertionException("The command wrote output.", result);

    public static CommandResult StandardOutputShouldBeEmpty(this CommandResult result) =>
        string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result
            : throw new CommandAssertionException("Standard output is not empty.", result);

    /// <summary>The text is on standard output, with the same case.</summary>
    public static CommandResult StandardOutputShouldContain(this CommandResult result, string text) =>
        result.StandardOutput.Contains(text, StringComparison.Ordinal)
            ? result
            : throw new CommandAssertionException($"Standard output does not contain '{text}'.", result);

    private static bool Contains(CommandResult result, string text) =>
        result.StandardOutput.Contains(text, StringComparison.Ordinal)
        || result.StandardError.Contains(text, StringComparison.Ordinal);
}
