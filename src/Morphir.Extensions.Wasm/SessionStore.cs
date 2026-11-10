// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>What one <c>handle</c> call left: its i32 result, and the output and error the kernel holds.</summary>
/// <param name="ReturnCode">The i32 that <c>handle</c> returned.</param>
/// <param name="OutputOffset">The kernel's <c>output_offset()</c> after the call.</param>
/// <param name="Output">The <c>output_length()</c> bytes at <paramref name="OutputOffset"/> in kernel memory.</param>
/// <param name="ErrorHandle">The kernel's <c>error_get()</c> after the call; 0 is no error.</param>
/// <param name="Error">The bytes of the error block, empty when there is none.</param>
public sealed record HandleCallResult(int ReturnCode, long OutputOffset, byte[] Output, long ErrorHandle, byte[] Error);

/// <summary>
/// The store of one session (parent decision ENG-1): a new Wasmtime store with its own limits, its own instance
/// of the embedded Extism kernel and its own instance of the guest, so a second session starts with nothing
/// from the first. The compiled kernel and guest modules are the engine's, shared by every session; nothing
/// else is. Each import the guest makes from <see cref="ExtismKernel.ImportModule"/> with a kernel function's
/// name is defined as that export of this session's kernel instance, so the guest's input, output and
/// allocations go through the kernel inside WASM. The store limit gives every linear memory in the store, the
/// kernel's and the guest's, the same ceiling. A store is not thread-safe; one session runs one call at a time.
/// </summary>
/// <remarks>
/// <para>
/// The bounds (parent decisions BND-1 and BND-2). Each call starts with the session's whole instruction budget
/// as fuel; running out is <see cref="GuestFailureKind.BudgetExhausted"/>. The engine's epoch advances every
/// <see cref="WasmEngine.EpochInterval"/>, and at every tick the store's epoch callback, which runs on the
/// thread of the call, stops the call when its <see cref="CancellationToken"/> is cancelled
/// (<see cref="GuestFailureKind.Cancelled"/>) or its deadline has passed (<see cref="GuestFailureKind.CallDeadline"/>),
/// so a spinning guest stops within one tick of either. The store limit caps each memory at the ceiling; the
/// engine refuses a growth past it without telling the host, so a call that fails with the guest's memory at the
/// ceiling is <see cref="GuestFailureKind.MemoryLimit"/>, and a module whose initial memory is above the ceiling
/// fails <see cref="Open"/> the same way. Every failure carries the memory sizes.
/// </para>
/// <para>
/// After a failed call the session is finished: <see cref="Failure"/> holds the failure, and every later call
/// returns <see cref="HandleOutcome.NotRun"/> with it, without running guest or kernel code.
/// </para>
/// <para>
/// The Extism adapter, <see cref="WasmChannel"/>, is built on <see cref="CallHandle"/>.
/// </para>
/// <para>
/// The sandbox (tasks 3.3 and 3.4). The Extism host functions and the Morphir functions do what the contract's policy
/// says (<see cref="ExtismHostFunctions"/>): no configuration, a variable store and an IR cache that belong to this
/// session and enforce their limits, logs as guest output, and <c>http_request</c> refused with no connection. WASI
/// preview 1 is the managed sandbox (<see cref="WasiSandbox"/>). Guest output is bounded in <see cref="GuestOutput"/>;
/// what the guest attempted and did not get is in <see cref="RefusedHttpRequests"/> and
/// <see cref="RefusedWasiCalls"/>; the private workspace is created on the guest's first request for it and removed
/// when the session fails or is disposed.
/// </para>
/// </remarks>
public sealed class SessionStore : IDisposable
{
    readonly Store store;
    readonly Linker linker;
    readonly Dictionary<string, Function> kernelFunctions;
    readonly ExtismHostFunctions hostFunctions;
    readonly WasiSandbox wasi;
    readonly Stopwatch callClock = new();
    CancellationToken callCancellation;
    Instance? guest;
    bool initialized;

    SessionStore(ExtismModule module, SessionLimits limits, Store store, Linker linker, SessionWorkspace workspace)
    {
        Module = module;
        Limits = limits;
        Workspace = workspace;
        var policy = WasmProfileContract.Pinned.Policy;
        hostFunctions = new ExtismHostFunctions(this, policy);
        wasi = new WasiSandbox(policy, GuestOutput, module.Compiled.GuestMemoryName);
        this.store = store;
        this.linker = linker;
        store.SetEpochDeadlineCallback(OnEpochDeadline);
        ArmCall(CancellationToken.None);

        var kernel = linker.Instantiate(store, module.Compiled.Engine.Kernel.Module);
        KernelMemory = kernel.GetMemory("memory")
            ?? throw new InvalidOperationException("The kernel exports no memory.");
        kernelFunctions = new Dictionary<string, Function>(StringComparer.Ordinal);
        foreach (var name in ExtismKernel.FunctionNames)
        {
            kernelFunctions[name] = kernel.GetFunction(name)
                ?? throw new InvalidOperationException($"The kernel does not export {name}.");
        }
    }

    /// <summary>
    /// Opens a session of <paramref name="module"/>: a new store with <paramref name="limits"/>, the kernel
    /// instantiated in it, and the guest instantiated and linked to that kernel. No guest export runs. A guest
    /// whose initial memory is above the ceiling is not instantiated: the session fails with
    /// <see cref="GuestFailureKind.MemoryLimit"/> before any request (the contract's <c>initial-above-ceiling</c> rule).
    /// </summary>
    /// <param name="module">The admitted module.</param>
    /// <param name="limits">The session's bounds.</param>
    /// <param name="workspaceRoot">The absolute directory below which the session's private workspace is created
    /// when the guest first asks for it, or null for the system's temporary directory.</param>
    /// <exception cref="ArgumentException"><paramref name="workspaceRoot"/> is not an absolute path.</exception>
    public static SessionOpening Open(ExtismModule module, SessionLimits limits, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.MemoryCeilingBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.CallDeadline, TimeSpan.Zero);

        var workspace = new SessionWorkspace(workspaceRoot);
        var engine = module.Compiled.Engine.Engine;
        var store = new Store(engine);
        Linker? linker = null;
        SessionStore? session = null;
        try
        {
            store.SetLimits(memorySize: limits.MemoryCeilingBytes, tableElements: null, instances: null, tables: null, memories: null);
            linker = new Linker(engine);
            session = new SessionStore(module, limits, store, linker, workspace);
            if (session.InitialMemoryAboveCeiling() is { } failure)
            {
                session.Dispose();
                return new SessionOpening.Failed(failure);
            }

            session.LinkGuest();
            return new SessionOpening.Opened(session);
        }
        catch
        {
            if (session is not null)
            {
                session.Dispose();
            }
            else
            {
                linker?.Dispose();
                store.Dispose();
            }

            throw;
        }
    }

    /// <summary>The module this session runs.</summary>
    public ExtismModule Module { get; }

    /// <summary>The bounds of this session.</summary>
    public SessionLimits Limits { get; }

    /// <summary>The guest's instance in this session.</summary>
    public Instance Guest => guest ?? throw new InvalidOperationException("The guest is not instantiated.");

    /// <summary>
    /// The guest's own memory, or null when it defines none. The host reads it through the module's memory export,
    /// or through the export it adds when the module does not export its memory
    /// (<see cref="CompiledModule.ExportsGuestMemoryForTheHost"/>).
    /// </summary>
    public Memory? GuestMemory { get; private set; }

    /// <summary>The kernel's linear memory, where the guest's input, output, error and allocations live.</summary>
    public Memory KernelMemory { get; }

    /// <summary>Which start-up export of the guest ran before its first call, or "not yet".</summary>
    public string Initialization { get; private set; } = "not yet";

    /// <summary>
    /// What the guest wrote to WASI stdout and stderr and logged, and the <c>http_request</c> calls the host refused,
    /// bounded (<see cref="GuestOutputTail"/>). None of it is part of a reply, and the library never writes it to the
    /// process's streams. The worker (task 4.1) drains it after each call for its <c>output</c> events. It stays
    /// readable after the session ends.
    /// </summary>
    public GuestOutputTail GuestOutput { get; } = new();

    /// <summary>The <c>http_request</c> calls the host refused, each with the method and URL the guest asked for.
    /// No connection was made for any of them. A refused request ends the session, so there is at most one.</summary>
    public IReadOnlyList<RefusedHttpRequest> RefusedHttpRequests => hostFunctions.HttpRequests;

    /// <summary>The WASI functions the guest called and was refused, errno 8 (<c>badf</c>) or 76
    /// (<c>notcapable</c>), with how many times each.</summary>
    public IReadOnlyDictionary<string, long> RefusedWasiCalls => wasi.RefusedCalls;

    /// <summary>The session's private workspace, the <c>root</c> that <c>morphir_get_workspace_info</c> gives, or
    /// null when the guest has not asked for it. The directory exists from the guest's first request for it until the
    /// session fails or is disposed; the path stays readable afterwards.</summary>
    public string? WorkspaceDirectory => Workspace.Directory;

    internal SessionWorkspace Workspace { get; }

    /// <summary>The failure that finished this session, or null while calls can still run the guest.</summary>
    public GuestFailure? Failure { get; private set; }

    /// <summary>The memory sizes now.</summary>
    public MemoryUse Memory => new(GuestMemory?.GetLength(), KernelMemory.GetLength(), Limits.MemoryCeilingBytes);

    /// <summary>Calls the kernel's <c>alloc</c> with the session's whole budget and deadline: a handle to a new
    /// block of <paramref name="length"/> bytes in kernel memory, or 0 when the kernel cannot allocate it.</summary>
    /// <exception cref="InvalidOperationException">The session has failed.</exception>
    public long Alloc(long length)
    {
        if (Failure is { } failure)
        {
            throw new InvalidOperationException($"The session has failed: {failure.Message}");
        }

        ArmCall(CancellationToken.None);
        return CallI64("alloc", length);
    }

    /// <summary>
    /// Calls <c>handle</c> as an Extism host does: the call is given the session's full instruction budget and
    /// call deadline, the guest's start-up export runs once, the kernel is reset, the input is written to a
    /// kernel block and set as the input, <c>handle</c> runs, and the host reads the i32 result, then the error
    /// and the output from the kernel. All of that counts toward the budget and the deadline.
    /// </summary>
    /// <param name="input">The message, as raw bytes.</param>
    /// <param name="cancellation">Stops the call within one epoch tick of being cancelled. A token cancelled
    /// before the call fails it without running the guest.</param>
    /// <returns>
    /// <see cref="HandleOutcome.Completed"/> when <c>handle</c> returned (or the guest called WASI
    /// <c>proc_exit(0)</c>, which the contract makes a successful call), <see cref="HandleOutcome.Failed"/> when
    /// the call failed, and <see cref="HandleOutcome.NotRun"/> when an earlier call had already failed.
    /// </returns>
    public HandleOutcome CallHandle(ReadOnlySpan<byte> input, CancellationToken cancellation = default)
    {
        if (Failure is { } ended)
        {
            return new HandleOutcome.NotRun(ended);
        }

        var handle = Guest.GetFunction("handle")
            ?? throw new InvalidOperationException("The guest does not export handle.");
        ArmCall(cancellation);
        if (cancellation.IsCancellationRequested)
        {
            return Fail(GuestFailureKind.Cancelled, "the call was cancelled before the guest ran");
        }

        try
        {
            Initialize();
            CallVoid("reset");
            if (input.Length > 0)
            {
                var block = CallI64("alloc", (long)input.Length);
                if (block == 0)
                {
                    return Fail(GuestFailureKind.IncompleteSession, $"the kernel has no room for the {input.Length} input bytes");
                }

                input.CopyTo(KernelMemory.GetSpan(block, input.Length));
                CallVoid("input_set", block, (long)input.Length);
            }

            var returnCode = handle.Invoke() is int code ? code : 0;
            return Complete(returnCode);
        }
        catch (WasmtimeException exited) when (Cause<GuestExited>(exited) is { Code: 0 })
        {
            // WASI proc_exit(0): the contract's policy makes the call succeed, with the output set before it
            // (wasi.procExitZero).
            return Complete(0);
        }
        catch (WasmtimeException failure)
        {
            var (kind, detail) = Classify(failure);
            return Fail(kind, detail);
        }
        finally
        {
            callCancellation = CancellationToken.None;
        }
    }

    HandleOutcome Complete(int returnCode)
    {
        var errorHandle = CallI64("error_get");
        var error = errorHandle == 0 ? [] : Read(errorHandle, CallI64("length", errorHandle));
        var outputOffset = CallI64("output_offset");
        var outputLength = CallI64("output_length");
        var output = outputLength == 0 ? [] : Read(outputOffset, outputLength);
        callClock.Stop();
        return new HandleOutcome.Completed(
            new HandleCallResult(returnCode, outputOffset, output, errorHandle, error), FuelUsed, callClock.Elapsed, Memory);
    }

    /// <summary>
    /// Finishes the session with a failure. The memory rule (contract <c>at-ceiling</c>, parent decision BND-2)
    /// applies to every failure that has a code: a trap, a failure and an exhausted budget become
    /// <see cref="GuestFailureKind.MemoryLimit"/> when the guest's memory is at the ceiling. The outcomes
    /// <c>deadline</c> and <c>cancelled</c> are kept, since the host itself stopped the call.
    /// </summary>
    HandleOutcome.Failed Fail(GuestFailureKind signal, string detail)
    {
        callClock.Stop();
        var memory = Memory;
        var kind = signal is GuestFailureKind.GuestTrap or GuestFailureKind.IncompleteSession or GuestFailureKind.BudgetExhausted
            && memory.GuestAtCeiling
                ? GuestFailureKind.MemoryLimit
                : signal;
        var failure = new GuestFailure(kind, signal, detail, memory, FuelUsed, callClock.Elapsed);
        Failure = failure;
        // The session is finished, so its workspace goes now, not when the store is disposed.
        Workspace.Remove();
        return new HandleOutcome.Failed(failure);
    }

    /// <summary>The first exception of type <typeparamref name="T"/> in <paramref name="failure"/>'s chain of inner
    /// exceptions: what a host callback threw, which Wasmtime returns as the call's inner exception.</summary>
    static T? Cause<T>(Exception failure)
        where T : Exception
    {
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is T found)
            {
                return found;
            }
        }

        return null;
    }

    ulong FuelUsed => Limits.InstructionBudget - Math.Min(store.Fuel, Limits.InstructionBudget);

    /// <summary>What a failed call's exception means. The epoch callback's <see cref="CallStopped"/> is the
    /// exception's inner exception; Wasmtime's own traps carry their trap code.</summary>
    (GuestFailureKind Kind, string Detail) Classify(WasmtimeException failure)
    {
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
        {
            switch (cause)
            {
                case CallStopped stopped:
                    return (stopped.Kind, stopped.Message);
                case HostCallStopped host:
                    return (GuestFailureKind.IncompleteSession, host.Message);
                case GuestExited exited:
                    return (GuestFailureKind.IncompleteSession, exited.Message);
            }
        }

        if (failure is TrapException trap)
        {
            return trap.Type switch
            {
                TrapCode.OutOfFuel => (GuestFailureKind.BudgetExhausted, $"the call used its {Limits.InstructionBudget} units of fuel"),
                TrapCode.Interrupt => (GuestFailureKind.CallDeadline, $"the engine interrupted the call after {callClock.Elapsed.TotalMilliseconds:0} ms"),
                _ => (GuestFailureKind.GuestTrap, FirstLine(trap.Message)),
            };
        }

        return (GuestFailureKind.GuestTrap, FirstLine(failure.Message));
    }

    static string FirstLine(string message) => message.Split('\n', 2)[0].TrimEnd('\r');

    /// <summary>The contract's <c>initial-above-ceiling</c> rule, checked before the guest is instantiated.</summary>
    GuestFailure? InitialMemoryAboveCeiling()
    {
        var compiled = Module.Compiled;
        var memory = compiled.RunModule.Exports.OfType<MemoryExport>().FirstOrDefault(export => export.Name == compiled.GuestMemoryName);
        if (memory is null || memory.Minimum * MemoryUse.PageBytes <= Limits.MemoryCeilingBytes)
        {
            return null;
        }

        var initial = memory.Minimum * MemoryUse.PageBytes;
        var failure = new GuestFailure(
            GuestFailureKind.MemoryLimit,
            GuestFailureKind.MemoryLimit,
            $"the module's initial memory of {memory.Minimum} pages is above the ceiling; it was not instantiated",
            new MemoryUse(initial, KernelMemory.GetLength(), Limits.MemoryCeilingBytes),
            0,
            TimeSpan.Zero);
        Failure = failure;
        return failure;
    }

    void LinkGuest()
    {
        var compiled = Module.Compiled;
        guest = Link(compiled.RunModule);
        GuestMemory = compiled.GuestMemoryName is { } name ? guest.GetMemory(name) : null;
    }

    /// <summary>Gives the next call the session's whole instruction budget, a deadline of
    /// <see cref="SessionLimits.CallDeadline"/> from now, and <paramref name="cancellation"/>. The epoch deadline
    /// is one tick, so the epoch callback checks both at every tick.</summary>
    void ArmCall(CancellationToken cancellation)
    {
        callCancellation = cancellation;
        store.Fuel = Limits.InstructionBudget;
        store.SetEpochDeadline(1);
        callClock.Restart();
    }

    /// <summary>
    /// The store's epoch callback. It runs on the thread of the call, at each epoch tick while WASM runs, and
    /// stops the call by throwing <see cref="CallStopped"/>, which Wasmtime returns as the inner exception of the
    /// call's <see cref="WasmtimeException"/>. Otherwise the call runs for one more tick.
    /// </summary>
    ulong OnEpochDeadline(Store _)
    {
        if (callCancellation.IsCancellationRequested)
        {
            throw new CallStopped(GuestFailureKind.Cancelled, $"the call was cancelled after {callClock.Elapsed.TotalMilliseconds:0} ms");
        }

        if (callClock.Elapsed >= Limits.CallDeadline)
        {
            throw new CallStopped(GuestFailureKind.CallDeadline,
                $"the call ran for {callClock.Elapsed.TotalMilliseconds:0} ms, past its deadline of {Limits.CallDeadline.TotalMilliseconds:0} ms");
        }

        return 1;
    }

    /// <summary>Thrown by the epoch callback to stop a call.</summary>
    sealed class CallStopped(GuestFailureKind kind, string message) : Exception(message)
    {
        public GuestFailureKind Kind { get; } = kind;
    }

    /// <summary>
    /// Instantiates <paramref name="guest"/> in this store. Its kernel imports are linked to the kernel instance, its
    /// Extism and Morphir host function imports to <see cref="ExtismHostFunctions"/>, and its WASI imports to the
    /// managed sandbox, <see cref="WasiSandbox"/>; the engine's own WASI is not linked, so the guest has no preopened
    /// directory, arguments, environment or stdin, and nothing reaches the host's streams. Every callback is an
    /// untyped Wasmtime callback with the import's own type, which classification has checked against the contract.
    /// Classification has already refused every import outside the contract's surface, so an import with no
    /// callback here is a defect of this library, not of the guest.
    /// </summary>
    Instance Link(Module guest)
    {
        foreach (var import in guest.Imports)
        {
            if (import is not FunctionImport function)
            {
                throw new NotSupportedException(
                    $"The guest imports {import.ModuleName} {import.Name}, which is not a function.");
            }

            if (function.ModuleName == ExtismKernel.ImportModule
                && kernelFunctions.TryGetValue(function.Name, out var exported))
            {
                linker.Define(function.ModuleName, function.Name, exported);
                continue;
            }

            var callback = function.ModuleName == WasiSandbox.Module
                ? wasi.Callback(function.Name)
                : hostFunctions.Callback(function.ModuleName, function.Name)
                    ?? throw new NotSupportedException(
                        $"The guest imports {function.ModuleName}.{function.Name}, which classification admitted and this host does not define.");
            linker.DefineFunction(function.ModuleName, function.Name, callback, function.Parameters, function.Results);
        }

        return linker.Instantiate(store, guest);
    }

    /// <summary>The size of the kernel block at <paramref name="handle"/>, or null when the handle is 0 or names no
    /// block: the kernel's <c>length</c> gives 0 for a handle that is not the start of an allocated block.</summary>
    internal long? TryBlockLength(long handle)
    {
        if (handle == 0)
        {
            return null;
        }

        var length = CallI64("length", handle);
        return length > 0 ? length : null;
    }

    /// <summary>The size of the kernel block at <paramref name="handle"/>; a handle that names no block stops the
    /// call of <paramref name="function"/>.</summary>
    internal long BlockLength(string function, long handle) =>
        TryBlockLength(handle) ?? throw new HostCallStopped($"{function}: no block at handle {handle}");

    /// <summary>The bytes of the kernel block at <paramref name="handle"/>; a handle that names no block stops the
    /// call.</summary>
    internal byte[] ReadBlock(string function, long handle)
    {
        var length = BlockLength(function, handle);
        return Read(handle, length);
    }

    /// <summary>The text of the kernel block at <paramref name="handle"/>, at most
    /// <see cref="GuestOutputTail.PayloadBytes"/> of it with bytes that are not UTF-8 replaced, and how many bytes
    /// after those were not read.</summary>
    internal (string Text, long Elided) ReadText(string function, long handle)
    {
        var length = BlockLength(function, handle);
        var read = Math.Min(length, GuestOutputTail.PayloadBytes);
        return (System.Text.Encoding.UTF8.GetString(KernelMemory.GetSpan(handle, (int)read)), length - read);
    }

    /// <summary>A new kernel block holding <paramref name="bytes"/>: its handle. A kernel with no room for it stops
    /// the call of <paramref name="function"/>.</summary>
    internal long NewBlock(string function, ReadOnlySpan<byte> bytes)
    {
        var handle = CallI64("alloc", (long)bytes.Length);
        if (handle == 0)
        {
            throw new HostCallStopped($"{function}: the kernel has no room for {bytes.Length} bytes");
        }

        bytes.CopyTo(KernelMemory.GetSpan(handle, bytes.Length));
        return handle;
    }

    /// <summary>Runs the guest's start-up export once, as upstream Extism does: <c>_initialize</c> and
    /// <c>hs_init(0, 0)</c> for a Haskell module, else <c>__wasm_call_ctors</c>, else <c>_initialize</c>.</summary>
    void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        if (Guest.GetFunction("hs_init") is { } hsInit)
        {
            var ran = "hs_init";
            if (UnitFunction(Guest, "_initialize") is { } start)
            {
                start.Invoke();
                ran = "_initialize, hs_init";
            }

            hsInit.Invoke((ValueBox)0, (ValueBox)0);
            Initialization = ran;
        }
        else if (Guest.GetFunction("__wasm_call_ctors") is { } constructors)
        {
            if (UnitFunction(Guest, "__wasm_call_ctors") is not null)
            {
                constructors.Invoke();
                Initialization = "__wasm_call_ctors";
            }
            else
            {
                Initialization = "none (__wasm_call_ctors has parameters or results)";
            }
        }
        else if (UnitFunction(Guest, "_initialize") is { } start)
        {
            start.Invoke();
            Initialization = "_initialize";
        }
        else
        {
            Initialization = "none (no start-up export)";
        }
    }

    static Function? UnitFunction(Instance instance, string name) =>
        instance.GetFunction(name) is { Parameters.Count: 0, Results.Count: 0 } function ? function : null;

    byte[] Read(long offset, long length) => KernelMemory.GetSpan(offset, checked((int)length)).ToArray();

    long CallI64(string name, params ReadOnlySpan<ValueBox> arguments) =>
        kernelFunctions[name].Invoke(arguments) is long value
            ? value
            : throw new InvalidOperationException($"The kernel function {name} did not return an i64.");

    void CallVoid(string name, params ReadOnlySpan<ValueBox> arguments) => kernelFunctions[name].Invoke(arguments);


    /// <inheritdoc />
    public void Dispose()
    {
        linker.Dispose();
        store.Dispose();
        Workspace.Remove();
    }
}
