// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>The engine for WASM extensions.</summary>
public static class WasmEngine
{
    /// <summary>
    /// Creates an engine with Wasmtime's default configuration. Fuel and epoch interruption are not enabled yet;
    /// the bounds come with the engine layer (task 2.4 of <c>add-wasm-extension-host</c>).
    /// </summary>
    public static Engine Create() => new();
}

/// <summary>What one <c>handle</c> call left: its i32 result, and the output and error the kernel holds.</summary>
/// <param name="ReturnCode">The i32 that <c>handle</c> returned.</param>
/// <param name="OutputOffset">The kernel's <c>output_offset()</c> after the call.</param>
/// <param name="Output">The <c>output_length()</c> bytes at <paramref name="OutputOffset"/> in kernel memory.</param>
/// <param name="ErrorHandle">The kernel's <c>error_get()</c> after the call; 0 is no error.</param>
/// <param name="Error">The bytes of the error block, empty when there is none.</param>
public sealed record HandleCallResult(int ReturnCode, long OutputOffset, byte[] Output, long ErrorHandle, byte[] Error);

/// <summary>
/// A Wasmtime store that holds one instance of the embedded Extism kernel and links guests to it: each import a
/// guest makes from <see cref="ExtismKernel.ImportModule"/> with a kernel function's name is defined as that
/// export of the kernel instance, so the guest's input, output and allocations go through the kernel inside
/// WASM. The store limiter gives every linear memory in the store, the kernel's and the guest's, the same
/// ceiling. This is the minimum for one call; sessions, classification, bounds and the sandbox come later.
/// </summary>
public sealed class KernelStore : IDisposable
{
    const string WasiModule = "wasi_snapshot_preview1";

    readonly Store store;
    readonly Linker linker;
    readonly Module kernelModule;
    readonly Dictionary<string, Function> kernelFunctions;
    readonly List<string> unprovidedCalls = [];
    Instance? initialized;

    KernelStore(Store store, Linker linker, Module kernelModule, Memory kernelMemory,
        Dictionary<string, Function> kernelFunctions)
    {
        this.store = store;
        this.linker = linker;
        this.kernelModule = kernelModule;
        KernelMemory = kernelMemory;
        this.kernelFunctions = kernelFunctions;
    }

    /// <summary>
    /// Creates a store whose limiter caps each linear memory at <paramref name="memoryCeilingBytes"/>, and
    /// instantiates the kernel in it.
    /// </summary>
    public static KernelStore Create(Engine engine, long memoryCeilingBytes)
    {
        var store = new Store(engine);
        Linker? linker = null;
        Module? module = null;
        try
        {
            store.SetLimits(memorySize: memoryCeilingBytes, tableElements: null, instances: null, tables: null, memories: null);
            linker = new Linker(engine);
            module = Module.FromBytes(engine, "extism-kernel", ExtismKernel.Bytes.Span);
            var kernel = linker.Instantiate(store, module);
            var memory = kernel.GetMemory("memory")
                ?? throw new InvalidOperationException("The kernel exports no memory.");
            var functions = new Dictionary<string, Function>(StringComparer.Ordinal);
            foreach (var name in ExtismKernel.FunctionNames)
            {
                functions[name] = kernel.GetFunction(name)
                    ?? throw new InvalidOperationException($"The kernel does not export {name}.");
            }

            return new KernelStore(store, linker, module, memory, functions);
        }
        catch
        {
            module?.Dispose();
            linker?.Dispose();
            store.Dispose();
            throw;
        }
    }

    /// <summary>The kernel's linear memory, where the guest's input, output, error and allocations live.</summary>
    public Memory KernelMemory { get; }

    /// <summary>Which start-up export of the guest ran before its first call, or "not yet".</summary>
    public string Initialization { get; private set; } = "not yet";

    /// <summary>Imports outside the kernel and WASI that a guest called. Each such call traps.</summary>
    public IReadOnlyList<string> UnprovidedCalls => unprovidedCalls;

    /// <summary>Calls the kernel's <c>alloc</c>: a handle to a new block of <paramref name="length"/> bytes in
    /// kernel memory, or 0 when the kernel cannot allocate it.</summary>
    public long Alloc(long length) => CallI64("alloc", length);

    /// <summary>
    /// Instantiates <paramref name="guest"/> in this store. Its kernel imports are linked to the kernel instance,
    /// its WASI imports to Wasmtime's WASI with an empty configuration (no arguments, environment, preopens or
    /// standard streams), and any other function import to a callback that records the call and traps.
    /// </summary>
    public Instance Link(Module guest)
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

    /// <summary>
    /// Calls <c>handle</c> on <paramref name="guest"/> as an Extism host does: the guest's start-up export runs
    /// once, the kernel is reset, the input is written to a kernel block and set as the input, <c>handle</c>
    /// runs, and the host reads the i32 result, then the error and the output from the kernel.
    /// </summary>
    public HandleCallResult CallHandle(Instance guest, ReadOnlySpan<byte> input)
    {
        var handle = guest.GetFunction("handle")
            ?? throw new InvalidOperationException("The guest does not export handle.");
        Initialize(guest);

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

    /// <summary>Runs the guest's start-up export once, as upstream Extism does: <c>_initialize</c> and
    /// <c>hs_init(0, 0)</c> for a Haskell module, else <c>__wasm_call_ctors</c>, else <c>_initialize</c>.</summary>
    void Initialize(Instance guest)
    {
        if (ReferenceEquals(initialized, guest))
        {
            return;
        }

        initialized = guest;
        if (guest.GetFunction("hs_init") is { } hsInit)
        {
            var ran = "hs_init";
            if (UnitFunction(guest, "_initialize") is { } start)
            {
                start.Invoke();
                ran = "_initialize, hs_init";
            }

            hsInit.Invoke((ValueBox)0, (ValueBox)0);
            Initialization = ran;
        }
        else if (guest.GetFunction("__wasm_call_ctors") is { } constructors)
        {
            if (UnitFunction(guest, "__wasm_call_ctors") is not null)
            {
                constructors.Invoke();
                Initialization = "__wasm_call_ctors";
            }
            else
            {
                Initialization = "none (__wasm_call_ctors has parameters or results)";
            }
        }
        else if (UnitFunction(guest, "_initialize") is { } start)
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
        kernelModule.Dispose();
    }
}
