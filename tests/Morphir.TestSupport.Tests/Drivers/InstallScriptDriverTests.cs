// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The install script driver: the process and the variables of a run, what a run reports, and the release mirror.
/// The scripts run in the end-to-end scenarios of the black-box project.
/// </summary>
public class InstallScriptDriverTests
{
    private static InstallScriptRun RunWith(string standardError, int exitCode = 1) =>
        new(new CommandResult("sh install-morphirup.sh", exitCode, "", standardError, TimeSpan.Zero, []), []);

    [Test]
    public void WindowsRunsThePowerShellScriptWithNoChangeToThePath()
    {
        var info = InstallScriptDriver.StartInfo(windows: true, @"C:\w\install-morphirup.ps1", @"C:\w\installed", "pwsh.exe");

        InstallScriptDriver.ScriptFor(windows: true).Should().Be("install-morphirup.ps1");
        info.FileName.Should().Be("pwsh.exe");
        info.ArgumentList.Should().Equal(
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", @"C:\w\install-morphirup.ps1",
            "-InstallDir", @"C:\w\installed", "-NoModifyPath");
        InstallScriptDriver.StartInfo(windows: true, "s.ps1", "d", "pwsh.exe", "0.4.0-beta.12").ArgumentList.TakeLast(2)
            .Should().Equal("-Version", "0.4.0-beta.12");
    }

    [Test]
    public void AnotherMachineRunsTheShScript()
    {
        var info = InstallScriptDriver.StartInfo(windows: false, "/w/install-morphirup.sh", "/w/installed", "sh");

        InstallScriptDriver.ScriptFor(windows: false).Should().Be("install-morphirup.sh");
        info.FileName.Should().Be("sh");
        info.ArgumentList.Should().Equal("/w/install-morphirup.sh", "--install-dir", "/w/installed");
        InstallScriptDriver.StartInfo(windows: false, "s.sh", "d", "sh", "0.4.0-beta.12").ArgumentList.TakeLast(2)
            .Should().Equal("--version", "0.4.0-beta.12");
    }

    [Test]
    public void ARunReadsItsInputsAndSendsEveryOtherRequestToARefusingProxy()
    {
        var variables = InstallScriptDriver.EnvironmentFor(windows: false, "/w/releases.json", "/w/feed.atom", "https://127.0.0.1:5000", "/w/ca.pem");

        variables["MORPHIRUP_RELEASES_FILE"].Should().Be("/w/releases.json");
        variables["MORPHIRUP_FEED_FILE"].Should().Be("/w/feed.atom");
        variables["MORPHIRUP_DOWNLOAD_URL"].Should().Be("https://127.0.0.1:5000");
        variables["CURL_CA_BUNDLE"].Should().Be("/w/ca.pem");
        variables["HTTPS_PROXY"].Should().Be(InstallScriptDriver.RefusingProxy);
        variables["https_proxy"].Should().Be(InstallScriptDriver.RefusingProxy);
        variables["NO_PROXY"].Should().Be("127.0.0.1", "the local mirror is reached directly");
        variables["MORPHIRUP_NO_MODIFY_PATH"].Should().Be("1");
        foreach (var name in new[] { "MORPHIRUP_VERSION", "MORPHIRUP_API_URL", "MORPHIRUP_SHA256_FILE", "GITHUB_TOKEN" })
            variables.Should().ContainKey(name).WhoseValue.Should().BeNull($"the run takes the default of {name}");
    }

    [Test]
    public void ARunWithNoMirrorDownloadsThroughTheRefusingProxy()
    {
        var variables = InstallScriptDriver.EnvironmentFor(windows: true, "C:\\w\\releases.json");

        variables["MORPHIRUP_DOWNLOAD_URL"].Should().BeNull();
        variables["MORPHIRUP_FEED_FILE"].Should().BeNull();
        variables["CURL_CA_BUNDLE"].Should().BeNull();
    }

    [Test]
    public void ARunReportsTheReleaseAndTheUrlOfItsDownload()
    {
        var run = RunWith(
            "morphirup-install: finding the newest release with morphirup-win-arm64.exe\r\n" +
            "morphirup-install: downloading morphirup-win-arm64.exe 0.5.0-alpha.2 from https://github.com/finos/morphir/releases/download/v0.5.0-alpha.2/morphirup-win-arm64.exe\r\n" +
            "could not download\r\n");

        run.ChosenVersion.Should().Be("0.5.0-alpha.2");
        run.DownloadUrl.Should().Be("https://github.com/finos/morphir/releases/download/v0.5.0-alpha.2/morphirup-win-arm64.exe");
    }

    [Test]
    public void ARunFromAnHttpMirrorReportsItsDownloadToo()
    {
        var run = RunWith("morphirup-install: downloading morphirup-win-x64.exe 0.5.0-alpha.2 from http://127.0.0.1:5000/v0.5.0-alpha.2/morphirup-win-x64.exe\n");

        run.DownloadUrl.Should().Be("http://127.0.0.1:5000/v0.5.0-alpha.2/morphirup-win-x64.exe");
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

    // Standard error of the pinned-tag refusal on the windows-latest runner (run 43525718486): PowerShell 7 colors the
    // error record and starts each wrapped line with escape sequences before its "|" frame.
    private const string ColoredRefusal =
        "morphirup-install: downloading morphirup-win-x64.exe 0.4.0-beta.12 from http://127.0.0.1:59000/v0.4.0-beta.12/morphirup-win-x64.exe\r\n" +
        "\u001b[31;1mException: \u001b[0mD:\\a\\_temp\\w\\install-morphirup.ps1:240\u001b[0m\r\n" +
        "\u001b[31;1m\u001b[0m\u001b[36;1mLine |\u001b[0m\r\n" +
        "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m 240 | \u001b[0m .             \u001b[36;1mthrow \"finos/morphir release v$Version has no $asset ($ba\u001b[0m .\u001b[0m\r\n" +
        "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m     | \u001b[31;1m               ~~~~~~~~~~~~~~~~\u001b[0m\r\n" +
        "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mfinos/morphir release v0.4.0-beta.12 has no morphirup-win-x64.exe\u001b[0m\r\n" +
        "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1m(http://127.0.0.1:59000/v0.4.0-beta.12/morphirup-win-x64.exe: HTTP 404); morphirup releases before 0.5.0-alpha.2\u001b[0m\r\n" +
        "\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mare on finos/morphir-dotnet: https://github.com/finos/morphir-dotnet/releases\u001b[0m\r\n";

    [Test]
    public void TheMessagesJoinAColoredErrorThatPowerShellWrapped()
    {
        var messages = RunWith(ColoredRefusal).Messages;

        messages.Should().NotContain("\u001b", "no escape sequence is left");
        messages.Should().Contain("finos/morphir release v0.4.0-beta.12 has no morphirup-win-x64.exe");
        messages.Should().Contain("morphirup releases before 0.5.0-alpha.2 are on finos/morphir-dotnet: https://github.com/finos/morphir-dotnet/releases");
    }

    [Test]
    public void TheMessagesKeepAShScriptRefusalAsItIs()
    {
        const string refusal =
            "morphirup-install: error: finos/morphir release v0.4.0-beta.12 has no morphirup-linux-x64 (https://127.0.0.1:5000/v0.4.0-beta.12/morphirup-linux-x64: HTTP 404); morphirup releases before 0.5.0-alpha.2 are on finos/morphir-dotnet: https://github.com/finos/morphir-dotnet/releases\n";

        InstallScriptRun.Normalize(refusal).Should().Be(refusal.TrimEnd('\n') + " ");
    }

    [Test]
    public void AMorphirupReleaseHasEachPlatformAndAChecksumFileThatMatchesIt()
    {
        var files = ReleaseMirror.MorphirupRelease("0.5.0-alpha.2");

        files.Keys.Should().BeEquivalentTo(
            "v0.5.0-alpha.2/morphirup-linux-arm64", "v0.5.0-alpha.2/morphirup-linux-x64", "v0.5.0-alpha.2/morphirup-osx-arm64",
            "v0.5.0-alpha.2/morphirup-win-arm64.exe", "v0.5.0-alpha.2/morphirup-win-x64.exe", "v0.5.0-alpha.2/morphirup-v0.5.0-alpha.2.sha256");
        var sums = Encoding.UTF8.GetString(files["v0.5.0-alpha.2/morphirup-v0.5.0-alpha.2.sha256"]);
        sums.Should().Contain($"{Convert.ToHexStringLower(SHA256.HashData(files["v0.5.0-alpha.2/morphirup-linux-x64"]))}  morphirup-linux-x64\n");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task AnHttpMirrorServesItsFilesAndAnswers404ForAnyOtherPath()
    {
        await using var mirror = ReleaseMirror.Start(ReleaseMirror.MorphirupRelease("0.5.0-alpha.2"), tls: false, "unused.pem");
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });

        var found = await client.GetAsync($"{mirror.Url}/v0.5.0-alpha.2/morphirup-linux-x64");
        var missing = await client.GetAsync($"{mirror.Url}/v0.4.0-beta.12/morphirup-linux-x64");

        found.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await found.Content.ReadAsStringAsync()).Should().Be("#!/bin/sh\necho 0.5.0-alpha.2\n");
        missing.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        mirror.Requests.Should().Equal("GET v0.5.0-alpha.2/morphirup-linux-x64", "GET v0.4.0-beta.12/morphirup-linux-x64");
    }

    [Test]
    [Category("PlatformSpecific")]
    public async Task AnHttpsMirrorIsTrustedThroughItsCertificateFile()
    {
        using var sandbox = TestSandbox.Create("mirror-");
        var pem = sandbox.PathOf("mirror.pem");
        await using var mirror = ReleaseMirror.Start(ReleaseMirror.MorphirupRelease("0.5.0-alpha.2"), tls: true, pem);
        using var trusted = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(File.ReadAllText(pem));
        using var handler = new SocketsHttpHandler { UseProxy = false };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null && certificate.GetCertHashString() == trusted.GetCertHashString();
        using var client = new HttpClient(handler);

        var response = await client.GetAsync($"{mirror.Url}/v0.5.0-alpha.2/morphirup-v0.5.0-alpha.2.sha256");

        mirror.Url.Should().StartWith("https://127.0.0.1:");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
