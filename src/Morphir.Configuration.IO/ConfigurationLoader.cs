using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

/// <summary>An explicit candidate and a host-supplied reader. The loader never inventories directories.</summary>
public sealed record ConfigurationCandidate(SourceIdentity Source, Func<SourceInput> Read);

/// <summary>Plans active profiles before invoking profile readers.</summary>
public static class ConfigurationLoader
{
    public static DocumentResolution LoadAndResolve(ResolutionInput input, IEnumerable<ConfigurationCandidate> candidates)
    {
        var inventory = candidates.ToArray();
        var sources = input.Sources.ToList();
        foreach (var candidate in inventory.Where(item => !IsProfile(item.Source)))
            sources.Add(candidate.Read());
        var explicitSource = new SourceIdentity("<explicit>", DocumentFormat.Memory, SourceRole.Explicit, int.MaxValue, FSharpOption<string>.None);
        var explicitDocument = new ConfigurationDocument(explicitSource, input.Explicit, input.Explicit,
            MapModule.Empty<FSharpList<string>, SourcePosition>(), FSharpList<ConfigDiagnostic>.Empty, FSharpList<LegacyField>.Empty);
        var ordinary = ReaderValues.List(sources.Append(new SourceInput(explicitSource, SourceContent.NewAvailable(explicitDocument), true)));
        var plan = Profiles.plan(ordinary, input.Environment, input.Profiles);
        if (plan.Enabled && !plan.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            foreach (var candidate in inventory.Where(item => IsProfile(item.Source)
                && item.Source.Profile is not null && plan.Active.Contains(item.Source.Profile.Value)))
                sources.Add(candidate.Read());
        }
        return Resolver.resolve(new ResolutionInput(ReaderValues.List(sources), input.Environment,
            input.Explicit, input.Profiles, input.Scope, input.OwningWorkspace, input.SuppressWarnings));
    }

    private static bool IsProfile(SourceIdentity source) =>
        source.Role is SourceRole.WorkspaceProfile or SourceRole.MemberProfile;
}
