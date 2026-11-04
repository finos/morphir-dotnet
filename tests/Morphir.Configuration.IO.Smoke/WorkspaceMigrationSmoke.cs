// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Tooling.Features.WorkspaceMigration;

internal static class WorkspaceMigrationSmoke
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Workspace smoke: " + message); checks++; }
        const string classic = "{name:'A',sourceDirectory:'../source',exposedModules:[],custom:{password:'private-literal',ref:{env:'PRIVATE_REFERENCE'}}}";
        var files = new Dictionary<string, string> { ["/repo/a/.morphir/morphir.json"] = classic, ["/repo/b/morphir.json"] = "{name:'B',exposedModules:[]}" };
        var memory = new InMemoryProjectTree("/", files);
        var readOnly = new WorkspaceMigrationService(memory);
        var preview = readOnly.Plan("/repo");
        Check(preview.CanApply && preview.Verified && !preview.RootIsProject, "read-only aggregate verification");
        Check(preview.Members.SequenceEqual(new[] { "a", "b" }), "explicit sorted membership");
        Check(preview.Projects.All(p => p.Verification!.Verified && p.Verification.BaseWorkspace!.Value.Root == "/repo"), "common proposed workspace");
        Check(preview.Projects[0].ProjectPath == "/repo/a", "hidden primary owner anchor");
        Check(preview.AdoptionEffects.Any(e => e.Kind == "shared-output-root" && e.ProjectPath == "/repo/a"), "adoption effects missing");
        Check(memory.ObservedPaths.All(p => ProjectPath.contains("/repo", p)), "ancestor adoption scan");
        var json = WorkspaceMigrationReport.From(preview, "preview", true).ToJson();
        Check(!json.Contains("private-literal", StringComparison.Ordinal) && !json.Contains("PRIVATE_REFERENCE", StringComparison.Ordinal), "raw secret escaped redaction");
        using (var parsed = JsonDocument.Parse(json)) Check(parsed.RootElement.GetProperty("verified").GetBoolean(), "generated report verification lost");
        Check(readOnly.Apply(preview).Status == ConfigurationTransactionStatus.IoFailure, "read-only apply allowed");
        var inherited = new WorkspaceMigrationService(new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=[]\n[pipeline]\nbackend='root-specific'\n",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'\n"
        })).Plan("/repo");
        Check(!inherited.CanApply && inherited.Diagnostics.Any(d => d.Code == "workspace.migrate_adoption_value"), "existing workspace masked a new member value change");
        var adopted = new WorkspaceMigrationService(new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.toml"] = "[workspace]\nmembers=[]\nout_dir='shared'\n",
            ["/repo/app/morphir.toml"] = "[project]\nname='App'\n"
        })).Plan("/repo");
        Check(adopted.CanApply && adopted.AdoptionEffects.Any(e => e.Kind == "ownership" && e.Before == "/repo/app"), "original member ownership lost");
        Check(adopted.AdoptionEffects.Any(e => e.Kind == "shared-output-root" && e.Before == "/repo/app/.morphir/out"), "original member output lost");
        var excludedRoot = new WorkspaceMigrationService(new InMemoryProjectTree("/repo", new Dictionary<string, string>
        {
            ["/repo/morphir.json"] = "{name:'Root',exposedModules:[]}",
            ["/repo/app/morphir.json"] = "{name:'App',exposedModules:[]}"
        })).Plan("/repo", excludes: ["."]);
        Check(excludedRoot.CanApply && excludedRoot.RootIsProject && excludedRoot.Inventory.Any(i => i.RelativePath == "." && i.Kind == WorkspaceInventoryKind.Excluded)
            && excludedRoot.Projects.All(p => p.ProjectPath != "/repo"), "root exclusion did not preserve conversion boundary");
        var root = ProjectPath.join(Path.GetTempPath(), "workspace-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in files)
            {
                var path = ProjectPath.join(root, ProjectPath.relative("/repo", file.Key));
                Directory.CreateDirectory(ProjectPath.parent(path)); File.WriteAllText(path, file.Value);
            }
            var sourceBytes = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
            Directory.CreateDirectory(root + "/a/.morphir/out"); File.WriteAllText(root + "/a/.morphir/out/generated.json", "keep-generated");
            var service = new WorkspaceMigrationService(new FileSystemProjectTree(root), new NativeConfigurationTransactionHost());
            var plan = service.Plan(root);
            Check(plan.CanApply && plan.Projects.Length == 2, "native plan differs from mounted plan");
            Check(service.Apply(plan).Status == ConfigurationTransactionStatus.Written, "batch application failed");
            Check(sourceBytes.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "classic source bytes changed");
            Check(File.ReadAllText(root + "/a/.morphir/out/generated.json") == "keep-generated", "generated artifact moved");
            var targets = Directory.GetFiles(root, "*.toml", SearchOption.AllDirectories);
            var bytes = targets.ToDictionary(p => p, File.ReadAllBytes);
            var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var path in targets) File.SetLastWriteTimeUtc(path, timestamp);
            var repeat = service.Plan(root);
            Check(repeat.CanApply && !repeat.Changed, "repeat not unchanged");
            Check(repeat.Inventory.Count(i => i.Kind == WorkspaceInventoryKind.AlreadyMigrated) == 2, "migrated inventory not retained");
            Check(service.Apply(repeat).Status == ConfigurationTransactionStatus.Unchanged, "repeat rewrote files");
            Check(targets.All(p => File.ReadAllBytes(p).SequenceEqual(bytes[p]) && File.GetLastWriteTimeUtc(p) == timestamp), "no-op bytes/timestamps changed");
            var stale = service.Plan(root); File.AppendAllText(root + "/b/morphir.json", "\n");
            Check(service.Apply(stale).Status == ConfigurationTransactionStatus.Conflict, "source drift ignored");
            var transaction = new ConfigurationTransaction();
            var target = root + "/b/morphir.toml"; var original = File.ReadAllBytes(target);
            var candidate = Encoding.UTF8.GetBytes("[project]\nname='B'\n");
            var pending = new ConfigurationTransaction(fault: (phase, _) => { if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("smoke interruption"); })
                .Apply(new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)], [new ConfigurationFileChange(target, ConfigurationTransaction.Observe(target), candidate)]));
            Check(pending.Status == ConfigurationTransactionStatus.RecoveryRequired, "interruption falsely completed");
            var journal = pending.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            Check(transaction.InspectRecovery(journal).Valid, "durable inspection rejected journal");
            Check(transaction.InspectPending([root, target]).Length > 0, "durable pending index absent");
            var blocked = service.Plan(root);
            Check(!blocked.CanApply && blocked.PendingRecovery.Length > 0, "pending recovery preview applicable");
            var failureJson = WorkspaceMigrationReport.From(blocked, "recovery-required", true, 2).ToJson();
            using (var parsed = JsonDocument.Parse(failureJson)) Check(parsed.RootElement.GetProperty("exitCode").GetInt32() == 2, "recovery report lost exit status");
            Check(transaction.Recover(journal).Status == ConfigurationTransactionStatus.RolledBack, "explicit recovery did not restore batch");
            Check(File.ReadAllBytes(target).SequenceEqual(original), "recovery lost original bytes");
            Check(transaction.InspectPending([root, target]).IsEmpty, "recovery left pending state");
            Check(!Directory.EnumerateFileSystemEntries(root, ".morphir-edit-*", SearchOption.AllDirectories).Any(), "owned artifacts remain after recovery");
            var yamlRoot = root + "/yaml-workspace";
            Directory.CreateDirectory(yamlRoot + "/app");
            const string yaml = "workspace:\n  members: [app]\n";
            File.WriteAllText(yamlRoot + "/morphir.yaml", yaml);
            File.WriteAllText(yamlRoot + "/app/morphir.toml", "[project]\nname='YamlApp'\n");
            var yamlService = new WorkspaceMigrationService(new FileSystemProjectTree(yamlRoot), new NativeConfigurationTransactionHost());
            var yamlPreview = yamlService.Plan(yamlRoot);
            Check(yamlPreview.CanApply && !yamlPreview.Changed, "existing YAML workspace not a no-op");
            Check(yamlService.Apply(yamlPreview).Status == ConfigurationTransactionStatus.Unchanged, "YAML no-op wrote a target");
            Check(!File.Exists(yamlRoot + "/morphir.toml"), "YAML no-op created an empty TOML primary");
            Check(File.ReadAllText(yamlRoot + "/morphir.yaml") == yaml, "YAML source was rewritten");
            var uncertainTarget = root + "/uncertain/new/morphir.toml";
            var uncertain = new ConfigurationTransaction(new InterruptedParentHost()).Apply(new ConfigurationTransactionPlan(
                [ConfigurationTransaction.Observe(root)], [new ConfigurationFileChange(uncertainTarget, ConfigurationTransaction.Observe(uncertainTarget), Encoding.UTF8.GetBytes("value=true\n"))]));
            Check(uncertain.Status == ConfigurationTransactionStatus.RecoveryRequired, "uncertain directory receipt falsely completed");
            var uncertainJournal = uncertain.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
            var uncertainInspection = transaction.InspectRecovery(uncertainJournal);
            Check(uncertainInspection.Valid && uncertainInspection.UncertainDirectories.Length > 0, "directory uncertainty lost after inspection");
            Check(transaction.Recover(uncertainJournal).Status == ConfigurationTransactionStatus.RecoveryRequired, "default recovery assumed directory ownership");
            var preserved = transaction.Recover(uncertainJournal, uncertainInspection.UncertainDirectories);
            Check(preserved.Status == ConfigurationTransactionStatus.RolledBack && preserved.PreservedDirectories.SequenceEqual(uncertainInspection.UncertainDirectories), "explicit reviewed retention failed");
            Check(Directory.Exists(root + "/uncertain/new"), "recovery deleted an unproven directory");
            Check(transaction.InspectPending([root]).IsEmpty, "reviewed retention left a recovery guard");
            var lossTree = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/a/morphir.json"] = "{name:'A',exposedModules:[],decorations:{lost:true}}" });
            Check(!new WorkspaceMigrationService(lossTree).Plan("/repo").CanApply, "lossy batch accepted");
        }
        finally
        {
            if (Directory.EnumerateFileSystemEntries(root, ".morphir-edit-*", SearchOption.AllDirectories).Any()) Console.Error.WriteLine("Retained workspace smoke recovery: " + root);
            else Directory.Delete(root, true);
        }
        return checks;
    }

    private sealed class InterruptedParentHost : IConfigurationTransactionHost
    {
        private readonly NativeConfigurationTransactionHost inner = new();
        public bool SupportsDurableRecovery => true;
        public ConfigurationObservation Observe(string path, IReadOnlyCollection<string>? excludedPaths = null) => inner.Observe(path, excludedPaths);
        public byte[] ReadFile(string path) => inner.ReadFile(path);
        public IDisposable Acquire(IReadOnlyList<string> paths) => inner.Acquire(paths);
        public IReadOnlyList<string> FindRecoveryJournals(IReadOnlyList<string> paths) => inner.FindRecoveryJournals(paths);
        public IReadOnlyList<string>? EnumerateTransactionArtifacts(string directory, string transactionId) => inner.EnumerateTransactionArtifacts(directory, transactionId);
        public IReadOnlyList<string> CreateParentDirectories(string destination)
        {
            inner.CreateParentDirectories(destination);
            throw new IOException("injected post-mkdir receipt interruption");
        }
        public string Stage(string destination, ReadOnlyMemory<byte> content, string transactionId, string role) => inner.Stage(destination, content, transactionId, role);
        public void Install(string stagedPath, string destination, bool overwrite) => inner.Install(stagedPath, destination, overwrite);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
    }
}
