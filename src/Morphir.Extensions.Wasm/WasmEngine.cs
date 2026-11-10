// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The one Wasmtime engine of the process (parent decision ENG-1), created on first need. It meters fuel and
/// is interrupted by epochs, which a background thread advances every <see cref="EpochInterval"/>, so each
/// session store can carry an instruction budget and a call deadline. It is configured without threads,
/// 64-bit memory and multiple memories, so a module with a shared, 64-bit or second memory fails validation
/// and is refused with <c>extension.wasm_feature_unsupported</c> (parent decision CLS-1). Compiled modules are
/// kept in memory for the life of the process, keyed by the SHA-256 of their bytes; nothing is cached on disk
/// (parent decision CACHE-1).
/// </summary>
public sealed class WasmEngine
{
    static readonly Lazy<WasmEngine> SharedEngine = new(() => new WasmEngine(), LazyThreadSafetyMode.ExecutionAndPublication);

    readonly ConcurrentDictionary<ModuleDigest, CompiledModule> modules = new();
    readonly Lazy<Engine> diagnostic = new(CreateDiagnosticEngine, LazyThreadSafetyMode.ExecutionAndPublication);
    readonly Lazy<CompiledModule> kernel;
    readonly Thread epochTicker;

    WasmEngine()
    {
        var config = new Config()
            .WithFuelConsumption(true)
            .WithEpochInterruption(true)
            .WithWasmThreads(false)
            .WithMemory64(false)
            .WithMultiMemory(false);
        // Experiment switch of the ci/wasm-win-x64-trap diagnosis, off by default: with the variable set to "false",
        // the engine detects traps with explicit checks instead of exception handlers (Config.WithSignalsBasedTraps).
        if (Environment.GetEnvironmentVariable(SignalsBasedTrapsExperimentVariable) == "false")
        {
            config = config.WithSignalsBasedTraps(false);
        }

        Engine = new Engine(config);
        kernel = new(LoadKernel, LazyThreadSafetyMode.ExecutionAndPublication);
        // A thread of its own, not a thread-pool timer: the pool adds no thread while every processor is busy, and
        // a guest that spins keeps one busy, so a pool timer can stop ticking exactly when a deadline is needed.
        epochTicker = new Thread(AdvanceEpochs) { IsBackground = true, Name = "morphir-wasm-epoch" };
        epochTicker.Start();
    }

    void AdvanceEpochs()
    {
        while (true)
        {
            Thread.Sleep(EpochInterval);
            Engine.IncrementEpoch();
        }
    }

    /// <summary>The environment variable of the signals-based-traps experiment (ci/wasm-win-x64-trap only).</summary>
    internal const string SignalsBasedTrapsExperimentVariable = "MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS";

    /// <summary>The engine of this process.</summary>
    public static WasmEngine Shared => SharedEngine.Value;

    /// <summary>How often the background thread advances the engine's epoch: the resolution of a call deadline.</summary>
    public static TimeSpan EpochInterval { get; } = TimeSpan.FromMilliseconds(10);

    /// <summary>The Wasmtime engine.</summary>
    public Engine Engine { get; }

    /// <summary>The embedded Extism kernel, compiled once for this engine.</summary>
    public CompiledModule Kernel => kernel.Value;

    /// <summary>
    /// Validates and compiles a module from its bytes, or returns the module compiled earlier in this process
    /// from bytes with the same SHA-256.
    /// </summary>
    public ModuleLoad Load(ReadOnlySpan<byte> bytes)
    {
        var digest = ModuleDigest.Of(bytes);
        if (modules.TryGetValue(digest, out var cached))
        {
            return new ModuleLoad.Compiled(cached);
        }

        Module module;
        try
        {
            module = Module.FromBytes(Engine, $"sha256:{digest}", bytes);
        }
        catch (WasmtimeException exception)
        {
            return Diagnose(bytes, exception);
        }

        var (runModule, memoryName) = GuestMemory(module, bytes);
        var compiled = new CompiledModule(this, digest, bytes.Length, module, runModule, memoryName);
        var kept = modules.GetOrAdd(digest, compiled);
        if (!ReferenceEquals(kept, compiled))
        {
            runModule?.Dispose();
            module.Dispose();
        }

        return new ModuleLoad.Compiled(kept);
    }

    /// <summary>
    /// Where the host reads the guest's own memory: the module's exported memory, or, for a module that defines a
    /// memory without exporting it, a second module compiled from the same bytes with that memory exported
    /// (<see cref="GuestMemoryExport"/>). A module with an imported memory gets neither; classification refuses it.
    /// </summary>
    (Module? RunModule, string? MemoryName) GuestMemory(Module module, ReadOnlySpan<byte> bytes)
    {
        if (module.Exports.OfType<MemoryExport>().FirstOrDefault() is { } exported)
        {
            return (null, exported.Name);
        }

        if (module.Imports.OfType<MemoryImport>().Any())
        {
            return (null, null);
        }

        var name = GuestMemoryExport.Name;
        var names = module.Exports.Select(export => export.Name).ToHashSet(StringComparer.Ordinal);
        for (var suffix = 1; names.Contains(name); suffix++)
        {
            name = $"{GuestMemoryExport.Name}-{suffix}";
        }

        return GuestMemoryExport.Exported(bytes, name) is { } exposed
            ? (Module.FromBytes(Engine, $"{module.Name}+{name}", exposed), name)
            : (null, null);
    }

    /// <summary>
    /// Tells a module that needs a feature this engine does not enable from bytes that are not a module. The
    /// bytes are validated again by an engine that enables every feature Wasmtime can; that engine only
    /// validates, and never compiles or runs anything.
    /// </summary>
    ModuleLoad Diagnose(ReadOnlySpan<byte> bytes, WasmtimeException compileFailure)
    {
        var report = Module.Validate(Engine, bytes);
        if (report is null)
        {
            return new ModuleLoad.Invalid(compileFailure.Message);
        }

        return Module.Validate(diagnostic.Value, bytes) is null
            ? new ModuleLoad.Refused(new WasmRefusal.FeatureUnsupported(report))
            : new ModuleLoad.Invalid(report);
    }

    static Engine CreateDiagnosticEngine() => new(new Config()
        .WithWasmThreads(true)
        .WithMemory64(true)
        .WithMultiMemory(true)
        .WithGc(true)
        .WithFunctionReferences(true)
        .WithExceptions(true)
        .WithTailCalls(true)
        .WithWideArithmetic(true)
        .WithRelaxedSIMD(true, false)
        .WithBranchHinting(true));

    CompiledModule LoadKernel() => Load(ExtismKernel.Bytes.Span).Match(
        compiled => compiled,
        refused => throw new InvalidOperationException($"The engine refuses the embedded Extism kernel: {refused.Message}"),
        invalid => throw new InvalidOperationException($"The engine cannot load the embedded Extism kernel: {invalid}"));
}
