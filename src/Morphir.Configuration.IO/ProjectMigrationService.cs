// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Morphir.Configuration.IO;

/// <summary>A host-independent preview. Ledger, verification and diagnostics are redacted.
/// Plan's candidate and edits retain canonical values for explicit application and must not be logged.</summary>
public sealed class ProjectMigrationPreview
{
    public string? SourcePath { get; }
    public string? TargetPath { get; }
    public string? ProjectPath { get; }
    public MigrationPlan? Plan { get; }
    public MigrationVerification? Verification { get; }
    /// <summary>Complete safe structured plan for read-only hosts and presentation.</summary>
    public MigrationPlan? Presentation => Plan is null ? null : ProjectMigration.redactPlan(Plan);
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }
    public bool CanApply => Plan is { Blocked: false } && Verification is { Verified: true }
        && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public bool Changed { get; }
    internal ConfigurationTransactionPlan? Transaction { get; }
    internal object Owner { get; }

    internal ProjectMigrationPreview(object owner, string? source, string? target, string? project,
        MigrationPlan? plan, MigrationVerification? verification, IEnumerable<ConfigDiagnostic> diagnostics,
        bool changed = false, ConfigurationTransactionPlan? transaction = null)
    {
        Owner = owner; SourcePath = source; TargetPath = target; ProjectPath = project;
        Plan = plan; Verification = verification; Diagnostics = Inspection.safeDiagnostics(ListModule.OfSeq(diagnostics)).ToImmutableArray();
        Changed = changed; Transaction = transaction;
    }
}

/// <summary>Explicit proposed base workspace shared by aggregate conversion hosts.
/// Contains only declared base primaries; no ambient overlays are loaded.</summary>
public sealed class ProjectMigrationContext(string rootPath, FSharpOption<OwningWorkspace>? workspace, IEnumerable<SourceInput> rootSources,
    IEnumerable<string>? proposedMembers = null, SourceInput? originalTarget = null)
{
    public string RootPath { get; } = rootPath;
    public FSharpOption<OwningWorkspace>? Workspace { get; } = workspace;
    public ImmutableArray<SourceInput> RootSources { get; } = rootSources.ToImmutableArray();
    public ImmutableArray<string> ProposedMembers { get; } = proposedMembers?.ToImmutableArray() ?? default;
    public SourceInput? OriginalTarget { get; } = originalTarget;
}

/// <summary>Plans one classic project over an explicitly bounded tree. A storage adapter is optional
/// for read-only hosts; only an adapter-backed verified plan can be applied by its creating service.</summary>
public sealed class ProjectMigrationService(IProjectTree tree, IConfigurationTransactionHost? storage = null)
{
    public ProjectMigrationPreview Plan(string startPath, string? inputPath = null, string? outputPath = null)
        => PlanCore(startPath, inputPath, outputPath, null);

    /// <summary>Read-only component composition for an aggregate. Apply the aggregate through
    /// WorkspaceMigrationService; hypothetical base contexts never grant individual write capability.</summary>
    public ProjectMigrationPreview PlanInWorkspace(string startPath, ProjectMigrationContext context, string? inputPath = null, string? outputPath = null)
        => PlanCore(startPath, inputPath, outputPath, context);

    private ProjectMigrationPreview PlanCore(string startPath, string? inputPath, string? outputPath, ProjectMigrationContext? context)
    {
        string? sourcePath = null, targetPath = null, projectPath = null;
        var diagnostics = new List<ConfigDiagnostic>();
        MigrationPlan? plan = null;
        MigrationVerification? verification = null;
        ProjectMigrationPreview Fail(string code, string message) => new(this, sourcePath, targetPath, projectPath,
            plan, verification, diagnostics.Append(Diagnostic(sourcePath ?? startPath, code, message)));
        try
        {
            var start = ProjectPath.normalize(startPath);
            if (!ProjectPath.isAbsolute(start) || !ProjectPath.contains(tree.Boundary, start))
                return Fail("migration.boundary", "Invocation must be inside the authorized project tree.");
            if (inputPath is not null && string.IsNullOrWhiteSpace(inputPath) || outputPath is not null && string.IsNullOrWhiteSpace(outputPath))
                return Fail("migration.input", "Project configuration paths must be nonempty.");
            var input = inputPath is null ? start : ProjectPath.join(start, inputPath);
            if (!ProjectPath.contains(tree.Boundary, input)) return Fail("migration.boundary", "Project input is outside the authorized tree.");
            if (input.EndsWith("/morphir-ir.json", StringComparison.OrdinalIgnoreCase))
                return Fail("migration.input", "Expected one project directory or classic project configuration, not an IR file.");
            var evidence = new Dictionary<string, ConfigurationObservation>(StringComparer.Ordinal);
            var observed = new MigrationTree(tree, storage, evidence);
            var entry = observed.Observe(input);
            if (entry is not ProjectEntry.Directory && entry is not ProjectEntry.File)
                return Fail("migration.input", "Expected an existing project directory or classic project configuration.");
            var explicitFile = entry is ProjectEntry.File;
            var discoveryInput = input;
            if (explicitFile)
                // A hidden primary is owned by the project above its layout directory.
                // Ask the shared candidate policy instead of treating the file's parent as a project.
                for (var directory = ProjectPath.parent(input); ProjectPath.contains(tree.Boundary, directory); directory = ProjectPath.parent(directory))
                {
                    if (DiscoveryPolicy.candidates(directory).Contains(input)) discoveryInput = directory;
                    if (directory == tree.Boundary) break;
                }
            var found = ProjectSystem.Discover(observed, discoveryInput);
            diagnostics.AddRange(found.Discovery.Diagnostics);
            diagnostics.AddRange(found.Selection.Diagnostics);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || found.Selection.Projects.Count() != 1)
                return Fail("migration.selection", "Select one unambiguous valid project.");
            var project = found.Selection.Projects.Head;
            if (!explicitFile && input != found.Discovery.Location)
            {
                if (!ProjectPath.contains(project.Path, input))
                    return Fail("migration.selection", "The requested directory does not belong to the selected project.");
                // Inventory selection can fall back to a containing root project even when
                // a nearer, undeclared primary was inspected on the upward discovery walk.
                var inspected = found.Discovery.Sources.Where(s => s.Status == SourceStatus.Loaded)
                    .Select(s => s.Source.Path).ToHashSet(StringComparer.Ordinal);
                for (var directory = input; directory != project.Path; directory = ProjectPath.parent(directory))
                    if (DiscoveryPolicy.candidates(directory).Any(inspected.Contains))
                        return Fail("migration.selection", "The requested directory has a nearer configuration outside the selected project.");
            }
            projectPath = project.Path;
            var role = project.Path == found.Discovery.Location ? SourceRole.RootPrimary : SourceRole.MemberPrimary;
            // Only declared base primary documents participate. No loader call can read personal,
            // machine or activated-profile sources during migration.
            if (context is not null && (context.RootPath != found.Discovery.Location
                || !Equals(context.Workspace, found.Discovery.Workspace)
                || context.RootSources.Any(s => s.Source.Role != SourceRole.RootPrimary
                    || !DiscoveryPolicy.candidates(context.RootPath).Contains(s.Source.Path))))
                return Fail("migration.context", "Proposed context must belong to the discovered workspace base primaries.");
            if (context is { ProposedMembers.IsDefault: false })
            {
                var declared = ConfigTree.tryFind(ReaderValues.List(new[] { "workspace", "members" }), found.Discovery.Root.Values)?.Value;
                var memberPaths = declared is ConfigValue.Array declaredArray ? declaredArray.Item.OfType<ConfigValue.String>().Select(s => s.Item) : [];
                if (!context.ProposedMembers.SequenceEqual(memberPaths))
                    return Fail("migration.context", "Proposed members must match the declared candidate workspace.");
            }
            var sources = context is null ? found.Discovery.Root.Sources.ToList() : context.RootSources.ToList();
            if (role == SourceRole.MemberPrimary)
                sources.AddRange(project.Location.Sources.Select(s => Rebind(s, role)));
            var classics = project.Location.Sources.Where(s => s.Source.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && s.Content is SourceContent.Available available
                && available.Item.LegacyFields.Any(f => f.RawPath.Any() && f.Disposition == "normalized")).ToArray();
            if (explicitFile) classics = classics.Where(s => s.Source.Path == input).ToArray();
            if (classics.Length != 1)
                return Fail("migration.classic_required", "Expected exactly one classic JSON project manifest at a supported primary layout.");
            var classic = Rebind(classics[0], role);
            sourcePath = classic.Source.Path;
            targetPath = outputPath is null ? Path.ChangeExtension(sourcePath, ".toml") : ProjectPath.join(start, outputPath);
            var allowed = DiscoveryPolicy.candidates(projectPath).Where(p => p.EndsWith(".toml", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            if (!allowed.Contains(targetPath))
                return Fail("migration.output", "Output must use a supported TOML primary layout at this project location.");
            var target = new SourceIdentity(targetPath, DocumentFormat.Toml, role, 4, null);
            foreach (var path in allowed)
            {
                var candidate = observed.Observe(path);
                if (path != targetPath && !candidate.IsMissing)
                    return Fail("migration.ambiguous_output", "Another TOML primary already exists for this project.");
            }
            var targetEntry = observed.Observe(targetPath);
            if (targetEntry.IsLink || targetEntry.IsDirectory || targetEntry.IsFailure)
                return Fail("migration.output", "Output is not a writable primary file location.");
            var targetSource = targetEntry is ProjectEntry.File file
                ? new SourceInput(target, file.Item, false) : new SourceInput(target, SourceContent.Missing, false);
            if (context?.OriginalTarget is { } original && role == SourceRole.RootPrimary)
            {
                if (original.Source.Path != targetPath || original.Source.Format != DocumentFormat.Toml || original.Source.Role != SourceRole.RootPrimary)
                    return Fail("migration.context", "Original target must identify this root TOML primary.");
                if (storage is not null)
                {
                    var expected = evidence[targetPath];
                    if (expected.Kind == ConfigurationObservationKind.Missing && !original.Content.IsMissing
                        || expected.Kind == ConfigurationObservationKind.File && original.Content is not SourceContent.Available)
                        return Fail("migration.context", "Original target does not match observed target bytes.");
                    if (expected.Kind == ConfigurationObservationKind.File)
                    {
                        var bytes = storage.ReadFile(targetPath);
                        if (storage.Observe(targetPath) != expected) return Fail("migration.stale", "Target changed while planning.");
                        var actual = ConfigurationReader.ReadText(target, TextBuffer.Decode(bytes).Text);
                        if (actual.Content is not SourceContent.Available actualDocument || original.Content is not SourceContent.Available originalDocument
                            || !ProjectMigration.same(actualDocument.Item.Raw, originalDocument.Item.Raw))
                            return Fail("migration.context", "Original target does not match observed target bytes.");
                    }
                }
                targetSource = new SourceInput(target, original.Content, false);
            }
            if (targetSource.Content is SourceContent.Invalid invalid)
            {
                diagnostics.AddRange(invalid.Item);
                return Fail("migration.target_invalid", "Existing TOML is invalid.");
            }
            var raw = targetSource.Content is SourceContent.Available current ? current.Item.Raw : DocumentDefaults.emptyValue;
            var profiles = new ProfileOptions(ProfileSelection.Disabled, DocumentDefaults.profileOptions.MaxDepth, DocumentDefaults.profileOptions.MaxExpansions);
            var baseInput = new ResolutionInput(ListModule.OfSeq(sources), MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
                profiles, role == SourceRole.MemberPrimary ? ProjectScope.Member : ProjectScope.Root, found.Discovery.Workspace, FSharpList<string>.Empty);
            plan = ProjectMigration.planWithContext(classic, target, raw, baseInput);
            diagnostics.AddRange(plan.Diagnostics);
            if (plan.Blocked) return new(this, sourcePath, targetPath, projectPath, plan, null, diagnostics);
            if (role == SourceRole.RootPrimary && context is { ProposedMembers.IsDefault: false })
            {
                // Membership adoption is an explicit composition step after the unchanged loss/conflict mapper.
                // Only this declared field can differ; independent verification still checks every value.
                var key = ReaderValues.List(new[] { "workspace", "members" });
                var value = ConfigValue.NewArray(ReaderValues.List(context.ProposedMembers.Select(ConfigValue.NewString)));
                var currentMembers = ConfigTree.tryFind(key, plan.Candidate)?.Value;
                if (currentMembers is null || !ProjectMigration.same(currentMembers, value))
                {
                    var composedEdits = plan.Edits.Where(e => !e.KeyPath.SequenceEqual(key)).Append(new MigrationEdit(key, value));
                    plan = new MigrationPlan(plan.Source, plan.Target, ConfigTree.set(key, value, plan.Candidate), ReaderValues.List(composedEdits),
                        plan.Ledger, plan.Conflicts, plan.Diagnostics, plan.Blocked);
                }
            }
            TextBuffer buffer;
            if (storage is not null)
            {
                var expected = evidence[targetPath];
                var bytes = expected.Kind == ConfigurationObservationKind.File ? storage.ReadFile(targetPath) : [];
                if (storage.Observe(targetPath) != expected) return Fail("migration.stale", "Target changed while planning.");
                buffer = TextBuffer.Decode(bytes);
            }
            else
            {
                // A parsed-only host can inspect the complete logical edits. It has no write capability
                // and cannot promise original text bytes. Rendering still checks representability.
                var rendered = "";
                if (raw is ConfigValue.Object fields)
                    foreach (var field in fields.Item)
                    {
                        var result = TomlEditor.Edit(rendered, [field.Key], field.Value);
                        if (!result.Success) return Fail("migration.render", "Existing target cannot be represented by the TOML editor.");
                        rendered = result.Text;
                    }
                buffer = new(rendered, new UTF8Encoding(false, true), []);
            }
            var text = buffer.Text;
            foreach (var edit in plan.Edits)
            {
                var result = TomlEditor.Edit(text, edit.KeyPath.ToArray(), edit.Value);
                if (!result.Success) return Fail("migration.render", "A mapped value cannot be represented by the TOML editor.");
                text = result.Text;
            }
            var renderedSource = ConfigurationReader.ReadText(target, text);
            if (renderedSource.Content is not SourceContent.Available renderedDocument)
                return Fail("migration.render", "Candidate TOML failed canonical reading.");
            verification = ProjectMigration.verify(classic, target, renderedDocument.Item.Raw, baseInput, projectPath);
            diagnostics.AddRange(verification.Diagnostics);
            if (!verification.Verified) return Fail("migration.verification", "Candidate changes project values or resolved path meaning.");
            var changed = plan.Edits.Any();
            ConfigurationTransactionPlan? transaction = null;
            if (storage is not null && context is null)
            {
                // Directory and missing-primary evidence captures topology drift in addition to exact source bytes.
                transaction = new ConfigurationTransactionPlan(evidence.Values,
                    [new ConfigurationFileChange(targetPath, evidence[targetPath], buffer.Encode(text))]);
            }
            return new(this, sourcePath, targetPath, projectPath, plan, verification, diagnostics, changed, transaction);
        }
        catch (MigrationObservationConflictException) { return Fail("migration.stale", "Project evidence changed while planning."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Fail("migration.io", "Unable to read or render the project migration. Check paths and access permissions."); }
    }

    public ConfigurationTransactionResult Apply(ProjectMigrationPreview preview) => ReferenceEquals(preview.Owner, this)
        && preview.CanApply && preview.Transaction is { } transaction && storage is not null
        ? new ConfigurationTransaction(storage).Apply(transaction)
        : new(ConfigurationTransactionStatus.IoFailure, "migration.unverified_plan", [], []);

    private static SourceInput Rebind(SourceInput input, SourceRole role) => new(
        new SourceIdentity(input.Source.Path, input.Source.Format, role, input.Source.Order, null), input.Content, input.Required);

    private static ConfigDiagnostic Diagnostic(string path, string code, string message) => new(code, message, DiagnosticSeverity.Error,
        new SourceIdentity(path, DocumentFormat.Memory, SourceRole.RootPrimary, 0, null), FSharpList<string>.Empty, null);

    private sealed class MigrationObservationConflictException : IOException;

    private sealed class MigrationTree(IProjectTree inner, IConfigurationTransactionHost? storage,
        Dictionary<string, ConfigurationObservation> evidence) : IProjectTree
    {
        public string Boundary => inner.Boundary;
        public ProjectEntry Observe(string path)
        {
            if (!ProjectPath.contains(Boundary, path)) return ProjectEntry.NewFailure("Outside authorized project tree");
            if (storage is null) return inner.Observe(path);
            var before = storage.Observe(path);
            var entry = inner.Observe(path);
            if (entry is ProjectEntry.File && before.Kind == ConfigurationObservationKind.File)
            {
                var bytes = storage.ReadFile(path);
                if (NativeConfigurationTransactionHost.Hash(bytes) != before.Identity) throw new MigrationObservationConflictException();
                entry = ProjectEntry.NewFile(ConfigurationReader.ReadText(path, TextBuffer.Decode(bytes).Text).Content);
            }
            if (storage.Observe(path) != before || evidence.TryGetValue(path, out var prior) && prior != before)
                throw new MigrationObservationConflictException();
            evidence[path] = before;
            return entry;
        }
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
            var utf8 = new UTF8Encoding(false, true);
            return new(utf8.GetString(bytes), utf8, []);
        }
    }
}
