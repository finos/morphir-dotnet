// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The driver of <c>extension install</c> (<see cref="ExtensionInstallDriver"/>) and its report
/// (<see cref="ExtensionInstallationReport"/>): the command lines, the NuGet folder feed it writes, and what it reads
/// from the extension home. A recording CLI driver stands in for the product.
/// </summary>
[Category("PlatformSpecific")]
public class ExtensionInstallDriverTests
{
    private static ScenarioWorkspace Workspace() =>
        ScenarioWorkspace.Create(Path.Combine(AppContext.BaseDirectory, "Fixtures"));

    private static CommandResult Result(int exitCode = 0) => new("morphir", exitCode, "", "", TimeSpan.Zero, []);

    private sealed class RecordingCli : ICliDriver
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public TestTarget Target { get; } = new TestTarget.DevelopmentBuild();

        public MorphirHost Host => MorphirHost.Native;

        public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CliRunOptions? options = null, CancellationToken cancellation = default)
        {
            Commands.Add(arguments);
            return Task.FromResult(Result());
        }

        public Task<CommandResult> RunCommandLineAsync(string commandLine, CliRunOptions? options = null, CancellationToken cancellation = default) =>
            throw new NotSupportedException("A capability driver gives each argument apart.");
    }

    [Test]
    public async Task InstallGivesTheSourceTheFeedAndTheHomeOfTheScenarioAsSeparateArguments()
    {
        await using var workspace = Workspace();
        var cli = new RecordingCli();
        var driver = new ExtensionInstallDriver(cli, workspace);
        var feed = workspace.Directory("my feed");

        await driver.InstallAsync(new ExtensionInstallOptions { Source = "nuget:Acme.Morphir.Python@1.2.3", NuGetSource = feed });
        await driver.InstallAsync(new ExtensionInstallOptions { Source = "nuget:Acme.Morphir.Python", NuGetSource = feed, DryRun = true });
        await driver.InstallAsync(new ExtensionInstallOptions { Source = "github:finos/morphir-python@0.6.2" });

        cli.Commands[0].Should().Equal("extension", "install", "nuget:Acme.Morphir.Python@1.2.3", "--source", feed.Path, "--home", driver.Home.Path, "--no-probe");
        cli.Commands[1].Should().Equal("extension", "install", "nuget:Acme.Morphir.Python", "--source", feed.Path, "--home", driver.Home.Path, "--no-probe", "--dry-run");
        cli.Commands[2].Should().Equal("extension", "install", "github:finos/morphir-python@0.6.2", "--home", driver.Home.Path, "--no-probe");
        feed.Path.Should().Contain(" ");
    }

    [Test]
    public async Task TheFolderFeedHasALayoutAPackageForEachPlatformPinnedByItsTreeDigest()
    {
        await using var workspace = Workspace();
        var driver = new ExtensionInstallDriver(new RecordingCli(), workspace);

        var feed = driver.WriteNuGetFolderFeed("Acme.Morphir.Python", "1.2.3");

        workspace.FilesIn(feed).Should().Equal("acme.morphir.python.1.2.3.nupkg");
        using var zip = new ZipArchive(new MemoryStream(workspace.ReadBytes(feed.File("acme.morphir.python.1.2.3.nupkg"))));
        zip.GetEntry("Acme.Morphir.Python.nuspec").Should().NotBeNull();
        using var release = JsonDocument.Parse(zip.GetEntry("morphir/release.json")!.Open());
        release.RootElement.GetProperty("version").GetString().Should().Be("1.2.3");
        var artifacts = release.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
        artifacts.Select(artifact => artifact.GetProperty("platform").GetString())
            .Should().Contain(["x86_64-unknown-linux-gnu", "aarch64-apple-darwin", "x86_64-pc-windows-msvc"]);

        foreach (var artifact in artifacts)
        {
            var package = artifact.GetProperty("package");
            var root = package.GetProperty("root").GetString()!;
            var entryPoint = package.GetProperty("entryPoint").GetString()!;
            using var input = zip.GetEntry($"{root}/{entryPoint}")!.Open();
            using var bytes = new MemoryStream();
            await input.CopyToAsync(bytes);
            var content = bytes.ToArray();
            var manifest = $"morphir-package-tree 0.1.0-preview.1\nx {content.Length} {Convert.ToHexStringLower(SHA256.HashData(content))} {entryPoint}\n";
            package.GetProperty("treeSha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))), root);
        }
    }

    [Test]
    public async Task TheReportReadsTheNuGetProvenanceOfTheLock()
    {
        await using var workspace = Workspace();
        var driver = new ExtensionInstallDriver(new RecordingCli(), workspace);
        workspace.WriteText(driver.Home.File("locks", "extensions", "com.acme.python.json"),
            """{"index":{"kind":"nuget-package","id":"acme.morphir.python","version":"1.2.3"},"source":{"kind":"nuget-package-entry"}}""");
        workspace.WriteText(driver.Home.File("tmp", "stage"), "not an installed file");

        var report = driver.Installation(Result());

        report.Installed.Should().Equal("locks/extensions/com.acme.python.json");
        report.ShouldRecordNuGetPackage("com.acme.python", "acme.morphir.python", "1.2.3");
        var other = () => report.ShouldRecordNuGetPackage("com.acme.python", "acme.morphir.python", "1.2.4");
        other.Should().Throw<CommandAssertionException>().WithMessage("*records the version 1.2.3, not 1.2.4*");
        var nothing = () => report.ShouldHaveNothingInstalled();
        nothing.Should().Throw<CommandAssertionException>().WithMessage("*locks/extensions/com.acme.python.json*");
    }

    [Test]
    public async Task AnEmptyHomeHasNothingInstalledAndNoLock()
    {
        await using var workspace = Workspace();
        var driver = new ExtensionInstallDriver(new RecordingCli(), workspace);

        var report = driver.Installation(Result(1));

        report.ShouldHaveNothingInstalled();
        var missing = () => report.ShouldRecordNuGetPackage("com.acme.python", "acme.morphir.python", "1.2.3");
        missing.Should().Throw<CommandAssertionException>().WithMessage("*no lock for com.acme.python*");
    }
}
