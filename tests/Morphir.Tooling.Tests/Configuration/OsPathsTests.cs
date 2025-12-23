using FluentAssertions;
using Morphir.Tooling.Configuration;
using System.Runtime.InteropServices;

namespace Morphir.Tooling.Tests.Configuration;

public class OsPathsTests
{
    [Test]
    public void GetGlobalConfigDirectory_ShouldReturnValidPath()
    {
        // Act
        var path = OsPaths.GetGlobalConfigDirectory();

        // Assert
        path.Should().NotBeNullOrWhiteSpace("global config directory should be set");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            path.Should().Contain("Morphir", "Windows path should use capitalized Morphir");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            path.Should().Contain("morphir", "macOS path should contain 'morphir'");
            path.Should().Contain("Library/Application Support", "macOS path should use Application Support");
        }
        else
        {
            // Linux
            path.Should().Contain("morphir", "Linux path should contain 'morphir'");
            path.Should().Match(p => p.Contains(".config") || p.Contains("XDG"),
                "Linux path should use .config or XDG");
        }
    }

    [Test]
    public void GetGlobalCacheDirectory_ShouldReturnValidPath()
    {
        // Act
        var path = OsPaths.GetGlobalCacheDirectory();

        // Assert
        path.Should().NotBeNullOrWhiteSpace("global cache directory should be set");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            path.Should().Contain("Morphir", "Windows path should contain 'Morphir'");
            path.Should().Contain("Cache", "Windows path should contain 'Cache'");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            path.Should().Contain("morphir", "macOS path should contain 'morphir'");
            path.Should().Contain("Library/Caches", "macOS path should use Caches");
        }
        else
        {
            // Linux
            path.Should().Contain("morphir", "Linux path should contain 'morphir'");
            path.Should().Contain("cache", "Linux path should contain 'cache'");
        }
    }

    [Test]
    public void GlobalConfigAndCachePaths_ShouldBeDifferent()
    {
        // Act
        var configPath = OsPaths.GetGlobalConfigDirectory();
        var cachePath = OsPaths.GetGlobalCacheDirectory();

        // Assert
        configPath.Should().NotBe(cachePath, "config and cache directories should be different");
    }
}
