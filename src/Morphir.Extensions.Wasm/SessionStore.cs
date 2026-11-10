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
/// Seams for later tasks. Task 3.1 builds the Extism adapter on <see cref="CallHandle"/>. Tasks 3.3 and 3.4
/// replace the trapping callbacks of the host functions and the empty WASI configuration.
/// </para>
/// </remarks>
public sealed class SessionStore : IDisposable
{
    const string WasiModule = "wasi_snapshot_preview1";

    readonly Store store;
    readonly Linker linker;
    readonly Dictionary<string, Function> kernelFunctions;
    readonly List<string> unprovidedCalls = [];
    readonly Stopwatch callClock = new();
    CancellationToken callCancellation;
    Instance? guest;
    bool initialized;

    SessionStore(ExtismModule module, SessionLimits limits, Store store, Linker linker)
    {
        Module = module;
        Limits = limits;
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
    public static SessionOpening Open(ExtismModule module, SessionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.MemoryCeilingBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.CallDeadline, TimeSpan.Zero);

        var engine = module.Compiled.Engine.Engine;
        var store = new Store(engine);
        Linker? linker = null;
        SessionStore? session = null;
        try
        {
            store.SetLimits(memorySize: limits.MemoryCeilingBytes, tableElements: null, instances: null, tables: null, memories: null);
            linker = new Linker(engine);
            session = new SessionStore(module, limits, store, linker);
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

    /// <summary>Imports outside the kernel and WASI that the guest called. Each such call stops the call.</summary>
    public IReadOnlyList<string> UnprovidedCalls => unprovidedCalls;

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
        catch (WasmtimeException exited) when (exited.ExitCode == 0)
        {
            // WASI proc_exit(0): the contract's policy makes the call succeed (wasi.procExitZero).
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
        return new HandleOutcome.Failed(failure);
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
                case HostFunctionStopped host:
                    return (GuestFailureKind.IncompleteSession, host.Message);
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

        return failure.ExitCode is { } exitCode
            ? (GuestFailureKind.IncompleteSession, $"the guest called proc_exit with code {exitCode} during a call")
            : (GuestFailureKind.GuestTrap, FirstLine(failure.Message));
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

    /// <summary>Thrown by the callback of an import this host does not provide yet: a host function that stops the call.</summary>
    sealed class HostFunctionStopped(string message) : Exception(message);

    /// <summary>
    /// Instantiates <paramref name="guest"/> in this store. Its kernel imports are linked to the kernel instance,
    /// its WASI imports to Wasmtime's WASI with an empty configuration (no arguments, environment, preopens or
    /// standard streams), and any other function import to a callback that records the call and stops it.
    /// Classification has already refused every import outside the contract's surface.
    /// </summary>
    Instance Link(Module guest)
    {
        var wasi = false;
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
            }
            else if (function.ModuleName == WasiModule)
            {
                wasi = true;
            }
            else
            {
                var label = $"{function.ModuleName} {function.Name}";
                linker.DefineFunction(function.ModuleName, function.Name,
                    (Caller _, ReadOnlySpan<ValueBox> _, Span<ValueBox> _) =>
                    {
                        unprovidedCalls.Add(label);
                        throw new HostFunctionStopped($"the guest called {label}, which this host does not provide yet");
                    },
                    function.Parameters, function.Results);
            }
        }

        if (wasi)
        {
            linker.DefineWasi();
            store.SetWasiConfiguration(new WasiConfiguration());
        }

        return linker.Instantiate(store, guest);
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
    }
}
