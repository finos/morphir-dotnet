// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Morphir.Configuration;
using Morphir.Configuration.IO;

internal static class RecoveryReceiptSmoke
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Recovery receipt smoke: " + message); checks++; }
        foreach (var mode in new[] { "missing", "persisted", "disconnected", "rejected" })
        {
            var root = ProjectPath.join(Path.GetTempPath(), "receipt-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var target = root + "/morphir.toml";
            var foreign = root + "/user.txt";
            File.WriteAllText(target, "original=true\n"); File.WriteAllText(foreign, "user=true\n");
            try
            {
                var host = new ReceiptHost(mode, foreign);
                var result = new ConfigurationTransaction(host).Apply(new ConfigurationTransactionPlan([],
                    [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), Encoding.UTF8.GetBytes("candidate=true\n"))]));
                var committed = mode is "persisted" or "disconnected";
                Check(result.Status == (mode == "missing" ? ConfigurationTransactionStatus.RolledBack : ConfigurationTransactionStatus.RecoveryRequired), "receipt outcome lost");
                Check(File.ReadAllText(target) == (committed ? "candidate=true\n" : "original=true\n"), "commit boundary lost");
                var native = new ConfigurationTransaction();
                if (mode != "missing")
                {
                    var journal = result.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
                    if (mode == "rejected")
                    {
                        Check(native.Recover(journal).Status == ConfigurationTransactionStatus.RecoveryRequired && File.Exists(host.Rejected), "rejected evidence falsely completed");
                        Check(ProjectPath.contains(root, host.Rejected!) && File.ReadAllBytes(host.Rejected!).SequenceEqual(host.Bytes!)
                            && File.ReadAllText(foreign) == "user=true\n", "test disposition ownership failed");
                        File.Delete(host.Rejected!);
                    }
                    Check(native.Recover(journal).Status == (committed ? ConfigurationTransactionStatus.Written : ConfigurationTransactionStatus.RolledBack), "explicit recovery failed");
                }
                Check(native.InspectPending([root]).IsEmpty, "receipt recovery left guards");
            }
            finally
            {
                if (Directory.EnumerateFiles(root, ".morphir-edit-*").Any()) Console.Error.WriteLine("Retained receipt smoke evidence: " + root);
                else Directory.Delete(root, true);
            }
        }
        return checks;
    }

    private sealed class ReceiptHost(string mode, string foreign) : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost native = new();
        private bool disconnected;
        public string? Rejected { get; private set; }
        public byte[]? Bytes { get; private set; }
        public bool SupportsDurableRecovery => true;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => disconnected ? throw new IOException("disconnected") : native.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => native.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => native.Acquire(paths);
        public IReadOnlyList<string> CreateParentDirectories(string path) => native.CreateParentDirectories(path);
        public IReadOnlyList<string> CreateParentDirectories(string path, string id) => native.CreateParentDirectories(path, id);
        public string Stage(string destination, ReadOnlyMemory<byte> content, string id, string role)
        {
            if (mode == "missing" && role == "journal-committed") throw new IOException("not persisted");
            var path = native.Stage(destination, content, id, role);
            if (mode == "rejected" && role == "journal") { Rejected = path; Bytes = content.ToArray(); return foreign; }
            if (mode == "disconnected" && role == "journal-committed") { disconnected = true; throw new IOException("receipt lost"); }
            if (mode == "persisted" && role == "journal-committed") throw new ConfigurationArtifactFailureException([new(path, ConfigurationArtifactKind.Journal, id)]);
            return path;
        }
        public void Install(string stage, string destination, bool overwrite) => native.Install(stage, destination, overwrite);
        public void DeleteFile(string path) => native.DeleteFile(path);
        public void DeleteDirectory(string path) => native.DeleteDirectory(path);
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => native.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string id) => native.EnumerateTransactionArtifacts(directory, id);
    }
}
