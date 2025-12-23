using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Morphir.Tooling.Configuration;

namespace Morphir.Tooling.Tests.Configuration;

public class WorkspaceDiscoveryTests
{
    private readonly WorkspaceDiscovery _sut;
    private readonly string _testRoot;

    public WorkspaceDiscoveryTests()
    {
        _sut = new WorkspaceDiscovery(NullLogger<WorkspaceDiscovery>.Instance);
        _testRoot = Path.Combine(Path.GetTempPath(), $"morphir-test-{Guid.NewGuid()}");
    }

    [Test]
    public async Task DiscoverWorkspaceRoot_ShouldReturnNull_WhenNoWorkspaceFound()
    {
        // Arrange
        Directory.CreateDirectory(_testRoot);
        try
        {
            // Act
            var result = _sut.DiscoverWorkspaceRoot(_testRoot);

            // Assert
            result.Should().BeNull("no workspace markers should be found");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverWorkspaceRoot_ShouldFindGitRoot()
    {
        // Arrange
        Directory.CreateDirectory(_testRoot);
        var gitDir = Path.Combine(_testRoot, ".git");
        Directory.CreateDirectory(gitDir);

        try
        {
            // Act
            var result = _sut.DiscoverWorkspaceRoot(_testRoot);

            // Assert
            result.Should().Be(_testRoot, "should find Git root");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverWorkspaceRoot_ShouldFindMorphirDirectory()
    {
        // Arrange
        Directory.CreateDirectory(_testRoot);
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        try
        {
            // Act
            var result = _sut.DiscoverWorkspaceRoot(_testRoot);

            // Assert
            result.Should().Be(_testRoot, "should find .morphir/ directory");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverWorkspaceRoot_ShouldPreferGitRoot_WhenBothExist()
    {
        // Arrange
        Directory.CreateDirectory(_testRoot);
        var gitDir = Path.Combine(_testRoot, ".git");
        Directory.CreateDirectory(gitDir);
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);

        try
        {
            // Act
            var result = _sut.DiscoverWorkspaceRoot(_testRoot);

            // Assert
            result.Should().Be(_testRoot, "should prefer Git root when both exist at same level");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverWorkspaceRoot_ShouldFindParentGitRoot()
    {
        // Arrange
        Directory.CreateDirectory(_testRoot);
        var gitDir = Path.Combine(_testRoot, ".git");
        Directory.CreateDirectory(gitDir);
        var subDir = Path.Combine(_testRoot, "sub", "nested");
        Directory.CreateDirectory(subDir);

        try
        {
            // Act
            var result = _sut.DiscoverWorkspaceRoot(subDir);

            // Assert
            result.Should().Be(_testRoot, "should walk up to find Git root");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverWorkspaceRoot_ShouldFindParentMorphirDirectory()
    {
        // Arrange
        Directory.CreateDirectory(_testRoot);
        var morphirDir = Path.Combine(_testRoot, ".morphir");
        Directory.CreateDirectory(morphirDir);
        var subDir = Path.Combine(_testRoot, "sub", "nested");
        Directory.CreateDirectory(subDir);

        try
        {
            // Act
            var result = _sut.DiscoverWorkspaceRoot(subDir);

            // Assert
            result.Should().Be(_testRoot, "should walk up to find .morphir/ directory");
        }
        finally
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
