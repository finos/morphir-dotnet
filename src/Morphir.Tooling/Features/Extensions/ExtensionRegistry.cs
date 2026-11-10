using System.Diagnostics.CodeAnalysis;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// Reads every statically registered <see cref="IExtensionSource"/> into one catalog for this host. There is no
/// assembly scanning. A catalog never holds two entries with the same id.
/// </summary>
/// <param name="sources">The registered extension sources.</param>
/// <param name="host">The host that this registry reads for.</param>
/// <param name="wasmRuntime">
/// The availability of this host's WASM runtime, as its runtime report states it. When it is not given, the runtime is
/// unavailable with <c>engine_missing</c>: no executable wires the WASM engine into the CLI yet.
/// </param>
public sealed class ExtensionRegistry(IEnumerable<IExtensionSource> sources, HostIdentity host, WasmRuntimeAvailability? wasmRuntime = null)
{
    /// <summary>The host that this registry reads for.</summary>
    public HostIdentity Host => host;

    /// <summary>The availability of this host's WASM runtime. A <c>wasm</c> entry is available only when the runtime is.</summary>
    public WasmRuntimeAvailability WasmRuntime { get; } =
        wasmRuntime ?? WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.EngineMissing);

    /// <summary>The catalog, or the error that names each duplicate id.</summary>
    public bool TryRead([MaybeNullWhen(false)] out ExtensionCatalog catalog, [MaybeNullWhen(true)] out RegistryError error) =>
        ExtensionCatalog.TryCreate(host, sources.SelectMany(source => source.Entries), out catalog, out error);
}
