// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>One stderr event of the worker: its name and the whole JSON object.</summary>
internal sealed record WorkerEvent(string Name, JsonElement Json)
{
    public string? Text(string path)
    {
        var current = Json;
        foreach (var name in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
    }

    public IReadOnlyList<string> Members => Json.EnumerateObject().Select(member => member.Name).ToArray();
}

/// <summary>What one in-process worker run gave: the exit status, every stdout byte and every stderr line.</summary>
internal sealed record WorkerRunResult(int ExitCode, byte[] Stdout, IReadOnlyList<string> StderrLines, TimeSpan Elapsed)
{
    /// <summary>Stdout read as Content-Length frames. Anything else on stdout fails the test.</summary>
    public IReadOnlyList<byte[]> Frames => WorkerHarness.ParseFrames(Stdout);

    /// <summary>The frames as JSON text.</summary>
    public IReadOnlyList<string> FrameTexts => Frames.Select(frame => Encoding.UTF8.GetString(frame)).ToArray();

    /// <summary>The stderr lines that are <c>morphir/command-event</c> objects.</summary>
    public IReadOnlyList<WorkerEvent> Events => StderrLines
        .Select(line => (Line: line, Json: WorkerHarness.TryParse(line)))
        .Where(item => item.Json is { ValueKind: JsonValueKind.Object } json
            && json.TryGetProperty("profile", out var profile) && profile.GetString() == "morphir/command-event")
        .Select(item => new WorkerEvent(item.Json!.Value.GetProperty("event").GetString()!, item.Json.Value))
        .ToArray();

    public IReadOnlyList<string> EventNames => Events.Select(e => e.Name).ToArray();

    public WorkerEvent Single(string name) => Events.Should().ContainSingle(e => e.Name == name, Describe()).Subject;

    /// <summary>The text of every <c>output</c> event, joined in order with nothing between them.</summary>
    public string GuestOutput => string.Concat(Events.Where(e => e.Name == "output").Select(e => e.Text("output")));

    public string Describe() =>
        $"exit {ExitCode}; stdout {Stdout.Length} bytes; stderr:{Environment.NewLine}{string.Join(Environment.NewLine, StderrLines.Take(40))}";
}

/// <summary>Runs <see cref="ExtensionWorker"/> in process, on streams, as the worker process runs it on stdio.</summary>
internal static class WorkerHarness
{
    /// <summary>The build identity the tests give the worker as its own.</summary>
    public static string Identity { get; } = WorkerBuildIdentity.Of(TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.0.0-test")));

    /// <summary>The host the tests run the worker as.</summary>
    public static HostIdentity Host { get; } = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.0.0-test"));

    /// <summary>The loader of a worker in this process: the process's WASM runtime, which has the engine.</summary>
    public static IWasmModuleLoader Loader { get; } = new WasmModuleLoader(WasmRuntime.Process, Host);

    public static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "MepWasm");

    /// <summary>A module of the pinned crafted corpus.</summary>
    public static string Corpus(string name) => Path.Combine(Fixtures, "fixtures", "wasm", $"{name}.wasm");

    /// <summary>A crafted module of this repository's worker tests (tests/Fixtures/WorkerGuests).</summary>
    public static string TestGuest(string name) => Path.Combine(AppContext.BaseDirectory, "WorkerGuests", $"{name}.wasm");

    public static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersion":"0.2.0-draft.1"}}""";
    public const string Initialized = """{"jsonrpc":"2.0","method":"morphir.initialized","params":{}}""";
    public const string Shutdown = """{"jsonrpc":"2.0","id":2,"method":"morphir.shutdown"}""";
    public const string Exit = """{"jsonrpc":"2.0","method":"morphir.exit"}""";

    /// <summary>A Content-Length frame of <paramref name="body"/>.</summary>
    public static byte[] Frame(byte[] body) =>
        [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];

    public static byte[] Frame(string json) => Frame(Encoding.UTF8.GetBytes(json));

    /// <summary>The arguments a runner passes, for a module and the tests' own identity, with <c>--json</c>.</summary>
    public static List<string> Arguments(string module, params string[] more) =>
        ["--abi", "extism-kernel-v1", "--module", module, "--build-identity", Identity, "--json", .. more];

    /// <summary>
    /// Runs the worker with these arguments (those after <c>extension worker</c>) and stdin holding exactly
    /// <paramref name="input"/>, then ending.
    /// </summary>
    public static Task<WorkerRunResult> RunAsync(IReadOnlyList<string> arguments, byte[] input, string? workspaceRoot = null) =>
        RunAsync(arguments, new MemoryStream(input), workspaceRoot, CancellationToken.None);

    public static Task<WorkerRunResult> RunAsync(IReadOnlyList<string> arguments, params string[] frames) =>
        RunAsync(arguments, frames.SelectMany(Frame).ToArray());

    /// <summary>Runs the worker on <paramref name="input"/>, with <paramref name="interrupt"/> as its interruption.</summary>
    public static async Task<WorkerRunResult> RunAsync(IReadOnlyList<string> arguments, Stream input, string? workspaceRoot, CancellationToken interrupt,
        MemoryStream? stdout = null, IWasmModuleLoader? loader = null)
    {
        stdout ??= new MemoryStream();
        var stderr = new MemoryStream();
        var root = workspaceRoot ?? Path.GetTempPath();
        var clock = Stopwatch.StartNew();
        var exit = await ExtensionWorker.RunAsync(arguments, new WorkerIo(input, stdout, stderr), new WorkerContext(Identity, root, loader ?? Loader), interrupt);
        clock.Stop();
        var lines = Encoding.UTF8.GetString(stderr.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new WorkerRunResult(exit, stdout.ToArray(), lines, clock.Elapsed);
    }

    /// <summary>A stdin that holds <paramref name="frames"/> and stays open until the returned writer is disposed,
    /// as a runner keeps it open until the worker exits.</summary>
    public static (Stream Input, IDisposable Writer) OpenInput(params string[] frames)
    {
        var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var reader = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        var bytes = frames.SelectMany(Frame).ToArray();
        writer.Write(bytes);
        writer.Flush();
        return (reader, writer);
    }

    public static JsonElement? TryParse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The bodies of the Content-Length frames that make up <paramref name="stdout"/>. Bytes that are not
    /// whole frames, each with exactly one header line <c>Content-Length: n</c>, fail the test.</summary>
    public static IReadOnlyList<byte[]> ParseFrames(byte[] stdout)
    {
        var frames = new List<byte[]>();
        var offset = 0;
        while (offset < stdout.Length)
        {
            var end = IndexOf(stdout, "\r\n\r\n"u8, offset);
            end.Should().BeGreaterThanOrEqualTo(0, "stdout must hold only Content-Length frames; at byte {0}: {1}", offset,
                Encoding.UTF8.GetString(stdout, offset, Math.Min(200, stdout.Length - offset)));
            var header = Encoding.ASCII.GetString(stdout, offset, end - offset);
            header.Should().MatchRegex("^Content-Length: [0-9]+$", "each frame has exactly one Content-Length header");
            var length = int.Parse(header["Content-Length: ".Length..], System.Globalization.CultureInfo.InvariantCulture);
            var start = end + 4;
            (start + length).Should().BeLessThanOrEqualTo(stdout.Length, "a frame's body must be whole");
            frames.Add(stdout[start..(start + length)]);
            offset = start + length;
        }

        return frames;
    }

    static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int from)
    {
        var found = haystack.AsSpan(from).IndexOf(needle);
        return found < 0 ? -1 : from + found;
    }
}
