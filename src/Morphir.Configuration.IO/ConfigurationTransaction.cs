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
    ImmutableArray<ConfigurationArtifact> Artifacts);

/// <summary>Explicit host boundary. Acquire must serialize overlapping paths across its writers;
/// Install must atomically replace one file, never a group of files.</summary>
public interface IConfigurationTransactionHost
{
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
}

/// <summary>Preconditioned configuration-file application. Each installation is atomic;
/// a transaction with multiple destinations is not group atomic.</summary>
public sealed class ConfigurationTransaction
{
    private readonly IConfigurationTransactionHost host;
    private readonly Action<ConfigurationTransactionPhase, string>? fault;

    public ConfigurationTransaction(IConfigurationTransactionHost? host = null, Action<ConfigurationTransactionPhase, string>? fault = null)
    {
        this.host = host ?? new NativeConfigurationTransactionHost();
        this.fault = fault;
    }

    public static ConfigurationObservation Observe(string path) => new NativeConfigurationTransactionHost().Observe(path);

    public ConfigurationTransactionResult Apply(ConfigurationTransactionPlan plan)
    {
        var targets = plan.Changes.Select(c => c.Path).ToImmutableArray();
        if (plan.Changes.Any(c => c.Path != c.Expected.Path
            || c.Expected.Kind is not (ConfigurationObservationKind.File or ConfigurationObservationKind.Missing)))
            return new(ConfigurationTransactionStatus.IoFailure, "config.edit_invalid_plan", targets, []);
        var observations = plan.Observations.Concat(plan.Changes.Select(c => c.Expected)).Distinct().ToArray();
        var paths = observations.Select(o => o.Path).Concat(targets).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var transactionId = Guid.NewGuid().ToString("N");
        var artifacts = new List<ConfigurationArtifact>();
        var attempted = new List<(ConfigurationFileChange Change, string? Backup)>();
        var ownedDirectories = new Dictionary<string, ConfigurationObservation>(StringComparer.Ordinal);
        bool ObservationsMatch()
        {
            var excluded = artifacts.Select(a => a.Path)
                .Concat(attempted.Where(a => a.Change.Expected.Kind == ConfigurationObservationKind.Missing).Select(a => a.Change.Path)).ToArray();
            return observations.All(o => host.Observe(o.Path, excluded) ==
                (attempted.FirstOrDefault(a => a.Change.Path == o.Path).Change is { } installed ? CandidateObservation(installed)
                    : ownedDirectories.GetValueOrDefault(o.Path, o)));
        }
        IDisposable? coordination = null;
        try
        {
            coordination = host.Acquire(paths);
            if (!ObservationsMatch())
                return new(ConfigurationTransactionStatus.Conflict, "config.edit_conflict", targets, []);
            var changed = false;
            foreach (var change in plan.Changes)
            {
                if (change.Expected.Kind == ConfigurationObservationKind.File && host.ReadFile(change.Path).AsSpan().SequenceEqual(change.Content.AsSpan())) continue;
                var createdDirectories = host.CreateParentDirectories(change.Path, transactionId);
                foreach (var directory in createdDirectories)
                    artifacts.Add(new(directory, ConfigurationArtifactKind.Directory, transactionId));
                foreach (var directory in createdDirectories)
                    ownedDirectories[directory] = host.Observe(directory, artifacts.Select(a => a.Path).ToArray());
                fault?.Invoke(ConfigurationTransactionPhase.BeforeStage, change.Path);
                var stage = host.Stage(change.Path, change.Content.AsMemory(), transactionId, "stage");
                artifacts.Add(new(stage, ConfigurationArtifactKind.Stage, transactionId));
                string? backup = null;
                if (change.Expected.Kind == ConfigurationObservationKind.File)
                {
                    backup = host.Stage(change.Path, host.ReadFile(change.Path), transactionId, "backup");
                    artifacts.Add(new(backup, ConfigurationArtifactKind.Backup, transactionId));
                }
                var journal = host.Stage(change.Path, RecoveryJournal(change, stage, backup, transactionId), transactionId, "journal");
                artifacts.Add(new(journal, ConfigurationArtifactKind.Journal, transactionId));
                fault?.Invoke(ConfigurationTransactionPhase.BeforeInstall, change.Path);
                if (host.Observe(stage) != new ConfigurationObservation(stage, ConfigurationObservationKind.File, CandidateObservation(change).Identity)
                    || (backup is not null && host.Observe(backup) != new ConfigurationObservation(backup, ConfigurationObservationKind.File, change.Expected.Identity))
                    || !ObservationsMatch()) throw new ConflictException();
                attempted.Add((change, backup));
                host.Install(stage, change.Path, change.Expected.Kind == ConfigurationObservationKind.File);
                changed = true;
                fault?.Invoke(ConfigurationTransactionPhase.AfterInstall, change.Path);
            }
            if (!TryCleanup(artifacts, true))
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_cleanup_required", targets, Retained(artifacts));
            return new(changed ? ConfigurationTransactionStatus.Written : ConfigurationTransactionStatus.Unchanged,
                changed ? "config.edit_written" : "config.edit_unchanged", targets, []);
        }
        catch (Exception error) when (IsIo(error))
        {
            var foreignArtifacts = ImmutableArray<ConfigurationArtifact>.Empty;
            if (error is ConfigurationArtifactFailureException artifactFailure)
            {
                artifacts.AddRange(artifactFailure.Artifacts.Where(a => a.TransactionId == transactionId));
                foreignArtifacts = artifactFailure.Artifacts.Where(a => a.TransactionId != transactionId).ToImmutableArray();
            }
            var recovered = foreignArtifacts.Length == 0;
            foreach (var (change, backup) in attempted.AsEnumerable().Reverse())
            {
                try
                {
                    var current = host.Observe(change.Path);
                    if (current == change.Expected) continue;
                    var candidate = new ConfigurationObservation(change.Path, ConfigurationObservationKind.File,
                        NativeConfigurationTransactionHost.Hash(change.Content.AsSpan()));
                    if (current != candidate) { recovered = false; continue; }
                    fault?.Invoke(ConfigurationTransactionPhase.BeforeRollback, change.Path);
                    if (host.Observe(change.Path) != candidate || (backup is not null && host.Observe(backup) !=
                        new ConfigurationObservation(backup, ConfigurationObservationKind.File, change.Expected.Identity)))
                    { recovered = false; continue; }
                    if (backup is null) host.DeleteFile(change.Path);
                    else host.Install(backup, change.Path, true);
                    fault?.Invoke(ConfigurationTransactionPhase.AfterRollback, change.Path);
                    if (host.Observe(change.Path) != change.Expected) recovered = false;
                }
                catch (Exception recoveryError) when (IsIo(recoveryError))
                {
                    try { if (host.Observe(change.Path) != change.Expected) recovered = false; }
                    catch (Exception observationError) when (IsIo(observationError)) { recovered = false; }
                }
            }
            if (!recovered)
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_recovery_required", targets,
                    Retained(artifacts).AddRange(foreignArtifacts));
            if (!TryCleanup(artifacts, false))
                return new(ConfigurationTransactionStatus.RecoveryRequired, "config.edit_cleanup_required", targets, Retained(artifacts));
            return new(error is ConflictException ? ConfigurationTransactionStatus.Conflict
                : attempted.Count > 0 ? ConfigurationTransactionStatus.RolledBack : ConfigurationTransactionStatus.IoFailure,
                error is ConflictException ? "config.edit_conflict"
                : attempted.Count > 0 ? "config.edit_rolled_back" : "config.edit_io", targets, []);
        }
        finally { coordination?.Dispose(); }
    }

    private bool TryCleanup(List<ConfigurationArtifact> artifacts, bool committed)
    {
        if (artifacts.Count == 0) return true;
        try
        {
            fault?.Invoke(ConfigurationTransactionPhase.BeforeCleanup, artifacts[0].Path);
            foreach (var artifact in artifacts.AsEnumerable().Reverse())
                if (artifact.Kind == ConfigurationArtifactKind.Directory) host.DeleteDirectory(artifact.Path);
                else host.DeleteFile(artifact.Path);
            return Retained(artifacts).All(a => committed && a.Kind == ConfigurationArtifactKind.Directory);
        }
        catch (Exception error) when (IsIo(error)) { return false; }
    }

    private ImmutableArray<ConfigurationArtifact> Retained(IEnumerable<ConfigurationArtifact> artifacts) =>
        artifacts.Where(a =>
        {
            try { return host.Observe(a.Path).Kind != ConfigurationObservationKind.Missing; }
            catch (Exception error) when (IsIo(error)) { return true; }
        }).ToImmutableArray();

    private static byte[] RecoveryJournal(ConfigurationFileChange change, string stage, string? backup, string transactionId)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("transactionId", transactionId);
            writer.WriteString("target", change.Path);
            writer.WriteString("originalKind", change.Expected.Kind.ToString());
            writer.WriteString("originalIdentity", change.Expected.Identity);
            writer.WriteString("candidateIdentity", CandidateObservation(change).Identity);
            writer.WriteString("stage", stage);
            writer.WriteString("backup", backup);
            writer.WriteEndObject();
        }
        return memory.ToArray();
    }

    private static ConfigurationObservation CandidateObservation(ConfigurationFileChange change) =>
        new(change.Path, ConfigurationObservationKind.File, NativeConfigurationTransactionHost.Hash(change.Content.AsSpan()));

    private sealed class ConflictException : IOException;

    private static bool IsIo(Exception error) => error is IOException or UnauthorizedAccessException or NotSupportedException;
}

/// <summary>Native same-filesystem stages and per-file atomic installation.</summary>
public sealed class NativeConfigurationTransactionHost : IConfigurationTransactionHost
{
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
        var path = Path.Combine(Path.GetDirectoryName(destination)!, ".morphir-edit-" + transactionId + "-" + Guid.NewGuid().ToString("N") + "-" + role);
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
                var kind = role == "backup" ? ConfigurationArtifactKind.Backup : role == "journal" ? ConfigurationArtifactKind.Journal : ConfigurationArtifactKind.Stage;
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
