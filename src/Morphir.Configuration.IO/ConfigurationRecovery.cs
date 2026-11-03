// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Morphir.Configuration.IO;

/// <summary>Metadata only. No target, stage or backup bytes are exposed.</summary>
public sealed record ConfigurationRecoveryInspection(
    string JournalPath, string? TransactionId, bool Valid, bool Committed,
    ImmutableArray<string> Targets, ImmutableArray<ConfigurationArtifact> Artifacts,
    ImmutableArray<string> UnresolvedPaths, string Code)
{
    public ImmutableArray<string> UncertainDirectories { get; init; } = [];
}

internal sealed record RecoveryEntry(string Target, ConfigurationObservationKind OriginalKind,
    string OriginalIdentity, string CandidateIdentity, string Stage, string? Backup);

internal sealed record RecoveryDocument(string TransactionId, bool Committed,
    ImmutableArray<string> Guards, ImmutableArray<RecoveryEntry> Entries,
    ImmutableArray<ConfigurationArtifact> Artifacts)
{
    internal ImmutableArray<string> PendingDirectories { get; init; } = [];

    // A committed witness must remain above every destination while cleanup runs.
    internal string MetadataDestination => ProjectPath.join(Guards.First(g => Entries.All(e => ProjectPath.contains(g, e.Target))), ".morphir-transaction");

    internal static string ArtifactPath(string destination, string transactionId, string role) =>
        ProjectPath.join(ProjectPath.parent(destination), ".morphir-edit-" + transactionId + "-"
            + NativeConfigurationTransactionHost.Hash(Encoding.UTF8.GetBytes(destination))[..16] + "-" + role);

    internal static bool IsJournal(string path) => Path.GetFileName(path).Contains("-journal", StringComparison.Ordinal)
        && Path.GetFileName(path).StartsWith(".morphir-edit-", StringComparison.Ordinal);

    internal byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", 2);
            writer.WriteString("transactionId", TransactionId); writer.WriteBoolean("committed", Committed);
            writer.WriteStartArray("pendingDirectories"); foreach (var path in PendingDirectories) writer.WriteStringValue(path); writer.WriteEndArray();
            writer.WriteStartArray("guards"); foreach (var path in Guards) writer.WriteStringValue(path); writer.WriteEndArray();
            writer.WriteStartArray("entries");
            foreach (var entry in Entries)
            {
                writer.WriteStartObject(); writer.WriteString("target", entry.Target);
                writer.WriteString("originalKind", entry.OriginalKind.ToString()); writer.WriteString("originalIdentity", entry.OriginalIdentity);
                writer.WriteString("candidateIdentity", entry.CandidateIdentity); writer.WriteString("stage", entry.Stage);
                writer.WriteString("backup", entry.Backup); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartArray("artifacts");
            foreach (var artifact in Artifacts)
            {
                writer.WriteStartObject(); writer.WriteString("path", artifact.Path); writer.WriteString("kind", artifact.Kind.ToString()); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static RecoveryDocument Decode(byte[] bytes, string journalPath)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = json.RootElement;
        if (!root.GetProperty("version").TryGetInt32(out var version) || version != 2) throw new InvalidDataException("Unknown journal version");
        static string Text(JsonElement value) => value.GetString() ?? throw new InvalidDataException("Missing journal string");
        var id = Text(root.GetProperty("transactionId"));
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid journal owner");
        var pendingDirectories = root.TryGetProperty("pendingDirectories", out var pending)
            ? pending.EnumerateArray().Select(Text).ToImmutableArray() : [];
        var guards = root.GetProperty("guards").EnumerateArray().Select(Text).ToImmutableArray();
        var entries = root.GetProperty("entries").EnumerateArray().Select(e => new RecoveryEntry(
            Text(e.GetProperty("target")), Enum.Parse<ConfigurationObservationKind>(Text(e.GetProperty("originalKind"))),
            Text(e.GetProperty("originalIdentity")), Text(e.GetProperty("candidateIdentity")),
            Text(e.GetProperty("stage")), e.GetProperty("backup").ValueKind == JsonValueKind.Null ? null : Text(e.GetProperty("backup")))).ToImmutableArray();
        var artifacts = root.GetProperty("artifacts").EnumerateArray().Select(a => new ConfigurationArtifact(
            Text(a.GetProperty("path")), Enum.Parse<ConfigurationArtifactKind>(Text(a.GetProperty("kind"))), id)).ToImmutableArray();
        static bool Canonical(string path) => ProjectPath.isAbsolute(path) && ProjectPath.normalize(path) == path;
        static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
        bool Owned(string path) => Canonical(path) && Path.GetFileName(path).StartsWith(".morphir-edit-" + id + "-", StringComparison.Ordinal)
            && guards.Contains(ProjectPath.parent(path), StringComparer.Ordinal);
        if (guards.IsDefaultOrEmpty || guards.Any(p => !Canonical(p)) || entries.IsDefaultOrEmpty
            || entries.Select(e => e.Target).Distinct(StringComparer.Ordinal).Count() != entries.Length
            || entries.Any(e => !Canonical(e.Target) || !Hash(e.CandidateIdentity)
                || e.OriginalKind is not (ConfigurationObservationKind.File or ConfigurationObservationKind.Missing)
                || e.OriginalKind == ConfigurationObservationKind.File && (!Hash(e.OriginalIdentity) || e.Backup is null)
                || e.OriginalKind == ConfigurationObservationKind.Missing && (e.OriginalIdentity.Length != 0 || e.Backup is not null)
                || !Owned(e.Stage) || e.Stage != ArtifactPath(e.Target, id, "stage")
                || e.Backup is not null && (!Owned(e.Backup) || e.Backup != ArtifactPath(e.Target, id, "backup"))
                || !guards.Contains(ProjectPath.parent(e.Target), StringComparer.Ordinal))
            || pendingDirectories.Any(p => !Canonical(p) || !entries.Any(e => e.Target != p && ProjectPath.contains(p, e.Target)))
            || artifacts.Any(a => !Enum.IsDefined(a.Kind))
            || artifacts.Any(a => a.Kind == ConfigurationArtifactKind.Directory
                ? !Canonical(a.Path) || !guards.Any(g => ProjectPath.contains(a.Path, g))
                : !Owned(a.Path))
            || !Owned(journalPath) || !IsJournal(journalPath))
            throw new InvalidDataException("Invalid journal ownership");
        return new(id, root.GetProperty("committed").GetBoolean(), guards, entries, artifacts) { PendingDirectories = pendingDirectories };
    }
}

public sealed partial class ConfigurationTransaction
{
    /// <summary>Inspect durable metadata without modifying any file. Invalid or linked journals
    /// remain explicit recovery blockers. This method never returns raw configuration bytes.</summary>
    public ConfigurationRecoveryInspection InspectRecovery(string journalPath)
    {
        if (string.IsNullOrWhiteSpace(journalPath) || !ProjectPath.isAbsolute(journalPath) || ProjectPath.normalize(journalPath) != journalPath)
            return new(journalPath ?? "", null, false, false, [], [], [], "config.recovery_invalid_journal");
        try
        {
            var document = ReadRecovery(journalPath);
            document = LatestRecoveryDocument(document);
            var artifacts = RecoveryArtifacts(document, journalPath);
            var unresolved = RecoveryUnresolved(document, artifacts);
            return new(journalPath, document.TransactionId, true, document.Committed, document.Entries.Select(e => e.Target).ToImmutableArray(),
                Retained(artifacts), unresolved, unresolved.Length == 0 ? "config.recovery_inspected" : "config.recovery_unresolved")
            { UncertainDirectories = document.PendingDirectories.Where(p => host.Observe(p).Kind != ConfigurationObservationKind.Missing).ToImmutableArray() };
        }
        catch (Exception error) when (IsRecoveryError(error))
        {
            return new(journalPath, null, false, false, [], [new(journalPath, ConfigurationArtifactKind.Journal, "unknown")],
                [journalPath], "config.recovery_invalid_journal");
        }
    }

    /// <summary>Find relevant durable recovery state through the host's bounded journal index.</summary>
    public ImmutableArray<ConfigurationRecoveryInspection> InspectPending(IEnumerable<string> paths) =>
        host.FindRecoveryJournals(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(InspectRecovery).ToImmutableArray();

    /// <summary>Explicitly restore an interrupted operation, or finish cleanup of a durably
    /// committed operation. User changes and unverifiable artifacts are preserved and reported.</summary>
    public ConfigurationTransactionResult Recover(string journalPath) => Recover(journalPath, []);

    /// <summary>Explicitly preserve reviewed directories whose creation receipt was interrupted.
    /// Such directories are never claimed or removed by transaction cleanup.</summary>
    public ConfigurationTransactionResult Recover(string journalPath, IEnumerable<string> preserveUncertainDirectories)
    {
        var preserved = preserveUncertainDirectories.ToHashSet(StringComparer.Ordinal);
        var inspection = InspectRecovery(journalPath);
        ConfigurationTransactionResult Required(string code, IEnumerable<string>? restored = null) =>
            new(ConfigurationTransactionStatus.RecoveryRequired, code, inspection.Targets, inspection.Artifacts)
            { RestoredPaths = (restored ?? []).ToImmutableArray(), UnresolvedPaths = inspection.UnresolvedPaths.Length > 0 ? inspection.UnresolvedPaths : inspection.Targets };
        if (!inspection.Valid) return Required(inspection.Code);
        try
        {
            var initial = ReadRecovery(journalPath);
            using var coordination = host.Acquire(initial.Guards.Concat(initial.Entries.Select(e => e.Target)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            inspection = InspectRecovery(journalPath);
            if (!inspection.Valid) return Required(inspection.Code);
            var document = LatestRecoveryDocument(ReadRecovery(journalPath));
            if (preserved.Any(p => !document.PendingDirectories.Contains(p, StringComparer.Ordinal)
                || host.Observe(p).Kind != ConfigurationObservationKind.Directory)) return Required("config.recovery_invalid_options");
            if (RecoveryUnresolved(document, RecoveryArtifacts(document, journalPath), preserved).Length > 0) return Required(inspection.Code);
            if (!SameOperation(initial, document)) return Required("config.recovery_changed");
            if (document.Guards.Any(g => host.EnumerateTransactionArtifacts(g, document.TransactionId) is null))
                return Required("config.recovery_host_capability");
            var restored = new List<string>();
            var unresolved = new List<string>();
            foreach (var entry in document.Entries.Reverse())
            {
                var expected = new ConfigurationObservation(entry.Target, entry.OriginalKind, entry.OriginalIdentity);
                var candidate = new ConfigurationObservation(entry.Target, ConfigurationObservationKind.File, entry.CandidateIdentity);
                try
                {
                    var current = host.Observe(entry.Target);
                    if (document.Committed)
                    {
                        if (current != candidate) unresolved.Add(entry.Target);
                        continue;
                    }
                    if (current == expected) { restored.Add(entry.Target); continue; }
                    if (current != candidate) { unresolved.Add(entry.Target); continue; }
                    fault?.Invoke(ConfigurationTransactionPhase.BeforeRollback, entry.Target);
                    if (host.Observe(entry.Target) != candidate) { unresolved.Add(entry.Target); continue; }
                    if (entry.Backup is null) host.DeleteFile(entry.Target);
                    else
                    {
                        if (host.Observe(entry.Backup) != new ConfigurationObservation(entry.Backup, ConfigurationObservationKind.File, entry.OriginalIdentity))
                        { unresolved.Add(entry.Target); continue; }
                        host.Install(entry.Backup, entry.Target, true);
                    }
                    fault?.Invoke(ConfigurationTransactionPhase.AfterRollback, entry.Target);
                    if (host.Observe(entry.Target) == expected) restored.Add(entry.Target); else unresolved.Add(entry.Target);
                }
                catch (Exception error) when (IsIo(error))
                {
                    if (host.Observe(entry.Target) == expected) restored.Add(entry.Target); else unresolved.Add(entry.Target);
                }
            }
            var artifacts = RecoveryArtifacts(document, journalPath).ToList();
            if (unresolved.Count > 0 || !TryCleanup(artifacts, document.Committed, document))
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.recovery_required", inspection.Targets, Retained(artifacts))
                { RestoredPaths = restored.ToImmutableArray(), UnresolvedPaths = unresolved.Count > 0 ? unresolved.ToImmutableArray() : inspection.Targets };
            return new(document.Committed ? ConfigurationTransactionStatus.Written : ConfigurationTransactionStatus.RolledBack,
                document.Committed ? "config.recovery_committed" : "config.recovery_rolled_back", inspection.Targets, [])
            { RestoredPaths = restored.ToImmutableArray(), PreservedDirectories = preserved.Order(StringComparer.Ordinal).ToImmutableArray() };
        }
        catch (Exception error) when (IsRecoveryError(error)) { return Required("config.recovery_io"); }
    }

    private RecoveryDocument LatestRecoveryDocument(RecoveryDocument initial)
    {
        foreach (var role in new[] { "journal-committed", "journal-ready", "journal-directories" })
        {
            var path = RecoveryDocument.ArtifactPath(initial.MetadataDestination, initial.TransactionId, role);
            if (host.Observe(path).Kind == ConfigurationObservationKind.Missing) continue;
            var snapshot = ReadRecovery(path);
            if (!SameOperation(initial, snapshot) || (role == "journal-committed") != snapshot.Committed)
                throw new InvalidDataException("Invalid recovery snapshot");
            return snapshot;
        }
        return initial;
    }

    private RecoveryDocument ReadRecovery(string path)
    {
        var observed = host.Observe(path);
        if (observed.Kind != ConfigurationObservationKind.File) throw new InvalidDataException("Journal is not a regular file");
        var bytes = host.ReadFile(path);
        if (NativeConfigurationTransactionHost.Hash(bytes) != observed.Identity || host.Observe(path) != observed)
            throw new InvalidDataException("Journal changed");
        return RecoveryDocument.Decode(bytes, path);
    }

    private ImmutableArray<ConfigurationArtifact> RecoveryArtifacts(RecoveryDocument document, string journalPath)
    {
        var known = document.Artifacts.Append(new(journalPath, ConfigurationArtifactKind.Journal, document.TransactionId)).ToList();
        foreach (var directory in document.Guards)
        {
            var found = host.EnumerateTransactionArtifacts(directory, document.TransactionId);
            if (found is null) continue;
            foreach (var path in found)
                known.Add(new(path, RecoveryDocument.IsJournal(path) ? ConfigurationArtifactKind.Journal
                    : path.EndsWith("-backup", StringComparison.Ordinal) ? ConfigurationArtifactKind.Backup : ConfigurationArtifactKind.Stage, document.TransactionId));
        }
        return known.Distinct().ToImmutableArray();
    }

    private ImmutableArray<string> RecoveryUnresolved(RecoveryDocument document, IEnumerable<ConfigurationArtifact> artifacts,
        IReadOnlySet<string>? preservedDirectories = null)
    {
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in document.PendingDirectories)
        {
            var observed = host.Observe(path);
            if (observed.Kind != ConfigurationObservationKind.Missing
                && (observed.Kind != ConfigurationObservationKind.Directory || preservedDirectories?.Contains(path) != true)) unresolved.Add(path);
        }
        foreach (var entry in document.Entries)
        {
            var current = host.Observe(entry.Target);
            var original = new ConfigurationObservation(entry.Target, entry.OriginalKind, entry.OriginalIdentity);
            var candidate = new ConfigurationObservation(entry.Target, ConfigurationObservationKind.File, entry.CandidateIdentity);
            if (document.Committed ? current != candidate : current != candidate && current != original) unresolved.Add(entry.Target);
            if (!document.Committed && current == candidate && entry.Backup is not null
                && host.Observe(entry.Backup) != new ConfigurationObservation(entry.Backup, ConfigurationObservationKind.File, entry.OriginalIdentity)) unresolved.Add(entry.Target);
        }
        foreach (var artifact in artifacts)
        {
            var observed = host.Observe(artifact.Path);
            if (observed.Kind == ConfigurationObservationKind.Missing) continue;
            if (artifact.Kind == ConfigurationArtifactKind.Directory)
            {
                if (observed.Kind != ConfigurationObservationKind.Directory) unresolved.Add(artifact.Path);
                continue;
            }
            if (observed.Kind != ConfigurationObservationKind.File) { unresolved.Add(artifact.Path); continue; }
            if (artifact.Kind == ConfigurationArtifactKind.Journal)
            {
                try { if (!SameOperation(document, ReadRecovery(artifact.Path))) unresolved.Add(artifact.Path); }
                catch (Exception error) when (IsRecoveryError(error)) { unresolved.Add(artifact.Path); }
                continue;
            }
            var entry = document.Entries.FirstOrDefault(e => e.Stage == artifact.Path || e.Backup == artifact.Path);
            if (entry is null || observed.Identity != (entry.Stage == artifact.Path ? entry.CandidateIdentity : entry.OriginalIdentity)) unresolved.Add(artifact.Path);
        }
        return unresolved.Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static bool SameOperation(RecoveryDocument first, RecoveryDocument second) => first.TransactionId == second.TransactionId
        && first.Guards.SequenceEqual(second.Guards)
        && first.Entries.SequenceEqual(second.Entries);
    private static bool IsRecoveryError(Exception error) => IsIo(error) || error is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or OverflowException;
}
