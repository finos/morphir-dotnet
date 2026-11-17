using FluentAssertions;
using Morphir.Tooling.Features.ModelVerify;

namespace Morphir.Tooling.Tests.Features.ModelVerify;

public class VersionDetectionTests
{
    [Test]
    public void DetectVersion_ShouldDetectV1_WhenFormatVersionIs1()
    {
        // Arrange
        var jsonContent = @"{
            ""formatVersion"": 1,
            ""distribution"": [""library"", [], [], {}]
        }";

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("1", "formatVersion 1 should be detected as version 1");
    }

    [Test]
    public void DetectVersion_ShouldDetectV2_WhenFormatVersionIs2()
    {
        // Arrange
        var jsonContent = @"{
            ""formatVersion"": 2,
            ""distribution"": [""Library"", [], [], {}]
        }";

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("2", "formatVersion 2 should be detected as version 2");
    }

    [Test]
    public void DetectVersion_ShouldDetectV3_WhenFormatVersionIs3()
    {
        // Arrange
        var jsonContent = @"{
            ""formatVersion"": 3,
            ""distribution"": [""Library"", [], [], {}]
        }";

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("3", "formatVersion 3 should be detected as version 3");
    }

    [Test]
    public void DetectVersion_ShouldDetectV4_WhenFormatVersionIs4()
    {
        var version = VersionDetector.DetectVersion("""{ "formatVersion": 4, "distribution": { "Library": { "packageName": "a/b" } } }""");

        version.Should().Be("4", "formatVersion 4 should be detected as version 4");
    }

    [Test]
    [Arguments("\"3.1.0\"", "3")]
    [Arguments("\"4.0.0\"", "4")]
    [Arguments("\"4.12.3\"", "4")]
    [Arguments("\"3.0.0\"", "3")]
    [Arguments("\"4\"", "3")]
    [Arguments("5", "3")]
    [Arguments("3.5", "3")]
    public void DetectVersion_ReadsTheReleasesOfV3AndV4(string formatVersion, string expected)
    {
        var version = VersionDetector.DetectVersion($$"""{ "formatVersion": {{formatVersion}} }""");

        version.Should().Be(expected, $"formatVersion {formatVersion} is version {expected}, or the default 3");
    }

    [Test]
    public void DetectVersion_ShouldDefaultToV3_WhenFormatVersionMissing()
    {
        // Arrange
        var jsonContent = @"{
            ""distribution"": [""Library"", [], [], {}]
        }";

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("3", "should default to version 3 when formatVersion is missing");
    }

    [Test]
    public void DetectVersion_ShouldDefaultToV3_WhenJsonIsInvalid()
    {
        // Arrange
        var jsonContent = "{invalid json";

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("3", "should default to version 3 when JSON is invalid");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task DetectVersion_ShouldWorkWithRealV1File()
    {
        // Arrange
        var jsonContent = await File.ReadAllTextAsync("TestData/valid-ir-v1.json");

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("1", "real v1 file should be detected as version 1");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task DetectVersion_ShouldWorkWithRealV2File()
    {
        // Arrange
        var jsonContent = await File.ReadAllTextAsync("TestData/valid-ir-v2.json");

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("2", "real v2 file should be detected as version 2");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task DetectVersion_ShouldWorkWithRealV3File()
    {
        // Arrange
        var jsonContent = await File.ReadAllTextAsync("TestData/valid-ir-v3.json");

        // Act
        var version = VersionDetector.DetectVersion(jsonContent);

        // Assert
        version.Should().Be("3", "real v3 file should be detected as version 3");
    }
}
