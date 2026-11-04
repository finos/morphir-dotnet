// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Tooling.Tests.Features.WorkspaceMigration;

/// <summary>A real interrupted process supplies a durable directory-ownership uncertainty fixture.</summary>
[Category("WorkspaceMigrationRecoveryWorker")]
public sealed class WorkspaceMigrationRecoveryWorkerTests
{
    [Test]
    public void ChildProcess()
    {
        var root = Environment.GetEnvironmentVariable("MORPHIR_WORKSPACE_RECOVERY_CHILD_ROOT");
        if (root is null) return;
        root = ProjectPath.normalize(root);
        var target = root + "/new/target.toml";
        var expected = ConfigurationTransaction.Observe(target);
        new ConfigurationTransaction(new InterruptedDirectoryHost()).Apply(new ConfigurationTransactionPlan(
            [ConfigurationTransaction.Observe(root), expected],
            [new ConfigurationFileChange(target, expected, Encoding.UTF8.GetBytes("candidate=true\n"))]));
        throw new InvalidOperationException("The controlled child must stop after creating its parent directory");
    }

    private sealed class InterruptedDirectoryHost : IConfigurationTransactionHost
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
}
