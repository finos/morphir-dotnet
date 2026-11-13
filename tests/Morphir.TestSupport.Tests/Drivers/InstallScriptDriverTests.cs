// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The install script driver: the process and the variables of a run, and what a run reports. The scripts run in
/// the end-to-end scenarios of the black-box project; these tests read no file and start no process.
/// </summary>
public class InstallScriptDriverTests
{
    private static InstallScriptRun RunWith(string standardError, int exitCode = 1) =>
        new(new CommandResult("sh install-morphirup.sh", exitCode, "", standardError, TimeSpan.Zero, []), []);

    [Test]
    public void WindowsRunsThePowerShellScriptWithNoVersionAndNoChangeToThePath()
    {
        var info = InstallScriptDriver.StartInfo(windows: true, @"C:\w\install-morphirup.ps1", @"C:\w\installed", "pwsh.exe");

        InstallScriptDriver.ScriptFor(windows: true).Should().Be("install-morphirup.ps1");
        info.FileName.Should().Be("pwsh.exe");
        info.ArgumentList.Should().Equal(
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", @"C:\w\install-morphirup.ps1",
            "-InstallDir", @"C:\w\installed", "-NoModifyPath");
    }

    [Test]
    public void AnotherMachineRunsTheShScriptWithNoVersion()
    {
        var info = InstallScriptDriver.StartInfo(windows: false, "/w/install-morphirup.sh", "/w/installed", "sh");

        InstallScriptDriver.ScriptFor(windows: false).Should().Be("install-morphirup.sh");
        info.FileName.Should().Be("sh");
        info.ArgumentList.Should().Equal("/w/install-morphirup.sh", "--install-dir", "/w/installed");
    }

    [Test]
    public void ARunReadsTheReleaseListOfTheScenarioAndSendsEachDownloadToARefusingProxy()
    {
        var variables = InstallScriptDriver.EnvironmentFor("/w/releases.json");

        variables["MORPHIRUP_RELEASES_FILE"].Should().Be("/w/releases.json");
        variables["HTTPS_PROXY"].Should().Be(InstallScriptDriver.RefusingProxy);
        variables["MORPHIRUP_NO_MODIFY_PATH"].Should().Be("1");
        foreach (var name in new[] { "MORPHIRUP_VERSION", "MORPHIRUP_DOWNLOAD_URL", "MORPHIRUP_API_URL", "MORPHIRUP_SHA256_FILE", "GITHUB_TOKEN", "NO_PROXY" })
            variables.Should().ContainKey(name).WhoseValue.Should().BeNull($"the run takes the default of {name}");
    }

    [Test]
    public void ARunReportsTheReleaseAndTheUrlOfItsDownload()
    {
        var run = RunWith(
            "morphirup-install: finding the newest release with morphirup-win-arm64.exe\r\n" +
            "morphirup-install: downloading morphirup-win-arm64.exe 0.5.0-alpha.1 from https://github.com/finos/morphir/releases/download/v0.5.0-alpha.1/morphirup-win-arm64.exe\r\n" +
            "could not download\r\n");

        run.ChosenVersion.Should().Be("0.5.0-alpha.1");
        run.DownloadUrl.Should().Be("https://github.com/finos/morphir/releases/download/v0.5.0-alpha.1/morphirup-win-arm64.exe");
    }

    [Test]
    public void ARunThatStartedNoDownloadReportsNoRelease()
    {
        var run = RunWith("morphirup-install: error: no release of finos/morphir has morphirup-linux-x64; pass --version <version>\n");

        run.ChosenVersion.Should().BeNull();
        run.DownloadUrl.Should().BeNull();
    }

    [Test]
    public void TheMessagesJoinAnErrorThatPowerShellWrapped()
    {
        var run = RunWith(
            "Exception: C:\\w\\install-morphirup.ps1:185\r\nLine |\r\n 185 |  . $release) { throw \"no release\r\n" +
            "     | no release of finos/morphir has\r\n     | morphirup-win-x64.exe; pass -Version <version>\r\n");

        run.Messages.Should().Contain("no release of finos/morphir has morphirup-win-x64.exe; pass -Version <version>");
    }
}
