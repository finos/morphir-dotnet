// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.FSharp.Collections;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using Morphir.Tooling.Features.ProjectMigration;

internal static class ProjectMigrationSmoke
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Migration smoke: " + message);
            checks++;
        }
        const string classic = "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Main'],custom:{password:'secret-literal',reference:{env:'SECRET_NAME'}}}\r\n";
        foreach (var layout in new[] { "morphir.toml", ".morphir/morphir.toml", ".config/morphir/config.toml" })
        {
            var memory = new InMemoryProjectTree("/repo", new Dictionary<string, string> { ["/repo/morphir.json"] = classic });
            var preview = new ProjectMigrationService(memory).Plan("/repo", outputPath: layout);
            Check(preview.CanApply && preview.Verification!.Verified, "read-only layout not independently verified");
            Check(preview.Presentation!.Edits.Any(e => e.Value.Equals(ConfigValue.NewString("[REDACTED]"))), "safe plan omitted redaction");
            var json = ProjectMigrationReport.From(preview, "preview", true).ToJson();
            Check(!json.Contains("secret-literal", StringComparison.Ordinal) && !json.Contains("SECRET_NAME", StringComparison.Ordinal)
                && json.Contains("[REDACTED]", StringComparison.Ordinal), "generated report leaked canonical secrets");
            using var report = System.Text.Json.JsonDocument.Parse(json);
            Check(report.RootElement.GetProperty("verification").GetProperty("verified").GetBoolean(), "generated report lost verification");
            Check(!memory.ObservedPaths.Any(p => p.Contains(".user.", StringComparison.Ordinal)), "read personal overlay");
        }
        var controlledSource = ConfigurationReader.ReadText("/repo/morphir.json",
            "{name:'Orders',exposedModules:[],profiles:{default:['dev']}}");
        var controlledTarget = new SourceIdentity("/repo/morphir.toml", DocumentFormat.Toml, SourceRole.RootPrimary, 0, null);
        var controlledPlan = ProjectMigration.plan(controlledSource, controlledTarget, DocumentDefaults.emptyValue);
        Check(ProjectMigration.verify(controlledSource, controlledTarget, controlledPlan.Candidate,
            DocumentDefaults.resolutionInput, "/repo").Verified, "unchanged profile declarations rejected");
        var missingControls = ConfigTree.remove("profiles", controlledPlan.Candidate);
        Check(!ProjectMigration.verify(controlledSource, controlledTarget, missingControls,
            DocumentDefaults.resolutionInput, "/repo").Verified, "lost profile declarations accepted");
        var invalidKey = ConfigurationReader.ReadText("/repo/morphir.json",
            "{name:'Orders',exposedModules:[],custom:{'\\ud800':{nested:'okay'}}}");
        Check(ProjectMigration.plan(invalidKey, controlledTarget, DocumentDefaults.emptyValue).Blocked,
            "invalid ancestor key escaped loss accounting");
        foreach (var includeRootProject in new[] { false, true })
        {
            var files = new Dictionary<string, string>
            {
                ["/repo/morphir.toml"] = "[workspace]\nmembers=['other']\n",
                ["/repo/app/morphir.json"] = classic,
                ["/repo/other/morphir.json"] = "{name:'Other',exposedModules:[]}"
            };
            if (includeRootProject) files["/repo/morphir.json"] = "{name:'Root',exposedModules:[]}";
            var unlisted = new ProjectMigrationService(new InMemoryProjectTree("/repo", files)).Plan("/repo", "app");
            Check(!unlisted.CanApply && unlisted.Diagnostics.Any(d => d.Code == "migration.selection"),
                "unlisted input selected another project");
        }
        foreach (var layout in new[] { "morphir.json", ".morphir/morphir.json", ".config/morphir/config.json" })
        {
            var files = new Dictionary<string, string> { ["/repo/" + layout] = classic };
            var explicitPreview = new ProjectMigrationService(new InMemoryProjectTree("/repo", files)).Plan("/repo", layout);
            Check(explicitPreview.CanApply && explicitPreview.ProjectPath == "/repo", "explicit primary changed the project anchor");
        }
        var root = ProjectPath.normalize(Path.Combine(Path.GetTempPath(), "migration-smoke-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var recovery = false;
        ConfigurationTransactionResult Apply(ProjectMigrationService service, ProjectMigrationPreview preview)
        {
            var result = service.Apply(preview);
            recovery |= result.Status == ConfigurationTransactionStatus.RecoveryRequired;
            return result;
        }
        try
        {
            File.WriteAllText(root + "/morphir.toml", "[workspace]\nmembers=['app']\nout_dir='base-out'\n[pipeline]\nbackend='shared'\n");
            Directory.CreateDirectory(root + "/app");
            var source = root + "/app/morphir.json";
            File.WriteAllText(source, classic);
            var original = File.ReadAllBytes(source);
            var service = new ProjectMigrationService(new FileSystemProjectTree(root), new NativeConfigurationTransactionHost());
            var preview = service.Plan(root + "/app", outputPath: ".config/morphir/config.toml");
            Check(preview.CanApply && preview.Verification!.OriginChanges.Any(), "member equivalence or origins lost");
            Check(!File.Exists(preview.TargetPath), "preview wrote target");
            Check(Apply(service, preview).Status == ConfigurationTransactionStatus.Written, "verified apply failed");
            Check(File.ReadAllBytes(source).SequenceEqual(original), "classic bytes changed");
            var target = preview.TargetPath!;
            var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(target, timestamp);
            var repeat = service.Plan(root + "/app", outputPath: ".config/morphir/config.toml");
            Check(repeat.CanApply && !repeat.Changed && Apply(service, repeat).Status == ConfigurationTransactionStatus.Unchanged,
                "repeat was not an unchanged success");
            Check(File.GetLastWriteTimeUtc(target) == timestamp, "repeat rewrote target");
            var stale = service.Plan(root + "/app", outputPath: ".config/morphir/config.toml");
            File.AppendAllText(root + "/morphir.toml", "# drift\n");
            Check(Apply(service, stale).Status == ConfigurationTransactionStatus.Conflict, "workspace drift was not checked");
            File.WriteAllText(source, "{name:'Orders',exposedModules:[],decorations:{unsupported:true}}\n");
            var blocked = service.Plan(root + "/app", outputPath: ".config/morphir/config.toml");
            Check(!blocked.CanApply && Apply(service, blocked).Code == "migration.unverified_plan", "loss plan was applied");
            Check(!new ProjectMigrationService(new FileSystemProjectTree(root)).Plan(root, "morphir-ir.json").CanApply, "IR input accepted");
        }
        finally
        {
            if (recovery) Console.Error.WriteLine("Retained migration recovery evidence at " + root);
            else Directory.Delete(root, true);
        }
        return checks;
    }
}
