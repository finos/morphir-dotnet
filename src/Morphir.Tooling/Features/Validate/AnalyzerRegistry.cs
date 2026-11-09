using Morphir.Analyzers;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// The analyzers that this host runs: the built-in analyzers, with the host's release version. Registration is static;
/// there is no assembly scanning. <c>morphir validate</c> runs these analyzers, and <see cref="AnalyzerExtensionSource"/>
/// lists the same ones, so the two cannot disagree.
/// </summary>
public sealed class AnalyzerRegistry(HostIdentity host)
{
    /// <summary>The analyzers, ordered by id.</summary>
    public IReadOnlyList<IModelAnalyzer> Analyzers { get; } = BuiltInAnalyzers.Create(host.Version);
}
