// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The internal and experimental <c>extension worker</c> command (add-wasm-extension-host, task 4.1) on a session
/// driver: a WASM module served over Content-Length frames on stdin and stdout, with events on stderr.
/// </summary>
public static class ExtensionWorker
{
    /// <summary>The command of the worker, before its own arguments.</summary>
    public static IReadOnlyList<string> Command { get; } = ["extension", "worker"];

    /// <summary>The profile of the worker of this host.</summary>
    public const string Abi = "extism-kernel-v1";

    private static readonly ConcurrentDictionary<string, Task<string>> Identities = new(StringComparer.Ordinal);

    /// <summary>
    /// The build identity of the host of <paramref name="sessions"/>, read once for each file of a host from
    /// <c>extension worker --help --json</c>.
    /// </summary>
    public static Task<string> BuildIdentityAsync(IProcessSessionDriver sessions) => Identities.GetOrAdd(sessions.Artifact, async _ =>
    {
        var help = await HelpAsync(sessions).ConfigureAwait(false);
        if (help.ExitCode != 0)
            throw new InvalidOperationException($"extension worker --help --json did not exit with 0: {help.Describe()}");
        using var document = JsonDocument.Parse(help.StandardOutput);
        return document.RootElement.GetProperty("result").GetProperty("buildIdentity").GetString()!;
    });

    /// <summary>Runs <c>extension worker --help --json</c> to its exit.</summary>
    public static async Task<SessionExit> HelpAsync(IProcessSessionDriver sessions, ProcessSessionOptions? options = null)
    {
        await using var help = await sessions.StartAsync([.. Command, "--help", "--json"], options).ConfigureAwait(false);
        return await help.WaitForExitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
    }

    /// <summary>The worker arguments that serve <paramref name="module"/>, then <paramref name="more"/>.</summary>
    public static IReadOnlyList<string> ServeArguments(string module, string identity, params string[] more) =>
        [.. Command, "--abi", Abi, "--module", module, "--build-identity", identity, "--json", .. more];

    /// <summary>A build identity that is not <paramref name="identity"/>: its last digit is changed.</summary>
    public static string OtherIdentity(string identity) => identity[..^1] + (identity[^1] == '0' ? '1' : '0');

    /// <summary>The frames of the corpus with the names <paramref name="names"/>, a list with commas.</summary>
    public static byte[][] Frames(string names) =>
        [.. names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => ContentLengthFrames.Frame(WorkerCorpus.FrameBody(name)))];
}
