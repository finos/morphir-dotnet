// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using Morphir.Extensions.Wasm;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// What <c>morphir extension worker</c> serves: one module of one declared profile, with the frame limits and call
/// bounds of the session. Every value that an argument leaves out is the pinned contract's default.
/// </summary>
/// <param name="Abi">The declared profile (<c>--abi</c>). A declaration only confirms the classified profile.</param>
/// <param name="ModulePath">The module's path as given (<c>--module</c>); a relative path is resolved against the current directory.</param>
/// <param name="BuildIdentity">The build identity the runner passed (<c>--build-identity</c>), in lower-case hexadecimal.</param>
/// <param name="RequestBytes">The largest request frame, its <c>Content-Length</c> header included (<c>--request-bytes</c>).</param>
/// <param name="ResponseBytes">The largest response frame, its header included (<c>--response-bytes</c>).</param>
/// <param name="CallDeadline">The deadline of each <c>handle</c> call (<c>--call-ms</c>).</param>
/// <param name="Instructions">The instruction budget of each call, in Wasmtime fuel units (<c>--instructions</c>).</param>
/// <param name="MemoryPages">The ceiling of each linear memory of the session, in 64 KiB pages (<c>--memory-pages</c>).</param>
/// <param name="Json">Whether the stderr report is <c>morphir/command-event</c> lines (<c>--json</c>).</param>
public sealed record WorkerOptions(
    WasmAbi Abi,
    string ModulePath,
    string BuildIdentity,
    long RequestBytes,
    long ResponseBytes,
    TimeSpan CallDeadline,
    ulong Instructions,
    int MemoryPages,
    bool Json)
{
    /// <summary>The bounds and frame limits of the session these options give, for <see cref="WasmChannel.Open"/>.</summary>
    /// <remarks>
    /// <see cref="SessionLimits"/> has one ceiling for every memory of the session, so <c>--memory-pages</c> also
    /// bounds the Extism kernel's memory, which the contract gives a ceiling of its own of 256 MiB. The contract allows
    /// this of a host whose engine has one limit for every memory of a session. At the default the two are the same.
    /// </remarks>
    public SessionLimits Limits => new(MemoryPages * MemoryUse.PageBytes, Instructions, CallDeadline)
    {
        RequestFrameBytes = RequestBytes,
        ResponseFrameBytes = ResponseBytes,
    };
}

/// <summary>
/// A command line of <c>morphir extension worker</c>, the arguments after <c>extension worker</c>: serve a module,
/// show the help, or a usage error. The arguments are the pinned contract's <c>worker.arguments</c>, in its order,
/// with its defaults (task 4.1 of <c>add-wasm-extension-host</c>, parent requirement "Worker mode").
/// </summary>
public abstract record WorkerInvocation
{
    private WorkerInvocation() { }

    /// <summary>The command, as the contract's <c>worker.command</c> and every report of the worker name it.</summary>
    public const string Command = "extension worker";

    /// <summary>The marker that the help of an internal, experimental command carries.</summary>
    public const string Experimental = "[experimental, internal]";

    /// <summary>The largest <c>--memory-pages</c>: 65,536 pages, the whole 4 GiB of a 32-bit memory. The contract
    /// states no range for it.</summary>
    public const int MaxMemoryPages = 65536;

    /// <summary>
    /// The largest frame the stdio framing carries: its largest body and that body's header. A frame limit above
    /// it could not be honoured, because the framing refuses the body first, on reading and on writing.
    /// </summary>
    public static readonly ulong MaxFrameBytes =
        (ulong)Morphir.Mep.ContentLengthFraming.MaxBodyBytes
        + (ulong)$"Content-Length: {Morphir.Mep.ContentLengthFraming.MaxBodyBytes}\r\n\r\n".Length;

    const string Abi = "--abi";
    const string Module = "--module";
    const string BuildIdentity = "--build-identity";
    const string RequestBytes = "--request-bytes";
    const string ResponseBytes = "--response-bytes";
    const string CallMs = "--call-ms";
    const string Instructions = "--instructions";
    const string MemoryPages = "--memory-pages";
    const string JsonFlag = "--json";
    const string HelpFlag = "--help";

    /// <summary>Every argument the worker takes, in the order of the contract's <c>worker.arguments</c>.</summary>
    public static IReadOnlyList<string> ArgumentNames { get; } =
        [Abi, Module, BuildIdentity, RequestBytes, ResponseBytes, CallMs, Instructions, MemoryPages, JsonFlag, HelpFlag];

    static readonly HashSet<string> Valued = [Abi, Module, BuildIdentity, RequestBytes, ResponseBytes, CallMs, Instructions, MemoryPages];

    /// <summary>The help text, which states every argument with its default from the pinned contract.</summary>
    public static string HelpText => Help.Text.Value;

    /// <summary>Serve the module with these options.</summary>
    public sealed record Serve(WorkerOptions Options) : WorkerInvocation;

    /// <summary>Show the help and serve nothing: the text, or with <c>--json</c> one <c>morphir/command-result</c>.</summary>
    public sealed record Help(bool Json) : WorkerInvocation
    {
        internal static readonly Lazy<string> Text = new(Render);
    }

    /// <summary>The arguments are not valid: the worker serves nothing and exits with 2.</summary>
    /// <param name="Message">What is wrong, naming the argument.</param>
    /// <param name="Json">Whether <c>--json</c> was among the arguments, so the error is written as JSON.</param>
    public sealed record Invalid(string Message, bool Json) : WorkerInvocation;

    /// <summary>Reads the arguments after <c>extension worker</c>. Options take their value as the next argument or
    /// after <c>=</c>, in any order. <c>--help</c> (or <c>-h</c>) anywhere shows the help.</summary>
    public static WorkerInvocation Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var json = arguments.Contains(JsonFlag);
        if (arguments.Any(argument => argument is HelpFlag or "-h"))
        {
            return new Help(json);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];
            var equals = token.StartsWith("--", StringComparison.Ordinal) ? token.IndexOf('=', StringComparison.Ordinal) : -1;
            var name = equals > 0 ? token[..equals] : token;
            var inline = equals > 0 ? token[(equals + 1)..] : null;
            if (name == JsonFlag)
            {
                if (inline is not null) return new Invalid($"{JsonFlag} takes no value", json);
                if (!flags.Add(name)) return new Invalid($"{JsonFlag} is given twice", json);
            }
            else if (Valued.Contains(name))
            {
                if (values.ContainsKey(name)) return new Invalid($"{name} is given twice", json);
                if (inline is null)
                {
                    if (index + 1 >= arguments.Count) return new Invalid($"{name} needs a value", json);
                    inline = arguments[++index];
                }

                values[name] = inline;
            }
            else
            {
                return new Invalid(token.StartsWith('-')
                    ? $"unknown argument '{token}'; the arguments are {string.Join(", ", ArgumentNames)}"
                    : $"unexpected argument '{token}'; the worker takes no positional argument", json);
            }
        }

        return Read(values, json);
    }

    static WorkerInvocation Read(Dictionary<string, string> values, bool json)
    {
        foreach (var required in new[] { Abi, Module, BuildIdentity })
        {
            if (!values.ContainsKey(required)) return new Invalid($"{required} is required", json);
        }

        if (!WasmAbi.TryParse(values[Abi], out var abi))
        {
            return new Invalid($"{Abi} must be {string.Join(" or ", WasmAbi.All)}", json);
        }

        var identity = values[BuildIdentity];
        if (identity.Length != 64 || !identity.All(char.IsAsciiHexDigit))
        {
            return new Invalid($"{BuildIdentity} must be 64 hexadecimal digits", json);
        }

        var contract = WasmProfileContract.Pinned;
        var problems = new[]
        {
            Number(values, RequestBytes, 1, MaxFrameBytes, (ulong)contract.RequestFrameLimitBytes, out var requestBytes),
            Number(values, ResponseBytes, 1, MaxFrameBytes, (ulong)contract.ResponseFrameLimitBytes, out var responseBytes),
            Number(values, CallMs, 1, int.MaxValue, (ulong)contract.Profile(abi).CallDeadline.TotalMilliseconds, out var callMs),
            Number(values, Instructions, contract.WorkerInstructionRange.Minimum, contract.WorkerInstructionRange.Maximum, contract.InstructionBudget, out var instructions),
            Number(values, MemoryPages, 1, MaxMemoryPages, (ulong)(contract.MemoryCeilingBytes / MemoryUse.PageBytes), out var memoryPages),
        };
        if (problems.FirstOrDefault(problem => problem is not null) is { } problem) return new Invalid(problem, json);

        return new Serve(new WorkerOptions(
            abi,
            values[Module],
            identity.ToLowerInvariant(),
            (long)requestBytes,
            (long)responseBytes,
            TimeSpan.FromMilliseconds(callMs),
            instructions,
            (int)memoryPages,
            json));
    }

    /// <summary>The value of a numeric option: decimal digits only, from <paramref name="min"/> to
    /// <paramref name="max"/>, or the default.</summary>
    static string? Number(Dictionary<string, string> values, string name, ulong min, ulong max, ulong fallback, out ulong value)
    {
        value = fallback;
        if (!values.TryGetValue(name, out var text)) return null;
        if (text.Length == 0 || !text.All(char.IsAsciiDigit)
            || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            || value < min || value > max)
        {
            value = fallback;
            return $"{name} must be an integer from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    static string Render()
    {
        var contract = WasmProfileContract.Pinned;
        var extism = (long)contract.Profile(WasmAbi.ExtismKernel).CallDeadline.TotalMilliseconds;
        var bytes = (long)contract.Profile(WasmAbi.MorphirBytes).CallDeadline.TotalMilliseconds;
        var pages = contract.MemoryCeilingBytes / MemoryUse.PageBytes;
        var instructions = contract.WorkerInstructionRange;
        string N(long value) => value.ToString(CultureInfo.InvariantCulture);
        string U(ulong value) => value.ToString(CultureInfo.InvariantCulture);
        return $"""
            morphir {Command} {Experimental}

            Serves one WASM extension module over stdio, so that a conformance runner can qualify this host with the
            crafted cases of the WASM execution profile contract ({contract.Id} {contract.Version}).
            Product commands do not use it.

            Each Content-Length framed MEP message on stdin is one handle call; each request's reply is one frame on
            stdout, and a notification gets none. Stdout carries only MEP frames. The report goes to
            stderr: one morphir/command-event line per event with --json (started, output, fault, refused, exited),
            text lines without it. Guest output is reported after each call: the newest 64 KiB, each message cut to
            4 KiB, and one output event with droppedBytes for the rest.

            Usage: morphir {Command} --abi <profile> --module <path> --build-identity <hex> [options]

            Options:
              {Abi} <profile>            Required. The declared profile: {string.Join(" or ", WasmAbi.All)}. This host
                                         runs extism-kernel-v1 only and refuses the other.
              {Module} <path>            Required. The module file; a relative path is resolved against the current
                                         directory.
              {BuildIdentity} <hex>     Required. This host's build identity, 64 hexadecimal digits, as
                                         --help --json reports it. Another identity is refused before the module is
                                         read.
              {RequestBytes} <n>        The largest request frame, its Content-Length header included.
                                         Default {N(contract.RequestFrameLimitBytes)}.
              {ResponseBytes} <n>       The largest response frame, its header included. Default {N(contract.ResponseFrameLimitBytes)}.
              {CallMs} <n>              The deadline of each guest call, in milliseconds. Default {N(extism)} for
                                         extism-kernel-v1, {N(bytes)} for morphir-bytes-v1.
              {Instructions} <n>         The instruction budget of each guest call, in Wasmtime fuel units, from {U(instructions.Minimum)} to
                                         {U(instructions.Maximum)}. Default {U(contract.InstructionBudget)}.
              {MemoryPages} <n>         The ceiling of each linear memory, in 64 KiB pages, from 1 to {MaxMemoryPages}.
                                         Default {N(pages)} ({N(contract.MemoryCeilingBytes / (1024 * 1024))} MiB).
              {JsonFlag}                     Write the stderr report as morphir/command-event lines.
              {HelpFlag}, -h                 Show this help on stdout and serve nothing. With --json, write one
                                         morphir/command-result whose result.buildIdentity is the identity to pass.

            Exit status: 0 the guest served until its instance ended; 1 a fault, an invalid frame, or stdin ended
            first; 2 invalid arguments; 3 a refusal before any request; 130 interrupted (Ctrl+C, or Ctrl+Break on
            Windows), with the guest stopped and no fault reported.

            """.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>Calls the function for this invocation.</summary>
    public TResult Match<TResult>(Func<WorkerOptions, TResult> serve, Func<bool, TResult> help, Func<string, bool, TResult> invalid) => this switch
    {
        Serve value => serve(value.Options),
        Help value => help(value.Json),
        Invalid value => invalid(value.Message, value.Json),
        _ => throw new UnreachableException(),
    };
}
