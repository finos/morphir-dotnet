using Morphir.Extensions;
using Morphir.Tooling.Features.Generate;

namespace Morphir.Tooling.Features.Validate;

/// <summary>The values that one <c>morphir validate</c> invocation gives, as text. <see cref="ModelValidation"/> parses them.</summary>
public sealed record ValidateOverrides(string? Input = null, string? InputFormat = null);

/// <summary>Reads which analyzers the configuration turns off. Only <c>extensions."&lt;id&gt;".enabled</c> is read.</summary>
public interface IAnalyzerConfiguration
{
    /// <summary>
    /// The ids among <paramref name="analyzers"/> that the layered configuration of <paramref name="workingDirectory"/>
    /// turns off with <c>enabled = false</c>, or a failure when the configuration cannot be read.
    /// </summary>
    Outcome<IReadOnlyList<ExtensionId>> TurnedOff(string workingDirectory, IReadOnlyList<ExtensionId> analyzers, CancellationToken ct);
}
