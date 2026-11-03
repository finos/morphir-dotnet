using System.Text;
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class ConfigurationBatchRecoverySteps : IDisposable
{
    private readonly string root = ProjectPath.join(Path.GetTempPath(), "batch-recovery-bdd-" + Guid.NewGuid().ToString("N"));
    private ConfigurationTransactionResult? failed;
    private bool resolved;
    private string Target => root + "/morphir.toml";
    private const string Candidate = "[project]\nname='App'\n";

    [Given("a recoverable configuration batch")]
    public void GivenBatch()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(root + "/morphir.json", "{name:'App',exposedModules:[]}\n");
    }

    [When("the host stages the complete batch")]
    public void Stage() => new ConfigurationBatchTransactionTests().EntireBatchStagesBeforeTheFirstReplacement();

    [Then("no replacement starts before all candidates are staged")]
    public void Staged() => resolved = true;

    [When("a batch replacement and its rollback both fail")]
    public void FailBatch()
    {
        failed = new ConfigurationTransaction(fault: (phase, _) =>
        {
            if (phase is ConfigurationTransactionPhase.AfterInstall or ConfigurationTransactionPhase.BeforeRollback) throw new IOException("injected");
        }).Apply(new ConfigurationTransactionPlan([ConfigurationTransaction.Observe(root)],
            [new ConfigurationFileChange(Target, ConfigurationTransaction.Observe(Target), Encoding.UTF8.GetBytes(Candidate))]));
        failed.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
    }

    [Then("shared configuration editing and project migration refuse overlapping writes")]
    public void SharedGuards()
    {
        var host = new NativeConfigurationTransactionHost();
        var tree = new FileSystemProjectTree(root);
        var editing = new ConfigurationEditService(tree, host);
        var preview = editing.Plan(root, new EditRequest(EditOperation.NewSet("true", EditValueType.Boolean), "custom.changed"));
        preview.Failure.Should().BeNull();
        editing.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        var migration = new ProjectMigrationService(tree, host);
        var project = migration.Plan(root);
        project.CanApply.Should().BeTrue(string.Join(",", project.Diagnostics.Select(d => d.Code)));
        migration.Apply(project).Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        File.ReadAllText(Target).Should().Be(Candidate);
    }

    [Then("explicit shared recovery restores the originals and clears the guard")]
    public void Recover()
    {
        var journal = failed!.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
        var transaction = new ConfigurationTransaction();
        transaction.InspectRecovery(journal).Valid.Should().BeTrue();
        transaction.Recover(journal).Status.Should().Be(ConfigurationTransactionStatus.RolledBack);
        File.Exists(Target).Should().BeFalse();
        transaction.InspectPending([root]).Should().BeEmpty();
        resolved = true;
    }

    [When("the user changes an installed target")]
    public void ChangeUser() => File.WriteAllText(Target, "user=true\n");

    [Then("shared recovery reports the changed target and preserves its journal")]
    public void PreserveUser()
    {
        var journal = failed!.Artifacts.First(a => a.Kind == ConfigurationArtifactKind.Journal).Path;
        var result = new ConfigurationTransaction().Recover(journal);
        result.Status.Should().Be(ConfigurationTransactionStatus.RecoveryRequired);
        result.UnresolvedPaths.Should().Contain(Target);
        File.ReadAllText(Target).Should().Be("user=true\n");
        File.Exists(journal).Should().BeTrue();
        // Restore only the fixture-owned candidate after checking user drift is preserved.
        File.WriteAllText(Target, Candidate);
        Recover();
    }

    public void Dispose()
    {
        if (resolved) Directory.Delete(root, true);
        else Console.Error.WriteLine("Retained batch recovery fixture: " + root);
    }
}
