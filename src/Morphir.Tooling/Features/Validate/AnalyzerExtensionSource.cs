using System.Collections.Immutable;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Validate;

/// <summary>One built-in <c>validator</c> entry for each analyzer that <c>morphir validate</c> runs.</summary>
public sealed class AnalyzerExtensionSource(AnalyzerRegistry analyzers) : IExtensionSource
{
    public IReadOnlyList<ExtensionEntry> Entries { get; } = analyzers.Analyzers.Select(analyzer => analyzer.Entry).ToImmutableArray();
}
