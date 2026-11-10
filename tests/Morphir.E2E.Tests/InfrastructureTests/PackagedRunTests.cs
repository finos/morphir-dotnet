// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;

namespace Morphir.E2E.Tests.InfrastructureTests;

/// <summary>How a packaged run hides the machine's .NET runtime, and when it cannot.</summary>
public sealed class PackagedRunTests
{
    [Test]
    public void HidingTheRuntimeRemovesEveryVariableAndPathEntryThatReachesIt()
    {
        var root = Directory.CreateTempSubdirectory("morphir hide ").FullName;
        try
        {
            var withDotnet = Directory.CreateDirectory(Path.Combine(root, "dotnet root")).FullName;
            File.WriteAllText(Path.Combine(withDotnet, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), "");
            var other = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
            var empty = Directory.CreateDirectory(Path.Combine(root, "empty program files")).FullName;

            var start = new ProcessStartInfo("morphir");
            start.Environment.Clear();
            start.Environment["PATH"] = string.Join(Path.PathSeparator, withDotnet, other, "");
            start.Environment["DOTNET_ROOT"] = withDotnet;
            start.Environment["DOTNET_ROOT_ARM64"] = withDotnet;
            start.Environment["DOTNET_HOST_PATH"] = Path.Combine(withDotnet, "dotnet");
            start.Environment["MORPHIR_HOME"] = Path.Combine(root, "home");

            PackagedRun.HideRuntime(start, empty);

            start.Environment.Keys.Should().NotContain(key => key.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) || key == "DOTNET_HOST_PATH");
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"].Should().Be("0");
            start.Environment["PATH"].Should().Be(other);
            start.Environment["MORPHIR_HOME"].Should().Be(Path.Combine(root, "home"), "variables that do not reach .NET stay");
            if (OperatingSystem.IsWindows())
                start.Environment["ProgramW6432"].Should().Be(empty, "the Windows apphost takes its default location from ProgramW6432");
            else
                start.Environment.Should().NotContainKey("ProgramW6432");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void OnWindowsARegisteredLocationCannotBeHidden()
    {
        var plan = PackagedRun.PlanHiding("windows", registered: [@"C:\Program Files\dotnet"], defaults: [@"C:\Program Files\dotnet"], containerImage: null);
        plan.Mode.Should().Be(RuntimeHidingMode.NotPossible);
        plan.Reason.Should().Contain(@"C:\Program Files\dotnet").And.Contain("registered");

        PackagedRun.PlanHiding("windows", registered: [], defaults: [@"C:\Program Files\dotnet"], containerImage: null)
            .Mode.Should().Be(RuntimeHidingMode.Environment, "the default location moves with ProgramW6432");
    }

    [Test]
    public void OnLinuxADefaultLocationNeedsAContainer()
    {
        var plan = PackagedRun.PlanHiding("linux", registered: [], defaults: ["/usr/share/dotnet"], containerImage: null);
        plan.Mode.Should().Be(RuntimeHidingMode.NotPossible);
        plan.Reason.Should().Contain("/usr/share/dotnet").And.Contain(PackagedRun.ContainerVariable);

        PackagedRun.PlanHiding("linux", registered: ["/opt/dotnet"], defaults: [], containerImage: "mcr.microsoft.com/dotnet/runtime-deps:10.0")
            .Mode.Should().Be(RuntimeHidingMode.Container);
        PackagedRun.PlanHiding("linux", registered: [], defaults: [], containerImage: null)
            .Mode.Should().Be(RuntimeHidingMode.Environment);
    }

    [Test]
    public void OnMacOSADefaultLocationCannotBeHidden()
    {
        PackagedRun.PlanHiding("macos", registered: [], defaults: ["/usr/local/share/dotnet"], containerImage: "image")
            .Mode.Should().Be(RuntimeHidingMode.NotPossible, "there is no Linux container for a macOS executable");
        PackagedRun.PlanHiding("macos", registered: [], defaults: [], containerImage: null).Mode.Should().Be(RuntimeHidingMode.Environment);
    }

    [Test]
    public void AContainerRunMountsTheInstallationAndSandboxAtTheirOwnPaths()
    {
        var start = new ProcessStartInfo("/tmp/morphir packaged e2e/self-contained install/morphir") { WorkingDirectory = "/tmp/sandbox/work dir" };
        start.ArgumentList.Add("--version");
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/share/dotnet:/usr/bin";
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        start.Environment["HOME"] = "/tmp/sandbox/home";
        start.Environment["MORPHIR_HOME"] = "/tmp/sandbox/home/.morphir";
        start.Environment["XDG_CONFIG_HOME"] = "/tmp/sandbox/xdg-config";
        start.Environment["COREHOST_TRACE"] = "1";
        start.Environment["COREHOST_TRACEFILE"] = "/tmp/sandbox/host-trace.txt";
        start.Environment["GITHUB_TOKEN"] = "secret";

        var container = PackagedRun.InContainer(start, "mcr.microsoft.com/dotnet/runtime-deps:10.0",
            readOnly: ["/tmp/morphir packaged e2e/self-contained install"], writable: ["/tmp/sandbox"], user: "1001:121");

        container.FileName.Should().Be("docker");
        var arguments = container.ArgumentList.ToList();
        arguments.Should().StartWith(["run", "--rm", "--network", "none", "--user", "1001:121"]);
        arguments.Should().ContainInOrder("--volume", "/tmp/morphir packaged e2e/self-contained install:/tmp/morphir packaged e2e/self-contained install:ro");
        arguments.Should().ContainInOrder("--volume", "/tmp/sandbox:/tmp/sandbox");
        arguments.Should().ContainInOrder("--workdir", "/tmp/sandbox/work dir");
        arguments.Should().ContainInOrder("--env", "COREHOST_TRACEFILE=/tmp/sandbox/host-trace.txt");
        arguments.Should().ContainInOrder("--env", "MORPHIR_HOME=/tmp/sandbox/home/.morphir");
        arguments.Should().NotContain(argument => argument.StartsWith("PATH=") || argument.StartsWith("GITHUB_TOKEN="), "only the sandbox and trace variables go in");
        arguments.Should().EndWith(["mcr.microsoft.com/dotnet/runtime-deps:10.0", "/tmp/morphir packaged e2e/self-contained install/morphir", "--version"]);
    }
}
