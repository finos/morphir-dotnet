// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using FluentAssertions;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ConfigurationRecoveryReceiptTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void CommitReceiptFailureRespectsWhetherTheWitnessPersisted(bool persisted)
    {
        using var fixture = new Fixture(persisted ? "persisted-commit" : "missing-commit");
        var result = new ConfigurationTransaction(fixture.Host).Apply(fixture.Plan());
        if (persisted)
        {
            result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
            File.ReadAllText(fixture.Target).Should().Be(Fixture.Candidate, "a durable commit witness prevents rollback");
            var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.Written);
        }
        else
        {
            result.Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
            File.ReadAllText(fixture.Target).Should().Be(Fixture.Original);
        }
        new ConfigurationTransaction().InspectPending([fixture.Root]).Should().BeEmpty();
        fixture.Resolved = true;
    }

    [Test]
    [Arguments("journal")]
    [Arguments("stage")]
    public void RejectedPersistedArtifactsRemainRecoveryBlockersUntilDisposition(string role)
    {
        using var fixture = new Fixture("foreign-" + role);
        var result = new ConfigurationTransaction(fixture.Host).Apply(fixture.Plan());
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
        var recovery = new ConfigurationTransaction().Recover(journal);
        recovery.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired, "rejected evidence is still present");
        recovery.UnresolvedPaths.Should().Contain(fixture.Host.CreatedPath);
        File.Exists(fixture.Host.CreatedPath).Should().BeTrue();
        new ConfigurationTransaction().Apply(fixture.Plan(Fixture.Original)).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        File.ReadAllText(fixture.Foreign).Should().Be("user=true\n");
        // Explicit test-owned disposition after proving the rejected file's exact origin and bytes.
        ProjectPath.contains(fixture.Root, fixture.Host.CreatedPath!).Should().BeTrue();
        File.ReadAllBytes(fixture.Host.CreatedPath!).Should().Equal(fixture.Host.CreatedBytes!);
        File.Delete(fixture.Host.CreatedPath!);
        new ConfigurationTransaction().Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        new ConfigurationTransaction().InspectPending([fixture.Root]).Should().BeEmpty();
        fixture.Resolved = true;
    }

    [Test]
    [Arguments("stage")]
    [Arguments("journal-committed")]
    public void DisconnectionAfterDurableStageReturnsTypedRecoverableMetadata(string role)
    {
        using var fixture = new Fixture("disconnect-" + role);
        var result = new ConfigurationTransaction(fixture.Host).Apply(fixture.Plan());
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        result.Artifacts.Should().Contain(a => a.Kind == ConfigurationArtifactKind.Journal);
        result.UnresolvedPaths.Should().Contain(fixture.Target);
        var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
        var recovered = new ConfigurationTransaction().Recover(journal);
        recovered.Status.Should().Be(role == "stage" ? ConfigurationTransactionStatus.RolledBack : ConfigurationTransactionStatus.Written);
        File.ReadAllText(fixture.Target).Should().Be(role == "stage" ? Fixture.Original : Fixture.Candidate);
        new ConfigurationTransaction().InspectPending([fixture.Root]).Should().BeEmpty();
        fixture.Resolved = true;
    }

    private sealed class Fixture : IDisposable
    {
        public const string Original = "original=true\n";
        public const string Candidate = "candidate=true\n";
        public string Root { get; } = ProjectPath.join(Path.GetTempPath(), "recovery-receipt-" + Guid.NewGuid().ToString("N"));
        public string Target => Root + "/morphir.toml";
        public string Foreign => Root + "/user.txt";
        public ReceiptHost Host { get; }
        public bool Resolved { get; set; }
        public Fixture(string mode)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Target, Original);
            File.WriteAllText(Foreign, "user=true\n");
            Host = new(mode, Foreign);
        }
        public ConfigurationTransactionPlan Plan(string content = Candidate) => new([],
            [new ConfigurationFileChange(Target, ConfigurationTransaction.Observe(Target), Encoding.UTF8.GetBytes(content))]);
        public void Dispose()
        {
            if (Resolved) Directory.Delete(Root, true);
            else Console.Error.WriteLine("Retained receipt regression evidence: " + Root);
        }
    }

    private sealed class ReceiptHost(string mode, string foreign) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        private bool disconnected;
        public string? CreatedPath { get; private set; }
        public byte[]? CreatedBytes { get; private set; }
        public bool SupportsDurableRecovery => true;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) =>
            disconnected ? throw new IOException("mount disconnected") : native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string path) => native.CreateParentDirectories(path);
        public IReadOnlyList<string> CreateParentDirectories(string path, string id) => native.CreateParentDirectories(path, id);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string id, string role)
        {
            if (mode == "missing-commit" && role == "journal-committed") throw new IOException("commit not persisted");
            var path = native.Stage(destination, content, id, role);
            if (mode == "foreign-" + role)
            { CreatedPath = path; CreatedBytes = content.ToArray(); return foreign; }
            if (mode == "disconnect-" + role)
            { disconnected = true; throw new IOException("receipt lost after persistence"); }
            if (mode == "persisted-commit" && role == "journal-committed")
                throw new ConfigurationArtifactFailureException([new(path, ConfigurationArtifactKind.Journal, id)]);
            return path;
        }
        public void Install(string stage, string target, bool overwrite) => native.Install(stage, target, overwrite);
        public void DeleteFile(string path) => native.DeleteFile(path);
        public void DeleteDirectory(string path) => native.DeleteDirectory(path);
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string id) => native.EnumerateTransactionArtifacts(directory, id);
    }
}
