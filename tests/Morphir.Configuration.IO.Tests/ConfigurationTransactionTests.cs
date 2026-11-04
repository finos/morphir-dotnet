using System.Text;
using System.Diagnostics;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ConfigurationTransactionTests
{
    [Test]
    public void InstallsOneFileAndCleansAllOwnedArtifacts()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction().Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllText(target).Should().Be("value = 2\n");
        Directory.GetFileSystemEntries(directory.Path).Select(ProjectPath.normalize).Should().Equal(target);
        result.Artifacts.Should().BeEmpty();
    }

    [Test]
    public void FailureAfterReplacementRestoresOriginalAndCleansArtifacts()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.AfterInstall) throw new IOException("injected replacement fault");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        File.ReadAllText(target).Should().Be("value = 1\n");
        Directory.GetFileSystemEntries(directory.Path).Select(ProjectPath.normalize).Should().Equal(target);
        result.Artifacts.Should().BeEmpty();
    }

    [Test]
    [Arguments("source")]
    [Arguments("alternate")]
    public void RechecksEveryObservationAfterStagingBeforeInstallation(string changedInput)
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        var source = directory.PathFor("morphir.yaml");
        var alternate = directory.PathFor(".morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        File.WriteAllText(source, "value: lower\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed, ConfigurationTransaction.Observe(source), ConfigurationTransaction.Observe(alternate)],
            [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.BeforeInstall)
                File.WriteAllText(changedInput == "source" ? source : alternate, "value = 3\n");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Conflict);
        File.ReadAllText(target).Should().Be("value = 1\n");
        result.Artifacts.Should().BeEmpty();
        Directory.EnumerateFiles(directory.Path, ".morphir-edit-*").Should().BeEmpty();
    }

    [Test]
    public void NativeObservationNeverReadsThroughLinkedAncestors()
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        var physical = directory.PathFor("physical");
        Directory.CreateDirectory(physical);
        File.WriteAllText(ProjectPath.join(physical, "morphir.toml"), "value = 1\n");
        var alias = directory.PathFor("alias");
        Directory.CreateSymbolicLink(alias, physical);
        ConfigurationTransaction.Observe(ProjectPath.join(alias, "morphir.toml")).Kind.Should().Be(ConfigurationObservationKind.Link);
    }

    [Test]
    public void NewlyCreatedParentAndStagesDoNotInvalidateDirectoryEvidence()
    {
        using var directory = new TestDirectory();
        var parent = directory.PathFor("new-settings");
        var target = ProjectPath.join(parent, "morphir.toml");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(directory.Path), ConfigurationTransaction.Observe(parent), observed],
            [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction().Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.ReadAllText(target).Should().Be("value = 2\n");
        Directory.GetFileSystemEntries(parent).Select(ProjectPath.normalize).Should().Equal(target);
    }

    [Test]
    public async Task OverlappingNativeWritersAreSerializedAndTheStaleWriterConflicts()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var first = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var second = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 3\n"))]);
        using var firstStaging = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        using var secondStaging = new ManualResetEventSlim();
        var firstTask = Task.Run(() => new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase != ConfigurationTransactionPhase.BeforeStage) return;
            firstStaging.Set();
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(10))) throw new IOException("test release timeout");
        }).Apply(first));
        firstStaging.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        var secondTask = Task.Run(() =>
        {
            secondStarted.Set();
            return new ConfigurationTransaction(fault: (phase, _) =>
            {
                if (phase == ConfigurationTransactionPhase.BeforeStage) secondStaging.Set();
            }).Apply(second);
        });
        try
        {
            secondStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            secondStaging.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse("the second writer must wait outside staging");
        }
        finally { releaseFirst.Set(); }
        (await firstTask).Status.Should().Be(ConfigurationTransactionStatus.Written);
        (await secondTask).Status.Should().Be(ConfigurationTransactionStatus.Conflict);
        File.ReadAllText(target).Should().Be("value = 2\n");
    }

    [Test]
    public async Task SeparateNativeProcessesUseTheSameCoordinator()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        var ready = directory.PathFor("child-ready");
        var entered = directory.PathFor("child-staging");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(typeof(ConfigurationTransactionTests).Assembly.Location);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/ConfigurationTransactionTests/NativeWriterChildProcess");
        start.ArgumentList.Add("--maximum-parallel-tests");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(directory.PathFor("child-results"));
        start.Environment["MORPHIR_CONFIGURATION_TX_CHILD"] = target;
        start.Environment["MORPHIR_CONFIGURATION_TX_IDENTITY"] = observed.Identity;
        start.Environment["MORPHIR_CONFIGURATION_TX_READY"] = ready;
        start.Environment["MORPHIR_CONFIGURATION_TX_ENTERED"] = entered;
        using var process = new Process { StartInfo = start };
        Task<string> output;
        Task<string> errors;
        using (new NativeConfigurationTransactionHost().Acquire([target]))
        {
            process.Start().Should().BeTrue();
            output = process.StandardOutput.ReadToEndAsync();
            errors = process.StandardError.ReadToEndAsync();
            SpinWait.SpinUntil(() => File.Exists(ready) || process.HasExited, TimeSpan.FromSeconds(30)).Should().BeTrue();
            File.Exists(ready).Should().BeTrue("the child must reach the writer boundary");
            Thread.Sleep(200);
            File.Exists(entered).Should().BeFalse("the child must wait outside staging while this process owns the lease");
            var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
            new ConfigurationTransaction().Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.Written);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); throw; }
        process.ExitCode.Should().Be(0, (await output) + (await errors));
        File.Exists(entered).Should().BeFalse("the stale child must conflict before staging");
        File.ReadAllText(target).Should().Be("value = 2\n");
    }

    [Test]
    public void NativeWriterChildProcess()
    {
        var target = Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_TX_CHILD");
        if (target is null) return;
        var expected = new ConfigurationObservation(target, ConfigurationObservationKind.File,
            Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_TX_IDENTITY")!);
        var plan = new ConfigurationTransactionPlan([expected], [new ConfigurationFileChange(target, expected, Encoding.UTF8.GetBytes("value = 3\n"))]);
        File.WriteAllText(Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_TX_READY")!, "ready");
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.BeforeStage)
                File.WriteAllText(Environment.GetEnvironmentVariable("MORPHIR_CONFIGURATION_TX_ENTERED")!, "entered");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Conflict);
    }

    [Test]
    public void IncompleteRollbackRetainsTypedOwnedBackupAndRecoveryJournal()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback)
                throw new IOException("injected fault");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        File.ReadAllText(target).Should().Be("value = 2\n");
        var backup = result.Artifacts.Single(a => a.Kind == ConfigurationArtifactKind.Backup);
        File.ReadAllText(backup.Path).Should().Be("value = 1\n");
        var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal);
        journal.TransactionId.Should().Be(backup.TransactionId);
        var inspection = new ConfigurationTransaction().InspectRecovery(journal.Path);
        inspection.Valid.Should().BeTrue();
        inspection.TransactionId.Should().Be(backup.TransactionId);
        inspection.Targets.Should().Equal(target);
        inspection.Artifacts.Should().Contain(backup);
        new ConfigurationTransaction().Recover(journal.Path).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        File.ReadAllText(target).Should().Be("value = 1\n");
    }

    [Test]
    public void CleanupFailureReturnsTypedRecoveryAndRetainsEvidence()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.BeforeCleanup) throw new IOException("injected cleanup fault");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        File.ReadAllText(target).Should().Be("value = 2\n");
        result.Artifacts.Should().Contain(a => a.Kind == ConfigurationArtifactKind.Backup);
        result.Artifacts.Should().Contain(a => a.Kind == ConfigurationArtifactKind.Journal);
    }

    [Test]
    public void ALinkedTargetAncestorCannotAuthorizeAWrite()
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        var physical = directory.PathFor("physical");
        Directory.CreateDirectory(physical);
        var alias = directory.PathFor("alias");
        Directory.CreateSymbolicLink(alias, physical);
        var target = ProjectPath.join(alias, "morphir.toml");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction().Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
        Directory.GetFileSystemEntries(physical).Should().BeEmpty();
    }

    [Test]
    [Arguments("target")]
    [Arguments("backup")]
    public void RecoveryRechecksTheTargetAndBackupBeforeRestoring(string tampered)
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.AfterInstall) throw new IOException("replacement fault");
            if (phase == ConfigurationTransactionPhase.BeforeRollback)
                File.WriteAllText(tampered == "target" ? target : Directory.GetFiles(directory.Path, ".morphir-edit-*-backup").Single(), "external = true\n");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        File.ReadAllText(target).Should().Be(tampered == "target" ? "external = true\n" : "value = 2\n");
        result.Artifacts.Should().Contain(a => a.Kind == ConfigurationArtifactKind.Journal);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void PartialStagingFailureHasExplicitArtifactOwnership(bool blockCleanup)
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var host = new PartialStageHost(blockCleanup);
        var result = new ConfigurationTransaction(host).Apply(plan);
        File.ReadAllText(target).Should().Be("value = 1\n");
        result.Status.Should().Be(blockCleanup ? ConfigurationTransactionStatus.RecoveryRequired : ConfigurationTransactionStatus.IoFailure);
        if (blockCleanup)
        {
            var artifact = result.Artifacts.Single();
            artifact.Kind.Should().Be(ConfigurationArtifactKind.Journal);
            artifact.TransactionId.Should().Be(host.Owner);
            File.Exists(artifact.Path).Should().BeTrue();
        }
        else
        {
            result.Artifacts.Should().BeEmpty();
            Directory.GetFileSystemEntries(directory.Path).Select(ProjectPath.normalize).Should().Equal(target);
        }
    }

    [Test]
    [Arguments(ConfigurationTransactionPhase.BeforeStage, ConfigurationTransactionStatus.IoFailure)]
    [Arguments(ConfigurationTransactionPhase.BeforeInstall, ConfigurationTransactionStatus.IoFailure)]
    [Arguments(ConfigurationTransactionPhase.AfterInstall, ConfigurationTransactionStatus.RolledBack)]
    [Arguments(ConfigurationTransactionPhase.AfterRollback, ConfigurationTransactionStatus.RolledBack)]
    public void FaultBoundariesHaveProvenOutcomesAndCleanArtifacts(ConfigurationTransactionPhase injected, ConfigurationTransactionStatus expected)
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == injected || (injected == ConfigurationTransactionPhase.AfterRollback && phase == ConfigurationTransactionPhase.AfterInstall))
                throw new IOException("injected fault");
        }).Apply(plan);
        result.Status.Should().Be(expected);
        File.ReadAllText(target).Should().Be("value = 1\n");
        result.Artifacts.Should().BeEmpty();
        Directory.GetFileSystemEntries(directory.Path).Select(ProjectPath.normalize).Should().Equal(target);
    }

    [Test]
    public void NativeReplacementPreservesExistingUnixPermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(target, permissions);
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        new ConfigurationTransaction().Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.Written);
        File.GetUnixFileMode(target).Should().Be(permissions);
    }

    [Test]
    public void TargetEvidenceCannotReferToAnotherFile()
    {
        using var directory = new TestDirectory();
        var source = directory.PathFor("other.toml");
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(source, "source = true\n");
        File.WriteAllText(target, "target = true\n");
        var observed = ConfigurationTransaction.Observe(source);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        new ConfigurationTransaction().Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
        File.ReadAllText(target).Should().Be("target = true\n");
        File.ReadAllText(source).Should().Be("source = true\n");
    }

    [Test]
    public void IdenticalBytesDoNotStageOrRewriteTheTarget()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        File.SetLastWriteTimeUtc(target, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        var before = File.GetLastWriteTimeUtc(target);
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, File.ReadAllBytes(target))]);
        var faults = 0;
        var result = new ConfigurationTransaction(fault: (_, _) => faults++).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Unchanged);
        File.GetLastWriteTimeUtc(target).Should().Be(before);
        faults.Should().Be(0);
        Directory.GetFileSystemEntries(directory.Path).Select(ProjectPath.normalize).Should().Equal(target);
    }

    [Test]
    public void EmbeddedHostAppliesAnImmutablePlanWithoutNativeFiles()
    {
        var host = new MemoryHost();
        const string target = "/memory/morphir.toml";
        host.Files[target] = Encoding.UTF8.GetBytes("value = 1\n");
        host.Files["/memory/morphir.yaml"] = Encoding.UTF8.GetBytes("value: lower\n");
        var observations = new List<ConfigurationObservation> { host.Observe("/memory"), host.Observe(target), host.Observe("/memory/morphir.yaml"), host.Observe("/memory/.morphir.toml") };
        var bytes = Encoding.UTF8.GetBytes("value = 2\n");
        var changes = new List<ConfigurationFileChange> { new(target, host.Observe(target), bytes) };
        var plan = new ConfigurationTransactionPlan(observations, changes);
        bytes[0] = (byte)'x';
        observations.Clear();
        changes.Clear();
        var result = new ConfigurationTransaction(host).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Written);
        Encoding.UTF8.GetString(host.Files[target]).Should().Be("value = 2\n");
        host.Files.Keys.Order(StringComparer.Ordinal).Should().Equal(new[] { target, "/memory/morphir.yaml" }.Order(StringComparer.Ordinal));
        result.Artifacts.Should().BeEmpty();
    }

    [Test]
    public void LaterFileFailureRollsBackEarlierFileWithoutClaimingGroupAtomicity()
    {
        using var directory = new TestDirectory();
        var first = directory.PathFor("first.toml");
        var second = directory.PathFor("second.toml");
        File.WriteAllText(first, "value = 1\n");
        File.WriteAllText(second, "value = 1\n");
        var observations = new[] { ConfigurationTransaction.Observe(directory.Path), ConfigurationTransaction.Observe(first), ConfigurationTransaction.Observe(second) };
        var plan = new ConfigurationTransactionPlan(observations,
            [new ConfigurationFileChange(first, observations[1], Encoding.UTF8.GetBytes("value = 2\n")), new ConfigurationFileChange(second, observations[2], Encoding.UTF8.GetBytes("value = 2\n"))]);
        var observedPartialInstallation = false;
        var result = new ConfigurationTransaction(fault: (phase, path) =>
        {
            if (phase == ConfigurationTransactionPhase.AfterInstall && path == first)
                observedPartialInstallation = File.ReadAllText(first) == "value = 2\n" && File.ReadAllText(second) == "value = 1\n";
            if (phase == ConfigurationTransactionPhase.BeforeInstall && path == second) throw new IOException("second installation failed");
        }).Apply(plan);
        observedPartialInstallation.Should().BeTrue();
        result.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        File.ReadAllText(first).Should().Be("value = 1\n");
        File.ReadAllText(second).Should().Be("value = 1\n");
        Directory.GetFiles(directory.Path, ".morphir-edit-*").Should().BeEmpty();
    }

    [Test]
    [Arguments("stage")]
    [Arguments("backup")]
    public void StagingIntegrityIsCheckedBeforeInstallation(string tampered)
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([observed], [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]);
        string? artifactPath = null;
        byte[]? original = null;
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase != ConfigurationTransactionPhase.BeforeInstall) return;
            artifactPath = ProjectPath.normalize(Directory.GetFiles(directory.Path, ".morphir-edit-*-" + tampered).Single());
            original = File.ReadAllBytes(artifactPath);
            File.WriteAllText(artifactPath, "tampered = true\n");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        File.ReadAllText(target).Should().Be("value = 1\n");
        File.ReadAllText(artifactPath!).Should().Be("tampered = true\n");
        result.Artifacts.Should().Contain(a => a.Path == artifactPath);
        File.WriteAllBytes(artifactPath!, original!);
        var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
        new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        Directory.GetFileSystemEntries(directory.Path).Select(ProjectPath.normalize).Should().Equal(target);
    }

    [Test]
    public void NativeInventoryIdentityUsesUnambiguousEntryEncoding()
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        File.WriteAllText(directory.PathFor("a"), "one");
        File.WriteAllText(directory.PathFor("b"), "two");
        var before = ConfigurationTransaction.Observe(directory.Path);
        File.Delete(directory.PathFor("a"));
        File.Delete(directory.PathFor("b"));
        File.WriteAllText(directory.PathFor("a:file\nb"), "three");
        ConfigurationTransaction.Observe(directory.Path).Identity.Should().NotBe(before.Identity);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void PartialParentCreationReportsOwnedDirectories(bool blockCleanup)
    {
        using var directory = new TestDirectory();
        var parent = directory.PathFor("new-settings");
        var target = ProjectPath.join(parent, "morphir.toml");
        var expected = ConfigurationTransaction.Observe(target);
        var host = new ParentFaultHost(blockCleanup);
        var result = new ConfigurationTransaction(host).Apply(new ConfigurationTransactionPlan([expected],
            [new ConfigurationFileChange(target, expected, Encoding.UTF8.GetBytes("value = 2\n"))]));
        result.Status.Should().Be(blockCleanup ? ConfigurationTransactionStatus.RecoveryRequired : ConfigurationTransactionStatus.IoFailure);
        File.Exists(target).Should().BeFalse();
        if (blockCleanup)
        {
            var artifact = result.Artifacts.Single(a => a.Kind == ConfigurationArtifactKind.Directory);
            result.Artifacts.Should().Contain(a => a.Kind == ConfigurationArtifactKind.Journal);
            artifact.Kind.Should().Be(ConfigurationArtifactKind.Directory);
            artifact.Path.Should().Be(parent);
            artifact.TransactionId.Should().Be(host.Owner);
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            new ConfigurationTransaction().Recover(journal, [parent]).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            Directory.Exists(parent).Should().BeTrue("explicitly reviewed retention never deletes the directory");
        }
        else Directory.Exists(parent).Should().BeFalse();
    }

    private sealed class ParentFaultHost(bool blockCleanup) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        public bool SupportsDurableRecovery => native.SupportsDurableRecovery;
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => native.EnumerateTransactionArtifacts(directory, transactionId);
        public string? Owner { get; private set; }
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination)
        {
            native.CreateParentDirectories(destination);
            throw new IOException("legacy parent callback lacks transaction ownership");
        }
        public IReadOnlyList<string> CreateParentDirectories(string destination, string transactionId)
        {
            var created = native.CreateParentDirectories(destination);
            Owner = transactionId;
            throw new ConfigurationArtifactFailureException(created.Select(path => new ConfigurationArtifact(path, ConfigurationArtifactKind.Directory, transactionId)));
        }
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => native.Stage(destination, content, transactionId, role);
        public void Install(string stagedPath, string destination, bool overwrite) => native.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => native.DeleteFile(path);
        public void DeleteDirectory(string path)
        {
            if (blockCleanup) throw new IOException("injected directory cleanup failure");
            native.DeleteDirectory(path);
        }
    }

    [Test]
    public void NewTargetRollbackRemovesOnlyItsOwnedFileAndDirectories()
    {
        using var directory = new TestDirectory();
        var source = directory.PathFor("morphir.yaml");
        File.WriteAllText(source, "value: lower\n");
        var parent = directory.PathFor("new-settings");
        var target = ProjectPath.join(parent, "morphir.toml");
        var expected = ConfigurationTransaction.Observe(target);
        var plan = new ConfigurationTransactionPlan([expected, ConfigurationTransaction.Observe(parent), ConfigurationTransaction.Observe(source)],
            [new ConfigurationFileChange(target, expected, Encoding.UTF8.GetBytes("value = 2\n"))]);
        var result = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.AfterInstall) throw new IOException("injected failure");
        }).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        File.Exists(target).Should().BeFalse();
        Directory.Exists(parent).Should().BeFalse();
        File.ReadAllText(source).Should().Be("value: lower\n");
        result.Artifacts.Should().BeEmpty();
    }

    [Test]
    public void AHostCannotTransferOwnershipOfAnotherTransactionsArtifact()
    {
        using var directory = new TestDirectory();
        var target = directory.PathFor("morphir.toml");
        File.WriteAllText(target, "value = 1\n");
        var observed = ConfigurationTransaction.Observe(target);
        var result = new ConfigurationTransaction(new PartialStageHost(false, true)).Apply(new ConfigurationTransactionPlan([observed],
            [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value = 2\n"))]));
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        result.Artifacts.Should().BeEmpty("unverified foreign paths never become owned artifacts");
        result.UnresolvedPaths.Should().NotBeEmpty();
        result.UnresolvedPaths.Should().OnlyContain(path => File.Exists(path));
        File.ReadAllText(target).Should().Be("value = 1\n");
    }

    [Test]
    public void TwoNewDestinationsRecheckTheirSharedInventoryAfterEachInstallation()
    {
        var host = new MemoryHost();
        var first = host.Observe("/memory/first.toml");
        var second = host.Observe("/memory/second.toml");
        var plan = new ConfigurationTransactionPlan([host.Observe("/memory"), first, second],
            [new ConfigurationFileChange(first.Path, first, Encoding.UTF8.GetBytes("value=1\n")), new ConfigurationFileChange(second.Path, second, Encoding.UTF8.GetBytes("value=2\n"))]);
        var result = new ConfigurationTransaction(host).Apply(plan);
        result.Status.Should().Be(ConfigurationTransactionStatus.Written);
        host.Files.Keys.Should().BeEquivalentTo([first.Path, second.Path]);
        result.Artifacts.Should().BeEmpty();
    }

    [Test]
    public void AFileAtTheParentLocationReturnsIoFailureWithoutChangingIt()
    {
        using var directory = new TestDirectory();
        var parent = directory.PathFor("settings");
        File.WriteAllText(parent, "retain original file");
        var target = ProjectPath.join(parent, "morphir.toml");
        var observed = ConfigurationTransaction.Observe(target);
        var result = new ConfigurationTransaction().Apply(new ConfigurationTransactionPlan([observed],
            [new ConfigurationFileChange(target, observed, Encoding.UTF8.GetBytes("value=2\n"))]));
        result.Status.Should().Be(ConfigurationTransactionStatus.IoFailure);
        File.ReadAllText(parent).Should().Be("retain original file");
        result.Artifacts.Should().BeEmpty();
    }

    [Test]
    public void MountedHostRecoversFromSerializedEvidenceInANewTransactionInstance()
    {
        var host = new MemoryHost();
        const string target = "/memory/morphir.toml";
        host.Files[target] = Encoding.UTF8.GetBytes("original=true\n");
        var plan = new ConfigurationTransactionPlan([host.Observe("/memory")],
            [new ConfigurationFileChange(target, host.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]);
        var interrupted = () => new ConfigurationTransaction(host, (phase, _) =>
        {
            if (phase == ConfigurationTransactionPhase.AfterInstall) throw new InvalidOperationException("simulated host interruption");
        }).Apply(plan);
        interrupted.Should().Throw<InvalidOperationException>();
        var restarted = new ConfigurationTransaction(host);
        var pending = restarted.InspectPending(["/memory"]);
        pending.Should().NotBeEmpty();
        pending.Should().OnlyContain(p => p.Valid);
        restarted.Apply(plan).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        restarted.Recover(pending[0].JournalPath).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        Encoding.UTF8.GetString(host.Files[target]).Should().Be("original=true\n");
        host.Files.Keys.Should().Equal(target);
    }

    private sealed class MemoryHost : IConfigurationTransactionHost
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public bool SupportsDurableRecovery => true;
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => Files.Keys.Where(p => Path.GetFileName(p).Contains("-journal", StringComparison.Ordinal)).ToArray();
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => Files.Keys.Where(p => ProjectPath.parent(p) == directory && Path.GetFileName(p).StartsWith(".morphir-edit-" + transactionId + "-", StringComparison.Ordinal)).ToArray();
        private readonly object coordination = new();
        private static string Hash(byte[] value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value));
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null)
        {
            if (Files.TryGetValue(path, out var value)) return new(path, ConfigurationObservationKind.File, Hash(value));
            if (path == "/memory") return new(path, ConfigurationObservationKind.Directory,
                Hash(Encoding.UTF8.GetBytes(string.Join("\n", Files.Keys.Where(p => excludedPaths is null || !excludedPaths.Contains(p)).Order(StringComparer.Ordinal)))));
            return new(path, ConfigurationObservationKind.Missing, "");
        }
        public byte[] ReadFile(string path) => Files[path].ToArray();
        public IDisposable Acquire(IReadOnlyList<string> paths)
        {
            Monitor.Enter(coordination);
            return new MemoryLease(coordination);
        }
        public IReadOnlyList<string> CreateParentDirectories(string destination) => [];
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role)
        {
            var path = ConfigurationTransaction.GetArtifactPath(destination, transactionId, role);
            Files[path] = content.ToArray();
            return path;
        }
        public void Install(string stagedPath, string destination, bool overwrite)
        {
            if (!overwrite && Files.ContainsKey(destination)) throw new IOException("Target already exists");
            Files[destination] = Files[stagedPath];
            Files.Remove(stagedPath);
        }
        public void DeleteFile(string path) => Files.Remove(path);
        public void DeleteDirectory(string path) { }
        private sealed class MemoryLease(object coordination) : IDisposable { public void Dispose() => Monitor.Exit(coordination); }
    }

    private sealed class PartialStageHost(bool blockCleanup, bool foreignOwner = false) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        public bool SupportsDurableRecovery => native.SupportsDurableRecovery;
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => native.EnumerateTransactionArtifacts(directory, transactionId);
        public string? Owner { get; private set; }
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string destination) => native.CreateParentDirectories(destination);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role)
        {
            var path = native.Stage(destination, content[..Math.Min(content.Length, 3)], transactionId, role);
            Owner = foreignOwner ? "another-transaction" : transactionId;
            var kind = role.StartsWith("journal", StringComparison.Ordinal) ? ConfigurationArtifactKind.Journal
                : role == "backup" ? ConfigurationArtifactKind.Backup : ConfigurationArtifactKind.Stage;
            throw new ConfigurationArtifactFailureException([new ConfigurationArtifact(path, kind, Owner)]);
        }
        public void Install(string stagedPath, string destination, bool overwrite) => native.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path)
        {
            if (blockCleanup) throw new IOException("injected cleanup failure");
            native.DeleteFile(path);
        }
        public void DeleteDirectory(string path) => native.DeleteDirectory(path);
    }

    private sealed class TestDirectory : IDisposable
    {
        // The shared transaction boundary requires canonical absolute paths on every platform.
        public string Path { get; } = ProjectPath.join(System.IO.Path.GetTempPath(), "morphir-transaction-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public string PathFor(string name) => ProjectPath.join(Path, name);
        public void Dispose()
        {
            if (Directory.GetFiles(Path, ".morphir-edit-*").Length > 0)
                Console.Error.WriteLine("Retained transaction fixture recovery evidence: " + Path);
            else Directory.Delete(Path, true);
        }
    }
}
