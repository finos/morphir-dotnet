using System.Collections.Immutable;
using Morphir.Extensions;
using Morphir.IR.Model;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// Makes one built-in extension entry for each registered generation backend, from its descriptor. The list of
/// extensions therefore always agrees with the targets that <c>morphir generate</c> can resolve. No parsing happens
/// here: the descriptors already hold parsed values.
/// </summary>
public sealed class BackendExtensionSource : IExtensionSource
{
    public BackendExtensionSource(IEnumerable<IGenerationBackend> backends, HostIdentity host) =>
        Entries = backends.Select(backend => Entry(backend.Descriptor, host)).ToImmutableArray();

    public IReadOnlyList<ExtensionEntry> Entries { get; }

    private static ExtensionEntry Entry(BackendDescriptor descriptor, HostIdentity host)
    {
        // Each capability has at least one version and a descriptor has at least one capability.
        var versions = descriptor.Capabilities.SelectMany(capability => capability.Versions).ToArray();
        var claim = new BackendClaim(NonEmptySet<TargetId>.Create(descriptor.Target), NonEmptySet<SourceFormatVersion>.Create(versions[0], versions[1..]));
        return ExtensionEntry.BuiltInBackend(ExtensionIdentity.Create(descriptor.Id, descriptor.Name, host.Version, descriptor.Description), claim);
    }
}
