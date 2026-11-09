using Morphir.Analyzers;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// The analyzers that this host runs: the built-in analyzers, with the host's release version. Registration is static;
/// there is no assembly scanning. <c>morphir validate</c> runs these analyzers, and <see cref="AnalyzerExtensionSource"/>
/// lists the same ones, so the two cannot disagree.
/// </summary>
public sealed class AnalyzerRegistry
{
    /// <summary>The built-in analyzers of the host.</summary>
    public AnalyzerRegistry(HostIdentity host) : this(BuiltInAnalyzers.Create(host.Version)) { }

    /// <summary>Other analyzers, for tests. Not public, so dependency injection always uses the host constructor.</summary>
    internal AnalyzerRegistry(IEnumerable<IModelAnalyzer> analyzers) => Analyzers = analyzers.ToArray();

    /// <summary>The analyzers.</summary>
    public IReadOnlyList<IModelAnalyzer> Analyzers { get; }
}
