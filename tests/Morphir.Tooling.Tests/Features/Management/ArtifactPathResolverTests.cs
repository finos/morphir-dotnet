using FluentAssertions;
using Morphir.Tooling.Features.Management;
using Morphir.Tooling.Configuration;

namespace Morphir.Tooling.Tests.Features.Management;

public class ArtifactPathResolverTests : IDisposable
{
    private readonly string _tempWorkspace;
    private readonly ArtifactPathResolver _resolver;

    public ArtifactPathResolverTests()
    {
        // Create a temp workspace for testing
        _tempWorkspace = Path.Combine(Path.GetTempPath(), $"morphir-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempWorkspace);
        _resolver = new ArtifactPathResolver(_tempWorkspace);
    }

    public void Dispose()
    {
        // Cleanup temp workspace
        if (Directory.Exists(_tempWorkspace))
        {
            Directory.Delete(_tempWorkspace, recursive: true);
        }
    }

    [Test]
    public void Constructor_WithoutWorkspace_ShouldOnlyHaveGlobalRoot()
    {
        // Arrange & Act
        var resolver = new ArtifactPathResolver();

        // Assert
        resolver.HasLocalRoot.Should().BeFalse("Resolver without workspace should not have local root");
        resolver.GlobalRoot.Should().NotBeNullOrWhiteSpace("Global root should always be set");
        resolver.LocalRoot.Should().BeNull("Local root should be null without workspace");
    }

    [Test]
    public void Constructor_WithWorkspace_ShouldHaveBothRoots()
    {
        // Assert
        _resolver.HasLocalRoot.Should().BeTrue("Resolver with workspace should have local root");
        _resolver.GlobalRoot.Should().NotBeNullOrWhiteSpace("Global root should always be set");
        _resolver.LocalRoot.Should().NotBeNullOrWhiteSpace("Local root should be set with workspace");
        _resolver.LocalRoot.Should().Contain(".morphir", "Local root should be in .morphir directory");
    }

    [Test]
    public void GetArtifactRoot_ForDist_ShouldReturnCorrectPath()
    {
        // Act
        var globalRoot = _resolver.GetArtifactRoot(ArtifactType.Dist, useLocal: false);
        var localRoot = _resolver.GetArtifactRoot(ArtifactType.Dist, useLocal: true);

        // Assert
        globalRoot.Should().EndWith("dist", "Dist root should end with 'dist'");
        localRoot.Should().Contain(".morphir", "Local root should contain .morphir");
        localRoot.Should().EndWith("dist", "Local dist root should end with 'dist'");
        globalRoot.Should().NotBe(localRoot, "Global and local roots should be different");
    }

    [Test]
    public void GetArtifactRoot_ForTool_ShouldReturnCorrectPath()
    {
        // Act
        var globalRoot = _resolver.GetArtifactRoot(ArtifactType.Tool, useLocal: false);
        var localRoot = _resolver.GetArtifactRoot(ArtifactType.Tool, useLocal: true);

        // Assert
        globalRoot.Should().EndWith("tool", "Tool root should end with 'tool'");
        localRoot.Should().EndWith("tool", "Local tool root should end with 'tool'");
    }

    [Test]
    public void GetArtifactRoot_ForExtension_ShouldReturnCorrectPath()
    {
        // Act
        var globalRoot = _resolver.GetArtifactRoot(ArtifactType.Extension, useLocal: false);
        var localRoot = _resolver.GetArtifactRoot(ArtifactType.Extension, useLocal: true);

        // Assert
        globalRoot.Should().EndWith("extension", "Extension root should end with 'extension'");
        localRoot.Should().EndWith("extension", "Local extension root should end with 'extension'");
    }

    [Test]
    public void GetArtifactPath_ForDist_ShouldIncludePlatformAndVersion()
    {
        // Act
        var path = _resolver.GetArtifactPath(
            ArtifactType.Dist,
            platform: "linux-x64",
            version: "1.0.0",
            useLocal: false);

        // Assert
        path.Should().Contain("dist", "Path should contain 'dist'");
        path.Should().Contain("linux-x64", "Path should contain platform");
        path.Should().Contain("1.0.0", "Path should contain version");
    }

    [Test]
    public void GetArtifactPath_ForTool_ShouldIncludePlatformNameAndVersion()
    {
        // Act
        var path = _resolver.GetArtifactPath(
            ArtifactType.Tool,
            platform: "linux-x64",
            version: "2.1.0",
            name: "mytool",
            useLocal: false);

        // Assert
        path.Should().Contain("tool", "Path should contain 'tool'");
        path.Should().Contain("linux-x64", "Path should contain platform");
        path.Should().Contain("mytool", "Path should contain tool name");
        path.Should().Contain("2.1.0", "Path should contain version");
    }

    [Test]
    public void GetArtifactPath_ForToolWithoutName_ShouldThrowException()
    {
        // Act
        var act = () => _resolver.GetArtifactPath(
            ArtifactType.Tool,
            platform: "linux-x64",
            version: "1.0.0",
            name: null,
            useLocal: false);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Name is required*");
    }

    [Test]
    public void GetManifestPath_ShouldReturnPathWithManifestJson()
    {
        // Act
        var path = _resolver.GetManifestPath(
            ArtifactType.Dist,
            platform: "linux-x64",
            version: "1.0.0",
            useLocal: false);

        // Assert
        path.Should().EndWith("manifest.json", "Manifest path should end with manifest.json");
        path.Should().Contain("dist", "Manifest path should contain artifact type");
        path.Should().Contain("linux-x64", "Manifest path should contain platform");
        path.Should().Contain("1.0.0", "Manifest path should contain version");
    }

    [Test]
    public void GetBinPath_ShouldReturnPathWithBin()
    {
        // Act
        var path = _resolver.GetBinPath(
            ArtifactType.Dist,
            platform: "linux-x64",
            version: "1.0.0",
            useLocal: false);

        // Assert
        path.Should().EndWith("bin", "Bin path should end with 'bin'");
        path.Should().Contain("dist", "Bin path should contain artifact type");
        path.Should().Contain("linux-x64", "Bin path should contain platform");
        path.Should().Contain("1.0.0", "Bin path should contain version");
    }

    [Test]
    public void SetActiveVersion_ShouldCreateSelectionFile()
    {
        // Act
        _resolver.SetActiveVersion(
            ArtifactType.Dist,
            version: "1.0.0",
            useLocal: true); // Use local to avoid global interference

        // Assert
        var activeVersion = _resolver.GetActiveVersion(ArtifactType.Dist, useLocal: true);
        activeVersion.Should().Be("1.0.0", "Active version should be set correctly");
    }

    [Test]
    public void SetActiveVersion_ForTool_ShouldCreateSelectionFile()
    {
        // Act
        _resolver.SetActiveVersion(
            ArtifactType.Tool,
            version: "2.1.0",
            name: "mytool",
            useLocal: true); // Use local to avoid global interference

        // Assert
        var activeVersion = _resolver.GetActiveVersion(ArtifactType.Tool, name: "mytool", useLocal: true);
        activeVersion.Should().Be("2.1.0", "Active tool version should be set correctly");
    }

    [Test]
    public void GetActiveVersion_WhenNotSet_ShouldReturnNull()
    {
        // Act - use a unique artifact name to avoid interference
        var activeVersion = _resolver.GetActiveVersion(ArtifactType.Tool, name: "nonexistent-tool-unique", useLocal: true);

        // Assert
        activeVersion.Should().BeNull("Active version should be null when not set");
    }

    [Test]
    public void ResolveActiveArtifact_WhenLocalAndGlobalSet_ShouldPreferLocal()
    {
        // Arrange
        var platform = "linux-x64";
        var localVersion = "2.0.0";
        var globalVersion = "1.0.0";

        // Create local artifact directory
        var localPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, localVersion, useLocal: true);
        Directory.CreateDirectory(localPath);

        // Create global artifact directory (using temp path for testing)
        var globalPath = _resolver.GetArtifactPath(ArtifactType.Dist, platform, globalVersion, useLocal: false);
        Directory.CreateDirectory(globalPath);

        // Set both versions
        _resolver.SetActiveVersion(ArtifactType.Dist, localVersion, useLocal: true);
        _resolver.SetActiveVersion(ArtifactType.Dist, globalVersion, useLocal: false);

        // Act
        var resolved = _resolver.ResolveActiveArtifact(ArtifactType.Dist, platform);

        // Assert
        resolved.Should().NotBeNull("Should resolve to an artifact");
        resolved.Should().Be(localPath, "Should prefer local over global");
    }
}
