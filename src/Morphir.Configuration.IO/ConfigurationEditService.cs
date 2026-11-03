// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

/// <summary>A read-only preview. Canonical values and candidate bytes can contain secrets;
/// use shared redaction for presentation. Application is an explicit separate operation.</summary>
public sealed class ConfigurationEditPreview
{
    public EditPlan? Plan { get; }
    public ConfigurationTransactionPlan? Transaction { get; }
    public EditFailure? Failure { get; }
    internal ConfigurationEditPreview(EditPlan plan, ConfigurationTransactionPlan transaction) { Plan = plan; Transaction = transaction; }
    internal ConfigurationEditPreview(EditFailure failure) => Failure = failure;
}

/// <summary>Reusable planning boundary over explicitly authorized trees and a supplied storage host.
/// Construction and Plan never acquire writer coordination or create artifacts.</summary>
public sealed class ConfigurationEditService(IProjectTree tree, IConfigurationTransactionHost storage)
{
    public ConfigurationEditPreview Plan(string startPath, EditRequest request, IEnumerable<EditScope>? selectors = null,
        string? projectSelector = null, ProjectLoadOptions? options = null)
    {
        var scopes = (selectors ?? []).ToArray();
        var start = ProjectPath.normalize(startPath);
        var parsed = KeyPath.parse(request.Key);
        if (parsed.IsError) return Fail("config.edit_key");
        if (request.Operation is EditOperation.Set set)
        {
            var value = Editing.parseValue(set.valueType, set.text);
            if (value.IsError) return new(value.ErrorValue);
        }
        var emptyContext = new EditTargetContext(start, null, null, FSharpList<ConfigDiagnostic>.Empty, FSharpList<SourceIdentity>.Empty, FSharpList<SourceInput>.Empty);
        if (scopes.Length > 1 || scopes.FirstOrDefault() is EditScope.File)
        {
            var selected = Editing.selectTarget(ListModule.OfSeq(scopes), emptyContext);
            if (selected.IsError) return new(selected.ErrorValue);
        }
        try
        {
            options ??= new();
            var evidence = new Dictionary<string, ConfigurationObservation>(StringComparer.Ordinal);
            var mounted = new[] { tree }.Concat(options.MachineTree is { } single ? [single] : []).Concat(options.MachineTrees)
                .Distinct().Select(t => new ObservedTree(t, storage, evidence)).ToArray();
            var projectTree = mounted[0];
            var discovery = ProjectSystem.Discover(projectTree, start, projectSelector);
            var independent = scopes.Any(s => s.IsWorkspace || s.IsGlobal || s.IsSystem || s is EditScope.File);
            ProjectInfo? SelectProject(ProjectDiscoveryResult found)
            {
                if (scopes.OfType<EditScope.File>().FirstOrDefault() is { } file)
                {
                    var path = ProjectPath.join(start, file.Item);
                    // A recognized member-personal file keeps its own project context,
                    // even when another member is selected by the invocation defaults.
                    var owner = found.Discovery.Projects.Where(p => p.Path != found.Discovery.Location)
                        .OrderByDescending(p => p.Path.Length)
                        .FirstOrDefault(p => ProjectSources.overlayCandidates(p.Location.Sources, SourceRole.UserOverride, null)
                            .Any(source => source.Path == path));
                    if (owner is not null) return owner;
                }
                var invalidSelection = found.Selection.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
                return (!independent || !invalidSelection) && found.Selection.Projects.Count() == 1 ? found.Selection.Projects.Head : null;
            }
            var selectedProject = SelectProject(discovery);
            ResolutionInput? captured = null;
            var detailed = ProjectSystem.LoadDetailed(projectTree, discovery.Discovery, selectedProject, options with
            {
                MachineTree = null,
                MachineTrees = mounted.Skip(1).Cast<IProjectTree>().ToArray(),
                CaptureInput = input => captured = input
            });
            if (captured is null) return Fail("config.edit_context");
            var sources = captured.Sources.ToList();
            var inventory = new List<SourceIdentity>();
            void Primaries(string path, SourceRole role) => inventory.AddRange(DiscoveryPolicy.candidates(path).Select(p =>
                new SourceIdentity(p, p.EndsWith(".toml", StringComparison.Ordinal) ? DocumentFormat.Toml : p.EndsWith(".yaml", StringComparison.Ordinal) || p.EndsWith(".yml", StringComparison.Ordinal) ? DocumentFormat.Yaml : DocumentFormat.Json,
                    role, p.EndsWith("morphir-workspace.json", StringComparison.Ordinal) ? 0 : 1, null)));
            Primaries(discovery.Discovery.Location, SourceRole.RootPrimary);
            if (selectedProject is not null && selectedProject.Path != discovery.Discovery.Location) Primaries(selectedProject.Path, SourceRole.MemberPrimary);
            var machine = options.Machine is null ? FSharpList<SourceIdentity>.Empty : ProjectSources.machineCandidates(ProjectSystem.ResolveMachineLocations(options.Machine, start), false);
            inventory.AddRange(machine.Where(source => !options.Isolated || scopes.Any(scope => scope.IsGlobal && source.Role == SourceRole.Global || scope.IsSystem && source.Role == SourceRole.System)));
            var personal = ProjectSources.overlayCandidates(discovery.Discovery.Root.Sources, selectedProject is not null && selectedProject.Path != discovery.Discovery.Location ? SourceRole.WorkspaceUserOverride : SourceRole.UserOverride, null).ToList();
            if (selectedProject is not null && selectedProject.Path != discovery.Discovery.Location)
                personal.AddRange(ProjectSources.overlayCandidates(ListModule.OfSeq(selectedProject.Location.Sources.Select(s => new SourceInput(new(s.Source.Path, s.Source.Format, SourceRole.MemberPrimary, s.Source.Order, s.Source.Profile), s.Content, s.Required))), SourceRole.UserOverride, null));
            var explicitPersonal = scopes.OfType<EditScope.File>().Any(scope => personal.Any(source => source.Path == ProjectPath.join(start, scope.Item)));
            if (!options.Isolated || scopes.Any(scope => scope.IsUser) || explicitPersonal) inventory.AddRange(personal);
            foreach (var source in inventory.DistinctBy(s => (s.Path, s.Role)))
            {
                if (sources.Any(s => s.Source.Path == source.Path && s.Source.Role == source.Role)) continue;
                var host = mounted.Where(t => ProjectPath.contains(t.Boundary, source.Path)).OrderByDescending(t => t.Boundary.Length).FirstOrDefault();
                if (host is null) continue;
                var entry = host.Observe(source.Path);
                sources.Add(new(source, entry is ProjectEntry.File file ? file.Item : SourceContent.Missing, false));
            }
            var context = new EditTargetContext(start, FSharpOption<ProjectDiscovery>.Some(discovery.Discovery),
                selectedProject is null ? null : FSharpOption<ProjectInfo>.Some(selectedProject), discovery.Selection.Diagnostics, machine, ListModule.OfSeq(sources));
            var targetResult = Editing.selectTarget(ListModule.OfSeq(scopes), context);
            if (targetResult.IsError) return new(targetResult.ErrorValue);
            var target = targetResult.ResultValue;
            var machineTarget = scopes.Any(s => s.IsGlobal || s.IsSystem);
            var targetHost = (machineTarget ? mounted : [projectTree]).Where(t => ProjectPath.contains(t.Boundary, target.Path)).OrderByDescending(t => t.Boundary.Length).FirstOrDefault();
            if (targetHost is null) return Fail("config.edit_boundary");
            var targetEntry = targetHost.Observe(target.Path);
            if (targetEntry.IsLink || targetEntry.IsDirectory || targetEntry.IsFailure) return Fail("config.edit_target");
            var expected = evidence[target.Path];
            var bytes = expected.Kind == ConfigurationObservationKind.File ? storage.ReadFile(target.Path) : [];
            if (storage.Observe(target.Path) != expected) return Fail("config.edit_conflict");
            var codec = TextCodec.Decode(bytes);
            // The target is always available for editing, even if absent from an isolated read.
            var inputSources = captured.Sources.ToList();
            if (!inputSources.Any(s => s.Source.Path == target.Path))
                inputSources.Add(ConfigurationReader.ReadText(target, codec.Text, false));
            var input = new ResolutionInput(ListModule.OfSeq(inputSources), captured.Environment, captured.Explicit, captured.Profiles, captured.Scope, captured.OwningWorkspace, captured.SuppressWarnings);
            var observations = ListModule.OfSeq(evidence.Values.OrderBy(o => o.Path, StringComparer.Ordinal).Select(o => new EditObservation(o.Path, (EditObservationKind)(int)o.Kind, o.Identity)));
            var planned = Editing.plan(target, request, input, observations);
            if (planned.IsError) return new(planned.ErrorValue);
            var plan = planned.ResultValue;
            var edited = TomlEditor.Edit(codec.Text, plan.Key.ToArray(), plan.After?.Value);
            if (!edited.Success) return Fail(edited.ErrorCode ?? "config.edit_render");
            var rendered = codec.Encode(edited.Text);
            // Resolve the proposed filesystem through the same discovery/loading boundary.
            // Topology and profile edits can change which sources participate.
            var candidateTrees = mounted.Select(t => (IProjectTree)new OverlayTree(t, target.Path, edited.Text)).ToArray();
            var candidateDiscovery = ProjectSystem.Discover(candidateTrees[0], start, projectSelector);
            var candidateProject = SelectProject(candidateDiscovery);
            ResolutionInput? candidateInput = null;
            var candidateDetails = ProjectSystem.LoadDetailed(candidateTrees[0], candidateDiscovery.Discovery, candidateProject, options with
            {
                MachineTree = null,
                MachineTrees = candidateTrees.Skip(1).ToArray(),
                SelectedPath = scopes.Any(s => s is EditScope.File) ? target.Path : options.SelectedPath,
                CaptureInput = input => candidateInput = input
            });
            var usesSelection = !independent;
            var errors = candidateDetails.Resolution.Diagnostics.Concat(usesSelection ? candidateDiscovery.Selection.Diagnostics : []).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0) return new(new EditFailure("config.edit_validation", 2, Inspection.safeDiagnostics(ListModule.OfSeq(errors))));
            var explanation = Inspection.explain(QueryScope.Effective, plan.Key, candidateDetails);
            var finalObservations = ListModule.OfSeq(evidence.Values.OrderBy(o => o.Path, StringComparer.Ordinal).Select(o => new EditObservation(o.Path, (EditObservationKind)(int)o.Kind, o.Identity)));
            plan = new EditPlan(plan.Target, plan.Key, plan.Operation, plan.Before, plan.After, plan.Candidate,
                candidateInput ?? plan.CandidateInput, explanation, plan.Changed, finalObservations);
            var transaction = new ConfigurationTransactionPlan(evidence.Values, [new ConfigurationFileChange(target.Path, expected, rendered)]);
            return new(plan, transaction);
        }
        catch (ObservationConflictException)
        {
            return Fail("config.edit_conflict");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail("config.edit_io");
        }
    }

    public ConfigurationTransactionResult Apply(ConfigurationEditPreview preview) => preview.Transaction is { } transaction
        ? new ConfigurationTransaction(storage).Apply(transaction)
        : new(ConfigurationTransactionStatus.IoFailure, "config.edit_invalid_plan", [], []);

    private sealed class ObservationConflictException : IOException;

    private static ConfigurationEditPreview Fail(string code) => new(new EditFailure(code, 2, FSharpList<ConfigDiagnostic>.Empty));

    private sealed class ObservedTree(IProjectTree inner, IConfigurationTransactionHost storage, Dictionary<string, ConfigurationObservation> evidence) : IProjectTree
    {
        public string Boundary => inner.Boundary;
        public ProjectEntry Observe(string path)
        {
            var before = storage.Observe(path);
            var entry = inner.Observe(path);
            if (entry is ProjectEntry.File && before.Kind == ConfigurationObservationKind.File)
            {
                var bytes = storage.ReadFile(path);
                if (NativeConfigurationTransactionHost.Hash(bytes) != before.Identity)
                    throw new ObservationConflictException();
                entry = ProjectEntry.NewFile(ConfigurationReader.ReadText(path, TextCodec.Decode(bytes).Text).Content);
            }
            if (storage.Observe(path) != before || evidence.TryGetValue(path, out var prior) && prior != before)
                throw new ObservationConflictException();
            evidence[path] = before;
            return entry;
        }
    }

    private sealed class OverlayTree(IProjectTree inner, string target, string text) : IProjectTree
    {
        public string Boundary => inner.Boundary;
        public ProjectEntry Observe(string path)
        {
            if (path == target) return ProjectEntry.NewFile(ConfigurationReader.ReadText(path, text).Content);
            var entry = inner.Observe(path);
            var parent = ProjectPath.parent(target);
            if (!ProjectPath.contains(path, parent) || !(entry.IsDirectory || entry.IsMissing)) return entry;
            var children = entry is ProjectEntry.Directory directory ? directory.children.AsEnumerable() : [];
            if (path != parent)
            {
                var relative = ProjectPath.relative(path, parent).Split('/')[0];
                children = children.Append(ProjectPath.join(path, relative));
            }
            return ProjectEntry.NewDirectory(ListModule.OfSeq(children.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }
    }

    private sealed record TextCodec(string Text, Encoding Encoding, byte[] Preamble)
    {
        internal byte[] Encode(string text) => [.. Preamble, .. Encoding.GetBytes(text)];
        internal static TextCodec Decode(byte[] bytes)
        {
            Encoding[] encodings = [new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true)];
            foreach (var encoding in encodings)
            {
                var preamble = encoding.GetPreamble();
                if (bytes.AsSpan().StartsWith(preamble)) return new(encoding.GetString(bytes.AsSpan(preamble.Length)), encoding, preamble);
            }
            var utf8 = new UTF8Encoding(false, true);
            return new(utf8.GetString(bytes), utf8, []);
        }
    }
}
