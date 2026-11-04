using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ConfigurationRecoveryProcessTests
{
    [Test]
    [Arguments("BeforeStage:first")]
    [Arguments("BeforeStage:second")]
    [Arguments("BeforeInstall:first")]
    [Arguments("AfterInstall:first")]
    [Arguments("AfterInstall:second")]
    [Arguments("BeforeCleanup:any")]
    public async Task ProcessInterruptionRetainsPersistentRecoveryAndBlocksAnotherProcess(string window)
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            File.WriteAllText(root + "/first.toml", "original=1\n");
            File.WriteAllText(root + "/second.toml", "original=2\n");
            var crash = await Child(root, window);
            crash.ExitCode.Should().Be(79, crash.Output);
            var transaction = new ConfigurationTransaction();
            var pending = transaction.InspectPending([root, root + "/first.toml"]);
            pending.Should().NotBeEmpty("a process exit cannot erase durable write-ahead state");
            pending.Should().OnlyContain(p => p.Valid);
            var blocked = await Child(root, "verify-blocked");
            blocked.ExitCode.Should().Be(0, blocked.Output);
            var outcome = transaction.Recover(pending[0].JournalPath);
            var committed = window.StartsWith("BeforeCleanup", StringComparison.Ordinal);
            outcome.Status.Should().Be(committed ? ConfigurationTransactionStatus.Written : ConfigurationTransactionStatus.RolledBack);
            File.ReadAllText(root + "/first.toml").Should().Be(committed ? "candidate=true\n" : "original=1\n");
            File.ReadAllText(root + "/second.toml").Should().Be(committed ? "candidate=true\n" : "original=2\n");
            transaction.InspectPending([root]).Should().BeEmpty();
            Directory.GetFiles(root, ".morphir-edit-*").Should().BeEmpty();
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained process recovery evidence: " + root);
        }
    }

    [Test]
    public async Task ProcessExitAfterDirectoryCreationBeforeHostReturnRetainsAnUncertainOwnershipJournal()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "recovery-parent-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolved = false;
        try
        {
            var child = await Child(root, "parent-create");
            child.ExitCode.Should().Be(79, child.Output);
            Directory.Exists(root + "/new").Should().BeTrue();
            var transaction = new ConfigurationTransaction();
            var pending = transaction.InspectPending([root]);
            pending.Should().NotBeEmpty("a write-ahead guard must exist before the first mkdir");
            pending[0].UncertainDirectories.Should().Contain(root + "/new");
            // Ownership is uncertain because the interrupted host never returned its creation receipt.
            transaction.Recover(pending[0].JournalPath).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            Directory.Exists(root + "/new").Should().BeTrue("recovery cannot assume an empty directory belongs to it");
            var recovered = transaction.Recover(pending[0].JournalPath, [root + "/new"]);
            recovered.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            recovered.PreservedDirectories.Should().Equal(root + "/new");
            Directory.Exists(root + "/new").Should().BeTrue();
            transaction.InspectPending([root]).Should().BeEmpty();
            resolved = true;
        }
        finally
        {
            if (resolved) Directory.Delete(root, true);
            else Console.Error.WriteLine("Retained parent-creation crash evidence: " + root);
        }
    }

    [Test]
    public void RecoveryChildProcess()
    {
        var root = Environment.GetEnvironmentVariable("MORPHIR_RECOVERY_CHILD_ROOT");
        if (root is null) return;
        var window = Environment.GetEnvironmentVariable("MORPHIR_RECOVERY_CHILD_WINDOW")!;
        var paths = window == "parent-create" ? new[] { root + "/new/first.toml", root + "/new/second.toml" }
            : new[] { root + "/first.toml", root + "/second.toml" };
        var plan = new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)], paths.Select(path =>
            new ConfigurationFileChange(path, ConfigurationTransaction.Observe(path), Encoding.UTF8.GetBytes("candidate=true\n"))));
        IConfigurationTransactionHost host = window == "parent-create" ? new ParentExitHost() : new NativeConfigurationTransactionHost();
        var result = new ConfigurationTransaction(host, (phase, path) =>
        {
            var point = phase + ":" + (path == paths[0] ? "first" : path == paths[1] ? "second" : "any");
            if (point == window || phase == ConfigurationTransactionPhase.BeforeCleanup && window == "BeforeCleanup:any") Environment.Exit(79);
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        result.Code.Should().Be("config.edit_pending_recovery");
    }

    private sealed class ParentExitHost : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        public bool SupportsDurableRecovery => true;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination)
        {
            native.CreateParentDirectories(destination);
            Environment.Exit(79);
            return [];
        }
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => native.Stage(destination, content, transactionId, role);
        public void Install(string stagedPath, string destination, bool overwrite) => native.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => native.DeleteFile(path);
        public void DeleteDirectory(string path) => native.DeleteDirectory(path);
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => native.EnumerateTransactionArtifacts(directory, transactionId);
    }

    private static async Task<(int ExitCode, string Output)> Child(string root, string window)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(ConfigurationRecoveryProcessTests).Assembly.Location);
        start.ArgumentList.Add("--maximum-parallel-tests"); start.ArgumentList.Add("1");
        start.ArgumentList.Add("--treenode-filter"); start.ArgumentList.Add("/*/*/ConfigurationRecoveryProcessTests/RecoveryChildProcess");
        start.ArgumentList.Add("--results-directory"); start.ArgumentList.Add(root + "/child-results-" + Guid.NewGuid().ToString("N"));
        start.Environment["MORPHIR_RECOVERY_CHILD_ROOT"] = root;
        start.Environment["MORPHIR_RECOVERY_CHILD_WINDOW"] = window;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); throw; }
        return (process.ExitCode, await output + await errors);
    }
}
