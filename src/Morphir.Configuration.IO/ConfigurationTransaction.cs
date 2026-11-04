using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Morphir.Configuration.IO;

public enum ConfigurationObservationKind { Missing, File, Directory, Link }

/// <summary>Content or directory-inventory evidence, including an observed absence.</summary>
public sealed record ConfigurationObservation(string Path, ConfigurationObservationKind Kind, string Identity);

/// <summary>One immutable candidate. The expected target is always a transaction precondition.</summary>
public sealed class ConfigurationFileChange
{
    public string Path { get; }
    public ConfigurationObservation Expected { get; }
    public ImmutableArray<byte> Content { get; }

    public ConfigurationFileChange(string path, ConfigurationObservation expected, IEnumerable<byte> content)
    {
        Path = path;
        Expected = expected;
        Content = content.ToImmutableArray();
    }
}

public sealed class ConfigurationTransactionPlan
{
    public ImmutableArray<ConfigurationObservation> Observations { get; }
    public ImmutableArray<ConfigurationFileChange> Changes { get; }

    public ConfigurationTransactionPlan(IEnumerable<ConfigurationObservation> observations, IEnumerable<ConfigurationFileChange> changes)
    {
        Observations = observations.ToImmutableArray();
        Changes = changes.ToImmutableArray();
    }
}

public enum ConfigurationTransactionStatus { Written, Unchanged, Conflict, IoFailure, RolledBack, RecoveryRequired }
public enum ConfigurationTransactionPhase { BeforeStage, BeforeInstall, AfterInstall, BeforeRollback, AfterRollback, BeforeCleanup }
public enum ConfigurationArtifactKind { Stage, Backup, Journal, Directory }
public sealed record ConfigurationArtifact(string Path, ConfigurationArtifactKind Kind, string TransactionId);
/// <summary>A host operation failed after creating artifacts it could not remove.
/// Only artifacts owned by the current transaction may be cleaned by application.</summary>
public sealed class ConfigurationArtifactFailureException : IOException
{
    public ImmutableArray<ConfigurationArtifact> Artifacts { get; }

    public ConfigurationArtifactFailureException(IEnumerable<ConfigurationArtifact> artifacts, Exception? innerException = null)
        : base("Configuration host retained artifacts after an I/O failure", innerException) => Artifacts = artifacts.ToImmutableArray();
}

public sealed record ConfigurationTransactionResult(
    ConfigurationTransactionStatus Status,
    string Code,
    ImmutableArray<string> Targets,
    ImmutableArray<ConfigurationArtifact> Artifacts)
{
    public ImmutableArray<string> RestoredPaths { get; init; } = [];
    public ImmutableArray<string> UnresolvedPaths { get; init; } = [];
    public ImmutableArray<string> PreservedDirectories { get; init; } = [];
}

/// <summary>Explicit host boundary. Acquire must serialize overlapping paths across its writers;
/// Install must atomically replace one file, never a group of files.</summary>
public interface IConfigurationTransactionHost
{
    /// <summary>Explicit opt-in to persistent journal discovery and complete artifact enumeration.
    /// Legacy/read-only hosts remain source compatible but cannot perform writes until qualified.</summary>
    bool SupportsDurableRecovery => false;
    /// <summary>Paths are canonical absolute paths. File identities are uppercase SHA-256 hex of exact bytes.
    /// Directory identities must be stable inventories excluding only the supplied owned paths.</summary>
    ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null);
    byte[] ReadFile(string path);
    IDisposable Acquire(IReadOnlyList<string> paths);
    IReadOnlyList<string> CreateParentDirectories(string destination);
    /// <summary>Hosts that can retain partially created directories report them with this transaction's owner.</summary>
    IReadOnlyList<string> CreateParentDirectories(string destination, string transactionId) => CreateParentDirectories(destination);
    string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role);
    void Install(string stagedPath, string destination, bool overwrite);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    /// <summary>Durable hosts enumerate metadata journals without following links. Older custom
    /// hosts can still apply plans, but must implement enumeration for automatic overlap guards.</summary>
    IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => [];
    /// <summary>Null means this host cannot prove crash-artifact enumeration completeness.</summary>
    IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => null;
}

/// <summary>Preconditioned configuration-file application. Each installation is atomic;
/// a transaction with multiple destinations is not group atomic.</summary>
public sealed partial class ConfigurationTransaction
{
    private readonly IConfigurationTransactionHost host;
    private readonly Action<ConfigurationTransactionPhase, string>? fault;

    public ConfigurationTransaction(IConfigurationTransactionHost? host = null, Action<ConfigurationTransactionPhase, string>? fault = null)
    {
        this.host = host ?? new NativeConfigurationTransactionHost();
        this.fault = fault;
    }

    public static ConfigurationObservation Observe(string path) => new NativeConfigurationTransactionHost().Observe(path);

    /// <summary>Stable owned-artifact naming for durable storage adapters. Recovery validates
    /// the transaction owner, destination anchor and role rather than trusting a prefix alone.</summary>
    public static string GetArtifactPath(string destination, string transactionId, string role) =>
        RecoveryDocument.ArtifactPath(destination, transactionId, role);

    public ConfigurationTransactionResult Apply(ConfigurationTransactionPlan plan)
    {
        var targets = plan.Changes.Select(c => c.Path).ToImmutableArray();
        if (!host.SupportsDurableRecovery)
            return new(ConfigurationTransactionStatus.IoFailure, "config.recovery_host_capability", targets, []);
        if (targets.Distinct(StringComparer.Ordinal).Count() != targets.Length || plan.Changes.Any(c => c.Path != c.Expected.Path
            || !ProjectPath.isAbsolute(c.Path) || ProjectPath.normalize(c.Path) != c.Path
            || c.Expected.Kind is not (ConfigurationObservationKind.File or ConfigurationObservationKind.Missing)))
            return new(ConfigurationTransactionStatus.IoFailure, "config.edit_invalid_plan", targets, []);
        var observations = plan.Observations.Concat(plan.Changes.Select(c => c.Expected)).Distinct().ToArray();
        var paths = observations.Select(o => o.Path).Concat(targets).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var transactionId = Guid.NewGuid().ToString("N");
        var artifacts = new List<ConfigurationArtifact>();
        var artifactEvidence = new Dictionary<string, ConfigurationObservation>(StringComparer.Ordinal);
        var stageAttempts = new Dictionary<string, ConfigurationArtifactKind>(StringComparer.Ordinal);
        var partialArtifacts = new HashSet<string>(StringComparer.Ordinal);
        var plannedDirectories = new HashSet<string>(StringComparer.Ordinal);
        var unverified = new HashSet<string>(StringComparer.Ordinal);
        var attempted = new List<(ConfigurationFileChange Change, string? Backup)>();
        var staged = new List<(ConfigurationFileChange Change, string Stage, string? Backup)>();
        var ownedDirectories = new Dictionary<string, ConfigurationObservation>(StringComparer.Ordinal);
        RecoveryDocument? document = null;
        var restored = new List<string>();
        var unresolved = new List<string>();
        bool ObservationsMatch()
        {
            var excluded = artifacts.Select(a => a.Path)
                .Concat(attempted.Where(a => a.Change.Expected.Kind == ConfigurationObservationKind.Missing).Select(a => a.Change.Path)).ToArray();
            return observations.All(o => host.Observe(o.Path, excluded) ==
                (attempted.FirstOrDefault(a => a.Change.Path == o.Path).Change is { } installed ? CandidateObservation(installed)
                    : ownedDirectories.GetValueOrDefault(o.Path, o)));
        }
        bool StagesMatch() => staged.All(s =>
            (attempted.Any(a => a.Change.Path == s.Change.Path)
                || host.Observe(s.Stage) == new ConfigurationObservation(s.Stage, ConfigurationObservationKind.File, CandidateObservation(s.Change).Identity))
            && (s.Backup is null || host.Observe(s.Backup) == new ConfigurationObservation(s.Backup, ConfigurationObservationKind.File, s.Change.Expected.Identity)));
        static ConfigurationArtifactKind RoleKind(string role) => role.StartsWith("journal", StringComparison.Ordinal)
            ? ConfigurationArtifactKind.Journal : role == "backup" ? ConfigurationArtifactKind.Backup : ConfigurationArtifactKind.Stage;
        void Unverified(string path)
        {
            path ??= "[null artifact path]";
            unverified.Add(path);
            unresolved.Add(path);
        }
        string StageOwned(string destination, ReadOnlyMemory<byte> content, string role)
        {
            var expected = GetArtifactPath(destination, transactionId, role);
            if (host.Observe(expected).Kind != ConfigurationObservationKind.Missing)
            { Unverified(expected); throw new OwnershipException(); }
            var kind = RoleKind(role);
            stageAttempts[expected] = kind;
            var returned = host.Stage(destination, content, transactionId, role);
            if (returned != expected)
            {
                Unverified(returned);
                if (host.Observe(expected).Kind != ConfigurationObservationKind.Missing) Unverified(expected);
                throw new OwnershipException();
            }
            var observed = host.Observe(expected);
            var identity = NativeConfigurationTransactionHost.Hash(content.Span);
            if (observed != new ConfigurationObservation(expected, ConfigurationObservationKind.File, identity))
            { Unverified(expected); throw new OwnershipException(); }
            artifacts.Add(new(expected, kind, transactionId));
            artifactEvidence[expected] = observed;
            return expected;
        }
        void RegisterDirectory(string path)
        {
            if (!plannedDirectories.Contains(path) || host.Observe(path).Kind != ConfigurationObservationKind.Directory)
            { Unverified(path); throw new OwnershipException(); }
            if (!artifacts.Any(a => a.Path == path)) artifacts.Add(new(path, ConfigurationArtifactKind.Directory, transactionId));
        }
        void ReportArtifacts(ConfigurationArtifactFailureException failure)
        {
            foreach (var artifact in failure.Artifacts)
            {
                if (artifact.TransactionId != transactionId)
                { Unverified(artifact.Path); continue; }
                if (artifact.Kind == ConfigurationArtifactKind.Directory && plannedDirectories.Contains(artifact.Path)
                    && host.Observe(artifact.Path).Kind == ConfigurationObservationKind.Directory)
                { RegisterDirectory(artifact.Path); continue; }
                if (stageAttempts.TryGetValue(artifact.Path, out var kind) && kind == artifact.Kind
                    && host.Observe(artifact.Path) is { Kind: ConfigurationObservationKind.File } observed)
                {
                    if (!artifacts.Any(a => a.Path == artifact.Path))
                    {
                        artifacts.Add(new(artifact.Path, kind, transactionId));
                        artifactEvidence[artifact.Path] = observed;
                        partialArtifacts.Add(artifact.Path);
                    }
                    continue;
                }
                Unverified(artifact.Path);
            }
        }
        void RecordRecoveryEvidence()
        {
            if (document is null) return;
            document = document with
            {
                Committed = false,
                Artifacts = artifacts.Where(a => !unverified.Contains(a.Path)).ToImmutableArray(),
                UnverifiedPaths = unverified.Order(StringComparer.Ordinal).ToImmutableArray()
            };
            try { StageOwned(document.MetadataDestination, document.Encode(), "journal-recovery"); }
            catch (ConfigurationArtifactFailureException failure) { ReportArtifacts(failure); }
            catch (Exception journalError) when (IsIo(journalError)) { }
        }
        IDisposable? coordination = null;
        try
        {
            coordination = host.Acquire(paths);
            if (observations.Where(o => o.Kind == ConfigurationObservationKind.Directory).Select(o => o.Path)
                .Concat(targets.Select(ProjectPath.parent)).Distinct(StringComparer.Ordinal)
                .Any(directory => host.EnumerateTransactionArtifacts(directory, transactionId) is null))
                return new(ConfigurationTransactionStatus.IoFailure, "config.recovery_host_capability", targets, []);
            var pending = InspectPending(paths);
            if (pending.Length > 0)
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_pending_recovery", targets,
                    pending.SelectMany(p => p.Artifacts).Distinct().ToImmutableArray())
                { UnresolvedPaths = pending.SelectMany(p => p.UnresolvedPaths.Length > 0 ? p.UnresolvedPaths : p.Targets).Distinct().ToImmutableArray() };
            if (!ObservationsMatch())
                return new(ConfigurationTransactionStatus.Conflict, "config.edit_conflict", targets, []);
            var changes = plan.Changes.Where(c => c.Expected.Kind != ConfigurationObservationKind.File
                || !host.ReadFile(c.Path).AsSpan().SequenceEqual(c.Content.AsSpan())).ToArray();
            if (changes.Length == 0) return new(ConfigurationTransactionStatus.Unchanged, "config.edit_unchanged", targets, []);
            foreach (var change in changes)
            {
                for (var parent = ProjectPath.parent(change.Path); ; parent = ProjectPath.parent(parent))
                {
                    var observed = host.Observe(parent);
                    if (observed.Kind == ConfigurationObservationKind.Directory) break;
                    if (observed.Kind != ConfigurationObservationKind.Missing) throw new IOException("A target parent is not a regular directory");
                    plannedDirectories.Add(parent);
                    if (ProjectPath.parent(parent) == parent) throw new IOException("No existing parent directory");
                }
            }
            var common = ProjectPath.parent(changes[0].Path);
            foreach (var change in changes)
                while (!ProjectPath.contains(common, change.Path)) common = ProjectPath.parent(common);
            while (host.Observe(common).Kind == ConfigurationObservationKind.Missing) common = ProjectPath.parent(common);
            if (host.Observe(common).Kind != ConfigurationObservationKind.Directory) throw new IOException("No regular common ancestor");
            var guards = observations.Where(o => o.Kind == ConfigurationObservationKind.Directory
                    && changes.Any(c => ProjectPath.contains(o.Path, c.Path))).Select(o => o.Path)
                .Concat(changes.Select(c => ProjectPath.parent(c.Path))).Append(common)
                .Distinct(StringComparer.Ordinal).OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal).ToImmutableArray();
            var entries = changes.Select(c => new RecoveryEntry(c.Path, c.Expected.Kind, c.Expected.Identity, CandidateObservation(c).Identity,
                RecoveryDocument.ArtifactPath(c.Path, transactionId, "stage"),
                c.Expected.Kind == ConfigurationObservationKind.File ? RecoveryDocument.ArtifactPath(c.Path, transactionId, "backup") : null)).ToImmutableArray();
            var plannedArtifacts = entries.SelectMany(e => e.Backup is null
                ? new[] { new ConfigurationArtifact(e.Stage, ConfigurationArtifactKind.Stage, transactionId) }
                : new[] { new ConfigurationArtifact(e.Stage, ConfigurationArtifactKind.Stage, transactionId), new ConfigurationArtifact(e.Backup, ConfigurationArtifactKind.Backup, transactionId) })
                .Concat(guards.Select(g => new ConfigurationArtifact(RecoveryDocument.ArtifactPath(ProjectPath.join(g, ".morphir-transaction"), transactionId, "journal"), ConfigurationArtifactKind.Journal, transactionId)))
                .Concat(artifacts).ToImmutableArray();
            document = new(transactionId, false, guards, entries, plannedArtifacts)
            {
                PendingDirectories = plannedDirectories.Order(StringComparer.Ordinal).ToImmutableArray(),
                PlannedDirectories = plannedDirectories.Order(StringComparer.Ordinal).ToImmutableArray()
            };
            // Write-ahead metadata is durable before any candidate or original backup is staged.
            // The common target ancestor is first, so even an interrupted setup blocks its descendants.
            foreach (var directory in guards.OrderBy(g => g == common ? 0 : 1).ThenBy(g => g, StringComparer.Ordinal))
            {
                if (host.Observe(directory).Kind == ConfigurationObservationKind.Missing) continue;
                StageOwned(ProjectPath.join(directory, ".morphir-transaction"), document.Encode(), "journal");
            }
            foreach (var change in changes)
            {
                var created = host.CreateParentDirectories(change.Path, transactionId);
                foreach (var directory in created) RegisterDirectory(directory);
                foreach (var directory in created)
                    ownedDirectories[directory] = host.Observe(directory, artifacts.Select(a => a.Path).ToArray());
            }
            document = document with
            {
                Artifacts = artifacts.ToImmutableArray(),
                PendingDirectories = plannedDirectories.Where(p => !artifacts.Any(a => a.Kind == ConfigurationArtifactKind.Directory && a.Path == p)
                    && host.Observe(p).Kind != ConfigurationObservationKind.Missing).Order(StringComparer.Ordinal).ToImmutableArray()
            };
            if (document.PendingDirectories.Length > 0) throw new IOException("Directory creation ownership is uncertain");
            StageOwned(document.MetadataDestination, document.Encode(), "journal-directories");
            foreach (var directory in guards.Where(g => !artifacts.Any(a => a.Kind == ConfigurationArtifactKind.Journal && ProjectPath.parent(a.Path) == g)))
            {
                StageOwned(ProjectPath.join(directory, ".morphir-transaction"), document.Encode(), "journal");
            }
            foreach (var change in changes)
            {
                fault?.Invoke(ConfigurationTransactionPhase.BeforeStage, change.Path);
                var stage = StageOwned(change.Path, change.Content.AsMemory(), "stage");
                string? backup = null;
                if (change.Expected.Kind == ConfigurationObservationKind.File)
                {
                    backup = StageOwned(change.Path, host.ReadFile(change.Path), "backup");
                }
                staged.Add((change, stage, backup));
            }
            // A ready snapshot records actual artifact paths for non-native storage adapters.
            document = document with
            {
                Entries = staged.Select(s => new RecoveryEntry(s.Change.Path, s.Change.Expected.Kind,
                s.Change.Expected.Identity, CandidateObservation(s.Change).Identity, s.Stage, s.Backup)).ToImmutableArray(),
                Artifacts = artifacts.ToImmutableArray()
            };
            StageOwned(document.MetadataDestination, document.Encode(), "journal-ready");
            if (!StagesMatch() || !ObservationsMatch()) throw new ConflictException();
            foreach (var (change, stage, backup) in staged)
            {
                fault?.Invoke(ConfigurationTransactionPhase.BeforeInstall, change.Path);
                if (!StagesMatch() || !ObservationsMatch()) throw new ConflictException();
                attempted.Add((change, backup));
                host.Install(stage, change.Path, change.Expected.Kind == ConfigurationObservationKind.File);
                fault?.Invoke(ConfigurationTransactionPhase.AfterInstall, change.Path);
            }
            if (!StagesMatch() || !ObservationsMatch()) throw new ConflictException();
            document = document with { Committed = true, Artifacts = artifacts.ToImmutableArray() };
            StageOwned(document.MetadataDestination, document.Encode(), "journal-committed");
            if (!TryCleanup(artifacts, true, document, artifactEvidence, partialArtifacts))
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_cleanup_required", targets, Retained(artifacts))
                { UnresolvedPaths = targets };
            return new(ConfigurationTransactionStatus.Written, "config.edit_written", targets, []);
        }
        catch (Exception error) when (IsIo(error))
        {
            try
            {
                if (error is ConfigurationArtifactFailureException artifactFailure) ReportArtifacts(artifactFailure);
                foreach (var path in stageAttempts.Keys.Where(p => !artifacts.Any(a => a.Path == p)))
                    if (host.Observe(path).Kind != ConfigurationObservationKind.Missing) Unverified(path);
                if (document is not null)
                    foreach (var directory in document.PendingDirectories)
                        if (!artifacts.Any(a => a.Kind == ConfigurationArtifactKind.Directory && a.Path == directory)
                            && host.Observe(directory).Kind != ConfigurationObservationKind.Missing) unresolved.Add(directory);
                if (document is { Committed: true })
                {
                    var witnessPath = RecoveryDocument.ArtifactPath(document.MetadataDestination, transactionId, "journal-committed");
                    if (host.Observe(witnessPath).Kind != ConfigurationObservationKind.Missing)
                    {
                        var witness = ReadRecovery(witnessPath);
                        if (!witness.Committed || !SameOperation(document, witness)
                            || !witness.Encode().AsSpan().SequenceEqual(document.Encode()))
                            throw new InvalidDataException("Commit witness does not match the attempted transaction");
                        if (!artifacts.Any(a => a.Path == witnessPath))
                            artifacts.Add(new(witnessPath, ConfigurationArtifactKind.Journal, transactionId));
                        // Durable commit is authoritative even when its acknowledgement failed.
                        // Preserve candidate bytes and backups for explicit cleanup/recovery.
                        return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_cleanup_required", targets, Retained(artifacts))
                        { UnresolvedPaths = targets };
                    }
                    document = document with { Committed = false };
                }
                foreach (var (change, backup) in attempted.AsEnumerable().Reverse())
                {
                    try
                    {
                        var current = host.Observe(change.Path);
                        if (current == change.Expected) { restored.Add(change.Path); continue; }
                        var candidate = CandidateObservation(change);
                        if (current != candidate) { unresolved.Add(change.Path); continue; }
                        fault?.Invoke(ConfigurationTransactionPhase.BeforeRollback, change.Path);
                        if (host.Observe(change.Path) != candidate || backup is not null && host.Observe(backup) !=
                            new ConfigurationObservation(backup, ConfigurationObservationKind.File, change.Expected.Identity))
                        { unresolved.Add(change.Path); continue; }
                        if (backup is null) host.DeleteFile(change.Path);
                        else host.Install(backup, change.Path, true);
                        fault?.Invoke(ConfigurationTransactionPhase.AfterRollback, change.Path);
                        if (host.Observe(change.Path) == change.Expected) restored.Add(change.Path);
                        else unresolved.Add(change.Path);
                    }
                    catch (Exception recoveryError) when (IsIo(recoveryError))
                    {
                        try { if (host.Observe(change.Path) == change.Expected) restored.Add(change.Path); else unresolved.Add(change.Path); }
                        catch (Exception observationError) when (IsIo(observationError)) { unresolved.Add(change.Path); }
                    }
                }
                if (unverified.Count > 0) RecordRecoveryEvidence();
                if (unresolved.Count > 0)
                    return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_recovery_required", targets,
                        Retained(artifacts))
                    { RestoredPaths = restored.ToImmutableArray(), UnresolvedPaths = unresolved.Distinct().ToImmutableArray() };
                if (!TryCleanup(artifacts, false, document, artifactEvidence, partialArtifacts))
                    return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_cleanup_required", targets, Retained(artifacts))
                    { RestoredPaths = restored.ToImmutableArray(), UnresolvedPaths = targets };
                return new(error is ConflictException ? ConfigurationTransactionStatus.Conflict
                    : attempted.Count > 0 ? ConfigurationTransactionStatus.RolledBack : ConfigurationTransactionStatus.IoFailure,
                    error is ConflictException ? "config.edit_conflict"
                    : attempted.Count > 0 ? "config.edit_rolled_back" : "config.edit_io", targets, [])
                { RestoredPaths = restored.ToImmutableArray() };
            }
            catch (Exception observationError) when (IsRecoveryError(observationError))
            {
                // The host may be disconnected after a durable write. Report possible
                // owned locations without querying it again or attempting blind rollback.
                var possible = artifacts.Concat(stageAttempts.Select(p => new ConfigurationArtifact(p.Key, p.Value, transactionId)))
                    .Distinct().ToImmutableArray();
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_recovery_io", targets, possible)
                {
                    RestoredPaths = restored.ToImmutableArray(),
                    UnresolvedPaths = targets.Concat(unresolved).Concat(unverified).Concat(plannedDirectories)
                        .Distinct(StringComparer.Ordinal).ToImmutableArray()
                };
            }
        }
        finally { coordination?.Dispose(); }
    }

    private bool TryCleanup(List<ConfigurationArtifact> artifacts, bool committed, RecoveryDocument? document,
        IReadOnlyDictionary<string, ConfigurationObservation>? evidence = null, IReadOnlySet<string>? partialArtifacts = null,
        IReadOnlySet<string>? preservedDirectories = null)
    {
        if (artifacts.Count == 0) return true;
        try
        {
            fault?.Invoke(ConfigurationTransactionPhase.BeforeCleanup, artifacts[0].Path);
            var cleanupDirectories = artifacts.Where(a => a.Kind == ConfigurationArtifactKind.Directory).Select(a => a.Path)
                .Concat(preservedDirectories ?? new HashSet<string>()).ToHashSet(StringComparer.Ordinal);
            if (CleanupUnresolved(artifacts, document, evidence, partialArtifacts, cleanupDirectories).Length > 0) return false;
            // Keep metadata outside newly created directories until directory cleanup is proven.
            // A committed witness remains last, above all destinations, throughout cleanup.
            var directories = artifacts.Where(a => a.Kind == ConfigurationArtifactKind.Directory).Select(a => a.Path).ToHashSet(StringComparer.Ordinal);
            int Order(ConfigurationArtifact artifact) => artifact.Kind != ConfigurationArtifactKind.Journal
                ? artifact.Kind == ConfigurationArtifactKind.Directory ? 2 : 0
                : directories.Contains(ProjectPath.parent(artifact.Path)) ? 1
                : artifact.Path.EndsWith("-journal-committed", StringComparison.Ordinal) ? 4 : 3;
            foreach (var artifact in artifacts.OrderBy(Order).ThenByDescending(a => a.Path.Length))
            {
                if (CleanupUnresolved([artifact], document, evidence, partialArtifacts, cleanupDirectories).Length > 0) return false;
                if (artifact.Kind == ConfigurationArtifactKind.Directory)
                {
                    host.DeleteDirectory(artifact.Path);
                    if (!committed && host.Observe(artifact.Path).Kind != ConfigurationObservationKind.Missing)
                        return false;
                }
                else host.DeleteFile(artifact.Path);
            }
            return Retained(artifacts).All(a => committed && a.Kind == ConfigurationArtifactKind.Directory);
        }
        catch (Exception error) when (IsIo(error)) { return false; }
    }

    private ImmutableArray<string> CleanupUnresolved(IEnumerable<ConfigurationArtifact> artifacts, RecoveryDocument? document,
        IReadOnlyDictionary<string, ConfigurationObservation>? evidence, IReadOnlySet<string>? partialArtifacts,
        IReadOnlySet<string>? preservedDirectories)
    {
        var list = artifacts.ToArray();
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in list.Where(a => a.Kind != ConfigurationArtifactKind.Directory))
        {
            var observed = host.Observe(artifact.Path);
            if (observed.Kind == ConfigurationObservationKind.Missing) continue;
            if (evidence is not null && (!evidence.TryGetValue(artifact.Path, out var expected) || expected != observed))
                unresolved.Add(artifact.Path);
        }
        if (document is not null)
            unresolved.UnionWith(RecoveryUnresolved(document, list.Where(a => partialArtifacts?.Contains(a.Path) != true), preservedDirectories, ignoreUnverified: true));
        return unresolved.Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private ImmutableArray<ConfigurationArtifact> Retained(IEnumerable<ConfigurationArtifact> artifacts) =>
        artifacts.Distinct().Where(a =>
        {
            try { return host.Observe(a.Path).Kind != ConfigurationObservationKind.Missing; }
            catch (Exception error) when (IsIo(error)) { return true; }
        }).ToImmutableArray();

    private static ConfigurationObservation CandidateObservation(ConfigurationFileChange change) =>
        new(change.Path, ConfigurationObservationKind.File, NativeConfigurationTransactionHost.Hash(change.Content.AsSpan()));
    private sealed class ConflictException : IOException;
    private sealed class OwnershipException : IOException;
    private static bool IsIo(Exception error) => error is IOException or UnauthorizedAccessException or NotSupportedException;
}

/// <summary>Native same-filesystem stages and per-file atomic installation.</summary>
public sealed class NativeConfigurationTransactionHost : IConfigurationTransactionHost
{
    public bool SupportsDurableRecovery => true;
    public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
    {
        path = Canonical(path);
        var nativeRoot = Path.GetPathRoot(path)!;
        var current = ProjectPath.normalize(nativeRoot);
        FileAttributes attributes = FileAttributes.Directory;
        foreach (var component in path[nativeRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = ProjectPath.join(current, component);
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return new(path, ConfigurationObservationKind.Missing, ""); }
            catch (DirectoryNotFoundException) { return new(path, ConfigurationObservationKind.Missing, ""); }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return new(path, ConfigurationObservationKind.Link,
                    Hash(Encoding.UTF8.GetBytes(current + "\n" + new FileInfo(current).LinkTarget)));
        }
        if ((attributes & FileAttributes.Directory) == 0)
            return new(path, ConfigurationObservationKind.File, Hash(File.ReadAllBytes(path)));
        if (Directory.Exists(path))
        {
            var excluded = (excludedPaths ?? []).ToHashSet(StringComparer.Ordinal);
            var children = Directory.EnumerateFileSystemEntries(path).Select(Canonical).Where(p => !excluded.Contains(p))
                .Select(p =>
                {
                    var childAttributes = File.GetAttributes(p);
                    var kind = (childAttributes & FileAttributes.ReparsePoint) != 0 ? "link"
                        : (childAttributes & FileAttributes.Directory) != 0 ? "directory" : "file";
                    return Convert.ToHexString(Encoding.UTF8.GetBytes(Path.GetFileName(p))) + ":" + kind;
                }).Order(StringComparer.Ordinal);
            return new(path, ConfigurationObservationKind.Directory, Hash(Encoding.UTF8.GetBytes(string.Join("\n", children))));
        }
        return new(path, ConfigurationObservationKind.Missing, "");
    }

    // Recovery enumeration needs directory kind, not a fingerprint of unrelated
    // ancestor entries. Keep the same no-follow checks as native observations.
    private static bool IsDirectoryWithoutLinks(string path)
    {
        var nativeRoot = Path.GetPathRoot(path)!;
        var current = ProjectPath.normalize(nativeRoot);
        var attributes = FileAttributes.Directory;
        foreach (var component in path[nativeRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = ProjectPath.join(current, component);
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return (attributes & FileAttributes.Directory) != 0 && Directory.Exists(path);
    }

    public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var current = IsDirectoryWithoutLinks(Canonical(path)) ? Canonical(path) : ProjectPath.parent(Canonical(path));
            while (true)
            {
                directories.Add(current);
                var parent = ProjectPath.parent(current);
                if (parent == current) break;
                current = parent;
            }
        }
        var result = new List<string>();
        foreach (var directory in directories.Order(StringComparer.Ordinal))
        {
            if (!IsDirectoryWithoutLinks(directory)) continue;
            result.AddRange(Directory.EnumerateFileSystemEntries(directory, ".morphir-edit-*")
                .Select(Canonical).Where(RecoveryDocument.IsJournal));
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId)
    {
        if (Observe(directory).Kind == ConfigurationObservationKind.Missing) return [];
        if (Observe(directory).Kind != ConfigurationObservationKind.Directory) throw new IOException("Recovery directory is not a regular directory");
        return Directory.EnumerateFileSystemEntries(directory, ".morphir-edit-" + transactionId + "-*").Select(Canonical).Order(StringComparer.Ordinal).ToArray();
    }

    public byte[] ReadFile(string path) => File.ReadAllBytes(path);
    public IDisposable Acquire(IReadOnlyList<string> paths) => new NativeLease();

    public IReadOnlyList<string> CreateParentDirectories(string destination) =>
        CreateParentDirectories(destination, Guid.NewGuid().ToString("N"));

    public IReadOnlyList<string> CreateParentDirectories(string destination, string transactionId)
    {
        var created = new List<string>();
        try
        {
            if (Observe(destination).Kind == ConfigurationObservationKind.Link) throw new IOException("Target has a linked ancestor");
            var parent = Path.GetDirectoryName(destination)!;
            var missing = new Stack<string>();
            while (!Directory.Exists(parent))
            {
                missing.Push(parent);
                parent = Path.GetDirectoryName(parent) ?? throw new IOException("No existing parent directory");
            }
            while (missing.TryPop(out var directory))
            {
                Directory.CreateDirectory(directory);
                created.Add(Canonical(directory));
            }
            return created;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var retained = new List<ConfigurationArtifact>();
            foreach (var directory in created.AsEnumerable().Reverse())
            {
                try { DeleteDirectory(directory); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or NotSupportedException) { }
                try { if (Observe(directory).Kind == ConfigurationObservationKind.Missing) continue; }
                catch (Exception observationError) when (observationError is IOException or UnauthorizedAccessException or NotSupportedException) { }
                retained.Add(new(directory, ConfigurationArtifactKind.Directory, transactionId));
            }
            if (retained.Count > 0) throw new ConfigurationArtifactFailureException(retained, error);
            throw;
        }
    }

    public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role)
    {
        var path = RecoveryDocument.ArtifactPath(Canonical(destination), transactionId, role);
        var created = false;
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, options);
            created = true;
            file.Write(content.Span);
            file.Flush(true);
            return Canonical(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!created) throw;
            try { File.Delete(path); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                var kind = role == "backup" ? ConfigurationArtifactKind.Backup : role.StartsWith("journal", StringComparison.Ordinal) ? ConfigurationArtifactKind.Journal : ConfigurationArtifactKind.Stage;
                throw new ConfigurationArtifactFailureException([new(Canonical(path), kind, transactionId)], error);
            }
            throw;
        }
    }

    public void Install(string stagedPath, string destination, bool overwrite)
    {
        if (overwrite)
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stagedPath, File.GetUnixFileMode(destination));
            File.Replace(stagedPath, destination, null);
        }
        else File.Move(stagedPath, destination);
    }
    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }

    internal static string Canonical(string path) => ProjectPath.normalize(Path.GetFullPath(path));
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class NativeLease : IDisposable
    {
        private readonly Mutex mutex = new(false, OperatingSystem.IsWindows()
            ? @"Global\Morphir.Configuration.Edit.v1" : "Morphir.Configuration.Edit.v1");
        private bool acquired;

        public NativeLease()
        {
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Configuration writer coordination timed out");
            }
            catch { mutex.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (acquired) { acquired = false; mutex.ReleaseMutex(); }
            mutex.Dispose();
        }
    }
}
