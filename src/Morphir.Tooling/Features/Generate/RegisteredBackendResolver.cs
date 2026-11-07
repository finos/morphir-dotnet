using System.Collections.Immutable;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Resolves statically registered providers by exact target and declared capabilities.</summary>
public sealed class RegisteredBackendResolver(IEnumerable<IGenerationBackend> backends) : IGenerationBackendResolver
{
    private readonly ImmutableArray<IGenerationBackend> _backends = backends.ToImmutableArray();

    public Outcome<IGenerationBackend> Resolve(RequestedTarget target, ModelDescriptor model)
    {
        // A malformed target names no registered target, so it fails exactly as an unknown target does.
        var candidates = target is RequestedTarget.Valid valid
            ? _backends.Where(backend => backend.Descriptor.Target.Equals(valid.Id)).ToArray()
            : [];
        if (candidates.Length == 0)
        {
            var available = string.Join(", ", _backends.Select(backend => backend.Descriptor.Target.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return Fail(GenerationFailureKind.Invocation, "unknown-target", $"Unknown target '{target.Text}'. Available targets: {available}.");
        }

        var compatible = candidates.Where(backend => backend.Descriptor.Capabilities.Any(capability => capability.Supports(model))).ToArray();
        if (compatible.Length > 0 && !model.RequiredSemantics.IsDefaultOrEmpty)
        {
            compatible = compatible.Where(backend => backend.Descriptor.SupportedSemantics.IsSupersetOf(model.RequiredSemantics)).ToArray();
            if (compatible.Length == 0)
                return Fail(GenerationFailureKind.Semantic, "unsupported-required-semantics", "No compatible provider declares all semantics required by this model.");
        }
        return compatible.Length switch
        {
            0 => Fail(GenerationFailureKind.Semantic, "incompatible-model", $"Target '{target.Text}' does not support {ModelLabels.Family(model.Family)} {model.Version.Text} {ModelLabels.Kind(model.Kind)}."),
            1 => new Outcome<IGenerationBackend>.Success(compatible[0]),
            _ => Fail(GenerationFailureKind.Invocation, "ambiguous-backend", $"Multiple compatible providers for '{target.Text}': {string.Join(", ", compatible.Select(backend => backend.Descriptor.Id.Value).Order(StringComparer.Ordinal))}.")
        };
    }

    private static Outcome<IGenerationBackend> Fail(GenerationFailureKind kind, string code, string message) =>
        new Outcome<IGenerationBackend>.Failure(GenerationFailure.One(kind, code, message));
}
