// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
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
/// Seams for later tasks. Task 2.5 maps the engine's signals: a call that runs out of fuel throws a
/// <see cref="TrapException"/> with <see cref="TrapCode.OutOfFuel"/>, a call past its deadline one with
/// <see cref="TrapCode.Interrupt"/>, and a module whose initial memory is above the ceiling fails
/// <see cref="Open"/> with a <see cref="WasmtimeException"/>; <see cref="GuestMemory"/> and
/// <see cref="KernelMemory"/> give the memory sizes for the at-ceiling rule. Task 3.1 builds the Extism adapter
/// on <see cref="CallHandle"/>. Tasks 3.3 and 3.4 replace the trapping callbacks of the host functions and the
/// empty WASI configuration.
/// </remarks>
public sealed class SessionStore : IDisposable
{
    const string WasiModule = "wasi_snapshot_preview1";

    readonly Store store;
    readonly Linker linker;
    readonly Dictionary<string, Function> kernelFunctions;
    readonly List<string> unprovidedCalls = [];
    readonly ulong deadlineTicks;
    bool initialized;

    SessionStore(ExtismModule module, SessionLimits limits, Store store, Linker linker)
    {
        Module = module;
        Limits = limits;
        this.store = store;
        this.linker = linker;
        deadlineTicks = WasmEngine.EpochTicks(limits.CallDeadline);
        ArmCall();

        var kernel = linker.Instantiate(store, module.Compiled.Engine.Kernel.Module);
        KernelMemory = kernel.GetMemory("memory")
            ?? throw new InvalidOperationException("The kernel exports no memory.");
        kernelFunctions = new Dictionary<string, Function>(StringComparer.Ordinal);
        foreach (var name in ExtismKernel.FunctionNames)
        {
            kernelFunctions[name] = kernel.GetFunction(name)
                ?? throw new InvalidOperationException($"The kernel does not export {name}.");
        }

        Guest = Link(module.Compiled.Module);
        GuestMemory = module.Compiled.Module.Exports.OfType<MemoryExport>().Select(memory => Guest.GetMemory(memory.Name)).FirstOrDefault();
    }

    /// <summary>
    /// Opens a session of <paramref name="module"/>: a new store with <paramref name="limits"/>, the kernel
    /// instantiated in it, and the guest instantiated and linked to that kernel. No guest export runs.
    /// </summary>
    public static SessionStore Open(ExtismModule module, SessionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limits.MemoryCeilingBytes, 0);

        var engine = module.Compiled.Engine.Engine;
        var store = new Store(engine);
        Linker? linker = null;
        try
        {
            store.SetLimits(memorySize: limits.MemoryCeilingBytes, tableElements: null, instances: null, tables: null, memories: null);
            linker = new Linker(engine);
            return new SessionStore(module, limits, store, linker);
        }
        catch
        {
            linker?.Dispose();
            store.Dispose();
            throw;
        }
    }

    /// <summary>The module this session runs.</summary>
    public ExtismModule Module { get; }

    /// <summary>The bounds of this session.</summary>
    public SessionLimits Limits { get; }

    /// <summary>The guest's instance in this session.</summary>
    public Instance Guest { get; }

    /// <summary>The guest's exported memory, or null when it exports none.</summary>
    public Memory? GuestMemory { get; }

    /// <summary>The kernel's linear memory, where the guest's input, output, error and allocations live.</summary>
    public Memory KernelMemory { get; }

    /// <summary>Which start-up export of the guest ran before its first call, or "not yet".</summary>
    public string Initialization { get; private set; } = "not yet";

    /// <summary>Imports outside the kernel and WASI that the guest called. Each such call traps.</summary>
    public IReadOnlyList<string> UnprovidedCalls => unprovidedCalls;

    /// <summary>Calls the kernel's <c>alloc</c>: a handle to a new block of <paramref name="length"/> bytes in
    /// kernel memory, or 0 when the kernel cannot allocate it.</summary>
    public long Alloc(long length) => CallI64("alloc", length);

    /// <summary>
    /// Calls <c>handle</c> as an Extism host does: the call is given the session's full instruction budget and
    /// call deadline, the guest's start-up export runs once, the kernel is reset, the input is written to a
    /// kernel block and set as the input, <c>handle</c> runs, and the host reads the i32 result, then the error
    /// and the output from the kernel.
    /// </summary>
    public HandleCallResult CallHandle(ReadOnlySpan<byte> input)
    {
        var handle = Guest.GetFunction("handle")
            ?? throw new InvalidOperationException("The guest does not export handle.");
        ArmCall();
        Initialize();

        CallVoid("reset");
        if (input.Length > 0)
        {
            var block = Alloc(input.Length);
            if (block == 0)
            {
                throw new InvalidOperationException($"The kernel has no room for {input.Length} input bytes.");
            }

            input.CopyTo(KernelMemory.GetSpan(block, input.Length));
            CallVoid("input_set", block, (long)input.Length);
        }

        var returnCode = handle.Invoke() is int code ? code : 0;

        var errorHandle = CallI64("error_get");
        var error = errorHandle == 0 ? [] : Read(errorHandle, CallI64("length", errorHandle));
        var outputOffset = CallI64("output_offset");
        var outputLength = CallI64("output_length");
        var output = outputLength == 0 ? [] : Read(outputOffset, outputLength);
        return new HandleCallResult(returnCode, outputOffset, output, errorHandle, error);
    }

    /// <summary>Gives the next call the session's whole instruction budget and a deadline of
    /// <see cref="SessionLimits.CallDeadline"/> from now.</summary>
    void ArmCall()
    {
        store.Fuel = Limits.InstructionBudget;
        store.SetEpochDeadline(deadlineTicks);
    }

    /// <summary>
    /// Instantiates <paramref name="guest"/> in this store. Its kernel imports are linked to the kernel instance,
    /// its WASI imports to Wasmtime's WASI with an empty configuration (no arguments, environment, preopens or
    /// standard streams), and any other function import to a callback that records the call and traps.
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
                        throw new InvalidOperationException($"{label} is not provided by this host.");
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
