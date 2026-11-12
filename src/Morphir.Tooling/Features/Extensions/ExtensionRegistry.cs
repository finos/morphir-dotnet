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
/// This host's WASM runtime, the one its runtime report (<c>extension runtimes</c>) reads: in the CLI,
/// <c>WasmRuntime.Process</c>. The registry never reads its availability itself.
/// </param>
public sealed class ExtensionRegistry(IEnumerable<IExtensionSource> sources, HostIdentity host, WasmRuntime wasmRuntime)
{
    /// <summary>The host that this registry reads for.</summary>
    public HostIdentity Host => host;

    /// <summary>
    /// This host's WASM runtime. A <c>wasm</c> entry is available only when the runtime is; a reader asks
    /// <c>WasmRuntime.Availability</c> only for such an entry, so a catalog of built-in entries never probes it.
    /// </summary>
    public WasmRuntime WasmRuntime { get; } = wasmRuntime ?? throw new ArgumentNullException(nameof(wasmRuntime));

    /// <summary>The catalog, or the error that names each duplicate id.</summary>
    public bool TryRead([MaybeNullWhen(false)] out ExtensionCatalog catalog, [MaybeNullWhen(true)] out RegistryError error) =>
        ExtensionCatalog.TryCreate(host, sources.SelectMany(source => source.Entries), out catalog, out error);
}
