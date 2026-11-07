using System.Diagnostics.CodeAnalysis;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// Reads every statically registered <see cref="IExtensionSource"/> into one catalog for this host. There is no
/// assembly scanning. A catalog never holds two entries with the same id.
/// </summary>
public sealed class ExtensionRegistry(IEnumerable<IExtensionSource> sources, HostIdentity host)
{
    /// <summary>The host that this registry reads for.</summary>
    public HostIdentity Host => host;

    /// <summary>The catalog, or the error that names each duplicate id.</summary>
    public bool TryRead([MaybeNullWhen(false)] out ExtensionCatalog catalog, [MaybeNullWhen(true)] out RegistryError error) =>
        ExtensionCatalog.TryCreate(host, sources.SelectMany(source => source.Entries), out catalog, out error);
}
