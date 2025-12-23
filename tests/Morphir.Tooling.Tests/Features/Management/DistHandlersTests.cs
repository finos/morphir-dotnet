using FluentAssertions;
using Morphir.Tooling.Features.Management;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;

namespace Morphir.Tooling.Tests.Features.Management;

public class DistHandlersTests : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly ArtifactPathResolver _resolver;
    private readonly HttpClient _httpClient;

    public DistHandlersTests()
    {
        _tempWorkspace = Path.Combine(Path.GetTempPath(), $"morphir-dist-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempWorkspace);
        _resolver = new ArtifactPathResolver(_tempWorkspace);
        _httpClient = new HttpClient();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkspace))
        {
            Directory.Delete(_tempWorkspace, recursive: true);
        }
        _httpClient.Dispose();
    }

    [Test]
    public async Task HandleList_WithNoDistributions_ShouldReturnEmptyList()
    {
        // Arrange
        var command = new DistList(Platform: "linux-x64", Local: true);

        // Act
        var result = await DistHandlers.HandleList(command, _resolver);

        // Assert
        result.Distributions.Should().BeEmpty("No distributions are installed");
        result.Platform.Should().Be("linux-x64");
        result.IsLocal.Should().BeTrue();
    }

    [Test]
    public async Task HandleList_WithInstalledDistributions_ShouldReturnList()
    {
        // Arrange
        var platform = "linux-x64";
        var version = "1.0.0";

        // Create a fake distribution directory
        var distPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, version, useLocal: true);
        Directory.CreateDirectory(distPath);

        // Create a manifest
        var manifestPath = _resolver.GetManifestPath(ArtifactType.Dist, platform, version, useLocal: true);
        var manifest = new Manifest
        {
            Name = "test-dist",
            Version = version,
            Platform = platform,
            Description = "Test distribution",
            InstalledAt = DateTime.UtcNow
        };
        var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson);

        var command = new DistList(Platform: platform, Local: true);

        // Act
        var result = await DistHandlers.HandleList(command, _resolver);

        // Assert
        result.Distributions.Should().HaveCount(1);
        result.Distributions[0].Version.Should().Be(version);
        result.Distributions[0].Description.Should().Be("Test distribution");
        result.Distributions[0].IsActive.Should().BeFalse("No active version set yet");
    }

    [Test]
    public async Task HandleList_WithActiveVersion_ShouldMarkActive()
    {
        // Arrange
        var platform = "linux-x64";
        var version = "1.0.0";

        var distPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, version, useLocal: true);
        Directory.CreateDirectory(distPath);

        _resolver.SetActiveVersion(ArtifactType.Dist, version, useLocal: true);

        var command = new DistList(Platform: platform, Local: true);

        // Act
        var result = await DistHandlers.HandleList(command, _resolver);

        // Assert
        result.Distributions.Should().HaveCount(1);
        result.Distributions[0].IsActive.Should().BeTrue("Version should be marked as active");
    }

    [Test]
    public async Task HandleUse_WithExistingDistribution_ShouldSetActive()
    {
        // Arrange
        var platform = "linux-x64";
        var version = "1.0.0";

        var distPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, version, useLocal: true);
        Directory.CreateDirectory(distPath);

        var command = new DistUse(Version: version, Platform: platform, Local: true);

        // Act
        var result = await DistHandlers.HandleUse(command, _resolver);

        // Assert
        result.Success.Should().BeTrue();
        result.Version.Should().Be(version);
        result.Platform.Should().Be(platform);

        var activeVersion = _resolver.GetActiveVersion(ArtifactType.Dist, useLocal: true);
        activeVersion.Should().Be(version);
    }

    [Test]
    public async Task HandleUse_WithNonExistentDistribution_ShouldFail()
    {
        // Arrange
        var command = new DistUse(Version: "99.99.99", Platform: "linux-x64", Local: true);

        // Act
        var result = await DistHandlers.HandleUse(command, _resolver);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not installed");
    }

    [Test]
    public async Task HandleRemove_WithExistingDistribution_ShouldRemove()
    {
        // Arrange
        var platform = "linux-x64";
        var version = "1.0.0";

        var distPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, version, useLocal: true);
        Directory.CreateDirectory(distPath);

        var command = new DistRemove(Version: version, Platform: platform, Local: true);

        // Act
        var result = await DistHandlers.HandleRemove(command, _resolver);

        // Assert
        result.Success.Should().BeTrue();
        Directory.Exists(distPath).Should().BeFalse("Distribution directory should be removed");
    }

    [Test]
    public async Task HandleRemove_WithNonExistentDistribution_ShouldFail()
    {
        // Arrange
        var command = new DistRemove(Version: "99.99.99", Platform: "linux-x64", Local: true);

        // Act
        var result = await DistHandlers.HandleRemove(command, _resolver);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not installed");
    }

    [Test]
    public async Task HandleWhich_WithNoActiveVersion_ShouldReturnNotFound()
    {
        // Arrange - create a new isolated workspace
        var isolatedWorkspace = Path.Combine(Path.GetTempPath(), $"morphir-isolated-{Guid.NewGuid()}");
        Directory.CreateDirectory(isolatedWorkspace);
        var isolatedResolver = new ArtifactPathResolver(isolatedWorkspace);
        
        try
        {
            var command = new DistWhich(Platform: "linux-x64");

            // Act
            var result = await DistHandlers.HandleWhich(command, isolatedResolver);

            // Assert
            result.Found.Should().BeFalse();
            result.Version.Should().BeNull();
            result.ErrorMessage.Should().Contain("No active distribution");
        }
        finally
        {
            if (Directory.Exists(isolatedWorkspace))
            {
                Directory.Delete(isolatedWorkspace, recursive: true);
            }
        }
    }

    [Test]
    public async Task HandleWhich_WithActiveVersion_ShouldReturnInfo()
    {
        // Arrange
        var platform = "linux-x64";
        var version = "1.0.0";

        var distPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, version, useLocal: true);
        Directory.CreateDirectory(distPath);

        _resolver.SetActiveVersion(ArtifactType.Dist, version, useLocal: true);

        var command = new DistWhich(Platform: platform);

        // Act
        var result = await DistHandlers.HandleWhich(command, _resolver);

        // Assert
        result.Found.Should().BeTrue();
        result.Version.Should().Be(version);
        result.Platform.Should().Be(platform);
        result.Path.Should().NotBeNullOrWhiteSpace();
        result.IsLocal.Should().BeTrue();
    }

    [Test]
    public async Task HandleList_WithDefaultPlatform_ShouldUseCurrentRid()
    {
        // Arrange
        var command = new DistList(Platform: null, Local: true);

        // Act
        var result = await DistHandlers.HandleList(command, _resolver);

        // Assert
        result.Platform.Should().NotBeNullOrWhiteSpace();
        Morphir.Tooling.Infrastructure.RuntimeIdentifier.IsValidRid(result.Platform).Should().BeTrue();
    }
}
