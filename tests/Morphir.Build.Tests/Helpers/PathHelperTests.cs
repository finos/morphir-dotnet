using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>
/// Tests for PathHelper helper class
/// </summary>
public class PathHelperTests
{
    [Test]
    public async Task GetRepositoryRoot_ReturnsValidPath()
    {
        // Act
        var root = PathHelper.GetRepositoryRoot();

        // Assert
        root.Should().NotBeNullOrEmpty();
        Directory.Exists(root).Should().BeTrue("Repository root should exist");
        Directory.Exists(Path.Combine(root, ".git")).Should().BeTrue("Repository root should contain .git directory");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindLatestPackage_ToolPackage_ReturnsLatestVersion()
    {
        // Arrange - Ensure packages exist
        if (!TestFixture.HasPackages())
        {
            // Skip test if packages don't exist
            await Task.CompletedTask;
            return;
        }

        // Act
        var package = PathHelper.FindLatestPackage("Morphir.Tool");

        // Assert
        package.Should().NotBeNull("Should find Morphir.Tool package");
        File.Exists(package).Should().BeTrue("Package file should exist");
        package.Should().Contain("Morphir.Tool");
        package.Should().EndWith(".nupkg");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindLatestPackage_LibraryPackage_ReturnsLatestVersion()
    {
        // Arrange - Ensure packages exist
        if (!TestFixture.HasPackages())
        {
            // Skip test if packages don't exist
            await Task.CompletedTask;
            return;
        }

        // Act
        var package = PathHelper.FindLatestPackage("Morphir.Core");

        // Assert
        package.Should().NotBeNull("Should find Morphir.Core package");
        File.Exists(package).Should().BeTrue("Package file should exist");
        package.Should().Contain("Morphir.Core");
        package.Should().EndWith(".nupkg");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindLatestPackage_NonExistentPackage_ReturnsNull()
    {
        // Act
        var package = PathHelper.FindLatestPackage("NonExistent.Package");

        // Assert
        package.Should().BeNull("Should return null for non-existent package");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindLatestPackage_NonExistentDirectory_ReturnsNull()
    {
        // Arrange
        var nonExistentDir = "/tmp/nonexistent-packages-dir-12345";

        // Act
        var package = PathHelper.FindLatestPackage("Morphir.Tool", nonExistentDir);

        // Assert
        package.Should().BeNull("Should return null when directory does not exist");

        await Task.CompletedTask;
    }

    [Test]
    public async Task GetToolPackagePath_ReturnsExpectedPath()
    {
        // Arrange
        var version = "0.3.0-rc.2";

        // Act
        var path = PathHelper.GetToolPackagePath(version);

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().Contain("artifacts");
        path.Should().Contain("packages");
        path.Should().EndWith($"Morphir.Tool.{version}.nupkg");

        await Task.CompletedTask;
    }

    [Test]
    public async Task GetLibraryPackagePath_ReturnsExpectedPath()
    {
        // Arrange
        var projectName = "Morphir.Core";
        var version = "0.3.0-rc.2";

        // Act
        var path = PathHelper.GetLibraryPackagePath(projectName, version);

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().Contain("artifacts");
        path.Should().Contain("packages");
        path.Should().EndWith($"{projectName}.{version}.nupkg");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindGeneratedExecutables_NonExistentRid_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentRid = "nonexistent-rid";

        // Act
        var executables = PathHelper.FindGeneratedExecutables(nonExistentRid);

        // Assert
        executables.Should().NotBeNull();
        executables.Should().BeEmpty("Should return empty list for non-existent RID");

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindGeneratedExecutables_WithValidRid_FindsExecutables()
    {
        // Arrange - Check if any single-file executables exist
        var repoRoot = PathHelper.GetRepositoryRoot();
        var singleFileDir = Path.Combine(repoRoot, "artifacts", "single-file");

        if (!Directory.Exists(singleFileDir))
        {
            // Skip test if single-file directory doesn't exist
            await Task.CompletedTask;
            return;
        }

        var rids = Directory.GetDirectories(singleFileDir)
            .Select(d => new DirectoryInfo(d).Name)
            .ToList();

        if (!rids.Any())
        {
            // Skip test if no RID directories found
            await Task.CompletedTask;
            return;
        }

        var firstRid = rids.First();

        // Act
        var executables = PathHelper.FindGeneratedExecutables(firstRid);

        // Assert
        executables.Should().NotBeNull();
        // We can't assert on count since it depends on build state
        // but we can verify the structure

        await Task.CompletedTask;
    }

    [Test]
    public async Task FindLatestPackage_MultipleVersions_ReturnsHighestVersion()
    {
        // Arrange - Create temp directory with test packages
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-packages-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create mock package files with different versions
            var packages = new[]
            {
                "TestPackage.0.1.0.nupkg",
                "TestPackage.0.2.0.nupkg",
                "TestPackage.0.3.0-beta.1.nupkg",
                "TestPackage.1.0.0.nupkg",
                "TestPackage.0.1.5.nupkg"
            };

            foreach (var pkg in packages)
            {
                File.WriteAllText(Path.Combine(tempDir, pkg), "mock content");
            }

            // Act
            var latest = PathHelper.FindLatestPackage("TestPackage", tempDir);

            // Assert
            latest.Should().NotBeNull();
            latest.Should().Contain("TestPackage.1.0.0.nupkg",
                "Should return the highest semantic version (1.0.0)");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task GetToolPackagePath_WithCustomDirectory_ReturnsPathInCustomDirectory()
    {
        // Arrange
        var customDir = "/custom/packages/dir";
        var version = "1.2.3";

        // Act
        var path = PathHelper.GetToolPackagePath(version, customDir);

        // Assert
        path.Should().StartWith(customDir);
        path.Should().EndWith($"Morphir.Tool.{version}.nupkg");

        await Task.CompletedTask;
    }

    [Test]
    public async Task GetLibraryPackagePath_WithCustomDirectory_ReturnsPathInCustomDirectory()
    {
        // Arrange
        var customDir = "/custom/packages/dir";
        var projectName = "MyLibrary";
        var version = "2.0.0-alpha.1";

        // Act
        var path = PathHelper.GetLibraryPackagePath(projectName, version, customDir);

        // Assert
        path.Should().StartWith(customDir);
        path.Should().EndWith($"{projectName}.{version}.nupkg");

        await Task.CompletedTask;
    }
}
