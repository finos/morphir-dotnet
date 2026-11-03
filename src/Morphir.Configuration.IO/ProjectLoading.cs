using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

/// <summary>Host controls. Machine access requires a separately authorized tree; no ambient access is implied.</summary>
public sealed record ProjectLoadOptions
{
    public ResolutionInput Input { get; init; } = DocumentDefaults.resolutionInput;
    public bool Isolated { get; init; }
    public string? SelectedPath { get; init; }
    public MachineLocationInput? Machine { get; init; }
    public IProjectTree? MachineTree { get; init; }
}

public static partial class ProjectSystem
{
    /// <summary>Resolve one root or member with the owning topology kept separately from inherited settings.</summary>
    public static DocumentResolution Load(IProjectTree tree, ProjectDiscovery discovery, ProjectInfo? project = null,
        ProjectLoadOptions? options = null)
    {
        options ??= new();
        var member = project is not null && project.Path != discovery.Location;
        var sources = options.Input.Sources.Where(s => !options.Isolated || s.Source.Role is not (SourceRole.System or SourceRole.Global or SourceRole.UserOverride or SourceRole.WorkspaceUserOverride)).ToList();
        var skipped = new Dictionary<string, SourceReport>(StringComparer.Ordinal);
        var diagnostics = discovery.Diagnostics.ToList();
        if (project is not null && (!project.IsValid || !discovery.Projects.Contains(project)))
            diagnostics.Add(ReaderValues.Diagnostic(new SourceIdentity(project.Path, DocumentFormat.Memory, SourceRole.MemberPrimary, 0, null),
                "discovery.invalid_selection", "Project must be a valid member of this discovery result"));

        SourceInput Read(IProjectTree host, SourceIdentity source, bool required = false)
        {
            var entry = ProjectPath.isAbsolute(source.Path) && ProjectPath.contains(host.Boundary, source.Path)
                ? host.Observe(source.Path) : ProjectEntry.NewFailure("Candidate is outside the authorized host boundary: " + source.Path);
            SourceContent content;
            switch (entry)
            {
                case ProjectEntry.File file: content = file.Item; break;
                case ProjectEntry.Failure error:
                    content = SourceContent.NewInvalid(ReaderValues.List([ReaderValues.Diagnostic(source, "discovery.read_error", error.Item)])); break;
                case ProjectEntry.Directory:
                    content = SourceContent.NewInvalid(ReaderValues.List([ReaderValues.Diagnostic(source, "discovery.invalid_candidate", "Configuration path is a directory")])); break;
                default:
                    content = SourceContent.Missing;
                    if (entry.IsLink)
                        skipped[source.Path] = new(source, SourceStatus.Skipped, false, "Discovered link is not followed");
                    break;
            }
            return new(source, content, required);
        }

        if (options.Machine is { } machine && !options.Isolated)
        {
            var environment = machine.Environment;
            if (environment.TryGetValue("MORPHIR_HOME", out var home) && !string.IsNullOrEmpty(home) && !ProjectPath.isAbsolute(home))
                environment = environment.Add("MORPHIR_HOME", ProjectPath.join(discovery.StartDirectory, home));
            machine = new(machine.Platform, machine.HomeDirectory, machine.RoamingAppData, machine.ProgramData, environment);
            if (options.MachineTree is null)
                diagnostics.Add(ReaderValues.Diagnostic(new SourceIdentity("<machine-host>", DocumentFormat.Memory, SourceRole.System, 0, null),
                    "discovery.machine_host_required", "Machine locations require an explicitly authorized machine tree"));
            else
                sources.AddRange(ProjectSources.machineCandidates(machine, false).Select(s => Read(options.MachineTree, s)));
        }

        var roots = discovery.Root.Sources;
        var members = member ? ReaderValues.List(project!.Location.Sources.Select(s => Rebind(s, SourceRole.MemberPrimary))) : FSharpList<SourceInput>.Empty;
        sources.AddRange(roots);
        sources.AddRange(members);
        var rootRole = member ? SourceRole.WorkspaceUserOverride : SourceRole.UserOverride;
        var rootPersonal = ProjectSources.overlayCandidates(roots, rootRole, null);
        var memberPersonal = ProjectSources.overlayCandidates(members, SourceRole.UserOverride, null);
        if (!options.Isolated)
        {
            sources.AddRange(rootPersonal.Select(s => Read(tree, s)));
            sources.AddRange(memberPersonal.Select(s => Read(tree, s)));
        }
        if (options.SelectedPath is not null)
        {
            var path = ProjectPath.join(discovery.StartDirectory, options.SelectedPath);
            var existing = sources.FirstOrDefault(s => s.Source.Path == path);
            if (existing is null)
            {
                var personal = rootPersonal.Concat(memberPersonal).FirstOrDefault(s => s.Path == path);
                if (personal is not null)
                {
                    // Isolation omits automatic personal reads, but explicit selection
                    // retains the selected file's discovered ownership metadata.
                    existing = Read(tree, personal, true);
                    sources.Add(existing);
                }
            }
            sources.Add(existing is null
                ? Read(tree, new SourceIdentity(path, FormatOf(path), SourceRole.Selected, 0, null), true)
                : Rebind(existing, SourceRole.Selected));
        }
        var explicitSource = new SourceIdentity("<explicit>", DocumentFormat.Memory, SourceRole.Explicit, int.MaxValue, null);
        var explicitDocument = new ConfigurationDocument(explicitSource, options.Input.Explicit, options.Input.Explicit,
            MapModule.Empty<FSharpList<string>, SourcePosition>(), FSharpList<ConfigDiagnostic>.Empty, FSharpList<LegacyField>.Empty);
        diagnostics.AddRange(ProjectSources.ambiguities(ReaderValues.List(sources)));
        var plan = Profiles.plan(ReaderValues.List(sources.Append(new SourceInput(explicitSource, SourceContent.NewAvailable(explicitDocument), true))),
            options.Input.Environment, options.Input.Profiles);
        if (plan.Enabled && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) && !plan.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            foreach (var name in plan.Active)
            {
                sources.AddRange(ProjectSources.overlayCandidates(roots, SourceRole.WorkspaceProfile, FSharpOption<string>.Some(name)).Select(s => Read(tree, s)));
                sources.AddRange(ProjectSources.overlayCandidates(members, SourceRole.MemberProfile, FSharpOption<string>.Some(name)).Select(s => Read(tree, s)));
            }
        diagnostics.AddRange(ProjectSources.ambiguities(ReaderValues.List(sources)));
        // Resolve the owner's permitted overlays independently. Member identity and
        // member-personal/profile output attempts cannot relocate this context.
        var owner = discovery.Workspace;
        if (owner is not null)
        {
            var memberPaths = sources.Where(s => s.Source.Role is SourceRole.MemberPrimary or SourceRole.MemberProfile or SourceRole.UserOverride)
                .Select(s => s.Source.Path).ToHashSet(StringComparer.Ordinal);
            var ownerSources = sources.Where(s => !member || (s.Source.Role is not (SourceRole.MemberPrimary or SourceRole.MemberProfile or SourceRole.UserOverride)
                && !(s.Source.Role == SourceRole.Selected && memberPaths.Contains(s.Source.Path))));
            var ownerInput = new ResolutionInput(ReaderValues.List(ownerSources), options.Input.Environment, options.Input.Explicit,
                options.Input.Profiles, ProjectScope.Root, owner, options.Input.SuppressWarnings);
            var ownerResult = Resolver.resolve(ownerInput);
            if (member) diagnostics.AddRange(ownerResult.Diagnostics.Where(d => d.Code == "config.validation" && d.KeyPath.FirstOrDefault() == "workspace"));
            var output = ConfigTree.tryFind(ReaderValues.List(new[] { "workspace", "out_dir" }), ownerResult.Effective);
            if (output?.Value is ConfigValue.String value)
                owner = FSharpOption<OwningWorkspace>.Some(new(owner.Value.Root, FSharpOption<string>.Some(ProjectPath.join(owner.Value.Root, value.Item))));
        }
        var input = new ResolutionInput(ReaderValues.List(sources), options.Input.Environment, options.Input.Explicit,
            options.Input.Profiles, member ? ProjectScope.Member : ProjectScope.Root, owner, options.Input.SuppressWarnings);
        var result = Resolver.resolve(input);
        return new(result.Effective, result.Origins, ReaderValues.List(result.Sources.Select(s => skipped.GetValueOrDefault(s.Source.Path, s))),
            result.ActiveProfiles, result.OwningWorkspace, ReaderValues.List(result.Diagnostics.Concat(diagnostics).Distinct()));
    }

    private static SourceInput Rebind(SourceInput input, SourceRole role) => new(
        new SourceIdentity(input.Source.Path, input.Source.Format, role, input.Source.Order, input.Source.Profile),
        input.Content, input.Required);

    private static DocumentFormat FormatOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".toml" => DocumentFormat.Toml,
        ".yaml" or ".yml" => DocumentFormat.Yaml,
        ".json" or ".json5" => DocumentFormat.Json,
        _ => DocumentFormat.Memory
    };
}
