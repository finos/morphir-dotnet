using FluentAssertions;
using Morphir.Tooling.Configuration;

namespace Morphir.Tooling.Tests.Configuration;

public class TomlParserTests
{
    private readonly string _testRoot;

    public TomlParserTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"morphir-toml-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
    }

    [Test]
    public void ParseConfigFile_ShouldReturnNull_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testRoot, "nonexistent.toml");

        // Act
        var result = TomlParser.ParseConfigFile(nonExistentPath);

        // Assert
        result.Should().BeNull("file does not exist");
    }

    [Test]
    public void ParseConfigFile_ShouldParseEmptyFile()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "empty.toml");
        File.WriteAllText(filePath, "");

        try
        {
            // Act
            var result = TomlParser.ParseConfigFile(filePath);

            // Assert
            result.Should().NotBeNull("empty file should parse as default config");
            result!.Cache.WorkspaceCache.Should().Be(Microsoft.FSharp.Core.FSharpOption<string>.None);
            result.Cache.GlobalCache.Should().Be(Microsoft.FSharp.Core.FSharpOption<string>.None);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public void ParseConfigFile_ShouldParseCacheConfiguration_InMorphirSection()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "with-cache.toml");
        File.WriteAllText(filePath, @"
[morphir.cache]
workspace = ""/custom/workspace/cache""
global = ""/custom/global/cache""
");

        try
        {
            // Act
            var result = TomlParser.ParseConfigFile(filePath);

            // Assert
            result.Should().NotBeNull();
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result!.Cache.WorkspaceCache).Should().BeTrue();
            result.Cache.WorkspaceCache.Value.Should().Be("/custom/workspace/cache");
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Cache.GlobalCache).Should().BeTrue();
            result.Cache.GlobalCache.Value.Should().Be("/custom/global/cache");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public void ParseConfigFile_ShouldParseCacheConfiguration_InRootCacheSection()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "root-cache.toml");
        File.WriteAllText(filePath, @"
[cache]
workspace = ""/root/workspace/cache""
global = ""/root/global/cache""
");

        try
        {
            // Act
            var result = TomlParser.ParseConfigFile(filePath);

            // Assert
            result.Should().NotBeNull();
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result!.Cache.WorkspaceCache).Should().BeTrue();
            result.Cache.WorkspaceCache.Value.Should().Be("/root/workspace/cache");
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result.Cache.GlobalCache).Should().BeTrue();
            result.Cache.GlobalCache.Value.Should().Be("/root/global/cache");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public void ParseConfigFile_ShouldHandlePartialCacheConfiguration()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "partial-cache.toml");
        File.WriteAllText(filePath, @"
[morphir.cache]
workspace = ""/only/workspace/cache""
");

        try
        {
            // Act
            var result = TomlParser.ParseConfigFile(filePath);

            // Assert
            result.Should().NotBeNull();
            Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(result!.Cache.WorkspaceCache).Should().BeTrue();
            result.Cache.WorkspaceCache.Value.Should().Be("/only/workspace/cache");
            result.Cache.GlobalCache.Should().Be(Microsoft.FSharp.Core.FSharpOption<string>.None);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
