using System.Text;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Category("PlatformSpecific")]
public sealed class ConfigurationArtifactOwnershipTests
{
    [Test]
    [Arguments("journal", false)]
    [Arguments("stage", false)]
    [Arguments("backup", false)]
    [Arguments("journal", true)]
    [Arguments("stage", true)]
    [Arguments("backup", true)]
    public void AHostCannotTransferAUserFileIntoTransactionOwnership(string role, bool forgedException)
    {
        var root = ProjectPath.join(Path.GetTempPath(), "transaction-foreign-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            var target = root + "/morphir.toml";
            var foreign = root + "/user-owned.txt";
            File.WriteAllText(target, "original=true\n");
            var foreignText = role == "stage" ? "candidate=true\n" : role == "backup" ? "original=true\n" : "user-owned=true\n";
            File.WriteAllText(foreign, foreignText);
            var host = new ForeignArtifactHost(role, foreign, forgedException);
            var result = new ConfigurationTransaction(host).Apply(new ConfigurationTransactionPlan([],
                [new ConfigurationFileChange(target, host.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
            File.Exists(foreign).Should().BeTrue("an adapter response cannot transfer ownership of a pre-existing user file");
            File.ReadAllText(foreign).Should().Be(foreignText);
            File.ReadAllText(target).Should().Be("original=true\n");
            result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            result.UnresolvedPaths.Should().Contain(foreign);
            result.Artifacts.Should().NotContain(a => a.Path == foreign, "unverified paths are not owned cleanup artifacts");
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            var transaction = new ConfigurationTransaction();
            transaction.InspectPending([root]).Should().NotBeEmpty();
            transaction.InspectRecovery(journal).UnresolvedPaths.Should().Contain(foreign,
                "inspection must report rejected receipt paths even when the Apply result was lost");
            transaction.Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            File.ReadAllText(foreign).Should().Be(foreignText);
            transaction.InspectPending([root]).Should().BeEmpty();
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained foreign-artifact evidence: " + root);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ParentCreationCannotTransferAnExistingForeignDirectoryIntoOwnership(bool forgedException)
    {
        var root = ProjectPath.join(Path.GetTempPath(), "transaction-foreign-directory-" + Guid.NewGuid().ToString("N"));
        var foreign = root + "/user-owned-empty";
        Directory.CreateDirectory(foreign);
        var resolved = false;
        try
        {
            var target = root + "/new/morphir.toml";
            var host = new ForeignArtifactHost("directory", foreign, forgedException);
            var result = new ConfigurationTransaction(host).Apply(new ConfigurationTransactionPlan([],
                [new ConfigurationFileChange(target, host.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
            Directory.Exists(foreign).Should().BeTrue("an existing empty directory is not transaction-owned");
            File.Exists(target).Should().BeFalse();
            result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            result.UnresolvedPaths.Should().Contain(foreign);
            result.Artifacts.Should().NotContain(a => a.Path == foreign);
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            new ConfigurationTransaction().InspectRecovery(journal).UnresolvedPaths.Should().Contain(foreign);
            new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            Directory.Exists(foreign).Should().BeTrue();
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained foreign-directory evidence: " + root);
        }
    }

    [Test]
    [Arguments("backup")]
    [Arguments("journal-ready")]
    public void CleanupPreservesAnArtifactWhoseBytesChangedAfterStaging(string role)
    {
        var root = ProjectPath.join(Path.GetTempPath(), "transaction-cleanup-drift-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        string? changed = null;
        byte[]? original = null;
        try
        {
            var target = root + "/morphir.toml";
            File.WriteAllText(target, "original=true\n");
            var changedText = role == "backup" ? "user-backup=true\n" : " ";
            var result = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase != ConfigurationTransactionPhase.BeforeCleanup) return;
                changed = ProjectPath.normalize(Directory.GetFiles(root, ".morphir-edit-*-" + role).Single());
                original = File.ReadAllBytes(changed);
                // A valid JSON journal with added whitespace still has different user-owned bytes.
                if (role == "backup") File.WriteAllText(changed, changedText);
                else File.AppendAllText(changed, changedText);
            }).Apply(new ConfigurationTransactionPlan([],
                [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
            result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            File.Exists(changed).Should().BeTrue("cleanup must not discard changed evidence");
            if (role == "backup") File.ReadAllText(changed!).Should().Be(changedText);
            else File.ReadAllBytes(changed!).Should().Equal(original!.Concat(Encoding.UTF8.GetBytes(changedText)));
            result.Artifacts.Should().Contain(a => a.Path == changed);
            var transaction = new ConfigurationTransaction();
            transaction.InspectPending([root]).Should().NotBeEmpty();
            File.ReadAllText(target).Should().Be("candidate=true\n");
            // The fixture explicitly restores its recorded bytes after checking preservation.
            File.WriteAllBytes(changed!, original!);
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            transaction.Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.Written);
            transaction.InspectPending([root]).Should().BeEmpty();
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained cleanup-drift evidence: " + root);
        }
    }

    private sealed class ForeignArtifactHost(string role, string foreign, bool forgedException) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        public bool SupportsDurableRecovery => true;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => native.CreateParentDirectories(destination);
        public IReadOnlyList<string> CreateParentDirectories(string destination, string transactionId)
        {
            if (role != "directory") return native.CreateParentDirectories(destination, transactionId);
            if (!forgedException) return [foreign];
            throw new ConfigurationArtifactFailureException([new(foreign, ConfigurationArtifactKind.Directory, transactionId)]);
        }
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string artifactRole)
        {
            if (artifactRole != role) return native.Stage(destination, content, transactionId, artifactRole);
            if (!forgedException) return foreign;
            var kind = role == "backup" ? ConfigurationArtifactKind.Backup : role == "stage" ? ConfigurationArtifactKind.Stage : ConfigurationArtifactKind.Journal;
            throw new ConfigurationArtifactFailureException([new(foreign, kind, transactionId)]);
        }
        public void Install(string stagedPath, string destination, bool overwrite) => native.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => native.DeleteFile(path);
        public void DeleteDirectory(string path) => native.DeleteDirectory(path);
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => native.EnumerateTransactionArtifacts(directory, transactionId);
    }
}
