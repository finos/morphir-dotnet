// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

public enum WorkspaceInventoryKind { Candidate, Excluded, Boundary, AlreadyMigrated, LinkSkipped }
public sealed record WorkspaceInventoryEntry(string Path, string RelativePath, WorkspaceInventoryKind Kind, string Reason, string? Name);
public sealed record WorkspaceAdoptionEffect(string ProjectPath, string Kind, string? Before, string? After);

/// <summary>An immutable aggregate. Project Presentation is safe for logs; project canonical plans
/// and internal transaction buffers are application data and must not be logged.</summary>
public sealed class WorkspaceMigrationPreview
{
    public string RootPath { get; }
    public bool RootIsProject { get; }
    public ImmutableArray<WorkspaceInventoryEntry> Inventory { get; }
    public ImmutableArray<ProjectMigrationPreview> Projects { get; }
    public ImmutableArray<string> Members { get; }
    public string? WorkspaceTarget { get; }
    public ImmutableArray<MigrationEdit> WorkspaceEdits { get; }
    public ImmutableArray<WorkspaceAdoptionEffect> AdoptionEffects { get; }
    public ImmutableArray<ConfigDiagnostic> Diagnostics { get; }
    public ImmutableArray<ConfigurationRecoveryInspection> PendingRecovery { get; }
    public bool Verified { get; }
    public bool Changed { get; }
    public bool CanApply => Verified && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    internal object Owner { get; }
    internal ConfigurationTransactionPlan? Transaction { get; }
    internal WorkspaceMigrationPreview(object owner, string root, bool rootIsProject,
        IEnumerable<WorkspaceInventoryEntry> inventory, IEnumerable<ProjectMigrationPreview> projects,
        IEnumerable<string> members, string? target, IEnumerable<MigrationEdit> edits,
        IEnumerable<WorkspaceAdoptionEffect> effects, IEnumerable<ConfigDiagnostic> diagnostics,
        bool verified = false, bool changed = false, ConfigurationTransactionPlan? transaction = null,
        IEnumerable<ConfigurationRecoveryInspection>? pendingRecovery = null)
    {
        Owner = owner; RootPath = root; RootIsProject = rootIsProject;
        Inventory = inventory.OrderBy(i => i.Path, StringComparer.Ordinal).ToImmutableArray();
        Projects = projects.OrderBy(p => p.ProjectPath, StringComparer.Ordinal).ToImmutableArray();
        Members = members.ToImmutableArray(); WorkspaceTarget = target; WorkspaceEdits = edits.ToImmutableArray();
        AdoptionEffects = effects.ToImmutableArray(); Diagnostics = Inspection.safeDiagnostics(ReaderValues.List(diagnostics)).ToImmutableArray();
        PendingRecovery = (pendingRecovery ?? []).ToImmutableArray();
        Verified = verified; Changed = changed; Transaction = transaction;
    }
}

/// <summary>Bounded inventory, adoption planning and verified aggregate application for any host.
/// Does not load ancestors, personal, machine, environment or activated profile overlays.</summary>
public sealed class WorkspaceMigrationService(IProjectTree tree, IConfigurationTransactionHost? storage = null)
{
    public WorkspaceMigrationPreview Plan(string root, IEnumerable<string>? includes = null, IEnumerable<string>? excludes = null)
    {
        var diagnostics = new List<ConfigDiagnostic>();
        var inventory = new List<WorkspaceInventoryEntry>();
        var projects = new List<ProjectMigrationPreview>();
        var members = new List<string>();
        var edits = new List<MigrationEdit>();
        var effects = new List<WorkspaceAdoptionEffect>();
        var pendingRecovery = new List<ConfigurationRecoveryInspection>();
        string? target = null;
        bool rootProject = false;
        WorkspaceMigrationPreview Result(bool verified = false, bool changed = false, ConfigurationTransactionPlan? transaction = null) =>
            new(this, root, rootProject, inventory, projects, members, target, edits, effects, diagnostics, verified, changed, transaction, pendingRecovery);
        void Error(string path, string code, string message) => diagnostics.Add(Diagnostic(path, code, message));
        try
        {
            // Validate before normalization so empty/relative invocations cannot acquire ambient scope.
            if (string.IsNullOrWhiteSpace(root) || !ProjectPath.isAbsolute(root))
            { Error(root, "workspace.migrate_boundary", "Select an absolute existing directory inside the authorized tree."); return Result(); }
            root = ProjectPath.normalize(root);
            if (!ProjectPath.contains(tree.Boundary, root))
            { Error(root, "workspace.migrate_boundary", "Selected root is outside the authorized tree."); return Result(); }
            var include = (includes ?? []).ToArray();
            var exclude = (excludes ?? []).ToArray();
            if (include.Concat(exclude).Any(p => !ValidPattern(p)))
            { Error(root, "workspace.migrate_filter", "Filters must be nonempty root-relative patterns without parent traversal."); return Result(); }
            var observed = new ObservedTree(tree, root, storage);
            if (observed.Observe(root) is not ProjectEntry.Directory)
            { Error(root, "workspace.migrate_root", "Selected root must be a readable directory without linked ancestors."); return Result(); }
            var locations = new SortedDictionary<string, ProjectLocation>(StringComparer.Ordinal);
            var rootLocation = Inspect(observed, root);
            diagnostics.AddRange(rootLocation.Diagnostics);
            rootProject = Has(rootLocation.Values, "project");
            var declared = Has(rootLocation.Values, "workspace");
            var workspaceBase = ProjectPath.join(root, Text(rootLocation.Values, ".", "workspace", "root"));
            if (!ProjectPath.contains(root, workspaceBase))
            { Error(root, "workspace.migrate_boundary", "Declared workspace root escapes the selected operation boundary."); return Result(); }
            // Existing exclusions are relative to the declared workspace base. Supplied filters are operation-root relative.
            var declaredExcludes = Strings(rootLocation.Values, "workspace", "exclude").ToArray();
            var outputDirectory = declared ? ProjectPath.join(workspaceBase, Text(rootLocation.Values, ".morphir/out", "workspace", "out_dir")) : null;
            var availableDirectories = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Stack<string>(); queue.Push(root);
            const int maxDirectories = 100000;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (queue.TryPop(out var path))
            {
                if (!visited.Add(path)) continue;
                if (visited.Count > maxDirectories)
                { Error(root, "workspace.migrate_inventory_limit", "Inventory directory limit exceeded."); break; }
                var relative = ProjectPath.relative(root, path);
                if (path != root && (Skipped(Path.GetFileName(path)) || outputDirectory is not null && ProjectPath.contains(outputDirectory, path) || Excluded(relative, exclude)
                    || ProjectPath.contains(workspaceBase, path) && Excluded(ProjectPath.relative(workspaceBase, path), declaredExcludes)))
                { inventory.Add(new(path, relative, WorkspaceInventoryKind.Excluded, "Directory excluded by default, supplied filter or declared workspace exclusion.", null)); continue; }
                var entry = observed.Observe(path);
                if (entry.IsLink) { inventory.Add(new(path, relative, WorkspaceInventoryKind.LinkSkipped, "Discovered link is not followed.", null)); continue; }
                if (entry is ProjectEntry.Failure)
                { Error(path, "workspace.migrate_inventory", "Unable to inspect a directory."); continue; }
                if (entry is not ProjectEntry.Directory directory) continue;
                var location = path == root ? rootLocation : Inspect(observed, path);
                if (path != root && Has(location.Values, "workspace"))
                { inventory.Add(new(path, relative, WorkspaceInventoryKind.Boundary, "Nested declared workspace is independent.", Name(location))); continue; }
                availableDirectories.Add(path);
                diagnostics.AddRange(path == root ? [] : location.Diagnostics);
                if (Has(location.Values, "project"))
                {
                    if (Excluded(relative, exclude) || include.Length > 0 && !include.Any(p => Glob(p, relative)))
                        inventory.Add(new(path, relative, WorkspaceInventoryKind.Excluded, "Project excluded by supplied include/exclude filters.", Name(location)));
                    else
                    {
                        var classics = Classics(location).ToArray();
                        locations[path] = location;
                        inventory.Add(new(path, relative, classics.Length == 0 ? WorkspaceInventoryKind.AlreadyMigrated : WorkspaceInventoryKind.Candidate,
                            classics.Length == 0 ? "Canonical project retained without rewriting." : "Classic project selected for verified conversion.", Name(location)));
                    }
                }
                foreach (var child in directory.children.Reverse())
                {
                    if (!ProjectPath.isAbsolute(child) || ProjectPath.parent(child) != path || !ProjectPath.contains(root, child))
                    { Error(path, "workspace.migrate_listing", "Host returned a child outside the bounded immediate directory."); continue; }
                    // Primary layout directories belong to this location, never separate projects.
                    if (Path.GetFileName(child) is ".morphir" or ".config") continue;
                    queue.Push(child);
                }
            }
            if (storage is { SupportsDurableRecovery: true })
            {
                pendingRecovery.AddRange(new ConfigurationTransaction(storage).InspectPending(observed.Evidence.Keys));
                if (pendingRecovery.Count > 0)
                {
                    Error(root, "workspace.migrate_recovery_required", "Pending recovery blocks this operation. Inspect and explicitly recover its journal before another write.");
                    return Result();
                }
            }
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result();
            if (!declared && locations.Count == 0)
            { Error(root, "workspace.migrate_no_adoption", "No eligible projects exist within the selected root; no adoption can be planned."); return Result(); }
            // Existing declarations are preserved verbatim. Only append explicit paths not already represented.
            var originalMembers = Strings(rootLocation.Values, "workspace", "members").ToArray();
            members.AddRange(originalMembers);
            var bounded = new MembershipTree(observed, root, availableDirectories, outputDirectory);
            var existingDiscovery = declared ? ProjectSystem.Discover(bounded, root).Discovery : null;
            if (existingDiscovery is not null)
            {
                diagnostics.AddRange(existingDiscovery.Diagnostics);
                // Never absorb a pre-existing declaration outside the operation root or through a nested boundary.
                foreach (var old in existingDiscovery.Projects.Where(p => p.Path != root))
                {
                    if (!ProjectPath.contains(root, old.Path) || inventory.Any(i => i.Kind == WorkspaceInventoryKind.Boundary && ProjectPath.contains(i.Path, old.Path)))
                        Error(old.Path, "workspace.migrate_membership", "Existing membership crosses the operation or nested-workspace boundary.");
                    if (!inventory.Any(i => i.Path == old.Path))
                        inventory.Add(new(old.Path, ProjectPath.relative(root, old.Path), Classics(old.Location).Any() ? WorkspaceInventoryKind.Excluded : WorkspaceInventoryKind.AlreadyMigrated,
                            "Existing declared member retained without conversion.", old.Name?.Value));
                }
            }
            var existingPaths = existingDiscovery?.Projects.Select(p => p.Path).ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var path in locations.Keys.Where(p => p != root))
            {
                if (!ProjectPath.contains(workspaceBase, path))
                { Error(path, "workspace.migrate_membership", "Candidate lies outside the declared workspace base."); continue; }
                if (!existingPaths.Contains(path))
                {
                    var member = ProjectPath.relative(workspaceBase, path);
                    if (member.IndexOfAny(['*', '?']) >= 0)
                    { Error(path, "workspace.migrate_member_path", "A project path containing glob characters cannot be declared as an explicit literal member."); continue; }
                    if (!members.Contains(member, StringComparer.Ordinal)) members.Add(member);
                }
            }
            var rootTomls = rootLocation.Sources.Where(s => s.Source.Format == DocumentFormat.Toml).ToArray();
            var rootClassic = Classics(rootLocation).SingleOrDefault();
            target = rootTomls.Length == 1 ? rootTomls[0].Source.Path : rootClassic is not null
                ? Path.ChangeExtension(rootClassic.Source.Path, ".toml") : ProjectPath.join(root, "morphir.toml");
            if (!declared || !originalMembers.SequenceEqual(members))
                edits.Add(new(ReaderValues.List(new[] { "workspace", "members" }), ArrayValue(members)));
            var targetRaw = rootTomls.FirstOrDefault()?.Content is SourceContent.Available current ? current.Item.Raw : DocumentDefaults.emptyValue;
            var rootText = BufferFor(observed, target, targetRaw);
            var workspaceText = rootText.Text;
            foreach (var edit in edits)
            {
                var rendered = TomlEditor.Edit(workspaceText, edit.KeyPath.ToArray(), edit.Value);
                if (!rendered.Success) { Error(target, "workspace.migrate_render", "Workspace edits cannot be represented without loss."); return Result(); }
                workspaceText = rendered.Text;
            }
            var overlay = new CandidateTree(bounded, root);
            if (edits.Count > 0) overlay.Set(target, workspaceText);
            // Convert root first so all members resolve against its final proposed canonical base.
            if (locations.TryGetValue(root, out var selectedRoot) && Classics(selectedRoot).Any())
            {
                var service = new ProjectMigrationService(overlay);
                var originalTarget = rootTomls.FirstOrDefault() ?? new SourceInput(new SourceIdentity(target, DocumentFormat.Toml, SourceRole.RootPrimary, 4, null), SourceContent.Missing, false);
                var rootContext = Context(overlay, root, Classics(selectedRoot).Select(s => s.Source.Path),
                    edits.Count > 0 ? members : null, originalTarget);
                var projected = rootContext.RootSources.FirstOrDefault(s => s.Source.Path == target)?.Content is SourceContent.Available projectedDocument
                    ? projectedDocument.Item.Raw : DocumentDefaults.emptyValue;
                // Carry declared ownership controls without retaining the converted classic as a base layer.
                // All remaining root conversion values still come through the unchanged loss/equivalence mapper.
                foreach (var control in new[] { "root", "out_dir" })
                {
                    var key = ReaderValues.List(new[] { "workspace", control });
                    var value = ConfigTree.tryFind(key, rootLocation.Values);
                    if (value is not null) projected = ConfigTree.set(key, value.Value, projected);
                }
                var projectedIdentity = new SourceIdentity(target, DocumentFormat.Toml, SourceRole.RootPrimary, 4, null);
                var projectedInput = new SourceInput(projectedIdentity, SourceContent.NewAvailable(Normalization.normalize(projectedIdentity, projected,
                    MapModule.Empty<FSharpList<string>, SourcePosition>())), false);
                rootContext = new ProjectMigrationContext(root, rootContext.Workspace,
                    rootContext.RootSources.Where(s => s.Source.Path != target).Append(projectedInput), edits.Count > 0 ? members : null, originalTarget);
                var rootPreview = service.PlanInWorkspace(root, rootContext, Classics(selectedRoot).Single().Source.Path, target);
                projects.Add(rootPreview); diagnostics.AddRange(rootPreview.Diagnostics);
                if (rootPreview.CanApply)
                {
                    workspaceText = Render(workspaceText, rootPreview.Plan!.Edits);
                    overlay.Set(target, workspaceText);
                }
            }
            var common = Context(overlay, root, projects.Select(p => p.SourcePath!));
            foreach (var (path, location) in locations.Where(p => p.Key != root))
            {
                var classics = Classics(location).ToArray();
                if (classics.Length == 0) continue;
                if (classics.Length != 1) { Error(path, "workspace.migrate_classic", "Expected one unambiguous classic source."); continue; }
                var service = new ProjectMigrationService(overlay);
                var existingTarget = location.Sources.FirstOrDefault(s => s.Source.Format == DocumentFormat.Toml)?.Source.Path;
                var preview = service.PlanInWorkspace(path, common, classics[0].Source.Path, existingTarget);
                projects.Add(preview); diagnostics.AddRange(preview.Diagnostics);
                if (preview.CanApply)
                {
                    var raw = location.Sources.FirstOrDefault(s => s.Source.Path == preview.TargetPath)?.Content is SourceContent.Available a ? a.Item.Raw : DocumentDefaults.emptyValue;
                    var original = BufferFor(observed, preview.TargetPath!, raw);
                    overlay.Set(preview.TargetPath!, Render(original.Text, preview.Plan!.Edits));
                }
            }
            foreach (var project in projects.Where(p => p.CanApply && !p.Changed))
            {
                var index = inventory.FindIndex(i => i.Path == project.ProjectPath);
                if (index >= 0) inventory[index] = inventory[index] with { Kind = WorkspaceInventoryKind.AlreadyMigrated, Reason = "Equivalent migrated project retained without rewriting." };
            }
            if (projects.Any(p => !p.CanApply) || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result();
            if (projects.Count > 1 && projects.Any(p => !Equals(p.Verification!.BaseWorkspace, projects[0].Verification!.BaseWorkspace)))
            { Error(root, "workspace.migrate_context", "Conversions do not share the same proposed base workspace context."); return Result(); }
            // Every newly adopted project keeps its original non-adoption settings,
            // whether the workspace is new or already declared.
            foreach (var (path, location) in locations.Where(p => !declared || !existingPaths.Contains(p.Key)))
            {
                var original = BaseResolution(location.Sources, ProjectScope.Root, null);
                var adoptedSources = path == root ? common.RootSources.Concat(location.Sources.Where(s => Classics(location).Any(c => c.Source.Path == s.Source.Path)))
                    : common.RootSources.Concat(location.Sources.Select(s => Rebind(s, SourceRole.MemberPrimary)));
                var adopted = BaseResolution(adoptedSources, path == root ? ProjectScope.Root : ProjectScope.Member, common.Workspace);
                var beforeValues = ComparableValues(original, location.Sources);
                var afterValues = ComparableValues(adopted, adoptedSources);
                if (!ProjectMigration.same(beforeValues, afterValues))
                    Error(path, "workspace.migrate_adoption_value", "Adoption changes a non-adoption setting; review the project and proposed shared base settings.");
            }
            // Check the actual retained-source view as well as independent conversion discovery.
            var retained = ProjectSystem.Discover(overlay, root).Discovery;
            // Remove every converted classic input together. Rerun actual discovery/selection over the whole candidate.
            foreach (var project in projects) overlay.Hide(project.SourcePath!);
            var candidate = ProjectSystem.Discover(overlay, root).Discovery;
            diagnostics.AddRange(candidate.Diagnostics);
            if (retained.Location != candidate.Location || !Equals(retained.Workspace, candidate.Workspace)
                || !retained.Projects.Select(p => (p.Path, p.Name?.Value, p.IsValid)).SequenceEqual(candidate.Projects.Select(p => (p.Path, p.Name?.Value, p.IsValid))))
                Error(root, "workspace.migrate_verification", "Retained-source discovery differs from the independently converted workspace.");
            if (candidate.Location != root || candidate.Workspace is null || candidate.Projects.Any(p => !p.IsValid))
                Error(root, "workspace.migrate_verification", "Proposed workspace discovery is invalid.");
            var expectedPaths = locations.Keys.Concat(existingPaths).Concat(rootProject ? new[] { root } : []).Where(p => p == root || ProjectPath.contains(workspaceBase, p)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!candidate.Projects.Select(p => p.Path).Order(StringComparer.Ordinal).SequenceEqual(expectedPaths))
                Error(root, "workspace.migrate_membership", "Proposed discovery differs from the reviewed project paths.");
            foreach (var project in candidate.Projects)
            {
                var selected = DiscoveryPolicy.select(candidate, FSharpOption<string>.Some(project.Path));
                if (selected.Projects.Count() != 1 || selected.Projects.Head.Path != project.Path)
                    Error(project.Path, "workspace.migrate_selection", "Proposed member cannot be selected by its distinct path.");
            }
            var beforeSelection = existingDiscovery is null ? null : DiscoveryPolicy.select(existingDiscovery, null);
            var afterSelection = DiscoveryPolicy.select(candidate, null);
            var retainedSelection = DiscoveryPolicy.select(retained, null);
            if (!retainedSelection.Projects.Select(p => p.Path).SequenceEqual(afterSelection.Projects.Select(p => p.Path))
                || !retainedSelection.Diagnostics.Select(d => d.Code).SequenceEqual(afterSelection.Diagnostics.Select(d => d.Code)))
                Error(root, "workspace.migrate_selection", "Retained-source default selection differs from the independently converted workspace.");
            if (Has(rootLocation.Values, "workspace", "default_member") && afterSelection.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                Error(root, "workspace.migrate_default", "The declared default member is not a valid unambiguous project in the proposed workspace.");
            var beforeSelected = beforeSelection is null ? "" : string.Join(",", beforeSelection.Projects.Select(p => p.Path));
            var afterSelected = string.Join(",", afterSelection.Projects.Select(p => p.Path));
            if (declared && (beforeSelected != afterSelected || !beforeSelection!.Diagnostics.Select(d => d.Code).SequenceEqual(afterSelection.Diagnostics.Select(d => d.Code))))
            {
                // An undeclared default can become ambiguous through new membership; this is an explicit adoption effect.
                effects.Add(new(root, "default-selection", beforeSelected, afterSelected));
            }
            else if (!declared) effects.Add(new(root, "default-selection", null, afterSelected));
            var output = candidate.Workspace?.Value.OutputDirectory?.Value;
            foreach (var path in locations.Keys)
            {
                var previouslyOwned = declared && (existingPaths.Contains(path) || path == root);
                var oldOutput = previouslyOwned ? existingDiscovery!.Workspace?.Value.OutputDirectory?.Value : ProjectPath.join(path, ".morphir/out");
                effects.Add(new(path, "ownership", previouslyOwned ? existingDiscovery!.Workspace?.Value.Root : path, candidate.Workspace?.Value.Root));
                effects.Add(new(path, "membership", existingPaths.Contains(path) || path == root && declared ? "retained" : "standalone", path == root ? "root-project" : ProjectPath.relative(workspaceBase, path)));
                effects.Add(new(path, "shared-output-root", oldOutput, output));
            }
            var verified = !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            var changes = new List<ConfigurationFileChange>();
            if (storage is not null && verified)
            {
                foreach (var (path, text) in overlay.Texts.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var oldRaw = path == target ? targetRaw : DocumentDefaults.emptyValue;
                    var buffer = BufferFor(observed, path, oldRaw);
                    var bytes = buffer.Encode(text);
                    var expected = observed.Evidence[path];
                    if (expected.Kind == ConfigurationObservationKind.File && storage.ReadFile(path).AsSpan().SequenceEqual(bytes)) continue;
                    changes.Add(new(path, expected, bytes));
                }
            }
            var changed = edits.Count > 0 || projects.Any(p => p.Changed);
            var transaction = storage is null ? null : new ConfigurationTransactionPlan(observed.Evidence.Values, changes);
            return Result(verified, changed, transaction);
        }
        catch (MembershipBoundaryException boundary)
        { Error(boundary.BoundaryPath, boundary.Code, boundary.Message); return Result(); }
        catch (RegexMatchTimeoutException)
        { Error(root, "workspace.migrate_filter_limit", "Filter evaluation exceeded its bounded matching time. Simplify the include/exclude patterns."); return Result(); }
        catch (ObservationConflictException) { Error(root, "workspace.migrate_stale", "Inventory or source changed while planning."); return Result(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { Error(root, "workspace.migrate_io", "Unable to inventory, parse or compose workspace migration. Check paths and permissions."); return Result(); }
    }

    public ConfigurationTransactionResult Apply(WorkspaceMigrationPreview preview) => ReferenceEquals(preview.Owner, this)
        && preview.CanApply && storage is not null && preview.Transaction is { } transaction
        ? new ConfigurationTransaction(storage).Apply(transaction)
        : new(ConfigurationTransactionStatus.IoFailure, "workspace.migrate_unverified", [], []);

    private static SourceInput Rebind(SourceInput source, SourceRole role) => new(
        new SourceIdentity(source.Source.Path, source.Source.Format, role, source.Source.Order, null), source.Content, source.Required);
    private static DocumentResolution BaseResolution(IEnumerable<SourceInput> sources, ProjectScope scope, FSharpOption<OwningWorkspace>? workspace) =>
        Resolver.resolve(new ResolutionInput(ReaderValues.List(sources), MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
            new ProfileOptions(ProfileSelection.Disabled, DocumentDefaults.profileOptions.MaxDepth, DocumentDefaults.profileOptions.MaxExpansions), scope, workspace, FSharpList<string>.Empty));

    private static ConfigValue ComparableValues(DocumentResolution resolution, IEnumerable<SourceInput> sources)
    {
        var values = ConfigTree.remove("workspace", resolution.Effective);
        var controls = ProjectMigration.declaredControls(ReaderValues.List(sources));
        return controls is null ? values : ConfigTree.set(ReaderValues.List(new[] { "profiles" }), controls.Value, values);
    }

    private static ProjectLocation Inspect(IProjectTree tree, string path)
    {
        var entries = DiscoveryPolicy.candidates(path).Select(p => Tuple.Create(p, tree.Observe(p)));
        return DiscoveryPolicy.inspectLocation(path, new ProjectSnapshot(MapModule.OfSeq(entries)));
    }
    private static ProjectMigrationContext Context(IProjectTree tree, string root, IEnumerable<string> convertedSources,
        IEnumerable<string>? proposedMembers = null, SourceInput? originalTarget = null)
    {
        var found = ProjectSystem.Discover(tree, root).Discovery;
        var converted = convertedSources.ToHashSet(StringComparer.Ordinal);
        return new(root, found.Workspace, found.Root.Sources.Where(s => !converted.Contains(s.Source.Path)), proposedMembers, originalTarget);
    }
    private static IEnumerable<SourceInput> Classics(ProjectLocation location) => location.Sources.Where(s =>
        s.Content is SourceContent.Available a
        && a.Item.LegacyFields.Any(f => f.RawPath.Any() && f.Disposition == "normalized"));
    private static bool Has(ConfigValue value, params string[] path) => ConfigTree.tryFind(ReaderValues.List(path), value) is not null;
    private static string? Name(ProjectLocation location) => (ConfigTree.tryFind(ReaderValues.List(new[] { "project", "name" }), location.Values)?.Value as ConfigValue.String)?.Item;
    private static string Text(ConfigValue value, string fallback, params string[] path) => (ConfigTree.tryFind(ReaderValues.List(path), value)?.Value as ConfigValue.String)?.Item ?? fallback;
    private static IEnumerable<string> Strings(ConfigValue value, params string[] path) => ConfigTree.tryFind(ReaderValues.List(path), value)?.Value is ConfigValue.Array array
        ? array.Item.OfType<ConfigValue.String>().Select(s => s.Item) : [];
    private static ConfigValue ArrayValue(IEnumerable<string> values) => ConfigValue.NewArray(ReaderValues.List(values.Select(ConfigValue.NewString)));
    private static bool Skipped(string name) => name is ".git" or ".hg" or ".svn" or "node_modules" or "elm-stuff" or "bin" or "obj" or "dist" or "build" or "target" or "vendor" or ".nuget" or "packages";
    private static bool ValidPattern(string pattern) => !string.IsNullOrWhiteSpace(pattern) && !ProjectPath.isAbsolute(pattern)
        && !pattern.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal) && !pattern.Contains(':');
    internal static bool Glob(string pattern, string relative)
    {
        var parts = pattern.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToArray();
        var path = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToArray();
        var memo = new Dictionary<(int, int), bool>();
        bool Match(int p, int v)
        {
            if (memo.TryGetValue((p, v), out var found)) return found;
            var result = p == parts.Length ? v == path.Length : parts[p] == "**"
                ? Match(p + 1, v) || v < path.Length && Match(p, v + 1)
                : v < path.Length && Regex.IsMatch(path[v], "\\A" + Regex.Escape(parts[p]).Replace("\\*", ".*").Replace("\\?", ".") + "\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) && Match(p + 1, v + 1);
            memo[(p, v)] = result; return result;
        }
        return Match(0, 0);
    }
    private static bool Excluded(string relative, IEnumerable<string> patterns) => patterns.Any(p => Glob(p, relative));
    private static ConfigDiagnostic Diagnostic(string path, string code, string message) => new(code, message, DiagnosticSeverity.Error,
        new SourceIdentity(path, DocumentFormat.Memory, SourceRole.RootPrimary, 0, null), FSharpList<string>.Empty, null);
    private static string Render(string text, IEnumerable<MigrationEdit> edits)
    {
        foreach (var edit in edits)
        {
            var rendered = TomlEditor.Edit(text, edit.KeyPath.ToArray(), edit.Value);
            if (!rendered.Success) throw new IOException("Mapped value cannot be represented.");
            text = rendered.Text;
        }
        return text;
    }
    private static TextBuffer BufferFor(ObservedTree tree, string path, ConfigValue raw)
    {
        tree.Observe(path);
        if (tree.Storage is { } storage)
        {
            var expected = tree.Evidence[path];
            var bytes = expected.Kind == ConfigurationObservationKind.File ? storage.ReadFile(path) : [];
            if (storage.Observe(path) != expected) throw new ObservationConflictException();
            return TextBuffer.Decode(bytes);
        }
        var text = "";
        if (raw is ConfigValue.Object fields)
            foreach (var field in fields.Item) text = Render(text, [new(ReaderValues.List(new[] { field.Key }), field.Value)]);
        return TextBuffer.Decode(Encoding.UTF8.GetBytes(text));
    }
    private sealed class ObservationConflictException : IOException;
    private sealed class ObservedTree(IProjectTree inner, string root, IConfigurationTransactionHost? storage) : IProjectTree
    {
        public string Boundary => root;
        public IConfigurationTransactionHost? Storage => storage;
        public Dictionary<string, ConfigurationObservation> Evidence { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectEntry> entries = new(StringComparer.Ordinal);
        public ProjectEntry Observe(string path)
        {
            if (!ProjectPath.contains(root, path)) return ProjectEntry.NewFailure("Outside operation boundary");
            var before = storage?.Observe(path);
            if (before is not null && Evidence.TryGetValue(path, out var prior) && prior != before) throw new ObservationConflictException();
            var entry = inner.Observe(path);
            if (entry is ProjectEntry.File && before?.Kind == ConfigurationObservationKind.File)
            {
                var bytes = storage!.ReadFile(path);
                if (NativeConfigurationTransactionHost.Hash(bytes) != before.Identity) throw new ObservationConflictException();
                entry = ProjectEntry.NewFile(ConfigurationReader.ReadText(path, TextBuffer.Decode(bytes).Text).Content);
            }
            if (storage is not null)
            {
                if (storage.Observe(path) != before) throw new ObservationConflictException();
                Evidence[path] = before!;
            }
            if (entries.TryGetValue(path, out var old) && !old.Equals(entry)) throw new ObservationConflictException();
            entries[path] = entry;
            return entry;
        }
    }
    // Keep the real shared discovery semantics. Existing ownership may require reading
    // filtered members, but a declaration cannot force reads across hard boundaries.
    private sealed class MembershipBoundaryException(string path, string code, string message) : IOException(message)
    {
        public string BoundaryPath { get; } = path;
        public string Code { get; } = code;
    }
    private sealed class MembershipTree(IProjectTree inner, string root, HashSet<string> inventoried, string? outputDirectory) : IProjectTree
    {
        public string Boundary => root;
        private readonly HashSet<string> checkedDirectories = new(inventoried, StringComparer.Ordinal);
        public ProjectEntry Observe(string path)
        {
            if (!ProjectPath.contains(root, path)) return inner.Observe(path);
            var anchor = path;
            // Resolve hidden primary ownership exactly as the single-project input policy does.
            for (var directory = ProjectPath.parent(path); ProjectPath.contains(root, directory); directory = ProjectPath.parent(directory))
            {
                if (DiscoveryPolicy.candidates(directory).Contains(path)) anchor = directory;
                if (directory == root) break;
            }
            var ancestors = new Stack<string>();
            for (var directory = anchor; directory != root && ProjectPath.contains(root, directory); directory = ProjectPath.parent(directory)) ancestors.Push(directory);
            while (ancestors.TryPop(out var directory))
            {
                if (checkedDirectories.Contains(directory)) continue;
                if (Skipped(Path.GetFileName(directory)) || Path.GetFileName(directory) is ".morphir" or ".config"
                    || outputDirectory is not null && ProjectPath.contains(outputDirectory, directory)) throw Conflict(directory);
                var entry = inner.Observe(directory);
                if (entry.IsLink)
                {
                    if (directory != anchor || path != anchor) throw Conflict(directory);
                    break;
                }
                if (entry is not ProjectEntry.Directory) break;
                if (Has(Inspect(inner, directory).Values, "workspace")) throw Conflict(directory);
                if (checkedDirectories.Count >= 100000)
                    throw new MembershipBoundaryException(directory, "workspace.migrate_inventory_limit", "Inventory directory limit exceeded while verifying existing membership.");
                checkedDirectories.Add(directory);
            }
            return inner.Observe(path);
        }
        private static MembershipBoundaryException Conflict(string path) => new(path, "workspace.migrate_membership_boundary",
            "Existing membership reaches a skipped directory or nested declared workspace. Refine its declarations or exclusions before migration.");
    }
    private sealed class CandidateTree(IProjectTree inner, string root) : IProjectTree
    {
        public string Boundary => root;
        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);
        private readonly HashSet<string> hidden = new(StringComparer.Ordinal);
        public void Set(string path, string text) => Texts[path] = text;
        public void Hide(string path) => hidden.Add(path);
        public ProjectEntry Observe(string path) => hidden.Contains(path) ? ProjectEntry.Missing
            : Texts.TryGetValue(path, out var text) ? ProjectEntry.NewFile(ConfigurationReader.ReadText(path, text).Content) : inner.Observe(path);
    }
    private sealed record TextBuffer(string Text, Encoding Encoding, byte[] Preamble)
    {
        public byte[] Encode(string text) => [.. Preamble, .. Encoding.GetBytes(text)];
        public static TextBuffer Decode(byte[] bytes)
        {
            Encoding[] encodings = [new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true)];
            foreach (var encoding in encodings)
            {
                var preamble = encoding.GetPreamble();
                if (bytes.AsSpan().StartsWith(preamble)) return new(encoding.GetString(bytes.AsSpan(preamble.Length)), encoding, preamble);
            }
            var utf8 = new UTF8Encoding(false, true); return new(utf8.GetString(bytes), utf8, []);
        }
    }
}
