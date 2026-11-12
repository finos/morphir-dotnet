// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using FluentAssertions;
using Morphir.Extensions;
using Morphir.TestSupport;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// The probe of the WASM runtime (task 4.2 of <c>add-wasm-extension-host</c>, design RPT-1): each unavailable reason,
/// decided from what starting the engine did, and the process value that probes at most once, on first need.
/// </summary>
public sealed class WasmRuntimeProbeTests
{
    private const string Supported = "win-arm64";

    private static WasmRuntimeAvailability Unavailable(WasmRuntimeUnavailableReason reason) =>
        WasmRuntimeAvailability.UnavailableBecause(reason);

    private static Action Throws(Exception exception) => () => throw exception;

    private static readonly Func<bool> Absent = () => false;
    private static readonly Func<bool> Present = () => true;

    // --- Reasons ------------------------------------------------------------------------------------------------

    [Test]
    public void AnEngineThatStartsIsAvailable() =>
        WasmRuntimeProbe.Probe(Supported, () => { }, Absent).Should().Be(WasmRuntimeAvailability.Available);

    [Test]
    public void WindowsX64RefusesBeforeLoadingTheEngineOrInspectingItsLibrary()
    {
        var availability = WasmRuntimeProbe.Probe("win-x64",
            () => throw new InvalidOperationException("must not load an unsafe engine"),
            () => throw new InvalidOperationException("must not inspect the library"));

        availability.CanRunModules.Should().BeFalse();
        availability.Match(() => "available", reason => reason.WireValue)
            .Should().Be("win_x64_trap_unsupported");
    }

    [Test]
    [Arguments("win-x64")]
    [Arguments("win-arm64")]
    public void AnAvailableRuntimeCannotOverrideWindowsX64SafetyBeforeModuleIo(string claimedPlatform)
    {
        if (claimedPlatform != "win-x64" && WasmRuntime.Process.Platform != "win-x64")
            Skip.Test("This row checks the actual Windows x64 process against a misidentified runtime.");
        using var sandbox = TestSandbox.Create();
        var runtime = new WasmRuntime(claimedPlatform, () => WasmRuntimeAvailability.Available);
        var host = TestHosts.Unprofiled(MorphirHost.Native, ReleaseVersion.Create("0.4.0"));
        var loader = new WasmModuleLoader(runtime, host);

        var outcome = loader.Load(new WasmModuleSource("python", sandbox.PathOf("does-not-exist.wasm")));

        var failure = outcome.Should().BeOfType<WasmModuleLoad.Failed>().Subject.Failure
            .Should().BeOfType<WasmModuleFailure.RuntimeUnavailable>().Subject;
        failure.Platform.Should().Be("win-x64");
        failure.Reason.Should().Be(WasmRuntimeUnavailableReason.WinX64TrapUnsupported);
        failure.Code.Should().Be("extension.runtime_unavailable");
    }

    [Test]
    [Arguments("linux-musl-x64")]
    [Arguments("linux-musl-arm64")]
    [Arguments("freebsd-x64")]
    [Arguments("win-x86")]
    [Arguments("linux-arm")]
    public void APlatformWithoutAnEnginePackageIsUnsupportedAndTheEngineIsNotStarted(string platform)
    {
        var started = false;
        var availability = WasmRuntimeProbe.Probe(platform, () => started = true, Present);

        availability.Should().Be(Unavailable(WasmRuntimeUnavailableReason.PlatformUnsupported));
        started.Should().BeFalse("a platform without an engine package is not probed");
    }

    [Test]
    public void AMissingNativeLibraryIsEngineMissing() =>
        WasmRuntimeProbe.Probe(Supported, Throws(new DllNotFoundException("Unable to load DLL 'wasmtime'")), Absent)
            .Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineMissing));

    [Test]
    public void AMissingNativeLibraryBehindATypeInitializerIsEngineMissing() =>
        WasmRuntimeProbe.Probe(Supported, Throws(new TypeInitializationException("Wasmtime.Engine", new DllNotFoundException("wasmtime"))), Absent)
            .Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineMissing));

    /// <summary>
    /// .NET on Linux and macOS reports every <c>dlopen</c> failure as <see cref="DllNotFoundException"/>, also for a
    /// library that is there; the library's presence then says it failed to load.
    /// </summary>
    [Test]
    public void ALibraryThatIsThereButReportedNotFoundFailedToLoad() =>
        WasmRuntimeProbe.Probe(Supported, Throws(new DllNotFoundException("wrong ELF class")), Present)
            .Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineLoadFailed));

    [Test]
    public void ALibraryOfAnotherArchitectureFailedToLoad() =>
        WasmRuntimeProbe.Probe(Supported, Throws(new BadImageFormatException("not a valid Win32 application")), Present)
            .Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineLoadFailed));

    /// <summary>
    /// Scenario "Engine library cannot be loaded": the engine library of the same operating system for another
    /// architecture, which the build copies under <c>runtimes/</c>, really loaded by the probe. The failure is
    /// <c>engine_load_failed</c>, whichever exception the platform's loader throws.
    /// </summary>
    [Test]
    public void ARealLibraryBuiltForAnotherArchitectureIsEngineLoadFailed()
    {
        var platform = WasmRuntimeProbe.CurrentPlatform;
        var system = platform[..platform.LastIndexOf('-')];
        if (platform == "win-x64")
            Skip.Test("win_x64_trap_unsupported: the engine is refused before loading; #609.");
        var other = WasmRuntimeProbe.EnginePlatforms.FirstOrDefault(candidate =>
            candidate != platform && candidate[..candidate.LastIndexOf('-')] == system);
        if (other is null || WasmRuntimeProbe.EngineLibraryFileName(other) is not { } name)
        {
            Skip.Test($"the engine ships no library for another architecture of {platform}");
            return;
        }

        var library = Path.Combine(AppContext.BaseDirectory, "runtimes", other, "native", name);
        if (!File.Exists(library))
        {
            Skip.Test($"the build output has no {other} engine library at {library}");
            return;
        }

        var availability = WasmRuntimeProbe.Probe(platform, () => NativeLibrary.Free(NativeLibrary.Load(library)), () => File.Exists(library));

        availability.Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineLoadFailed), $"{library} is built for {other}");
    }

    [Test]
    public void ALibraryOfAnotherVersionFailedToLoad() =>
        WasmRuntimeProbe.Probe(Supported, Throws(new EntryPointNotFoundException("wasm_engine_new_with_config")), Present)
            .Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineLoadFailed));

    [Test]
    public void AnEngineThatLoadsButCannotBeCreatedFailedToLoad() =>
        WasmRuntimeProbe.Probe(Supported, Throws(new TypeInitializationException("Wasmtime.Engine", new InvalidOperationException("no engine"))), Present)
            .Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineLoadFailed));

    [Test]
    public void TheLibraryFileIsNotLookedAtWhenTheEngineStarts() =>
        WasmRuntimeProbe.Probe(Supported, () => { }, () => throw new InvalidOperationException("the file system was asked"))
            .Should().Be(WasmRuntimeAvailability.Available);

    // --- Platforms ----------------------------------------------------------------------------------------------

    /// <summary>The platforms with an engine are those the Wasmtime package ships a native library for.</summary>
    [Test]
    public void TheEnginePlatformsAreThoseOfTheWasmtimePackage()
    {
        var package = Path.Combine(TestSandbox.RealNuGetPackagesFolder, "wasmtime", WasmRuntimeIdentity.EngineVersion, "runtimes");
        Directory.Exists(package).Should().BeTrue($"the Wasmtime package is restored at {package}");
        var shipped = Directory.EnumerateDirectories(package)
            .Where(directory => Directory.EnumerateFiles(Path.Combine(directory, "native")).Any())
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        WasmRuntimeProbe.EnginePlatforms.Should().Equal(shipped);
        foreach (var platform in WasmRuntimeProbe.EnginePlatforms)
            File.Exists(Path.Combine(package, platform, "native", WasmRuntimeProbe.EngineLibraryFileName(platform)!)).Should().BeTrue(platform);
    }

    [Test]
    [Arguments("win-x64", "wasmtime.dll")]
    [Arguments("linux-arm64", "libwasmtime.so")]
    [Arguments("osx-arm64", "libwasmtime.dylib")]
    public void TheEngineLibraryHasThePlatformsFileName(string platform, string fileName) =>
        WasmRuntimeProbe.EngineLibraryFileName(platform).Should().Be(fileName);

    [Test]
    public void APlatformWithoutAnEngineHasNoLibraryFileName() =>
        WasmRuntimeProbe.EngineLibraryFileName("linux-musl-x64").Should().BeNull();

    [Test]
    [Arguments("Windows", Architecture.Arm64, "win-arm64", "win-arm64")]
    [Arguments("Windows", Architecture.X64, "win-x64", "win-x64")]
    [Arguments("OSX", Architecture.Arm64, "osx-arm64", "osx-arm64")]
    [Arguments("Linux", Architecture.X64, "linux-x64", "linux-x64")]
    [Arguments("Linux", Architecture.X64, "linux-musl-x64", "linux-musl-x64")]
    [Arguments("Linux", Architecture.Arm64, "linux-musl-arm64", "linux-musl-arm64")]
    [Arguments("Linux", Architecture.X64, "ubuntu.24.04-x64", "linux-x64")]
    [Arguments("FreeBSD", Architecture.X64, "freebsd-x64", "freebsd-x64")]
    public void ThePlatformComesFromTheOperatingSystemTheArchitectureAndTheCLibrary(string os, Architecture architecture, string runtimeIdentifier, string platform) =>
        WasmRuntimeProbe.PlatformOf(OSPlatform.Create(os.ToUpperInvariant()), architecture, runtimeIdentifier).Should().Be(platform);

    [Test]
    public void ThisProcessIsOnAPlatformWithAnEngineAndItsEngineIsAvailable()
    {
        WasmRuntimeProbe.EnginePlatforms.Should().Contain(WasmRuntime.Process.Platform);
        WasmRuntime.Process.Availability.Should().Be(WasmRuntime.Process.Platform == "win-x64"
            ? Unavailable(WasmRuntimeUnavailableReason.WinX64TrapUnsupported)
            : WasmRuntimeAvailability.Available);
    }

    // --- First need, once ---------------------------------------------------------------------------------------

    [Test]
    public void TheRuntimeIsNotProbedBeforeItsAvailabilityIsRead()
    {
        var probes = 0;
        var runtime = new WasmRuntime(Supported, () =>
        {
            Interlocked.Increment(ref probes);
            return WasmRuntimeAvailability.Available;
        });

        runtime.Platform.Should().Be(Supported);
        runtime.HasProbed.Should().BeFalse();
        probes.Should().Be(0);
    }

    [Test]
    public async Task TheRuntimeIsProbedAtMostOnceAndRemembersTheAnswer()
    {
        var probes = 0;
        var runtime = new WasmRuntime(Supported, () =>
        {
            Interlocked.Increment(ref probes);
            Thread.Sleep(50);
            return Unavailable(WasmRuntimeUnavailableReason.EngineMissing);
        });

        var answers = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => runtime.Availability)));

        answers.Should().AllSatisfy(answer => answer.Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineMissing)));
        runtime.Availability.Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineMissing));
        runtime.HasProbed.Should().BeTrue();
        probes.Should().Be(1);
    }

    [Test]
    public void AProbeThatComposesTheRealDecisionNeverThrowsTheLoadError()
    {
        var runtime = new WasmRuntime(Supported, () => WasmRuntimeProbe.Probe(Supported, Throws(new DllNotFoundException("wasmtime")), Absent));

        var read = () => runtime.Availability;

        read.Should().NotThrow().Which.Should().Be(Unavailable(WasmRuntimeUnavailableReason.EngineMissing));
    }
}
