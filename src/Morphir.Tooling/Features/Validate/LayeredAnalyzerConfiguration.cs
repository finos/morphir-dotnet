using System.Collections.Immutable;
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Extensions;
using Morphir.Tooling.Features.Configuration;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Validate;

/// <summary>
/// Reads <c>extensions."&lt;id&gt;".enabled</c> from the effective layered configuration of the invocation directory,
/// with the same discovery, containment and resolution as <c>morphir config show</c>.
/// </summary>
/// <param name="environment">The command's environment snapshot: containment and machine locations come from it.</param>
public sealed class LayeredAnalyzerConfiguration(IReadOnlyDictionary<string, string> environment) : IAnalyzerConfiguration
{
    private static readonly ConfigValue TurnedOffValue = ConfigValue.NewBoolean(false);

    public Outcome<IReadOnlyList<ExtensionId>> TurnedOff(string workingDirectory, IReadOnlyList<ExtensionId> analyzers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var resolution = Containment.Resolve(null, null, environment, workingDirectory);
        if (resolution.Failure is { } invalid) return Failure(invalid.Code, invalid.Message);
        var containment = resolution.Value!;
        try
        {
            var invocation = new ConfigurationInvocation(QueryScope.Effective, null, null, containment.NativeBoundary,
                DocumentDefaults.profileOptions, containment.Isolated, [], environment);
            var detailed = new NativeConfigurationInspectionHost(workingDirectory).Load(invocation);
            var errors = detailed.Resolution.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                return new Outcome<IReadOnlyList<ExtensionId>>.Failure(new GenerationFailure(GenerationFailureKind.Invocation,
                    errors.Select(error => new GenerationDiagnostic(error.Code, error.Message, NullIfEmpty(error.Source.Path))).ToImmutableArray()));
            var effective = detailed.Resolution.Effective;
            // Compare with Equals: the value must be the boolean false. Any other value, or none, leaves the analyzer on.
            var turnedOff = analyzers.Where(id =>
                ConfigTree.tryFind(ListModule.OfSeq(["extensions", id.Value, "enabled"]), effective) is { } value && value.Value.Equals(TurnedOffValue));
            return new Outcome<IReadOnlyList<ExtensionId>>.Success(turnedOff.ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure("config.load", "Configuration loading failed. Check paths and access permissions.");
        }
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;

    private static Outcome<IReadOnlyList<ExtensionId>> Failure(string code, string message) =>
        new Outcome<IReadOnlyList<ExtensionId>>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation, code, message));
}
