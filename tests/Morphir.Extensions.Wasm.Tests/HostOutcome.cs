namespace Morphir.Extensions.Wasm.Tests;

/// <summary>What loading and classifying a module gave, in a shape tests can compare with a corpus expectation.</summary>
internal sealed record HostOutcome(string? RefusalCode, string? Message, WasmAbi? Profile)
{
    public static HostOutcome Refused(WasmRefusal refusal) => new(refusal.Code, refusal.Message, null);

    public static HostOutcome Accepted(WasmAbi profile) => new(null, null, profile);

    public override string ToString() =>
        RefusalCode is null ? $"accepted as {Profile}" : $"refused with {RefusalCode}: {Message}";

    /// <summary>Loads <paramref name="bytes"/> on the shared engine and admits the module to this host.</summary>
    public static HostOutcome Admit(byte[] bytes, string? declaredAbi) =>
        Load(bytes).Match(
            compiled => WasmHost.Admit(compiled, Declared(declaredAbi)).Match(
                admitted => Accepted(admitted.Profile),
                Refused),
            Refused,
            notWasm => throw new InvalidOperationException($"The engine does not read the bytes as WASM: {notWasm}"));

    /// <summary>Loads <paramref name="bytes"/> and classifies the module against the contract alone, as a host
    /// that runs every profile of the contract would.</summary>
    public static HostOutcome Classify(byte[] bytes, string? declaredAbi) =>
        Load(bytes).Match(
            compiled => ModuleClassifier.Classify(compiled, Declared(declaredAbi)).Match(Accepted, Refused),
            Refused,
            notWasm => throw new InvalidOperationException($"The engine does not read the bytes as WASM: {notWasm}"));

    static ModuleLoad Load(byte[] bytes) => WasmEngine.Shared.Load(bytes);

    static WasmAbi? Declared(string? declaredAbi) =>
        declaredAbi is null ? null
        : WasmAbi.TryParse(declaredAbi, out var abi) ? abi
        : throw new InvalidOperationException($"The corpus declares an ABI the contract lacks: {declaredAbi}");
}
