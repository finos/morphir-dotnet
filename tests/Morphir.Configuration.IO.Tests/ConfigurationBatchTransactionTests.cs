using System.Text;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Category("PlatformSpecific")]
public sealed class ConfigurationBatchTransactionTests
{
    [Test]
    public void EntireBatchStagesBeforeTheFirstReplacement()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = root + "/first.toml";
            var second = root + "/second.toml";
            File.WriteAllText(first, "original=1\n");
            File.WriteAllText(second, "original=2\n");
            var staged = new List<string>();
            var plan = new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
                new[] { first, second }.Select(path => new ConfigurationFileChange(path, ConfigurationTransaction.Observe(path), Encoding.UTF8.GetBytes("candidate=true\n"))));
            var result = new ConfigurationTransaction(fault: (phase, path) =>
            {
                if (phase == ConfigurationTransactionPhase.BeforeStage) staged.Add(path);
                if (phase == ConfigurationTransactionPhase.BeforeInstall)
                    staged.Should().Contain(second, "all destinations must stage before replacing the first destination");
            }).Apply(plan);
            result.Status.Should().Be(ConfigurationTransactionStatus.Written);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void LaterStageFailureDoesNotReplaceAnyDestination()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-stage-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = root + "/first.toml";
            var second = root + "/second.toml";
            File.WriteAllText(first, "original=1\n");
            File.WriteAllText(second, "original=2\n");
            var installs = 0;
            var plan = new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
                new[] { first, second }.Select(path => new ConfigurationFileChange(path, ConfigurationTransaction.Observe(path), Encoding.UTF8.GetBytes("candidate=true\n"))));
            var result = new ConfigurationTransaction(fault: (phase, path) =>
            {
                if (phase == ConfigurationTransactionPhase.BeforeStage && path == second) throw new IOException("stage unavailable");
                if (phase == ConfigurationTransactionPhase.AfterInstall) installs++;
            }).Apply(plan);
            installs.Should().Be(0);
            result.Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
            File.ReadAllText(first).Should().Be("original=1\n");
            File.ReadAllText(second).Should().Be("original=2\n");
        }
        finally { Directory.Delete(root, true); }
    }
}

[Category("PlatformSpecific")]
public sealed class ConfigurationBatchRecoveryBoundaryTests
{
    [Test]
    public void RecoveryMetadataDoesNotWriteIntoObservedExcludedSiblingDirectories()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-metadata-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root + "/excluded");
        try
        {
            var target = root + "/morphir.toml";
            var host = new MetadataHost(root + "/excluded");
            var plan = new ConfigurationTransactionPlan([host.Observe(root), host.Observe(root + "/excluded")],
                [new ConfigurationFileChange(target, host.Observe(target), Encoding.UTF8.GetBytes("value=true\n"))]);
            new ConfigurationTransaction(host).Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.Written,
                "read-only inventory evidence does not authorize transaction artifacts in excluded siblings");
            Directory.GetFileSystemEntries(root + "/excluded").Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void CleanupInterruptionKeepsTheCommonAncestorGuardForSiblingWriters()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-cleanup-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root + "/one");
        Directory.CreateDirectory(root + "/two");
        var resolved = false;
        try
        {
            var first = root + "/one/morphir.toml";
            var second = root + "/two/morphir.toml";
            var host = new CleanupHost(root);
            var plan = new ConfigurationTransactionPlan([host.Observe(root)], new[] { first, second }
                .Select(p => new ConfigurationFileChange(p, host.Observe(p), Encoding.UTF8.GetBytes("value=true\n"))));
            var failed = new ConfigurationTransaction(host).Apply(plan);
            failed.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            var siblingPlan = new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
                [new ConfigurationFileChange(second, ConfigurationTransaction.Observe(second), Encoding.UTF8.GetBytes("new=true\n"))]);
            new ConfigurationTransaction().Apply(siblingPlan).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            var journal = failed.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.Written);
            File.ReadAllText(second).Should().Be("value=true\n");
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained cleanup interruption evidence: " + root);
        }
    }

    private class MetadataHost(string forbidden) : IConfigurationTransactionHost
    {
        protected readonly NativeConfigurationTransactionHost Native = new();
        public bool SupportsDurableRecovery => Native.SupportsDurableRecovery;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => Native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => Native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => Native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => Native.CreateParentDirectories(destination);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role)
        {
            if (ProjectPath.contains(forbidden, destination)) throw new IOException("Excluded sibling is not writable");
            return Native.Stage(destination, content, transactionId, role);
        }
        public void Install(string stagedPath, string destination, bool overwrite) => Native.Install(stagedPath, destination, overwrite);
        public virtual void DeleteFile(string path) => Native.DeleteFile(path);
        public void DeleteDirectory(string path) => Native.DeleteDirectory(path);
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => Native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => Native.EnumerateTransactionArtifacts(directory, transactionId);
    }

    private sealed class CleanupHost(string root) : MetadataHost(root + "/not-used")
    {
        private bool failed;
        public override void DeleteFile(string path)
        {
            Native.DeleteFile(path);
            if (!failed && ProjectPath.parent(path) == root && path.EndsWith("-journal", StringComparison.Ordinal))
            {
                failed = true;
                throw new IOException("Crash window after root journal deletion");
            }
        }
    }
}

[Category("PlatformSpecific")]
public sealed class ConfigurationFinalPreconditionTests
{
    [Test]
    public void SourceDriftAfterTheFinalReplacementIsDetectedBeforeCommitIsDeclared()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "batch-final-precondition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = root + "/morphir.json";
            var target = root + "/morphir.toml";
            File.WriteAllText(source, "source-before\n");
            File.WriteAllText(target, "original=true\n");
            var plan = new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root), ConfigurationTransaction.Observe(source)],
                [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]);
            var result = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase == ConfigurationTransactionPhase.AfterInstall) File.WriteAllText(source, "source-after\n");
            }).Apply(plan);
            result.Status.Should().Be(ConfigurationTransactionStatus.Conflict);
            File.ReadAllText(target).Should().Be("original=true\n");
            File.ReadAllText(source).Should().Be("source-after\n");
            result.Artifacts.Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }
}
