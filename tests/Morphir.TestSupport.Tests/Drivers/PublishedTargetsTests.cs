// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Morphir.TestSupport.Drivers;
using Morphir.TestSupport.Packaging;

namespace Morphir.TestSupport.Tests.Drivers;

/// <summary>
/// The published and packaged targets resolve to the artifact of their own form, or the session stops with the
/// command that makes it (design DRV-2 of the change consolidate-black-box-tests, task 6.2). No target uses the
/// artifact of another form.
/// </summary>
[Category("PlatformSpecific")]
public class PublishedTargetsTests
{
    private const string Rid = "linux-x64";

    private static readonly ArtifactLocations Locations = new("/repo/artifacts", "/repo/artifacts/release", Rid);

    private static Dictionary<string, string> Metadata(string artifacts) => new()
    {
        ["Morphir.TestSupport.Cli.Native"] = "/repo/src/Morphir/bin/Release/net11.0/morphir.dll",
        ["Morphir.TestSupport.Cli.Managed"] = "/repo/src/Morphir.Managed/bin/Release/net11.0/morphir.dll",
        ["Morphir.TestSupport.Cli.Tool"] = "/repo/src/Morphir.Tool/bin/Release/net11.0/dotnet-morphir.dll",
        ["Morphir.TestSupport.Cli.Configuration"] = "Release",
        [ArtifactLocations.ArtifactsMetadata] = artifacts,
    };

    private static Func<string, string?> Variables(params (string Name, string Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return environment.GetValueOrDefault;
    }

    [Test]
    [Arguments("trimmed", "/repo/artifacts/single-file/linux-x64/morphir")]
    [Arguments("untrimmed", "/repo/artifacts/single-file-untrimmed/linux-x64/morphir")]
    [Arguments("aot", "/repo/artifacts/executables/linux-x64/morphir")]
    public void APublishedExecutableIsTheFileOfItsOwnPublishTarget(string name, string expected)
    {
        var target = TestRunSettings.Parse(Variables(("MORPHIR_TEST_TARGET", name)), _ => false).Target;

        var path = PublishedExecutables.PathOf(target, Locations, file => file == expected.Replace('/', Path.DirectorySeparatorChar));

        path.Should().Be(expected.Replace('/', Path.DirectorySeparatorChar));
    }

    [Test]
    [Arguments("trimmed", "./build.sh --target PublishSingleFile --rid linux-x64")]
    [Arguments("untrimmed", "./build.sh --target PublishSingleFileUntrimmed --rid linux-x64")]
    [Arguments("aot", "./build.sh --target PublishExecutable --rid linux-x64")]
    public void AMissingPublishedExecutableStopsTheRunWithTheCommandThatPublishesIt(string name, string command)
    {
        var target = TestRunSettings.Parse(Variables(("MORPHIR_TEST_TARGET", name)), _ => false).Target;

        var resolve = () => PublishedExecutables.PathOf(target, Locations, _ => false);

        resolve.Should().Throw<TestSessionException>().WithMessage($"*{name}*").WithMessage($"*{command}*");
    }

    [Test]
    public void AMissingTrimmedExecutableDoesNotRunTheUntrimmedOrTheNativeAotExecutable()
    {
        var asked = new List<string>();
        var others = new[] { "single-file-untrimmed", "executables" };

        var resolve = () => PublishedExecutables.PathOf(new TestTarget.Trimmed(), Locations, path =>
        {
            asked.Add(path);
            return others.Any(folder => path.Contains(folder, StringComparison.Ordinal));
        });

        resolve.Should().Throw<TestSessionException>().WithMessage("*PublishSingleFile*");
        asked.Should().ContainSingle().Which.Should().Contain(Path.Combine("single-file", Rid));
    }

    [Test]
    public void AWindowsExecutableHasTheExeExtension() =>
        PublishedExecutables.PathOf(new TestTarget.NativeAot(), Locations with { Rid = "win-arm64" }, _ => true)
            .Should().EndWith(Path.Combine("executables", "win-arm64", "morphir.exe"));

    [Test]
    public void TheReleaseFolderIsMorphirReleaseDirOrTheReleaseFolderOfTheArtifacts()
    {
        ArtifactLocations.Of(Metadata("/repo/artifacts"), "A.Tests", Variables()).ReleaseDirectory
            .Should().Be(Path.Combine("/repo/artifacts", "release"));
        var custom = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "my release"));
        ArtifactLocations.Of(Metadata("/repo/artifacts"), "A.Tests", Variables(("MORPHIR_RELEASE_DIR", custom))).ReleaseDirectory
            .Should().Be(custom);
    }

    [Test]
    public void ATestAssemblyWithNoArtifactsFolderStopsTheRunWithTheFileToImport()
    {
        var metadata = Metadata("/repo/artifacts");
        metadata.Remove(ArtifactLocations.ArtifactsMetadata);

        var locations = () => ArtifactLocations.Of(metadata, "A.Tests", Variables());

        locations.Should().Throw<TestSessionException>().WithMessage("*A.Tests*Morphir.CliArtifacts.targets*");
    }

    [Test]
    public void TheTestAssemblyHasTheArtifactsFolderOfTheRepository() =>
        ArtifactLocations.MetadataOf(typeof(PublishedTargetsTests).Assembly)[ArtifactLocations.ArtifactsMetadata]
            .Should().EndWith(Path.DirectorySeparatorChar + "artifacts");

    [Test]
    [Arguments("trimmed")]
    [Arguments("untrimmed")]
    [Arguments("aot")]
    public void ASessionOnAPublishedTargetRunsItsExecutableOnTheNativeHost(string name)
    {
        var artifacts = Path.Combine(Path.GetTempPath(), "repo", "artifacts");
        var expected = PublishedExecutables.PathOf(
            TestRunSettings.Parse(Variables(("MORPHIR_TEST_TARGET", name)), _ => false).Target, Locations with { ArtifactsDirectory = artifacts, Rid = RuntimeIdentifiers.Current }, _ => true);

        var session = BlackBoxSession.Start(Metadata(artifacts), "A.Tests", Variables(("MORPHIR_TEST_TARGET", name)), path => path == expected, AppContext.BaseDirectory);

        session.Artifacts.PathOf(MorphirHost.Native).Should().Be(expected);
        session.Settings.DefaultHost.Should().Be(MorphirHost.Native);
        session.Packaged.Should().BeNull();
        session.Decide(TestNeeds.FromTags(["host-managed"])).Should().BeOfType<Eligibility.Skipped>()
            .Which.Reason.Should().Be($"host: managed is not in target {name}");
    }

    [Test]
    public void ASessionOnAPublishedTargetWithNoExecutableStopsBeforeTheFirstTestAndUsesNoOtherForm()
    {
        var asked = new List<string>();

        var start = () => BlackBoxSession.Start(Metadata("/repo/artifacts"), "A.Tests", Variables(("MORPHIR_TEST_TARGET", "aot")), path =>
        {
            asked.Add(path);
            return !path.Contains("executables", StringComparison.Ordinal);
        }, AppContext.BaseDirectory);

        start.Should().Throw<TestSessionException>().WithMessage("*PublishExecutable*");
        asked.Should().NotContain(path => path.EndsWith(".dll", StringComparison.Ordinal), "the development build is not used instead");
    }

    [Test]
    [Arguments("native-aot", "native")]
    [Arguments("self-contained", "managed")]
    [Arguments("framework-dependent", "managed")]
    public async Task APackagedOfferingIsInstalledOnceFromTheReleaseFolderAndRemovedAtTheEndOfTheRun(string deployment, string host)
    {
        using var sandbox = TestSandbox.Create("morphir release ");
        var release = ReleaseFolder.Write(sandbox.PathOf("release"), deployment, RuntimeIdentifiers.Current);
        var session = BlackBoxSession.Start(
            Metadata(sandbox.PathOf("artifacts")), "A.Tests",
            Variables(("MORPHIR_TEST_TARGET", deployment), ("MORPHIR_RELEASE_DIR", release)), File.Exists, AppContext.BaseDirectory);

        var installation = session.Packaged!.Installation;
        installation.Directory.Should().Contain(" ", "the installation path contains a space");
        File.ReadAllText(installation.LaunchPath).Should().Be("launch");
        session.Settings.DefaultHost.Name().Should().Be(host);
        session.Artifacts.PathOf(session.Settings.DefaultHost).Should().Be(installation.LaunchPath);
        await using (var drivers = session.BeginTest(session.Settings.DefaultHost))
        {
            drivers.Capabilities.Should().Contain(DriverCapability.PackagedInstallation);
            drivers.Packaged.Installation.Should().BeSameAs(installation);
        }

        session.EndRun(new StringWriter());

        Directory.Exists(session.Packaged.Root).Should().BeFalse();
    }

    [Test]
    public async Task ThePackagedArtifactDriverChecksTheLayoutAndMakesAReadOnlyCopyThatTheEndOfTheTestGivesBack()
    {
        using var sandbox = TestSandbox.Create("morphir release ");
        var release = ReleaseFolder.Write(sandbox.PathOf("release"), "self-contained", RuntimeIdentifiers.Current);
        var session = BlackBoxSession.Start(
            Metadata(sandbox.PathOf("artifacts")), "A.Tests",
            Variables(("MORPHIR_TEST_TARGET", "self-contained"), ("MORPHIR_RELEASE_DIR", release)), File.Exists, AppContext.BaseDirectory);
        string copy;
        await using (var drivers = session.BeginTest(MorphirHost.Managed))
        {
            // The fake archive holds only the launch file, so the layout of a self-contained folder is not there.
            drivers.Packaged.LayoutProblems().Should().Contain("The installation has no morphir.dll.").And.Contain("The installation has no FSharp.Compiler.Service.dll.");
            var work = drivers.Packaged.NewWorkDirectory();
            work.Folder.Path.Should().Contain(" ").And.StartWith(drivers.Workspace.Work.Path);
            drivers.Workspace.EntriesIn(work.Folder).Should().BeEmpty();

            drivers.Packaged.InstallAReadOnlyCopy();
            copy = drivers.Packaged.Installation.Directory;

            copy.Should().StartWith(drivers.Workspace.Root).And.Contain(" ");
            drivers.Packaged.Installation.Should().NotBeSameAs(session.Packaged!.Installation);
            drivers.Packaged.WriteProblems().Should().BeEmpty("the copy is read-only before its first run");
            drivers.Packaged.Snapshot().Should().Equal("morphir 6");
        }

        Directory.Exists(copy).Should().BeFalse("the end of the test gives back the permissions, so the workspace is removed");
        session.EndRun(new StringWriter());
    }

    [Test]
    public void APackagedOfferingWithNoArtifactStopsTheRunWithThePackCommand()
    {
        using var sandbox = TestSandbox.Create("morphir release ");
        var release = Directory.CreateDirectory(sandbox.PathOf("release")).FullName;

        var start = () => BlackBoxSession.Start(
            Metadata(sandbox.PathOf("artifacts")), "A.Tests",
            Variables(("MORPHIR_TEST_TARGET", "self-contained"), ("MORPHIR_RELEASE_DIR", release)), File.Exists, AppContext.BaseDirectory);

        start.Should().Throw<TestSessionException>()
            .WithMessage($"*No packaged self-contained artifact*--target PackRelease --deployment self-contained --rid {RuntimeIdentifiers.Current}*");
    }

    [Test]
    public void APackagedArchiveWhoseDigestIsNotTheDigestOfItsLaunchMetadataIsRefused()
    {
        using var sandbox = TestSandbox.Create("morphir release ");
        var release = ReleaseFolder.Write(sandbox.PathOf("release"), "self-contained", RuntimeIdentifiers.Current, sha256: new string('0', 64));

        var start = () => BlackBoxSession.Start(
            Metadata(sandbox.PathOf("artifacts")), "A.Tests",
            Variables(("MORPHIR_TEST_TARGET", "self-contained"), ("MORPHIR_RELEASE_DIR", release)), File.Exists, AppContext.BaseDirectory);

        start.Should().Throw<TestSessionException>().WithMessage("*SHA-256*");
    }

    [Test]
    [Arguments("dev", "native")]
    [Arguments("dev", "managed")]
    [Arguments("dev", "tool")]
    [Arguments("trimmed", "native")]
    [Arguments("aot", "native")]
    [Arguments("native-aot", "native")]
    [Arguments("self-contained", "managed")]
    [Arguments("dotnet-tool", "tool")]
    public void TheCapabilitiesOfATargetAndAHostAreTheRowsOfTheTables(string name, string host)
    {
        var target = TestRunSettings.Parse(Variables(("MORPHIR_TEST_TARGET", name)), _ => false).Target;
        MorphirHosts.TryParse(host, out var parsed).Should().BeTrue();

        var capabilities = DriverCapabilities.Of(target, parsed);

        capabilities.Contains(DriverCapability.NativeAndToolHosts).Should().Be(name == "dev");
        capabilities.Contains(DriverCapability.PackagedInstallation).Should().Be(name is "native-aot" or "self-contained" or "dotnet-tool");
        capabilities.Contains(DriverCapability.NativeAotExecutable).Should().Be(name is "aot" or "native-aot");
        capabilities.Contains(DriverCapability.InProcessFSharpFrontend).Should().Be(host is "managed" or "tool");
        capabilities.Should().Contain([DriverCapability.ModelVerification, DriverCapability.Generation, DriverCapability.Validation, DriverCapability.KnownForm]);
    }

    [Test]
    public void AnExecutableAtAPathHasNoKnownForm() =>
        DriverCapabilities.Of(new TestTarget.Executable("/opt/morphir", MorphirHost.Native), MorphirHost.Native)
            .Should().NotContain([DriverCapability.KnownForm, DriverCapability.NativeAotExecutable, DriverCapability.PackagedInstallation]);

    /// <summary>A release folder with one packaged archive and its launch metadata, as PackRelease writes it.</summary>
    private static class ReleaseFolder
    {
        public static string Write(string folder, string deployment, string rid, string? sha256 = null)
        {
            Directory.CreateDirectory(folder);
            var artifact = $"morphir-dotnet-1.2.3-{deployment}-{rid}.zip";
            var archive = Path.Combine(folder, artifact);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("morphir").Open());
                writer.Write("launch");
            }

            File.WriteAllText(archive + ".build-launch.json", JsonSerializer.Serialize(new
            {
                release = "1.2.3",
                deployment,
                platform = rid,
                artifact,
                sha256 = sha256 ?? ReleaseFiles.Sha256(archive),
                launchPath = "morphir",
                execution = deployment == "native-aot" ? "native" : "managed",
                bundledRuntime = deployment is "native-aot" or "self-contained",
                requiredRuntime = deployment is "native-aot" or "self-contained" ? null : "Microsoft.NETCore.App 11.0",
                requiredSdk = (string?)null,
                inProcessFSharpFrontend = deployment != "native-aot",
            }));
            return folder;
        }
    }
}
