// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;

namespace Morphir.Extensions.Wasm;

/// <summary>
/// The WASM execution profile contract (<c>morphir/mep-wasm-execution-profiles</c>) at the version this
/// repository pins. The library reads it from <c>wasm-execution-profiles.json</c>, embedded unchanged from
/// <c>tests/Fixtures/MepWasm/</c>, so the import table, the bounds and the refusal codes exist once, in the
/// pinned file, and not as a second copy in code.
/// </summary>
public sealed class WasmProfileContract
{
    const string ResourceName = "Morphir.Extensions.Wasm.Contract.wasm-execution-profiles.json";

    static readonly Lazy<WasmProfileContract> Embedded = new(Load);

    readonly FrozenDictionary<WasmAbi, WasmProfileSurface> profiles;
    readonly FrozenDictionary<string, ulong> instructionBudgets;
    readonly FrozenDictionary<string, ContractFault> faults;

    WasmProfileContract(
        string id,
        string version,
        string sha256,
        string handleExportName,
        WasmFunctionType handleExport,
        WasmAbi defaultProfile,
        long memoryCeilingBytes,
        long kernelMemoryCeilingBytes,
        FrozenDictionary<string, ulong> instructionBudgets,
        FrozenSet<string> refusalCodes,
        FrozenDictionary<string, ContractFault> faults,
        FrozenDictionary<WasmAbi, WasmProfileSurface> profiles)
    {
        this.faults = faults;
        Id = id;
        Version = version;
        Sha256 = sha256;
        HandleExportName = handleExportName;
        HandleExport = handleExport;
        DefaultProfile = defaultProfile;
        MemoryCeilingBytes = memoryCeilingBytes;
        KernelMemoryCeilingBytes = kernelMemoryCeilingBytes;
        this.instructionBudgets = instructionBudgets;
        RefusalCodes = refusalCodes;
        this.profiles = profiles;
    }

    /// <summary>The contract embedded in this library: the pinned <c>wasm-execution-profiles.json</c>.</summary>
    public static WasmProfileContract Pinned => Embedded.Value;

    /// <summary>The contract identifier, <c>morphir/mep-wasm-execution-profiles</c>.</summary>
    public string Id { get; }

    /// <summary>The contract version, such as <c>0.1.0-draft.1</c>.</summary>
    public string Version { get; }

    /// <summary>The SHA-256 of the embedded contract file, in lowercase hexadecimal.</summary>
    public string Sha256 { get; }

    /// <summary>The type every module of either profile must export <c>handle</c> with.</summary>
    public WasmFunctionType HandleExport { get; }

    /// <summary>The name of the export every module must have, <c>handle</c>.</summary>
    public string HandleExportName { get; }

    /// <summary>The profile of a module that imports from no profile namespace and declares none.</summary>
    public WasmAbi DefaultProfile { get; }

    /// <summary>The ceiling of each linear memory of a session, in bytes.</summary>
    public long MemoryCeilingBytes { get; }

    /// <summary>The ceiling of the Extism kernel's own memory, in bytes.</summary>
    public long KernelMemoryCeilingBytes { get; }

    /// <summary>The refusal codes the contract defines for a module that is refused before any request.</summary>
    public IReadOnlySet<string> RefusalCodes { get; }

    /// <summary>The fault kinds the contract defines, each with its host code or host outcome.</summary>
    public IReadOnlyCollection<ContractFault> Faults => faults.Values;

    /// <summary>The fault the contract defines as <paramref name="kind"/>, such as <c>budget-exhausted</c>.</summary>
    public ContractFault Fault(string kind) => faults[kind];

    /// <summary>The import surface and call rules of <paramref name="profile"/>.</summary>
    public WasmProfileSurface Profile(WasmAbi profile) => profiles[profile];

    /// <summary>
    /// The per-call instruction budget the contract gives for <paramref name="key"/>: a MEP version such as
    /// <c>0.2.0-draft.1</c> or <c>0.1</c>, or <c>probe</c>.
    /// </summary>
    public bool TryGetInstructionBudget(string key, out ulong budget) => instructionBudgets.TryGetValue(key, out budget);

    static WasmProfileContract Load()
    {
        using var stream = typeof(WasmProfileContract).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The assembly has no resource {ResourceName}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Parse(copy.ToArray());
    }

    static WasmProfileContract Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        var profiles = new Dictionary<WasmAbi, WasmProfileSurface>();
        foreach (var entry in root.GetProperty("profiles").EnumerateObject())
        {
            if (!WasmAbi.TryParse(entry.Name, out var abi))
            {
                throw Invalid($"it defines the profile {entry.Name}, which this library does not know");
            }

            profiles[abi] = ReadProfile(abi, entry.Value);
        }

        foreach (var abi in WasmAbi.All.Where(abi => !profiles.ContainsKey(abi)))
        {
            throw Invalid($"it does not define the profile {abi}");
        }

        var defaultId = root.GetProperty("classification").GetProperty("defaultProfile").GetString();
        if (!WasmAbi.TryParse(defaultId, out var defaultProfile))
        {
            throw Invalid($"its default profile {defaultId} is not a profile");
        }

        var bounds = root.GetProperty("bounds");
        var memoryCeiling = bounds.GetProperty("memoryCeilingBytes").GetInt64();
        var kernelCeiling = bounds.GetProperty("kernelMemoryCeilingBytes").GetInt64();
        if (memoryCeiling != kernelCeiling)
        {
            // One store limit applies to every memory of a session, the kernel's included.
            throw Invalid($"its kernel memory ceiling {kernelCeiling} differs from the memory ceiling {memoryCeiling}, which one store limit cannot apply");
        }

        var budgets = bounds.GetProperty("instructionBudget").EnumerateObject()
            .ToFrozenDictionary(budget => budget.Name, budget => budget.Value.GetUInt64(), StringComparer.Ordinal);

        var refusalCodes = root.GetProperty("refusals").EnumerateArray()
            .Select(refusal => refusal.GetProperty("code").GetString()!)
            .ToFrozenSet(StringComparer.Ordinal);
        foreach (var code in WasmRefusal.Codes.Where(code => !refusalCodes.Contains(code)))
        {
            throw Invalid($"it lacks the refusal code {code}");
        }

        var faults = root.GetProperty("faults").EnumerateArray()
            .Select(fault => new ContractFault(
                fault.GetProperty("kind").GetString()!,
                fault.TryGetProperty("hostCode", out var code) ? code.GetString() : null,
                fault.TryGetProperty("hostOutcome", out var outcome) ? outcome.GetString() : null))
            .ToFrozenDictionary(fault => fault.Kind, StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<GuestFailureKind>().Select(GuestFailure.FaultKindOf).OfType<string>().Where(kind => !faults.ContainsKey(kind)))
        {
            throw Invalid($"it lacks the fault kind {kind}");
        }

        var handle = root.GetProperty("handleExport");
        return new WasmProfileContract(
            root.GetProperty("contract").GetString()!,
            root.GetProperty("version").GetString()!,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            handle.GetProperty("name").GetString()!,
            ReadType(handle),
            defaultProfile,
            memoryCeiling,
            kernelCeiling,
            budgets,
            refusalCodes,
            faults,
            profiles.ToFrozenDictionary());
    }

    static WasmProfileSurface ReadProfile(WasmAbi abi, JsonElement profile)
    {
        var namespaces = profile.GetProperty("namespaces").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var imports = profile.GetProperty("imports").EnumerateArray()
            .ToFrozenDictionary(
                import => new WasmImportName(import.GetProperty("module").GetString()!, import.GetProperty("name").GetString()!),
                ReadType);
        var wasi = profile.GetProperty("wasi");
        var wasiNamespace = wasi.ValueKind switch
        {
            JsonValueKind.String => wasi.GetString(),
            JsonValueKind.False => null,
            _ => throw Invalid($"the wasi member of {abi} is neither a namespace nor false"),
        };
        var deadline = TimeSpan.FromSeconds(profile.GetProperty("callDeadlineSeconds").GetInt32());
        return new WasmProfileSurface(abi, profile.GetProperty("status").GetString()!, namespaces, imports, wasiNamespace, deadline);
    }

    static WasmFunctionType ReadType(JsonElement function) =>
        new(ReadKinds(function.GetProperty("params")), ReadKinds(function.GetProperty("results")));

    static IEnumerable<Wasmtime.ValueKind> ReadKinds(JsonElement kinds) =>
        kinds.EnumerateArray().Select(kind => WasmFunctionType.TryParseKind(kind.GetString(), out var parsed)
            ? parsed
            : throw Invalid($"it names the value type {kind}, which is not a WASM value type")).ToArray();

    static InvalidOperationException Invalid(string reason) =>
        new($"The embedded WASM execution profile contract cannot be used: {reason}.");
}

/// <summary>A fault kind of the contract: how a worker names it and what the host session reports.</summary>
/// <param name="Kind">The fault kind, such as <c>trap</c> or <c>call-deadline</c>.</param>
/// <param name="HostCode">The host code, such as <c>extension.guest_trap</c>, or null when the fault is an outcome.</param>
/// <param name="HostOutcome">The host outcome, such as <c>deadline</c>, or null when the fault has a code.</param>
public sealed record ContractFault(string Kind, string? HostCode, string? HostOutcome);

/// <summary>The name of an import: its module and its name, written <c>module.name</c>.</summary>
/// <param name="Module">The import module, such as <c>extism:host/env</c>.</param>
/// <param name="Name">The import name, such as <c>alloc</c>.</param>
public readonly record struct WasmImportName(string Module, string Name)
{
    /// <summary>The import as the contract names it in a refusal: <c>module.name</c>.</summary>
    public override string ToString() => $"{Module}.{Name}";
}

/// <summary>One profile of the contract: its namespaces, its import table and its call deadline.</summary>
/// <param name="Profile">The profile.</param>
/// <param name="Status"><c>required</c> or <c>transitional</c>.</param>
/// <param name="Namespaces">The import modules that make a module this profile when one is its first profile import.</param>
/// <param name="Imports">Every function of the profile's table, with its exact type.</param>
/// <param name="WasiNamespace">The WASI preview 1 module the profile admits with standard types, or null when it admits no WASI.</param>
/// <param name="CallDeadline">The deadline of one <c>handle</c> call.</param>
public sealed record WasmProfileSurface(
    WasmAbi Profile,
    string Status,
    IReadOnlyList<string> Namespaces,
    IReadOnlyDictionary<WasmImportName, WasmFunctionType> Imports,
    string? WasiNamespace,
    TimeSpan CallDeadline);
