using FluentAssertions;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Tests.Infrastructure;

public class RuntimeIdentifierTests
{
    [Test]
    public void GetCurrentRid_ShouldReturnValidRid()
    {
        // Act
        var rid = RuntimeIdentifier.GetCurrentRid();

        // Assert
        rid.Should().NotBeNullOrWhiteSpace("RID should be generated");
        rid.Should().Contain("-", "RID should be in format 'os-arch'");

        var parts = rid.Split('-');
        parts.Should().HaveCount(2, "RID should have exactly 2 parts separated by hyphen");
    }

    [Test]
    public void GetCurrentRid_ShouldMatchExpectedPlatform()
    {
        // Act
        var rid = RuntimeIdentifier.GetCurrentRid();

        // Assert
        var validOsParts = new[] { "win", "linux", "osx", "freebsd" };
        var validArchParts = new[] { "x64", "x86", "arm64", "arm" };

        var parts = rid.Split('-');
        validOsParts.Should().Contain(parts[0], "OS part should be one of the known OS identifiers");
        validArchParts.Should().Contain(parts[1], "Arch part should be one of the known architecture identifiers");
    }

    [Test]
    public void IsValidRid_WithValidRids_ShouldReturnTrue()
    {
        // Arrange
        var validRids = new[]
        {
            "linux-x64",
            "win-x64",
            "osx-arm64",
            "linux-arm64",
            "win-x86",
            "osx-x64"
        };

        // Act & Assert
        foreach (var rid in validRids)
        {
            RuntimeIdentifier.IsValidRid(rid).Should().BeTrue($"{rid} should be valid");
        }
    }

    [Test]
    public void IsValidRid_WithInvalidRids_ShouldReturnFalse()
    {
        // Arrange
        var invalidRids = new[]
        {
            "",
            "   ",
            "linux",
            "x64",
            "linux-x64-extra",
            "invalid-x64",
            "linux-invalid",
            "linux_x64", // Wrong separator
            null!
        };

        // Act & Assert
        foreach (var rid in invalidRids)
        {
            RuntimeIdentifier.IsValidRid(rid).Should().BeFalse($"{rid ?? "null"} should be invalid");
        }
    }

    [Test]
    public void GetCurrentRid_ShouldBeValid()
    {
        // Act
        var rid = RuntimeIdentifier.GetCurrentRid();

        // Assert
        RuntimeIdentifier.IsValidRid(rid).Should().BeTrue("Current RID should always be valid");
    }
}
