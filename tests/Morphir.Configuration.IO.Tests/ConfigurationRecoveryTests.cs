using System.Text;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ConfigurationRecoveryTests
{
    [Test]
    public void PendingRecoveryBlocksAnotherWriterUntilExplicitRecoveryRestoresOriginals()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            var first = root + "/one/morphir.toml";
            var second = root + "/two/morphir.toml";
            Directory.CreateDirectory(ProjectPath.parent(first));
            Directory.CreateDirectory(ProjectPath.parent(second));
            File.WriteAllText(first, "original=1\n");
            File.WriteAllText(second, "original=2\n");
            var plan = Plan(root, first, second);
            var failed = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback)
                    throw new IOException("injected interruption and rollback failure");
            }).Apply(plan);
            failed.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            failed.UnresolvedPaths.Should().Contain(first);
            var next = new ConfigurationTransaction().Apply(Plan(root, first, second));
            next.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            var journal = failed.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            var inspection = new ConfigurationTransaction().InspectRecovery(journal);
            inspection.Valid.Should().BeTrue();
            inspection.Targets.Should().BeEquivalentTo([first, second]);
            new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            File.ReadAllText(first).Should().Be("original=1\n");
            File.ReadAllText(second).Should().Be("original=2\n");
            new ConfigurationTransaction().InspectPending([root, first]).Should().BeEmpty();
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained recovery evidence: " + root);
        }
    }

    [Test]
    public void RecoveryPreservesUserDriftAndRetainsItsJournal()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-recovery-drift-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            var target = root + "/morphir.toml";
            File.WriteAllText(target, "original=1\n");
            var result = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("injected");
            }).Apply(Plan(root, target));
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            File.WriteAllText(target, "user=true\n");
            var recovered = new ConfigurationTransaction().Recover(journal);
            recovered.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            recovered.UnresolvedPaths.Should().Contain(target);
            File.ReadAllText(target).Should().Be("user=true\n");
            File.Exists(journal).Should().BeTrue();
            // The fixture explicitly returns the target to the recorded candidate, then recovers.
            File.WriteAllText(target, "candidate=true\n");
            new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained recovery drift evidence: " + root);
        }
    }

    private static ConfigurationTransactionPlan Plan(string root, params string[] targets) => new(
        new[] { ConfigurationTransaction.Observe(root) },
        targets.Select(path => new ConfigurationFileChange(path, ConfigurationTransaction.Observe(path), Encoding.UTF8.GetBytes("candidate=true\n"))));
}

public sealed class ConfigurationRecoveryHostCapabilityTests
{
    [Test]
    public void LegacyStorageHostCannotSilentlyWriteWithoutDurableRecoveryEnumeration()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-host-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = root + "/morphir.toml";
            var host = new LegacyHost();
            var result = new ConfigurationTransaction(host).Apply(new ConfigurationTransactionPlan([host.Observe(root)],
                [new ConfigurationFileChange(target, host.Observe(target), Encoding.UTF8.GetBytes("value=true\n"))]));
            result.Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
            result.Code.Should().Be("config.recovery_host_capability");
            File.Exists(target).Should().BeFalse();
            Directory.GetFiles(root, ".morphir-edit-*").Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class LegacyHost : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => native.CreateParentDirectories(destination);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => native.Stage(destination, content, transactionId, role);
        public void Install(string stagedPath, string destination, bool overwrite) => native.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => native.DeleteFile(path);
        public void DeleteDirectory(string path) => native.DeleteDirectory(path);
    }
}

public sealed class ConfigurationRecoveryEvidenceTests
{
    [Test]
    [Arguments("{\"version\":2,\"transactionId\":null}")]
    [Arguments("{\"version\":999999999999999999999999999}")]
    [Arguments("{\"version\":2,\"transactionId\":\"11111111111111111111111111111111\",\"guards\":[null],\"entries\":[],\"artifacts\":[],\"committed\":false}")]
    [Arguments("{")]
    public void MalformedJournalShapesReturnStructuredRecoveryRequiredAndRemainUntouched(string content)
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = root + "/.morphir-edit-11111111111111111111111111111111-1111111111111111-journal";
            File.WriteAllText(journal, content);
            var transaction = new ConfigurationTransaction();
            var inspection = transaction.InspectRecovery(journal);
            inspection.Valid.Should().BeFalse();
            inspection.Code.Should().Be("config.recovery_invalid_journal");
            transaction.Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            var target = root + "/morphir.toml";
            transaction.Apply(new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
                [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), Encoding.UTF8.GetBytes("new=true\n"))]))
                .Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            File.ReadAllText(journal).Should().Be(content);
            File.Exists(target).Should().BeFalse();
            // This fixture created the invalid text; no real transaction ownership is inferred.
            File.Delete(journal);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void JournalCannotReassignAnArtifactsTargetByEditingItsOwnedPrefix()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-artifact-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            var target = root + "/morphir.toml";
            File.WriteAllText(target, "original=true\n");
            var failed = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("injected");
            }).Apply(new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
                [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
            var journals = failed.Artifacts.Where(a => a.Kind == ConfigurationArtifactKind.Journal)
                .ToDictionary(a => a.Path, a => File.ReadAllBytes(a.Path));
            try
            {
                foreach (var (path, bytes) in journals)
                {
                    var json = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
                    foreach (var entry in json["entries"]!.AsArray())
                        entry!["stage"] = root + "/.morphir-edit-" + failed.Artifacts[0].TransactionId + "-forged-stage";
                    File.WriteAllText(path, json.ToJsonString());
                }
                new ConfigurationTransaction().InspectRecovery(journals.Keys.First()).Valid.Should().BeFalse();
                new ConfigurationTransaction().Recover(journals.Keys.First()).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
                File.ReadAllText(target).Should().Be("candidate=true\n");
            }
            finally
            {
                foreach (var (path, bytes) in journals) File.WriteAllBytes(path, bytes);
                var result = new ConfigurationTransaction().Recover(journals.Keys.First());
                result.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
                resolved = true;
            }
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained artifact ownership evidence: " + root);
        }
    }

    [Test]
    public void ChangedBackupIsPreservedUntilItsExactRecordedBytesAreExplicitlyRestored()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-backup-drift-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            var target = root + "/morphir.toml";
            File.WriteAllText(target, "original=true\n");
            var result = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("injected");
            }).Apply(new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
                [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
            var backup = result.Artifacts.Single(a => a.Kind == ConfigurationArtifactKind.Backup).Path;
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            var original = File.ReadAllBytes(backup);
            File.WriteAllText(backup, "user-backup=true\n");
            var transaction = new ConfigurationTransaction();
            transaction.InspectRecovery(journal).UnresolvedPaths.Should().Contain(backup);
            var recovered = transaction.Recover(journal);
            recovered.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            File.ReadAllText(backup).Should().Be("user-backup=true\n");
            File.ReadAllText(target).Should().Be("candidate=true\n");
            File.Exists(journal).Should().BeTrue();
            File.WriteAllBytes(backup, original);
            transaction.Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            File.ReadAllText(target).Should().Be("original=true\n");
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained backup drift evidence: " + root);
        }
    }
}
