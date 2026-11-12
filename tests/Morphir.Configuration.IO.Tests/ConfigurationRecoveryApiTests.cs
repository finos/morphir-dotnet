// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ConfigurationRecoveryApiTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("relative")]
    public void InvalidJournalPathsReturnMetadataWithoutAttemptingAWrite(string? path)
    {
        var transaction = new ConfigurationTransaction();
        transaction.InspectRecovery(path!).Valid.Should().BeFalse();
        transaction.Recover(path!).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
    }

    [Test]
    [Category("PlatformSpecific")]
    public void UnrelatedDirectoryCannotBePresentedAsAnUncertainCreationReceipt()
    {
        using var fixture = new RecoveryFixture();
        new ConfigurationTransaction().Recover(fixture.Journal, [fixture.Root]).Code.Should().Be("config.recovery_invalid_options");
        File.ReadAllText(fixture.Target).Should().Be("candidate=true\n");
        fixture.Resolve();
    }

    [Test]
    [Category("PlatformSpecific")]
    public void AHostWithoutArtifactEnumerationCanInspectButCannotRecover()
    {
        using var fixture = new RecoveryFixture();
        var transaction = new ConfigurationTransaction(new RecoveryHost("enumeration"));
        transaction.InspectRecovery(fixture.Journal).Valid.Should().BeTrue();
        transaction.Recover(fixture.Journal).Code.Should().Be("config.recovery_host_capability");
        File.ReadAllText(fixture.Target).Should().Be("candidate=true\n");
        fixture.Resolve();
    }

    [Test]
    [Category("PlatformSpecific")]
    [Arguments(ConfigurationTransactionPhase.BeforeRollback, ConfigurationTransactionStatus.RecoveryRequired)]
    [Arguments(ConfigurationTransactionPhase.AfterRollback, ConfigurationTransactionStatus.RolledBack)]
    public void ExplicitRecoveryFaultsReportOnlyProvenRestoration(ConfigurationTransactionPhase phase, ConfigurationTransactionStatus expected)
    {
        using var fixture = new RecoveryFixture();
        var result = new ConfigurationTransaction(fault: (current, _) => { if (current == phase) throw new IOException("injected recovery boundary"); }).Recover(fixture.Journal);
        result.Status.Should().Be(expected);
        if (expected == ConfigurationTransactionStatus.RecoveryRequired)
        {
            result.UnresolvedPaths.Should().Contain(fixture.Target);
            File.ReadAllText(fixture.Target).Should().Be("candidate=true\n");
            fixture.Resolve();
        }
        else File.ReadAllText(fixture.Target).Should().Be("original=true\n");
    }

    [Test]
    [Category("PlatformSpecific")]
    public void RecoveryCoordinationFailureRetainsTheJournalAndCandidate()
    {
        using var fixture = new RecoveryFixture();
        new ConfigurationTransaction(new RecoveryHost("acquire")).Recover(fixture.Journal).Code.Should().Be("config.recovery_io");
        File.Exists(fixture.Journal).Should().BeTrue();
        File.ReadAllText(fixture.Target).Should().Be("candidate=true\n");
        fixture.Resolve();
    }

    [Test]
    [Category("PlatformSpecific")]
    public void NativeHostRefusesBlockedParentsAndMissingStageDirectoriesWithoutChangingThem()
    {
        var root = ProjectPath.join(Path.GetTempPath(), "native-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(root + "/block", "user-owned");
            var host = new NativeConfigurationTransactionHost();
            Action create = () => host.CreateParentDirectories(root + "/block/target.toml");
            create.Should().Throw<IOException>();
            File.ReadAllText(root + "/block").Should().Be("user-owned");
            Action stage = () => host.Stage(root + "/absent/target.toml", Encoding.UTF8.GetBytes("value=true\n"), Guid.NewGuid().ToString("N"), "stage");
            stage.Should().Throw<IOException>();
            Directory.Exists(root + "/absent").Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RecoveryFixture : IDisposable
    {
        public string Root { get; } = ProjectPath.join(Path.GetTempPath(), "recovery-api-" + Guid.NewGuid().ToString("N"));
        public string Target => Root + "/morphir.toml";
        public string Journal { get; }
        public RecoveryFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Target, "original=true\n");
            var failed = new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("injected fixture interruption");
            }).Apply(new ConfigurationTransactionPlan([], [new ConfigurationFileChange(Target, ConfigurationTransaction.Observe(Target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
            failed.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            Journal = failed.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
        }
        public void Resolve() => new ConfigurationTransaction().Recover(Journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        public void Dispose()
        {
            if (Directory.EnumerateFileSystemEntries(Root, ".morphir-edit-*", SearchOption.AllDirectories).Any()) Console.Error.WriteLine("Retained API recovery evidence: " + Root);
            else Directory.Delete(Root, true);
        }
    }

    private sealed class RecoveryHost(string mode) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost inner = new();
        public bool SupportsDurableRecovery => true;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => inner.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => inner.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => mode == "acquire" ? throw new IOException("injected coordinator failure") : inner.Acquire(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => mode == "enumeration" ? null : inner.EnumerateTransactionArtifacts(directory, transactionId);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => throw new InvalidOperationException("Recovery created directories");
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => throw new InvalidOperationException("Recovery staged bytes");
        public void Install(string stagedPath, string destination, bool overwrite) => inner.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
    }
}
