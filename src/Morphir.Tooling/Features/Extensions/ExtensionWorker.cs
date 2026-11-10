// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using Morphir.Extensions.Wasm;
using Morphir.Mep;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>The worker's three streams: the frames it reads, the frames it writes, and its report.</summary>
/// <param name="Input">Stdin: Content-Length framed MEP messages.</param>
/// <param name="Output">Stdout: the reply frames, and nothing else.</param>
/// <param name="Report">Stderr: the report, the help never.</param>
public sealed record WorkerIo(Stream Input, Stream Output, Stream Report);

/// <summary>What the worker knows of the process it runs in.</summary>
/// <param name="BuildIdentity">This worker's own build identity (<see cref="WorkerBuildIdentity"/>).</param>
/// <param name="WorkspaceRoot">The absolute directory below which a session's private workspace is created.</param>
/// <param name="Loader">
/// The module loader, with the process's WASM runtime (<see cref="WasmRuntime.Process"/> in the worker process). It
/// asks the runtime before it reads the module, and that is the worker's only probe of the engine.
/// </param>
public sealed record WorkerContext(string BuildIdentity, string WorkspaceRoot, IWasmModuleLoader Loader);

/// <summary>
/// <c>morphir extension worker</c> (task 4.1 of <c>add-wasm-extension-host</c>, design WRK-2): the mode through which a
/// conformance runner drives one WASM module. It relays messages; it is not a second MEP host. It joins a stream
/// (Content-Length frames on stdin and stdout, <see cref="ContentLengthFraming"/>) to a WASM channel
/// (<see cref="WasmChannel"/>), the same channel, adapter, bounds and policy a host session uses. It does not
/// negotiate and does not read the protocol version (design MEP-1).
/// </summary>
/// <remarks>
/// <para>The order of a run, which the pinned contract's worker rules fix:</para>
/// <list type="number">
/// <item>The arguments. Invalid ones serve nothing and exit with 2; <c>--help</c> writes the help and exits with 0.</item>
/// <item>The build identity, before the module file is read: another identity is <c>refused</c>
/// <c>extension.worker_runtime_mismatch</c>, exit 3.</item>
/// <item>The WASM runtime, before the module file is read (requirement "Unavailable runtime fails by name"): a host
/// without it is <c>refused</c> <c>extension.runtime_unavailable</c>, naming the host, the platform and the reason,
/// exit 3, a refusal before any request. The arguments, <c>--help</c> and the identity check never probe the engine.</item>
/// <item>The module (<see cref="WasmModuleLoader"/>): read, hashed, validated and compiled, classified against the
/// pinned contract and admitted with the declared ABI. A file that cannot be read or is not a module is
/// <c>fault</c> <c>failure</c>, exit 1; a refusal is <c>refused</c> with its code, exit 3.</item>
/// <item>The channel: one store and one instance under the session's bounds. A guest whose initial memory is above the
/// ceiling is <c>fault</c> <c>memory-limit</c>, exit 1.</item>
/// <item><c>started</c>, then one frame at a time until the instance ends: at <c>morphir.exit</c> (exit 0) or at a fault
/// (exit 1). The worker stops reading when the instance ends and exits by itself; a runner never closes stdin.</item>
/// </list>
/// <para>
/// Every run that gets past the arguments ends with one <c>exited</c> event. Stdout carries reply frames only, in
/// every path; nothing else in this class writes to it.
/// </para>
/// <para>
/// <b>Interruption.</b> When <c>interrupt</c> is cancelled, a call inside the guest stops at the channel's next epoch
/// tick (10 ms), the instance and its private workspace are discarded, no <c>fault</c> event is written, and the run
/// ends with 130 (parent requirement "Worker interruption"). The guest did not fail.
/// </para>
/// </remarks>
public static class ExtensionWorker
{
    /// <summary>Exit status: the guest served until its instance ended.</summary>
    public const int Served = 0;

    /// <summary>Exit status: a fault, an invalid frame, or stdin ended first.</summary>
    public const int Faulted = 1;

    /// <summary>Exit status: invalid arguments.</summary>
    public const int Usage = 2;

    /// <summary>Exit status: a refusal before any request.</summary>
    public const int Refused = 3;

    /// <summary>Exit status: the worker was interrupted (parent requirement "Worker interruption"): 128 + SIGINT.</summary>
    public const int Interrupted = 130;

    /// <summary>The contract's refusal code for another build identity.</summary>
    public const string RuntimeMismatchCode = "extension.worker_runtime_mismatch";

    const string FailureKind = "failure";

    /// <summary>Runs the worker for the arguments after <c>extension worker</c>, and returns its exit status.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, WorkerIo io, WorkerContext context, CancellationToken interrupt)
    {
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(context);
        switch (WorkerInvocation.Parse(arguments))
        {
            case WorkerInvocation.Help help:
                // Help serves no guest, so it may use stdout.
                io.Output.Write(help.Json ? WorkerReport.HelpResult(WorkerInvocation.HelpText, context.BuildIdentity) : Encoding.UTF8.GetBytes(WorkerInvocation.HelpText));
                io.Output.Flush();
                return Served;
            case WorkerInvocation.Invalid invalid:
                io.Report.Write(invalid.Json ? WorkerReport.UsageFailure(invalid.Message, Usage) : WorkerReport.UsageText(invalid.Message));
                io.Report.Flush();
                return Usage;
            case WorkerInvocation.Serve serve:
                return await ServeAsync(serve.Options, io, context, interrupt).ConfigureAwait(false);
            default:
                throw new InvalidOperationException("An invocation this worker does not know.");
        }
    }

    /// <summary>Serves one module with <paramref name="options"/>, and returns the exit status.</summary>
    public static async Task<int> ServeAsync(WorkerOptions options, WorkerIo io, WorkerContext context, CancellationToken interrupt)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(context);
        var report = new WorkerReport(io.Report, options.Json);

        if (!string.Equals(options.BuildIdentity, context.BuildIdentity, StringComparison.OrdinalIgnoreCase))
        {
            report.Refused(RuntimeMismatchCode,
                $"this worker is build {context.BuildIdentity}; the runner passed --build-identity {options.BuildIdentity}, another build");
            return report.Exited(Refused);
        }

        var load = Load(options, context.Loader);
        if (load.Fault is { } loadFault)
        {
            report.Fault(FailureKind, loadFault);
            return report.Exited(Faulted);
        }

        if (load.Refusal is { } refusal)
        {
            report.Refused(refusal.Code, refusal.Message);
            return report.Exited(Refused);
        }

        var module = load.Module!;
        if (interrupt.IsCancellationRequested) return report.Exited(Interrupted);

        var opening = WasmChannel.Open(module.Module, options.Limits, context.WorkspaceRoot);
        if (opening is WasmChannelOpening.Failed failed)
        {
            report.Fault(failed.Failure.FaultKind ?? FailureKind, $"{failed.Failure.Message}; {Identity(module)}");
            return report.Exited(Faulted);
        }

        using var channel = ((WasmChannelOpening.Opened)opening).Channel;
        report.Started(options.Abi, module.Digest.Hex, context.BuildIdentity);
        var exit = await Relay(channel, options, io, report, interrupt).ConfigureAwait(false);
        channel.Dispose();
        return report.Exited(exit);
    }

    /// <summary>Reads one frame at a time and passes it to the guest, until the instance ends.</summary>
    static async Task<int> Relay(WasmChannel channel, WorkerOptions options, WorkerIo io, WorkerReport report, CancellationToken interrupt)
    {
        var input = new BufferedStream(io.Input, 64 * 1024);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = interrupt.Register(() => interrupted.TrySetResult()).ConfigureAwait(false);

        while (true)
        {
            var reading = ContentLengthFraming.ReadAsync(input, CancellationToken.None);
            if (await Task.WhenAny(reading, interrupted.Task).ConfigureAwait(false) != reading) return Interrupted;

            FrameRead frame;
            try
            {
                frame = await reading.ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                report.Fault(FailureKind, $"stdin failed: {error.Message}");
                return Faulted;
            }

            var (body, invalidFrame) = frame.Match<(byte[]? Body, string? Problem)>(
                message => (message, null),
                () => (null, "stdin ended before the guest's instance ended (the runner closed stdin before morphir.exit)"),
                failure => (null, $"an invalid request frame: {failure}"));
            if (body is null)
            {
                report.Fault(FailureKind, invalidFrame);
                return Faulted;
            }

            var frameBytes = FrameBytes(body.Length);
            if (frameBytes > options.RequestBytes)
            {
                report.Fault(FailureKind,
                    $"the request frame has {N(frameBytes)} bytes, over --request-bytes {N(options.RequestBytes)}; the guest was not called");
                return Faulted;
            }

            if (!WorkerMessage.TryRead(body, out var message, out var problem))
            {
                report.Fault(FailureKind, $"the request is not valid: {problem}; the guest was not called");
                return Faulted;
            }

            byte[]? reply;
            try
            {
                reply = message.IsRequest
                    ? await channel.RequestAsync(message.Method, body, interrupt).ConfigureAwait(false)
                    : await channel.NotifyAsync(message.Method, body, interrupt).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (interrupt.IsCancellationRequested)
            {
                report.Outputs(channel.GuestOutput.Drain());
                return Interrupted;
            }
            catch (WasmChannelException error)
            {
                report.Outputs(channel.GuestOutput.Drain());
                report.Fault(error.Failure.FaultKind ?? FailureKind, error.Failure.Message);
                return Faulted;
            }
            catch (ObjectDisposedException error)
            {
                report.Outputs(channel.GuestOutput.Drain());
                report.Fault(FailureKind, error.Message);
                return Faulted;
            }

            report.Outputs(channel.GuestOutput.Drain());

            // No output for a request, and output for a delivered notification, end the instance as a failure.
            if (channel.Failure is { } failure)
            {
                report.Fault(failure.FaultKind ?? FailureKind, failure.Message);
                return Faulted;
            }

            if (reply is not null)
            {
                if (!WorkerMessage.IsOneObject(reply, out var replyProblem))
                {
                    channel.Dispose();
                    report.Fault(FailureKind, $"the guest's response to {message.Method} is not valid: {replyProblem}; nothing of it was written");
                    return Faulted;
                }

                try
                {
                    await ContentLengthFraming.WriteAsync(io.Output, reply, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    report.Fault(FailureKind, $"stdout failed: {error.Message}");
                    return Faulted;
                }
            }

            if (channel.InstanceEnded) return Served;
            if (interrupt.IsCancellationRequested) return Interrupted;
        }
    }

    /// <summary>The module, a loading error (fault <c>failure</c> before <c>started</c>) or a refusal.</summary>
    sealed record Loaded(LoadedWasmModule? Module, string? Fault, Refusal? Refusal);

    /// <summary>A refusal as the worker reports it: the contract's code, and the module's refusal message with the
    /// path and the digest of the file.</summary>
    sealed record Refusal(string Code, string Message);

    static Loaded Load(WorkerOptions options, IWasmModuleLoader loader)
    {
        string path;
        try
        {
            path = Path.GetFullPath(options.ModulePath);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return new Loaded(null, $"the module at '{options.ModulePath}' could not be read: {error.Message}", null);
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var source = new WasmModuleSource(string.IsNullOrWhiteSpace(name) ? "worker" : name, path, options.Abi);
        return loader.Load(source).Match(
            module => new Loaded(module, null, null),
            failure => failure switch
            {
                WasmModuleFailure.RuntimeUnavailable unavailable => new Loaded(null, null, new Refusal(unavailable.Code!, unavailable.Message)),
                WasmModuleFailure.Refused refused => new Loaded(null, null,
                    new Refusal(refused.Refusal.Code, $"{refused.Refusal.Message}; module {path}, SHA-256 {refused.Digest}")),
                WasmModuleFailure.NotFound => new Loaded(null, $"the module could not be read: there is no file at '{path}'", null),
                WasmModuleFailure.Unreadable unreadable => new Loaded(null, $"the module at '{path}' could not be read: {unreadable.Reason}", null),
                WasmModuleFailure.NotAModule notModule => new Loaded(null,
                    $"the file '{path}' is not a WASM module (SHA-256 {notModule.Digest}): {notModule.EngineReport}", null),
                _ => new Loaded(null, failure.Message, null),
            });
    }

    static string Identity(LoadedWasmModule module) => $"module {module.Path}, SHA-256 {module.Digest}, {module.Profile}";

    /// <summary>The size of a message's frame on a stream: its <c>Content-Length</c> header and its body, as the
    /// channel and the contract count it.</summary>
    static long FrameBytes(int bodyBytes) =>
        Encoding.ASCII.GetByteCount($"Content-Length: {bodyBytes.ToString(CultureInfo.InvariantCulture)}\r\n\r\n") + (long)bodyBytes;

    static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
}
