// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.InfrastructureTests;

/// <summary>
/// The host trace reader that decides whether a packaged run loaded a machine runtime. The fixtures are real .NET 10
/// host traces from a win-arm64 machine, cut to the lines the reader uses, with the paths replaced by placeholders.
/// </summary>
public sealed class HostTraceTests
{
    [Test]
    public void ASelfContainedStartLoadsOnlyFromItsInstallation()
    {
        using var paths = new TracePaths();
        var trace = paths.Fixture("self-contained-start.trace.txt");

        HostTrace.ResolvedFxr(trace).Should().Be(paths.Install("hostfxr.dll"));
        HostTrace.LoadedRuntimePaths(trace).Should().Contain([paths.Install("hostfxr.dll"), paths.Install("coreclr.dll"), paths.Install("hostpolicy.dll")]);
        HostTrace.BundledRuntimeViolations(trace, paths.InstallDirectory, [paths.MachineDirectory]).Should().BeEmpty();
    }

    [Test]
    public void AFrameworkDependentStartFailsABundledRuntimeClaim()
    {
        // The deliberate break of task 2.4, as a test: launch metadata that claims a bundled runtime, judged against a
        // start that loaded hostfxr and CoreCLR from the machine's .NET.
        using var paths = new TracePaths();
        var trace = paths.Fixture("machine-runtime-start.trace.txt");

        HostTrace.ResolvedFxr(trace).Should().Be(paths.Machine("host", "fxr", "10.0.14", "hostfxr.dll"));
        var violations = HostTrace.BundledRuntimeViolations(trace, paths.InstallDirectory, [paths.MachineDirectory]);
        violations.Should().Contain(problem => problem.Contains("app-local"));
        violations.Should().Contain(problem => problem.Contains(paths.Machine("host", "fxr", "10.0.14", "hostfxr.dll")));
    }

    [Test]
    public void ATraceWithNoLoadedRuntimeIsAViolation() =>
        HostTrace.BundledRuntimeViolations("Tracing enabled @ x\nUsing app-local location [/a/] as runtime location.\n", "/a", [])
            .Should().ContainSingle().Which.Should().Contain("no hostfxr");

    [Test]
    public void TheChosenFrameworkIsTheFirstChoseFxLine()
    {
        using var paths = new TracePaths();
        HostTrace.ChosenFramework(paths.Fixture("machine-runtime-start.trace.txt")).Should().Be("10.0.14");

        // A path to another framework version before the choice is not the choice.
        var trace = $"Probe {paths.Machine("shared", "Microsoft.NETCore.App", "10.0.13")}\n" +
                    $"Chose FX version [{paths.Machine("shared", "Microsoft.NETCore.App", "10.0.14")}]\n" +
                    $"Chose FX version [{paths.Machine("shared", "Microsoft.NETCore.App", "10.0.15")}]\n";
        HostTrace.ChosenFramework(trace).Should().Be("10.0.14");
        HostTrace.ChosenFramework("no framework here").Should().BeNull();
    }

    [Test]
    public void TheDefaultLocationIsReadFromAStartAndFromTheFailureMessage()
    {
        using var paths = new TracePaths();
        HostTrace.DefaultLocation(paths.Fixture("no-runtime.trace.txt")).Should().Be(paths.MachineDirectory);
        HostTrace.DefaultLocation("Using global install location [/x/dotnet] as runtime location.").Should().Be("/x/dotnet");
        HostTrace.DefaultLocation(paths.Fixture("self-contained-start.trace.txt")).Should().BeNull();
    }

    [Test]
    public void HostTracingOnStandardErrorIsRecognized()
    {
        HostTrace.IsTracingOutput("Tracing enabled @ Tue Nov 10 02:09:07 2026 GMT\n--- Invoked apphost [version: 10.0.14]").Should().BeTrue();
        HostTrace.IsTracingOutput("--- Invoked hostfxr_main_startupinfo [version: 10.0.14]").Should().BeTrue();
        HostTrace.IsTracingOutput("").Should().BeFalse();
        HostTrace.IsTracingOutput("error: compile.frontend_unavailable").Should().BeFalse();
    }

    [Test]
    public void IsUnderComparesWholePathSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "morphir trace", "install");
        HostTrace.IsUnder(Path.Combine(root, "hostfxr.dll"), root).Should().BeTrue();
        HostTrace.IsUnder(root, root).Should().BeTrue();
        HostTrace.IsUnder(root + Path.DirectorySeparatorChar, root).Should().BeTrue();
        HostTrace.IsUnder(Path.Combine(root + "-other", "hostfxr.dll"), root).Should().BeFalse("a sibling that shares a prefix is not inside");
        HostTrace.IsUnder(Path.Combine(root, "..", "hostfxr.dll"), root).Should().BeFalse();
        if (!OperatingSystem.IsLinux())
            HostTrace.IsUnder(Path.Combine(root.ToUpperInvariant(), "HOSTFXR.DLL"), root).Should().BeTrue("Windows and macOS file systems ignore case");
    }

    /// <summary>Placeholder paths of the fixtures, mapped to this machine's path syntax.</summary>
    private sealed class TracePaths : IDisposable
    {
        private readonly TestSandbox _sandbox = TestSandbox.Create("morphir trace ");

        public TracePaths()
        {
            InstallDirectory = Directory.CreateDirectory(_sandbox.PathOf("self-contained install")).FullName;
            MachineDirectory = Directory.CreateDirectory(_sandbox.PathOf("machine dotnet")).FullName;
        }

        public string InstallDirectory { get; }

        public string MachineDirectory { get; }

        public string Install(params string[] segments) => Path.Combine([InstallDirectory, .. segments]);

        public string Machine(params string[] segments) => Path.Combine([MachineDirectory, .. segments]);

        public string Fixture(string name)
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InfrastructureTests", "Fixtures", name));
            if (!OperatingSystem.IsWindows()) text = text.Replace('\\', '/');
            return text.Replace("<INSTALL>", InstallDirectory).Replace("<MACHINE>", MachineDirectory).Replace("<WORK>", _sandbox.Work);
        }

        public void Dispose() => _sandbox.Dispose();
    }
}
