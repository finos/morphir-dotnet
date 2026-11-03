// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

[Binding]
public sealed class ProjectMigrationSteps : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "migration-bdd-" + Guid.NewGuid().ToString("N"));
    private const string Classic = "{\"name\":\"Orders\",\"sourceDirectory\":\"elm-src\",\"exposedModules\":[\"Main\"]}\n";
    private ProjectMigrationPreview? preview;

    [Given("a classic project migration source")]
    public void GivenClassic() { Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "morphir.json"), Classic); }

    [When("a read-only host previews its project migration")]
    public void Preview() => preview = new ProjectMigrationService(new FileSystemProjectTree(root)).Plan(root);

    [Then("the project migration is verified with no source mutation")]
    public void Verified()
    {
        preview!.CanApply.Should().BeTrue();
        preview.Verification!.Verified.Should().BeTrue();
        File.Exists(Path.Combine(root, "morphir.toml")).Should().BeFalse();
        SourceUnchanged();
    }

    [When("the host applies its project migration")]
    public void Apply()
    {
        var service = new ProjectMigrationService(new FileSystemProjectTree(root), new NativeConfigurationTransactionHost());
        preview = service.Plan(root);
        service.Apply(preview).Status.Should().Be(ConfigurationTransactionStatus.Written);
    }

    [Then("the original classic project bytes remain unchanged")]
    public void SourceUnchanged() => File.ReadAllText(Path.Combine(root, "morphir.json")).Should().Be(Classic);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
