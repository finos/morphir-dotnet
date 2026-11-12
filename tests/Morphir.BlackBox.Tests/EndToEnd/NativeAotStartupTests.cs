// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.BlackBox.Tests.EndToEnd;

/// <summary>
/// The start time of the Native AOT executable of a release: a test with a time budget, so a framework test (design
/// FORM-1). It runs on the targets <c>aot</c> and <c>native-aot</c> in the release and manual workflows.
/// </summary>
[Category("e2e")]
[Category("lane")]
[Category("published")]
[Category("needs-native-aot-executable")]
public sealed class NativeAotStartupTests : BlackBoxTest
{
    [Test]
    public async Task The_Native_AOT_executable_gives_its_version_in_less_than_100_ms()
    {
        var result = await Drivers.Cli.RunAsync(["--version"]);

        result.ShouldExitWith(0);
        await Assert.That(result.Duration).IsLessThan(TimeSpan.FromMilliseconds(100)).Because(result.Describe());
    }
}
