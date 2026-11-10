// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Morphir.Mep;
using Wasmtime;
using Module = Wasmtime.Module;

namespace Morphir.Extensions.Wasm.AotProbe;

/// <summary>
/// The kernel proof of task 2.2 (design KPF-1). It loads the embedded Extism kernel and a published Extism
/// guest into one Wasmtime store, sends one <c>morphir.initialize</c> through <c>handle</c> and checks the
/// three claims of the task. It prints what it observed and returns 0 only when every claim holds.
/// </summary>
public static partial class KernelProof
{
    /// <summary>The SHA-256 of <c>morphir-python-binding-0.6.0.wasm</c> (finos/morphir-python release
    /// <c>extension/python/v0.6.0</c>), as finos/morphir <c>7b049c223</c> pins it in
    /// <c>.config/published-extension-bundles.toml</c> (design MEP-1).</summary>
    public const string PythonGuestSha256 = "ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c";

    /// <summary>The one MEP version this host speaks (design MEP-1).</summary>
    const string ProtocolVersion = "0.2.0-draft.1";

    /// <summary>
    /// The model-transport offer of the generic guest kit's <c>model-initialize</c> (finos/morphir <c>7b049c223</c>,
    /// <c>crates/morphir-mck/src/mep/generic.rs</c>, <c>offer</c> and <c>limits</c>). A published guest accepts
    /// MEP <c>0.2.0-draft.1</c> only with an offer; without one it refuses with -32011 (evidence 2.5).
    /// </summary>
    const string ModelTransportOffer =
        """{"tuples":[{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"},{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"},{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"},{"operation":"morphir.backend.generateModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}],"limits":{"maxEncodedBytes":1000000,"maxDecodedBytes":750000,"maxDepth":128,"maxNodes":100000},"guarantees":[]}""";

    /// <summary>
    /// The params of the kit's <c>compile-model-vthree</c> for the <c>python</c> language (finos/morphir
    /// <c>7b049c223</c>: <c>spec/mep/mck/guest-generic-json.json</c> and <c>compile_params</c> in <c>generic.rs</c>).
    /// </summary>
    const string CompileModelParams =
        """{"languageId":"python","sources":{"documents":[{"uri":"main.py","languageId":"python","version":1,"text":"def ready() -> bool:\n    return True\n\n\ndef blocked() -> bool:\n    return False\n"}]},"package":{"name":"mck/generic","exposedModules":["main"]},"options":{"typesOnly":false,"irVersion":"3.0.0"},"contextDigest":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","output":{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"3.0.0"}}""";

    /// <summary>The SHA-256 of the crafted <c>extism-spin.wasm</c>, as the pinned corpus records it.</summary>
    public const string SpinGuestSha256 = "013df84b7cdecce0386f1b20b6395d663df1533c01fcac16141019ce87d3e11e";

    const long PageBytes = 65536;

    /// <summary>The memory ceiling of the store that runs the call, as in the runtime report sample of the
    /// design (RPT-1): 256 MiB for each linear memory, the contract's ceiling.</summary>
    const long CallCeilingBytes = 256L * 1024 * 1024;

    /// <summary>The first request of a session on MEP <c>0.2.0-draft.1</c>, with a numeric id and the kit's
    /// model-transport offer (design MEP-1).</summary>
    public const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersions":["0.2.0-draft.1"],"host":{"kind":"cli","name":"morphir-dotnet-kernel-proof","version":"0.0.0"},"modelTransport":""" + ModelTransportOffer + "}}";

    enum Verdict
    {
        Holds,
        Fails,
        NotChecked,
    }

    /// <summary>Runs the proof. Arguments: <c>&lt;guest.wasm&gt; [--publish-log &lt;file&gt;] [--source &lt;path&gt;]...
    /// [--bounds &lt;extism-spin.wasm&gt;] [--sandbox &lt;directory of the pinned crafted modules&gt;]</c>.</summary>
    public static int Run(string[] args, TextWriter output)
    {
        if (!TryParse(args, out var guestPath, out var publishLog, out var sources, out var spinPath, out var sandboxModules))
        {
            output.WriteLine("usage: Morphir.Extensions.Wasm.AotProbe <guest.wasm> [--publish-log <file>] [--source <path>]... [--bounds <extism-spin.wasm>] [--sandbox <fixtures/wasm directory>]");
            return 2;
        }

        output.WriteLine("kernel proof: add-wasm-extension-host task 2.2, after decision MEP-1, with the task 3.1 session");
        output.WriteLine($"os: {RuntimeInformation.OSDescription}");
        output.WriteLine($"os architecture: {RuntimeInformation.OSArchitecture}; process architecture: {RuntimeInformation.ProcessArchitecture}; runtime identifier: {RuntimeInformation.RuntimeIdentifier}");
        output.WriteLine($"native aot: {!RuntimeFeature.IsDynamicCodeSupported}");
        output.WriteLine($"wasmtime package: {typeof(Engine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"}");
        output.WriteLine();

        var guestBytes = File.ReadAllBytes(guestPath);
        var guestDigest = Convert.ToHexStringLower(SHA256.HashData(guestBytes));
        output.WriteLine($"guest: {Path.GetFileName(guestPath)}, {guestBytes.Length} bytes, sha256 {guestDigest}");
        if (guestDigest != PythonGuestSha256)
        {
            output.WriteLine($"guest digest FAILS: expected the published {PythonGuestSha256}");
            return 1;
        }

        output.WriteLine("guest digest equals the published digest");

        var kernelBytes = ExtismKernel.Bytes;
        output.WriteLine($"kernel: moonrockz/extism {ExtismKernel.PackageVersion}, {kernelBytes.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(kernelBytes.Span))} (recorded {ExtismKernel.Sha256})");
        output.WriteLine();

        var engine = WasmEngine.Shared;
        var kernelModule = engine.Kernel.Module;
        var guestCompiled = engine.Load(guestBytes).Match(
            compiled => compiled,
            refused => throw new InvalidOperationException($"the engine refuses the guest: {refused.Code}: {refused.Message}"),
            invalid => throw new InvalidOperationException($"the guest is not a WASM module: {invalid}"));
        var guestModule = guestCompiled.Module;
        Describe(output, "kernel", kernelModule);
        Describe(output, "guest", guestModule);

        var admission = WasmHost.Admit(guestCompiled, WasmAbi.ExtismKernel);
        var guest = admission.Match<ExtismModule?>(admitted => admitted, _ => null);
        output.WriteLine($"guest classification: {admission.Match(admitted => admitted.Profile.Id, refused => $"refused {refused.Code}: {refused.Message}")}");
        output.WriteLine();
        if (guest is null)
        {
            return 1;
        }

        var claim2 = Check(output, "claim 2", () => CheckMemoryLimit(output, guest, MemoryMinimumPages(kernelModule)));
        var claim3 = Check(output, "claim 3", () => CheckHandle(output, guest));
        var session = Check(output, "session", () => CheckSession(output, guest));
        var claim1 = Check(output, "claim 1", () => CheckNoTrimWarnings(output, publishLog, sources));
        var bounds = spinPath is null ? Verdict.NotChecked : Check(output, "bounds", () => CheckBounds(output, spinPath));
        var sandbox = sandboxModules is null ? Verdict.NotChecked : Check(output, "sandbox", () => CheckSandbox(output, sandboxModules));

        output.WriteLine("summary:");
        output.WriteLine($"claim 1, no IL warning but the FSharp.Core summary baseline (so zero IL2026 and IL3050), and no suppressions: {Text(claim1)}");
        output.WriteLine($"claim 2, the store limit covers the kernel's memory: {Text(claim2)}");
        output.WriteLine($"claim 3, the host reads the result of handle directly: {Text(claim3)}");
        output.WriteLine($"session (task 3.1), a whole HostSession through the WASM channel: {Text(session)}");
        output.WriteLine($"bounds (task 2.5), budget, deadline and cancellation stop a spinning guest: {Text(bounds)}");
        output.WriteLine($"sandbox (tasks 3.3 and 3.4), host functions and WASI callbacks answer as the contract says: {Text(sandbox)}");
        return claim1 == Verdict.Holds && claim2 == Verdict.Holds && claim3 == Verdict.Holds && session == Verdict.Holds
            && bounds != Verdict.Fails && sandbox != Verdict.Fails ? 0 : 1;
    }

    /// <summary>The pinned crafted modules the sandbox check runs, each with
    /// the reply it must give to <c>morphir.initialize</c>, or null for a call that must fail.</summary>
    static readonly (string Module, string? Reply, string[] Output)[] SandboxModules =
    [
        ("extism-log", """{"jsonrpc":"2.0","id":1,"result":"level-2"}""", ["log info: kit-info--marker", "morphir_log trace: kit-morph-marker"]),
        ("extism-var-round-trip", """{"jsonrpc":"2.0","id":1,"result":"stored"}""", []),
        ("extism-fd-write", """{"jsonrpc":"2.0","id":1,"result":"printed"}""", ["stdout: kit-stdout-marker\n", "stderr: kit-stderr-marker\n"]),
        ("extism-wasi-empty", """{"jsonrpc":"2.0","id":1,"result":"tttt"}""", []),
        ("extism-clock-random", """{"jsonrpc":"2.0","id":1,"result":"ttt"}""", []),
        ("extism-http", null, ["refused http_request GET https://example.org/"]),
    ];

    /// <summary>
    /// Tasks 3.3 and 3.4 under the same executable: the untyped callbacks of the Extism host functions, the Morphir
    /// functions and the managed WASI sandbox run on pinned crafted modules, so a trimming or AOT defect in them
    /// shows here. Each module's digest is checked against the pinned corpus first.
    /// </summary>
    static Verdict CheckSandbox(TextWriter output, string directory)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "..", "..", "wasm-host.cases.json")));
        var modules = corpus.RootElement.GetProperty("modules");
        var request = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersion":"0.2.0-draft.1"}}""");
        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        foreach (var (name, reply, expectedOutput) in SandboxModules)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, name + ".wasm"));
            var recorded = modules.GetProperty(name).GetProperty("sha256").GetString();
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Expect(digest == recorded, $"{name}.wasm has the corpus digest {recorded}");
            var module = WasmEngine.Shared.Load(bytes).Match(
                compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(m => m, r => throw new InvalidOperationException(r.Message)),
                refused => throw new InvalidOperationException(refused.Message),
                invalid => throw new InvalidOperationException(invalid));
            using var store = Opened(SessionStore.Open(module, SessionLimits.Default));
            var outcome = store.CallHandle(request);
            var text = outcome.Match(
                completed => Encoding.UTF8.GetString(completed.Result.Output),
                failed => $"failed: {failed.Message}",
                notRun => $"not run: {notRun.Message}");
            var outputs = string.Join(" | ", store.GuestOutput.Outputs.Select(item => item.Text.Replace("\n", "\\n", StringComparison.Ordinal)));
            output.WriteLine($"  {name}: {text}; guest output: {(outputs.Length == 0 ? "none" : outputs)}");
            if (reply is null)
            {
                Expect(outcome.Match(_ => false, failed => failed.Code == "extension.incomplete_session", _ => false), $"{name} fails the session with extension.incomplete_session");
                Expect(store.RefusedHttpRequests.Count == 1, $"{name} records the refused request: {string.Join(", ", store.RefusedHttpRequests)}");
            }
            else
            {
                Expect(text == reply, $"{name} answers {reply}");
            }

            foreach (var fragment in expectedOutput)
            {
                Expect(store.GuestOutput.Outputs.Any(item => item.Text.StartsWith(fragment, StringComparison.Ordinal)), $"{name} records the guest output {fragment.Replace("\n", "\\n", StringComparison.Ordinal)}");
            }
        }

        return ok ? Verdict.Holds : Verdict.Fails;
    }

    /// <summary>
    /// Task 3.1 under the same executable: one whole <see cref="HostSession"/> on the guest through
    /// <see cref="WasmChannel"/>, on MEP <c>0.2.0-draft.1</c> with the kit's offer: initialize, one
    /// <c>morphir.frontend.compileModel</c>, shutdown and exit. Each request must be one <c>handle</c> call, and
    /// no notification may reach the guest, which advertises nothing.
    /// </summary>
    static Verdict CheckSession(TextWriter output, ExtismModule guest)
    {
        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        using var channel = WasmChannel.Open(guest, SessionLimits.Default).Match(
            opened => opened,
            failure => throw new InvalidOperationException($"the channel did not open: {failure.Message}"));
        var recording = new RecordingChannel(channel, output);
        var offer = JsonSerializer.Deserialize(ModelTransportOffer, MepJson.Default.ModelTransportSupport)
            ?? throw new InvalidOperationException("the offer is empty");
        var session = new HostSession(recording, new PeerInfo(PeerKind.Cli, "morphir-dotnet-kernel-proof", "0.0.0"), offer);
        var run = session.RunAsync(CancellationToken.None);

        var initialized = session.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        var negotiated = initialized.Match(value => value.ProtocolVersion.Value, failure => $"failed: {failure}");
        output.WriteLine($"  negotiated protocol version: {negotiated}");
        Expect(negotiated == ProtocolVersion, $"the session negotiated {ProtocolVersion}");

        using var parameters = JsonDocument.Parse(CompileModelParams);
        var compiled = session.RequestAsync(Method.CompileModel, parameters.RootElement.Clone(), CancellationToken.None).GetAwaiter().GetResult();
        var success = compiled.Match(
            result => result.TryGetProperty("success", out var flag) && flag.ValueKind == JsonValueKind.True
                && result.TryGetProperty("model", out var model) && model.TryGetProperty("irVersion", out var ir) && ir.GetString() == "3.0.0",
            error => false);
        output.WriteLine($"  compileModel: {compiled.Match(result => $"success {result.GetProperty("success")}, model irVersion {result.GetProperty("model").GetProperty("irVersion")}", error => $"error {error}")}");
        Expect(success, "compileModel returned a successful result with an IR 3.0.0 model");

        var shutdown = session.ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        Expect(shutdown.Match(_ => true, _ => false), "shutdown returned a result");
        session.ExitAsync().GetAwaiter().GetResult();
        var ended = run.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

        output.WriteLine($"  handle calls: {channel.HandleCalls}; requests: {recording.Requests}; notifications sent by the session: {recording.Notifications}, of which delivered: {recording.Notifications - recording.Withheld}");
        output.WriteLine($"  session failure: {session.FailureOrNull()?.ToString() ?? "none"}; channel failure: {channel.Failure?.Message ?? "none"}; instance ended: {channel.InstanceEnded}; run ended: {ended.Match(() => "end-of-stream", () => "stopped", failure => $"framing {failure}")}");
        Expect(channel.HandleCalls == recording.Requests && recording.Requests == 3, "one handle call per request (initialize, compileModel, shutdown)");
        Expect(recording.Notifications == 2 && recording.Withheld == 2, "morphir.initialized and morphir.exit were withheld without a call");
        Expect(session.FailureOrNull() is null && channel.Failure is null && channel.InstanceEnded, "the session ended cleanly and exit ended the instance");
        return ok ? Verdict.Holds : Verdict.Fails;
    }

    /// <summary>Passes every message to the WASM channel and prints it, with the reply.</summary>
    sealed class RecordingChannel(WasmChannel inner, TextWriter output) : ISingleMessageChannel
    {
        public int Requests { get; private set; }

        public int Notifications { get; private set; }

        public int Withheld { get; private set; }

        public async Task<byte[]?> RequestAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            Requests++;
            output.WriteLine($"  request {method} ({message.Length} bytes): {Shorten(Encoding.UTF8.GetString(message.Span))}");
            var calls = inner.HandleCalls;
            var reply = await inner.RequestAsync(method, message, cancellation);
            output.WriteLine($"  reply ({reply?.Length ?? 0} bytes, {inner.HandleCalls - calls} handle call): {(reply is null ? "none" : Shorten(Encoding.UTF8.GetString(reply)))}");
            return reply;
        }

        public async Task<byte[]?> NotifyAsync(string method, ReadOnlyMemory<byte> message, CancellationToken cancellation)
        {
            Notifications++;
            var calls = inner.HandleCalls;
            var reply = await inner.NotifyAsync(method, message, cancellation);
            var delivered = inner.HandleCalls - calls;
            if (delivered == 0)
            {
                Withheld++;
            }

            output.WriteLine($"  notification {method}: {(delivered == 0 ? "withheld, no handle call" : $"{delivered} handle call")}");
            return reply;
        }

        static string Shorten(string text) => text.Length <= 1200 ? text : $"{text[..1200]}... ({text.Length} characters)";
    }

    /// <summary>
    /// Task 2.5 under the same executable: the crafted spinning guest is stopped by the instruction budget, by an
    /// explicit short deadline through the epoch callback, and by cancellation through the same callback.
    /// </summary>
    static Verdict CheckBounds(TextWriter output, string spinPath)
    {
        var bytes = File.ReadAllBytes(spinPath);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        output.WriteLine($"  spinning guest: {Path.GetFileName(spinPath)}, sha256 {digest}");
        if (digest != SpinGuestSha256)
        {
            output.WriteLine($"  FAILED: expected the corpus digest {SpinGuestSha256}");
            return Verdict.Fails;
        }

        var spin = WasmEngine.Shared.Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, WasmAbi.ExtismKernel).Match(m => m, r => throw new InvalidOperationException(r.Message)),
            refused => throw new InvalidOperationException(refused.Message),
            invalid => throw new InvalidOperationException(invalid));
        var request = Encoding.UTF8.GetBytes(InitializeRequest);
        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        GuestFailure? Fail(SessionLimits limits, CancellationToken cancellation, out TimeSpan elapsed)
        {
            using var session = Opened(SessionStore.Open(spin, limits));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var failure = session.CallHandle(request, cancellation).Match<GuestFailure?>(_ => null, failed => failed, _ => null);
            elapsed = clock.Elapsed;
            output.WriteLine($"  {failure?.Message ?? "the call did not fail"} ({elapsed.TotalMilliseconds:0} ms)");
            return failure;
        }

        var budget = Fail(SessionLimits.Default with { InstructionBudget = 10_000_000 }, CancellationToken.None, out _);
        Expect(budget?.Code == "extension.budget_exhausted", "a budget of 10,000,000 ends the call with extension.budget_exhausted");

        var deadline = TimeSpan.FromMilliseconds(200);
        var late = Fail(SessionLimits.Default with { InstructionBudget = ulong.MaxValue, CallDeadline = deadline }, CancellationToken.None, out var lateElapsed);
        Expect(late?.Outcome == "deadline" && lateElapsed >= deadline && lateElapsed < deadline + TimeSpan.FromSeconds(2),
            "a 200 ms deadline ends the call with outcome deadline within the corpus margin of 2 s");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var cancelled = Fail(SessionLimits.Default with { InstructionBudget = ulong.MaxValue }, cancellation.Token, out var cancelElapsed);
        Expect(cancelled?.Outcome == "cancelled" && cancelElapsed < TimeSpan.FromMilliseconds(1200),
            "cancellation after 200 ms ends the call with outcome cancelled within one second");
        return ok ? Verdict.Holds : Verdict.Fails;
    }

    static string Text(Verdict verdict) => verdict switch
    {
        Verdict.Holds => "HOLDS",
        Verdict.Fails => "FAILS",
        Verdict.NotChecked => "NOT CHECKED",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
    };

    static Verdict Check(TextWriter output, string name, Func<Verdict> check)
    {
        output.WriteLine($"{name}:");
        Verdict verdict;
        try
        {
            verdict = check();
        }
        catch (Exception exception)
        {
            output.WriteLine($"  {exception.GetType().Name}: {exception.Message}");
            verdict = Verdict.Fails;
        }

        output.WriteLine($"  {name}: {Text(verdict)}");
        output.WriteLine();
        return verdict;
    }

    static bool TryParse(string[] args, out string guestPath, out string? publishLog, out List<string> sources, out string? spinPath, out string? sandboxModules)
    {
        guestPath = "";
        publishLog = null;
        spinPath = null;
        sandboxModules = null;
        sources = [];
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--publish-log" when i + 1 < args.Length:
                    publishLog = args[++i];
                    break;
                case "--source" when i + 1 < args.Length:
                    sources.Add(args[++i]);
                    break;
                case "--bounds" when i + 1 < args.Length:
                    spinPath = args[++i];
                    break;
                case "--sandbox" when i + 1 < args.Length:
                    sandboxModules = args[++i];
                    break;
                case var path when !path.StartsWith("--", StringComparison.Ordinal) && guestPath.Length == 0:
                    guestPath = path;
                    break;
                default:
                    return false;
            }
        }

        return guestPath.Length > 0;
    }

    static void Describe(TextWriter output, string label, Module module)
    {
        output.WriteLine($"{label} imports ({module.Imports.Count}):");
        foreach (var import in module.Imports)
        {
            output.WriteLine($"  {import.ModuleName} {import.Name} {Signature(import)}");
        }

        output.WriteLine($"{label} exports ({module.Exports.Count}):");
        foreach (var export in module.Exports)
        {
            output.WriteLine($"  {export.Name} {Signature(export)}");
        }

        output.WriteLine();
    }

    static string Signature(Import import) => import switch
    {
        FunctionImport f => $"func ({Kinds(f.Parameters)}) -> ({Kinds(f.Results)})",
        MemoryImport m => $"memory min {m.Minimum} max {m.Maximum?.ToString() ?? "none"} pages",
        _ => import.GetType().Name,
    };

    static string Signature(Export export) => export switch
    {
        FunctionExport f => $"func ({Kinds(f.Parameters)}) -> ({Kinds(f.Results)})",
        MemoryExport m => $"memory min {m.Minimum} max {m.Maximum?.ToString() ?? "none"} pages",
        _ => export.GetType().Name,
    };

    static string Kinds(IReadOnlyList<ValueKind> kinds) => string.Join(", ", kinds.Select(k => k.ToString().ToLowerInvariant()));

    static long MemoryMinimumPages(Module module) =>
        module.Exports.OfType<MemoryExport>().Select(m => m.Minimum).DefaultIfEmpty(0).Max();

    static string MemoryExportName(Module module) =>
        module.Exports.OfType<MemoryExport>().Select(m => m.Name).FirstOrDefault()
        ?? throw new InvalidOperationException($"{module.Name} exports no memory");

    /// <summary>Claim 2. In a store whose limiter sets a small per-memory ceiling, a kernel alloc larger than
    /// the ceiling returns 0 and the kernel memory stays at or under the ceiling, and the guest memory cannot
    /// grow past it either. The same requests in a store with a larger ceiling succeed, so the limiter is
    /// what refused them.</summary>
    static Verdict CheckMemoryLimit(TextWriter output, ExtismModule guest, long kernelMinimum)
    {
        var guestModule = guest.Compiled.Module;
        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        var guestMemoryName = MemoryExportName(guestModule);
        var guestMinimum = MemoryMinimumPages(guestModule);
        var ceilingPages = Math.Max(guestMinimum, kernelMinimum) + 16;
        var ceiling = ceilingPages * PageBytes;
        var request = ceiling + PageBytes;
        var growPages = ceilingPages - guestMinimum + 1;
        output.WriteLine($"  test ceiling: {ceiling} bytes ({ceilingPages} pages) for each memory; kernel alloc request {request} bytes; guest grow {growPages} pages from its minimum {guestMinimum}");

        using (var limited = Opened(SessionStore.Open(guest, SessionLimits.Default with { MemoryCeilingBytes = ceiling })))
        {
            var guestMemory = limited.Guest.GetMemory(guestMemoryName) ?? throw new InvalidOperationException("the guest memory is not exported");
            output.WriteLine($"  small store after instantiation: kernel memory {limited.KernelMemory.GetLength()} bytes, guest memory {guestMemory.GetLength()} bytes");

            var refused = limited.Alloc(request);
            var kernelAfter = limited.KernelMemory.GetLength();
            output.WriteLine($"  small store: alloc({request}) = {refused}; kernel memory {kernelAfter} bytes");
            Expect(refused == 0, "a kernel alloc larger than the ceiling returns 0");
            Expect(kernelAfter <= ceiling, "the kernel memory is at or under the ceiling");

            var accepted = limited.Alloc(4096);
            output.WriteLine($"  small store: alloc(4096) = {accepted}; kernel memory {limited.KernelMemory.GetLength()} bytes");
            Expect(accepted != 0, "a kernel alloc under the ceiling still succeeds");

            var guestBefore = guestMemory.GetLength();
            string grow;
            try
            {
                guestMemory.Grow(growPages);
                grow = "grew";
            }
            catch (WasmtimeException exception)
            {
                grow = $"refused ({exception.Message.Split('\n')[0].Trim()})";
            }

            var guestAfter = guestMemory.GetLength();
            output.WriteLine($"  small store: guest memory grow by {growPages} pages {grow}; guest memory {guestBefore} -> {guestAfter} bytes");
            Expect(guestAfter <= ceiling, "the guest memory is at or under the ceiling");
            Expect(guestAfter == guestBefore, "the refused grow left the guest memory unchanged");
        }

        var largeCeiling = 4 * ceiling;
        using (var large = Opened(SessionStore.Open(guest, SessionLimits.Default with { MemoryCeilingBytes = largeCeiling })))
        {
            var guestMemory = large.Guest.GetMemory(guestMemoryName) ?? throw new InvalidOperationException("the guest memory is not exported");
            var handle = large.Alloc(request);
            var kernelAfter = large.KernelMemory.GetLength();
            output.WriteLine($"  large store ({largeCeiling} bytes): alloc({request}) = {handle}; kernel memory {kernelAfter} bytes");
            Expect(handle != 0, "the same alloc succeeds under a larger ceiling");
            Expect(kernelAfter > ceiling && kernelAfter <= largeCeiling, "the kernel memory grew past the small ceiling and stayed under the large one");

            var guestBefore = guestMemory.GetLength();
            guestMemory.Grow(growPages);
            var guestAfter = guestMemory.GetLength();
            output.WriteLine($"  large store: guest memory grow by {growPages} pages; guest memory {guestBefore} -> {guestAfter} bytes");
            Expect(guestAfter > ceiling && guestAfter <= largeCeiling, "the same guest grow succeeds under a larger ceiling");
        }

        return ok ? Verdict.Holds : Verdict.Fails;
    }

    /// <summary>Claim 3. One <c>handle</c> call with the initialize request; the host reads the i32 result, the
    /// output and the error value from the kernel itself. The output must be one JSON-RPC response to id 1.</summary>
    static SessionStore Opened(SessionOpening opening) => opening.Match(
        session => session,
        failure => throw new InvalidOperationException($"the session did not open: {failure.Message}"));

    static Verdict CheckHandle(TextWriter output, ExtismModule guest)
    {
        var guestModule = guest.Compiled.Module;
        using var store = Opened(SessionStore.Open(guest, SessionLimits.Default with { MemoryCeilingBytes = CallCeilingBytes }));
        var guestMemory = store.GuestMemory;
        output.WriteLine($"  store: {CallCeilingBytes} bytes ceiling for each memory; 2 instances (kernel, guest); memories: kernel 1, guest {guestModule.Exports.OfType<MemoryExport>().Count()} exported, {guestModule.Imports.OfType<MemoryImport>().Count()} imported");
        output.WriteLine($"  request ({Encoding.UTF8.GetByteCount(InitializeRequest)} bytes): {InitializeRequest}");

        var outcome = store.CallHandle(Encoding.UTF8.GetBytes(InitializeRequest));
        var completed = outcome.Match(
            completed => completed,
            failed => throw new InvalidOperationException($"handle failed: {failed.Message}"),
            notRun => throw new InvalidOperationException($"handle did not run: {notRun.Message}"));
        var result = completed.Result;
        output.WriteLine($"  guest initialization: {store.Initialization}");
        output.WriteLine($"  handle returned i32: {result.ReturnCode}");
        output.WriteLine($"  kernel error value: error_get() = {result.ErrorHandle}, {result.Error.Length} bytes{(result.Error.Length > 0 ? ": " + Encoding.UTF8.GetString(result.Error) : "")}");
        output.WriteLine($"  kernel output: output_offset() = {result.OutputOffset}, output_length() = {result.Output.Length}");
        output.WriteLine($"  output bytes (UTF-8): {Encoding.UTF8.GetString(result.Output)}");
        output.WriteLine($"  memory after the call: kernel {store.KernelMemory.GetLength()} bytes, guest {guestMemory?.GetLength().ToString() ?? "unknown"} bytes");
        output.WriteLine($"  fuel used by the call: {completed.FuelUsed} of {store.Limits.InstructionBudget}");
        output.WriteLine($"  guest output: {store.GuestOutput.Outputs.Count} outputs; refused http_request: {store.RefusedHttpRequests.Count}; refused WASI calls: {(store.RefusedWasiCalls.Count == 0 ? "none" : string.Join(", ", store.RefusedWasiCalls.Select(call => $"{call.Key} x{call.Value}")))}");

        var ok = true;
        void Expect(bool condition, string what)
        {
            output.WriteLine($"  {(condition ? "ok" : "FAILED")}: {what}");
            ok &= condition;
        }

        Expect(result.ReturnCode == 0, "handle returned 0");
        Expect(result.ErrorHandle == 0, "the kernel holds no error");
        Expect(result.Output.Length > 0, "the kernel holds output");
        var (wellFormed, kind) = JsonRpcResponse(result.Output);
        Expect(wellFormed, $"the output is one JSON-RPC 2.0 response to id 1 ({kind})");
        var negotiated = ResultProtocolVersion(result.Output);
        Expect(negotiated == ProtocolVersion, $"the guest chose MEP {ProtocolVersion} (result.protocolVersion {negotiated ?? "absent"})");
        return ok ? Verdict.Holds : Verdict.Fails;
    }

    static string? ResultProtocolVersion(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("protocolVersion", out var version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static (bool WellFormed, string Kind) JsonRpcResponse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, "not an object");
            }

            var version = root.TryGetProperty("jsonrpc", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == "2.0";
            var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number && i.GetInt64() == 1;
            var hasResult = root.TryGetProperty("result", out _);
            var hasError = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
                && e.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String;
            var kind = hasResult && !hasError ? "result" : hasError && !hasResult ? "error" : "neither result nor error";
            return (version && id && hasResult != hasError, kind);
        }
        catch (JsonException exception)
        {
            return (false, $"not JSON: {exception.Message}");
        }
    }

    /// <summary>Claim 1. The Native AOT publish log has no IL warning or error of any number, and the project
    /// files and sources hold no trim or AOT suppression.</summary>
    static Verdict CheckNoTrimWarnings(TextWriter output, string? publishLog, List<string> sources)
    {
        if (publishLog is null || sources.Count == 0)
        {
            output.WriteLine("  pass --publish-log <file> and --source <path> to check the Native AOT publish log and the sources");
            return Verdict.NotChecked;
        }

        var lines = File.ReadAllLines(publishLog);
        var ilLines = lines.Where(line => IlDiagnostic().IsMatch(line)).Select(line => line.Trim()).Distinct().ToArray();
        output.WriteLine($"  publish log: {publishLog}, {lines.Length} lines");
        foreach (var line in lines.Where(line => LogSummary().IsMatch(line)))
        {
            output.WriteLine($"  log summary: {line.Trim()}");
        }

        // The FSharp.Core assembly-level summary lines, which every F#-carrying Native AOT publish in this repository
        // has, are the baseline that the TestMepAot target accepts and Morphir.Mep.AotProbe keeps as warnings. Every
        // other IL line fails the claim.
        var baseline = ilLines.Where(IsFSharpCoreSummary).ToArray();
        var unexpected = ilLines.Where(line => !IsFSharpCoreSummary(line)).ToArray();
        output.WriteLine($"  IL warnings and errors in the publish log: {ilLines.Length}");
        output.WriteLine($"  IL2026: {ilLines.Count(line => line.Contains("IL2026", StringComparison.Ordinal))}; IL3050: {ilLines.Count(line => line.Contains("IL3050", StringComparison.Ordinal))}");
        output.WriteLine($"  the FSharp.Core summary baseline (IL2104, IL3053), as TestMepAot accepts it: {baseline.Length}");
        foreach (var line in baseline)
        {
            output.WriteLine($"    {line}");
        }

        output.WriteLine($"  other IL warnings and errors: {unexpected.Length}");
        foreach (var line in unexpected)
        {
            output.WriteLine($"    {line}");
        }

        var findings = new List<string>();
        var baselineSettings = new List<string>();
        var files = 0;
        foreach (var file in sources.SelectMany(SourceFiles))
        {
            files++;
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (line.Trim() == FSharpCoreBaselineSetting)
                {
                    baselineSettings.Add($"{file}:{lineNumber}: {line.Trim()}");
                }
                else if (Suppressions.Any(pattern => pattern.IsMatch(line)))
                {
                    findings.Add($"{file}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        output.WriteLine($"  suppressions in {files} project and source files: {findings.Count}");
        foreach (var finding in findings)
        {
            output.WriteLine($"    {finding}");
        }

        output.WriteLine($"  the FSharp.Core baseline kept as warnings, not errors, as in Morphir.Mep.AotProbe: {baselineSettings.Count}");
        foreach (var setting in baselineSettings)
        {
            output.WriteLine($"    {setting}");
        }

        return unexpected.Length == 0 && findings.Count == 0 && files > 0 ? Verdict.Holds : Verdict.Fails;
    }

    /// <summary>The same test as the <c>TestMepAot</c> build target (<c>build/Build.MyriadJson.cs</c>).</summary>
    static bool IsFSharpCoreSummary(string line) =>
        line.Contains("FSharp.Core", StringComparison.Ordinal)
        && (line.Contains("IL2104", StringComparison.Ordinal) || line.Contains("IL3053", StringComparison.Ordinal));

    /// <summary>The one project setting that names an IL code: the FSharp.Core summary baseline of
    /// <c>tests/Morphir.Mep.AotProbe</c>, which keeps those two lines as warnings under warnings-as-errors. It hides
    /// nothing; the lines stay in the log and are counted above.</summary>
    const string FSharpCoreBaselineSetting = "<Warnings" + "NotAsErrors>$(Warnings" + "NotAsErrors);IL2104;IL3053</Warnings" + "NotAsErrors>";

    static IEnumerable<string> SourceFiles(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        string[] extensions = [".cs", ".csproj", ".props", ".targets", ".xml", ".json"];
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal);
    }

    // The patterns are put together from pieces so that this file does not match them itself.
    static readonly Regex[] Suppressions =
    [
        new("<" + "NoWarn>[^<]*IL\\d", RegexOptions.IgnoreCase),
        new("No" + "Warn=\"[^\"]*IL\\d", RegexOptions.IgnoreCase),
        new("Warnings" + "NotAsErrors[^<]*IL\\d", RegexOptions.IgnoreCase),
        new("Unconditional" + "SuppressMessage"),
        new("Suppress" + "Message\\([^)]*IL\\d{4}"),
        new("Trimmer" + "Root(Descriptor|Assembly)"),
        new("Suppress" + "TrimAnalysisWarnings"),
        new("#pragma\\s+warning\\s+disable\\s+IL", RegexOptions.IgnoreCase),
        new("<(Enable" + "TrimAnalyzer|EnableAot" + "Analyzer|IsAot" + "Compatible|IsTrim" + "mable)>\\s*false", RegexOptions.IgnoreCase),
    ];

    [GeneratedRegex(@"\b(warning|error)\s+IL\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IlDiagnostic();

    [GeneratedRegex(@"Warning\(s\)|Error\(s\)|Build succeeded|Build FAILED")]
    private static partial Regex LogSummary();
}
