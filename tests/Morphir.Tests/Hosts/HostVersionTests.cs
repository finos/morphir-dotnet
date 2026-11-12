using FluentAssertions;
using Morphir.Extensions;

namespace Morphir.Tests.Hosts;

/// <summary>
/// Each host starts by parsing its build version with <c>ReleaseVersion.Create</c>, which throws on a version that is
/// not canonical SemVer. These tests parse the versions that the build writes. The run of <c>--version</c> on each
/// built host is in tests/Morphir.BlackBox.Tests/Smoke/Version.feature.
/// </summary>
public sealed class HostVersionTests
{
    [Test]
    [Arguments("0.0.0-dev")]
    [Arguments("0.4.0-alpha.1+a5f91af10d75bd58027e4ac4173a056e7360d667")]
    public void TheVersionsThatTheBuildWritesParse(string version) =>
        ReleaseVersion.TryParse(version, out _, out _).Should().BeTrue();
}
