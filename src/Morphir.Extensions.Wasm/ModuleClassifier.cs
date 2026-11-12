// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using Wasmtime;

namespace Morphir.Extensions.Wasm;

/// <summary>What classification against the contract gave: a profile, or a refusal.</summary>
public abstract record ModuleClassification
{
    private ModuleClassification() { }

    /// <summary>The module is this profile, and every import and the <c>handle</c> export conform to it.</summary>
    public sealed record Classified(WasmAbi Profile) : ModuleClassification;

    /// <summary>The module is refused.</summary>
    public sealed record Refused(WasmRefusal Refusal) : ModuleClassification;

    /// <summary>Calls the function for this result.</summary>
    public TResult Match<TResult>(Func<WasmAbi, TResult> classified, Func<WasmRefusal, TResult> refused) => this switch
    {
        Classified result => classified(result.Profile),
        Refused result => refused(result.Refusal),
        _ => throw new UnreachableException(),
    };
}

/// <summary>
/// Classifies a validated module against the pinned contract's table, before anything is instantiated
/// (parent decision CLS-1). The rules and their order are the contract's, as the MoonBit host applies them:
/// <list type="number">
/// <item>The first import from a profile namespace decides the profile; a module with none takes the declared
/// profile, or the contract's default.</item>
/// <item>Every import, in order, must be a function of that profile's surface with the surface's exact type.
/// The first that is not is refused with <c>extension.abi_unsupported</c>, named <c>module.name</c>. A profile
/// that admits WASI admits every WASI preview 1 function with its standard type, which the engine's own WASI
/// definitions supply.</item>
/// <item>The module must export <c>handle</c> with the contract's type, or it is refused with
/// <c>extension.abi_unsupported</c>.</item>
/// <item>A declared ABI must equal the classified profile, or it is refused with <c>extension.abi_mismatch</c>.</item>
/// </list>
/// Whether this host runs the profile is <see cref="WasmHost.Admit"/>'s question, not the contract's.
/// </summary>
public static class ModuleClassifier
{
    /// <summary>Classifies <paramref name="module"/>, with the ABI its descriptor, configuration or command
    /// declares, or null when none is declared.</summary>
    public static ModuleClassification Classify(CompiledModule module, WasmAbi? declared)
    {
        var contract = WasmProfileContract.Pinned;
        var profile = NamedProfile(module.Module.Imports, contract) ?? declared ?? contract.DefaultProfile;
        var surface = contract.Profile(profile);

        using var wasi = new WasiPreview1(module.Engine.Engine);
        foreach (var import in module.Module.Imports)
        {
            var name = new WasmImportName(import.ModuleName, import.Name);
            if (import is not FunctionImport function)
            {
                return Refuse(new WasmRefusal.ImportOutsideSurface(profile, name, Kind(import), null));
            }

            var imported = new WasmFunctionType(function.Parameters, function.Results);
            var expected = surface.Imports.TryGetValue(name, out var listed) ? listed
                : string.Equals(import.ModuleName, surface.WasiNamespace, StringComparison.Ordinal) ? wasi.TypeOf(import.ModuleName, import.Name)
                : null;
            if (expected is null)
            {
                return Refuse(new WasmRefusal.ImportOutsideSurface(profile, name, Kind(import), null));
            }

            if (!expected.Equals(imported))
            {
                return Refuse(new WasmRefusal.ImportOutsideSurface(profile, name, imported.ToString(), expected));
            }
        }

        var handle = module.Module.Exports.FirstOrDefault(export => export.Name == contract.HandleExportName);
        if (handle is not FunctionExport handleFunction
            || !new WasmFunctionType(handleFunction.Parameters, handleFunction.Results).Equals(contract.HandleExport))
        {
            return Refuse(new WasmRefusal.HandleExportMissing(contract.HandleExportName, contract.HandleExport, Exported(handle)));
        }

        if (declared is not null && declared != profile)
        {
            return Refuse(new WasmRefusal.DeclaredAbiMismatch(declared, profile));
        }

        return new ModuleClassification.Classified(profile);
    }

    static ModuleClassification Refuse(WasmRefusal refusal) => new ModuleClassification.Refused(refusal);

    /// <summary>The profile of the first import from a profile namespace, or null when there is none.</summary>
    static WasmAbi? NamedProfile(IReadOnlyList<Import> imports, WasmProfileContract contract)
    {
        foreach (var import in imports)
        {
            var named = WasmAbi.All.FirstOrDefault(abi => contract.Profile(abi).Namespaces.Contains(import.ModuleName, StringComparer.Ordinal));
            if (named is not null)
            {
                return named;
            }
        }

        return null;
    }

    static string Kind(Import import) => import switch
    {
        FunctionImport function => $"a function {new WasmFunctionType(function.Parameters, function.Results)}",
        MemoryImport => "a memory",
        TableImport => "a table",
        GlobalImport => "a global",
        _ => $"an import of kind {import.GetType().Name}",
    };

    static string? Exported(Export? export) => export switch
    {
        null => null,
        FunctionExport function => new WasmFunctionType(function.Parameters, function.Results).ToString(),
        MemoryExport => "a memory",
        TableExport => "a table",
        GlobalExport => "a global",
        _ => $"an export of kind {export.GetType().Name}",
    };

    /// <summary>
    /// The standard types of WASI preview 1, read from the engine's own WASI definitions, so that no second
    /// table of WASI signatures is kept in code. The linker and store exist only to look types up; nothing in
    /// them is instantiated or run. They are created on the first WASI import of a classification.
    /// </summary>
    sealed class WasiPreview1(Engine engine) : IDisposable
    {
        Linker? linker;
        Store? store;

        public WasmFunctionType? TypeOf(string module, string name)
        {
            if (linker is null || store is null)
            {
                linker = new Linker(engine);
                linker.DefineWasi();
                store = new Store(engine);
            }

            return linker.GetFunction(store, module, name) is { } function
                ? new WasmFunctionType(function.Parameters, function.Results)
                : null;
        }

        public void Dispose()
        {
            linker?.Dispose();
            store?.Dispose();
        }
    }
}

/// <summary>A module this host admitted: an <c>extism-kernel-v1</c> module that a session can run.</summary>
public sealed class ExtismModule
{
    internal ExtismModule(CompiledModule compiled, WasmAbi profile)
    {
        Compiled = compiled;
        Profile = profile;
    }

    /// <summary>The compiled module.</summary>
    public CompiledModule Compiled { get; }

    /// <summary>The classified profile, <c>extism-kernel-v1</c>.</summary>
    public WasmAbi Profile { get; }
}

/// <summary>Whether this host runs a module.</summary>
public abstract record ModuleAdmission
{
    private ModuleAdmission() { }

    /// <summary>The host runs the module.</summary>
    public sealed record Admitted(ExtismModule Module) : ModuleAdmission;

    /// <summary>The module is refused.</summary>
    public sealed record Refused(WasmRefusal Refusal) : ModuleAdmission;

    /// <summary>Calls the function for this result.</summary>
    public TResult Match<TResult>(Func<ExtismModule, TResult> admitted, Func<WasmRefusal, TResult> refused) => this switch
    {
        Admitted result => admitted(result.Module),
        Refused result => refused(result.Refusal),
        _ => throw new UnreachableException(),
    };
}

/// <summary>
/// The profiles this host runs, and the admission of a classified module. This host runs
/// <c>extism-kernel-v1</c> only (parent decision ABI-1). It still classifies a <c>morphir-bytes-v1</c>
/// module, so that it refuses it with <c>extension.abi_unsupported</c> naming the profile.
/// </summary>
public static class WasmHost
{
    /// <summary>The profiles this host runs.</summary>
    public static IReadOnlyList<WasmAbi> Profiles { get; } = [WasmAbi.ExtismKernel];

    /// <summary>Classifies <paramref name="module"/> and admits it when this host runs its profile.</summary>
    public static ModuleAdmission Admit(CompiledModule module, WasmAbi? declared) =>
        ModuleClassifier.Classify(module, declared).Match<ModuleAdmission>(
            profile => Profiles.Contains(profile)
                ? new ModuleAdmission.Admitted(new ExtismModule(module, profile))
                : new ModuleAdmission.Refused(new WasmRefusal.ProfileNotRun(profile, Profiles)),
            refusal => new ModuleAdmission.Refused(refusal));
}
