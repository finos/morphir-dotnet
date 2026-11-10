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
        ulong instructionBudget,
        HostSessionLimits hostSession,
        FrozenSet<string> refusalCodes,
        FrozenDictionary<string, ContractFault> faults,
        FrozenDictionary<WasmAbi, WasmProfileSurface> profiles,
        WasmHostPolicy policy)
    {
        this.faults = faults;
        Policy = policy;
        Id = id;
        Version = version;
        Sha256 = sha256;
        HandleExportName = handleExportName;
        HandleExport = handleExport;
        DefaultProfile = defaultProfile;
        MemoryCeilingBytes = memoryCeilingBytes;
        KernelMemoryCeilingBytes = kernelMemoryCeilingBytes;
        InstructionBudget = instructionBudget;
        RequestFrameLimitBytes = hostSession.RequestBytes;
        ResponseFrameLimitBytes = hostSession.ResponseBytes;
        RequestLimitCode = hostSession.RequestCode;
        ResponseLimitCode = hostSession.ResponseCode;
        RefusalCodes = refusalCodes;
        this.profiles = profiles;
    }

    /// <summary>The host-session frame limits and their codes, as the contract's <c>hostSession</c> object gives them.</summary>
    readonly record struct HostSessionLimits(long RequestBytes, long ResponseBytes, string RequestCode, string ResponseCode);

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

    /// <summary>The host-function and WASI policy: the limits of the session stores and what each call returns.</summary>
    public WasmHostPolicy Policy { get; }

    /// <summary>The refusal codes the contract defines for a module that is refused before any request.</summary>
    public IReadOnlySet<string> RefusalCodes { get; }

    /// <summary>The fault kinds the contract defines, each with its host code or host outcome.</summary>
    public IReadOnlyCollection<ContractFault> Faults => faults.Values;

    /// <summary>The fault the contract defines as <paramref name="kind"/>, such as <c>budget-exhausted</c>.</summary>
    public ContractFault Fault(string kind) => faults[kind];

    /// <summary>The import surface and call rules of <paramref name="profile"/>.</summary>
    public WasmProfileSurface Profile(WasmAbi profile) => profiles[profile];

    /// <summary>The key of the session budget in the contract's <c>bounds.instructionBudget</c>.</summary>
    const string SessionBudget = "session";

    /// <summary>The key of the probe budget, which a host session never uses.</summary>
    const string ProbeBudget = "probe";

    /// <summary>
    /// The per-call instruction budget of a session: the contract's <c>bounds.instructionBudget.session</c>
    /// (5,000,000,000 at the pin), the host's only budget (design MEP-1). The contract also gives a budget for a
    /// <c>probe</c>; a host session does not use it.
    /// </summary>
    public ulong InstructionBudget { get; }

    /// <summary>
    /// The classification checks in the order this library makes them, which the contract's
    /// <c>classification.order</c> must give: the profile, the imports, the <c>handle</c> export, the declared ABI
    /// (all <see cref="ModuleClassifier.Classify"/>) and last whether this host runs the profile
    /// (<see cref="WasmHost.Admit"/>).
    /// </summary>
    public static IReadOnlyList<string> ClassificationOrder { get; } =
        ["profile", "imports", "handle-export", "declared-abi", "host-runs-profile"];

    /// <summary>
    /// The memory rules this library applies, which the contract's <c>memory</c> must give, in this order: a trap at
    /// the ceiling is <c>memory-limit</c> (<c>at-ceiling</c>, in <see cref="SessionStore"/>); every other end keeps its
    /// own result (<c>guest-reported</c>); a host that observes refused growth may report more, which this host cannot
    /// observe (<c>observed-refusal</c>); and an initial memory above the ceiling fails before any request
    /// (<c>initial-above-ceiling</c>).
    /// </summary>
    public static IReadOnlyList<string> MemoryRules { get; } =
        ["at-ceiling", "guest-reported", "observed-refusal", "initial-above-ceiling"];

    /// <summary>The number of functions of WASI preview 1, whose signatures the engine's own definitions give.</summary>
    const int WasiPreview1Functions = 46;

    /// <summary>The host session's request frame limit, in bytes, counted over the whole frame, its
    /// <c>Content-Length</c> header included (6 MiB at the pin).</summary>
    public long RequestFrameLimitBytes { get; }

    /// <summary>The host session's response frame limit, in bytes, counted over the whole frame (16 MiB at the pin).</summary>
    public long ResponseFrameLimitBytes { get; }

    /// <summary>The code of a request frame over its limit, <c>extension.request_limit</c> (host-session rule <c>request-limit</c>).</summary>
    public string RequestLimitCode { get; }

    /// <summary>The code of a response frame over its limit, <c>extension.incomplete_session</c> (host-session rule <c>response-limit</c>).</summary>
    public string ResponseLimitCode { get; }

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

        var classification = root.GetProperty("classification");
        var defaultId = classification.GetProperty("defaultProfile").GetString();
        if (!WasmAbi.TryParse(defaultId, out var defaultProfile))
        {
            throw Invalid($"its default profile {defaultId} is not a profile");
        }

        var order = classification.GetProperty("order").EnumerateArray().Select(step => step.GetString()).ToArray();
        if (!order.SequenceEqual(ClassificationOrder))
        {
            throw Invalid($"its classification order is {string.Join(", ", order)}, and this library checks {string.Join(", ", ClassificationOrder)}");
        }

        var bounds = root.GetProperty("bounds");
        var memoryCeiling = bounds.GetProperty("memoryCeilingBytes").GetInt64();
        var kernelCeiling = bounds.GetProperty("kernelMemoryCeilingBytes").GetInt64();
        if (memoryCeiling != kernelCeiling)
        {
            // One store limit applies to every memory of a session, the kernel's included.
            throw Invalid($"its kernel memory ceiling {kernelCeiling} differs from the memory ceiling {memoryCeiling}, which one store limit cannot apply");
        }

        var memoryRules = root.GetProperty("memory").EnumerateArray().Select(rule => rule.GetProperty("id").GetString()).ToArray();
        if (!memoryRules.SequenceEqual(MemoryRules))
        {
            throw Invalid($"its memory rules are {string.Join(", ", memoryRules)}, and this library applies {string.Join(", ", MemoryRules)}");
        }

        var budgets = bounds.GetProperty("instructionBudget");
        foreach (var entry in budgets.EnumerateObject().Where(entry => entry.Name is not (SessionBudget or ProbeBudget)))
        {
            throw Invalid($"it gives an instruction budget for {entry.Name}, and this library knows a {SessionBudget} and a {ProbeBudget} budget");
        }

        if (!budgets.TryGetProperty(SessionBudget, out var sessionBudget))
        {
            throw Invalid($"it gives no instruction budget for a {SessionBudget}");
        }

        var budget = sessionBudget.GetUInt64();
        var hostSession = ReadHostSession(root.GetProperty("hostSession"));

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
            budget,
            hostSession,
            refusalCodes,
            faults,
            profiles.ToFrozenDictionary(),
            WasmHostPolicy.Read(root.GetProperty("policy"), Invalid));
    }

    /// <summary>The frame limits of the host session, counted over the whole frame, and the codes of its
    /// <c>request-limit</c> and <c>response-limit</c> rules.</summary>
    static HostSessionLimits ReadHostSession(JsonElement hostSession)
    {
        var limits = hostSession.GetProperty("frameLimits");
        if (limits.GetProperty("counted").GetString() is not "whole-frame" and var counted)
        {
            throw Invalid($"its frame limits are counted over {counted}, and this library counts the whole frame");
        }

        string Code(string rule) =>
            hostSession.GetProperty("rules").EnumerateArray()
                .Where(entry => entry.GetProperty("id").GetString() == rule)
                .Select(entry => entry.GetProperty("code").GetString())
                .SingleOrDefault() ?? throw Invalid($"it has no code for the host-session rule {rule}");

        // A cancelled call is an outcome with no fault kind and no code (GuestFailureKind.Cancelled).
        var cancelled = hostSession.GetProperty("rules").EnumerateArray()
            .SingleOrDefault(entry => entry.GetProperty("id").GetString() == "cancelled");
        if (cancelled.ValueKind != JsonValueKind.Object || cancelled.TryGetProperty("code", out _))
        {
            throw Invalid("its host-session rules do not make a cancelled call an outcome without a code");
        }

        return new HostSessionLimits(
            limits.GetProperty("requestBytes").GetInt64(),
            limits.GetProperty("responseBytes").GetInt64(),
            Code("request-limit"),
            Code("response-limit"));
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
        var wasiFunctions = ReadWasiSurface(abi, profile, wasiNamespace);
        var deadline = TimeSpan.FromSeconds(profile.GetProperty("callDeadlineSeconds").GetInt32());
        var delivery = profile.TryGetProperty("notificationDelivery", out var advertisement)
            ? ReadNotificationDelivery(abi, profile, advertisement)
            : null;
        if (abi == WasmAbi.ExtismKernel && delivery is null)
        {
            throw Invalid($"it does not say how a {abi} guest advertises notification delivery");
        }

        return new WasmProfileSurface(abi, profile.GetProperty("status").GetString()!, namespaces, imports, wasiNamespace, deadline)
        {
            WasiFunctions = wasiFunctions,
            NotificationDelivery = delivery,
        };
    }

    /// <summary>
    /// The number of WASI functions the profile admits: 0 without WASI, else the 46 of preview 1, named by the
    /// contract's <c>wasiSurface</c> through its witx definition. The classifier reads their signatures from the
    /// engine's own preview 1 definitions, so the contract must name that definition and its whole surface.
    /// </summary>
    static int ReadWasiSurface(WasmAbi abi, JsonElement profile, string? wasiNamespace)
    {
        var listed = profile.TryGetProperty("wasiSurface", out var surface);
        if (wasiNamespace is null)
        {
            return listed ? throw Invalid($"{abi} admits no WASI and still gives a WASI surface") : 0;
        }

        if (!listed)
        {
            throw Invalid($"{abi} admits {wasiNamespace} and gives no WASI surface");
        }

        var functions = surface.GetProperty("functions").GetInt32();
        var signatures = surface.GetProperty("signatures").GetString();
        if (functions != WasiPreview1Functions || signatures != $"{wasiNamespace}.witx")
        {
            throw Invalid($"the WASI surface of {abi} is {functions} functions of {signatures}, and this library admits the {WasiPreview1Functions} functions of {wasiNamespace}.witx");
        }

        return functions;
    }

    /// <summary>
    /// The profile's <c>notificationDelivery</c> object: the string member of the <c>morphir.initialize</c> result
    /// that advertises delivery, its default and its values. This library implements the values <c>none</c> and
    /// <c>call</c> and the four notification rules of the Extism profile; any other listed value, a default that is
    /// not <c>none</c>, or a missing rule fails the load.
    /// </summary>
    static WasmNotificationDelivery ReadNotificationDelivery(WasmAbi abi, JsonElement profile, JsonElement delivery)
    {
        if (delivery.GetProperty("of").GetString() is not "morphir.initialize result" and var of)
        {
            throw Invalid($"the notification advertisement of {abi} is a member of {of}, and this library reads the morphir.initialize result");
        }

        if (delivery.GetProperty("type").GetString() is not "string" and var type)
        {
            throw Invalid($"the notification advertisement of {abi} is a {type}, and this library reads a string");
        }

        var member = delivery.GetProperty("member").GetString()!.Split('.');
        if (member.Any(string.IsNullOrEmpty))
        {
            throw Invalid($"the notification advertisement of {abi} names the member {string.Join('.', member)}");
        }

        var defaultValue = delivery.GetProperty("default").GetString();
        var values = delivery.GetProperty("values").EnumerateArray().Select(value => value.GetProperty("value").GetString()!).ToArray();
        if (defaultValue != WasmNotificationDelivery.None
            || values.Length != 2
            || !values.ToHashSet(StringComparer.Ordinal).SetEquals([WasmNotificationDelivery.None, WasmNotificationDelivery.Call]))
        {
            throw Invalid($"the notification delivery of {abi} has the values {string.Join(", ", values)} with the default {defaultValue}, and this library implements {WasmNotificationDelivery.None} (the default) and {WasmNotificationDelivery.Call}");
        }

        var rules = profile.GetProperty("notifications").EnumerateArray().Select(rule => rule.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        string[] implemented = ["advertised", "output-fails", "unadvertised", "unlisted-value"];
        if (!rules.SetEquals(implemented))
        {
            throw Invalid($"the notification rules of {abi} are {string.Join(", ", rules)}, and this library implements {string.Join(", ", implemented)}");
        }

        return new WasmNotificationDelivery(member);
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
    TimeSpan CallDeadline)
{
    /// <summary>How many WASI preview 1 functions the profile admits, each with the signature of its witx
    /// definition (the contract's <c>wasiSurface</c>): 46, or 0 when the profile admits no WASI.</summary>
    public int WasiFunctions { get; init; }

    /// <summary>How a guest of the profile advertises notification delivery (the contract's
    /// <c>notificationDelivery</c>), or null when the profile delivers every message without one.</summary>
    public WasmNotificationDelivery? NotificationDelivery { get; init; }
}

/// <summary>
/// How an <c>extism-kernel-v1</c> guest asks for notifications (the contract's <c>notificationDelivery</c>): a
/// string member of its <c>morphir.initialize</c> result. <see cref="Call"/> asks for each notification as one
/// <c>handle</c> call; <see cref="None"/>, an absent member and any value the contract does not list, the reserved
/// <c>poll</c> among them, mean no delivery.
/// </summary>
/// <param name="Member">The path of the member inside the result, such as <c>capabilities</c>,
/// <c>notificationDelivery</c>.</param>
public sealed record WasmNotificationDelivery(IReadOnlyList<string> Member)
{
    /// <summary>The value that asks for no delivery, and the default.</summary>
    public const string None = "none";

    /// <summary>The value that asks for each notification as one <c>handle</c> call.</summary>
    public const string Call = "call";

    /// <summary>The delivery when the member is absent or holds a value the contract does not list.</summary>
    public string Default => None;

    /// <summary>The value under which the host delivers notifications.</summary>
    public string Delivering => Call;

    /// <summary>
    /// The delivery a guest's reply to <c>morphir.initialize</c> asks for: <see cref="Call"/> or
    /// <see cref="None"/>. A reply that is not a JSON object with an object <c>result</c> is not this reader's to
    /// judge (the host session checks the reply) and advertises nothing; nor does a member that is absent, not a
    /// string or a value the contract does not list.
    /// </summary>
    public string ReadFrom(ReadOnlyMemory<byte> initializeReply)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(initializeReply);
        }
        catch (JsonException)
        {
            return Default;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("result", out var current))
            {
                return Default;
            }

            foreach (var name in Member)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                {
                    return Default;
                }
            }

            return current.ValueKind == JsonValueKind.String && current.GetString() is Call or None ? current.GetString()! : Default;
        }
    }
}
