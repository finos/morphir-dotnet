// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;
using Reqnroll.UnitTestProvider;

namespace Morphir.E2E.Tests.Features.Packaged;

/// <summary>Steps of PackagedArtifacts.feature: the four offerings, run from their packaged release artifacts.</summary>
[Binding]
public sealed class PackagedArtifactsSteps(ScenarioContext scenario, IUnitTestRuntimeProvider runtime)
{
    private const string ResultKey = "PackagedRunResult";
    private const string AccessKey = "PackagedRunAccess";

    /// <summary>What the apphost returns when it finds no .NET: 0x80008083 on Windows; a Unix shell sees its low byte, 131.</summary>
    private static int FrameworkMissingExitCode => OperatingSystem.IsWindows() ? unchecked((int)0x80008083) : 0x83;

    private static PackagedInstallation Installation => PackagedInstallation.For(ExecutableLocator.CurrentType);

    private PackagedRunResult Result => scenario.TryGetValue(ResultKey, out PackagedRunResult result)
        ? result
        : throw new InvalidOperationException("No packaged run in this scenario.");

    [Given("the packaged artifact under test is installed into a new directory whose path contains a space")]
    public void GivenTheArtifactIsInstalled()
    {
        var installation = Installation;
        installation.Directory.Should().Contain(" ", "the installation path contains a space");
        File.Exists(installation.LaunchPath).Should().BeTrue($"{installation.LaunchPath} is the launch path");
    }

    [Then("the archive matches the SHA-256 in its launch metadata")]
    public void ThenTheArchiveMatches() =>
        ReleaseFiles.Sha256(Installation.Artifact.ArchivePath).Should().Be(Installation.Artifact.Sha256);

    [Then("the launch path of the launch metadata is in the installed directory")]
    public void ThenTheLaunchPathIsInstalled()
    {
        Path.GetFullPath(Installation.LaunchPath).Should().StartWith(Path.GetFullPath(Installation.Directory));
        File.Exists(Installation.LaunchPath).Should().BeTrue();
    }

    [Then("the installed layout is what the offering claims")]
    public void ThenTheLayoutIsWhatTheOfferingClaims()
    {
        var installation = Installation;
        var files = Directory.GetFiles(installation.Directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(installation.Directory, path).Replace('\\', '/'))
            .ToList();
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtime = new[] { RuntimeLibrary("hostfxr"), RuntimeLibrary("coreclr"), "System.Private.CoreLib.dll" };

        switch (installation.Artifact.Deployment)
        {
            case "native-aot":
                // One native file. Not a .NET single-file bundle, which would also be one file: the executable has no
                // bundle signature and carries the Native AOT runtime's debug header export.
                files.Should().Equal(installation.Artifact.LaunchPath);
                names.Should().NotContain(runtime);
                ExecutableStructure.Of(installation.LaunchPath).Should().Be(ExecutableKind.NativeAot);
                break;
            case "self-contained":
                // A folder that carries its runtime and the F# compiler service, untrimmed.
                names.Should().Contain(["morphir.dll", "morphir.runtimeconfig.json", "FSharp.Compiler.Service.dll", .. runtime]);
                break;
            case "framework-dependent":
                // A folder without a runtime; its runtimeconfig names the shared framework.
                names.Should().Contain(["morphir.dll", "morphir.runtimeconfig.json", "FSharp.Compiler.Service.dll"]);
                names.Should().NotContain(runtime);
                File.ReadAllText(Path.Combine(installation.Directory, "morphir.runtimeconfig.json")).Should().Contain("Microsoft.NETCore.App");
                break;
            case "dotnet-tool":
                // A .NET tool installation: the dotnet-morphir apphost and the tool store with the managed composition.
                names.Should().Contain(["dotnet-morphir.dll", "Morphir.Tooling.Managed.dll", "FSharp.Compiler.Service.dll"]);
                names.Should().NotContain(["morphir.dll", .. runtime]);
                break;
            default:
                throw new InvalidOperationException($"No layout claim for {installation.Artifact.Deployment}.");
        }
    }

    [When("I run the installed artifact with \"(.*)\" from a new empty directory")]
    public async Task WhenIRunFromAnEmptyDirectory(string arguments) =>
        await Run(arguments.Split(' '), RuntimeAccess.Visible, work => Directory.EnumerateFileSystemEntries(work).Should().BeEmpty());

    [When("I run the installed artifact with \"(.*)\" with no .NET runtime reachable")]
    public async Task WhenIRunWithoutARuntime(string arguments)
    {
        // A runtime at a location that this run cannot hide would make the scenario prove nothing, so it is skipped
        // with the reason, never passed.
        var plan = PackagedRun.HidingPlanForThisMachine();
        if (plan.Mode == RuntimeHidingMode.NotPossible)
            runtime.TestIgnore($"No .NET runtime can be hidden on this machine: {plan.Reason}.");
        await Run(arguments.Split(' '), RuntimeAccess.Hidden);
    }

    [When("I run the installed artifact with \"(.*)\" with the machine's .NET runtime reachable")]
    public async Task WhenIRunWithTheMachineRuntime(string arguments) => await Run(arguments.Split(' '), RuntimeAccess.Visible);

    [When("I compile an F# source file with the installed artifact")]
    public async Task WhenICompileFSharp() =>
        await Run(["compile", "--language", "fsharp", "--input", "my sources/Rates.fs", "--package-name", "acme/demo", "--json"], RuntimeAccess.Visible,
            work => File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(work, "my sources")).FullName, "Rates.fs"),
                "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n"));

    [When("I validate the model fixture \"(.*)\" with the installed artifact")]
    public async Task WhenIValidate(string fixture)
    {
        // The input file is passed by an absolute path that contains spaces.
        var source = new TestDataResolver().ResolvePath($"<model-fixtures>/{fixture}");
        string? input = null;
        await Run(() => ["validate", "--input", input!, "--json"], RuntimeAccess.Visible, work =>
        {
            input = Path.Combine(Directory.CreateDirectory(Path.Combine(work, "model inputs")).FullName, fixture);
            File.Copy(source, input);
        });
    }

    private Task Run(string[] arguments, RuntimeAccess access, Action<string>? prepare = null) => Run(() => arguments, access, prepare);

    private async Task Run(Func<string[]> arguments, RuntimeAccess access, Action<string>? prepare)
    {
        scenario[AccessKey] = access;
        scenario[ResultKey] = await PackagedRun.RunAsync(Installation, arguments, access, work =>
        {
            work.Should().Contain(" ", "the working directory path contains a space");
            prepare?.Invoke(work);
        });
    }

    [Then("the installed artifact exits with code (\\d+)")]
    public void ThenTheArtifactExitsWith(int code) => Result.ExitCode.Should().Be(code, Result.Describe());

    [Then("the installed artifact prints the release of its launch metadata")]
    public void ThenTheArtifactPrintsItsRelease() =>
        Result.StandardOutput.Trim().Split('+')[0].Should().Be(Installation.Artifact.Release, Result.Describe());

    [Then("the installed artifact lists the extensions of its host")]
    public void ThenTheArtifactListsItsHost()
    {
        using var report = JsonDocument.Parse(Result.StandardOutput);
        report.RootElement.GetProperty("host").GetString().Should().Be(ExecutableLocator.HostFor(ExecutableLocator.CurrentType));
        report.RootElement.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetProperty("id").GetString())
            .Should().Contain(["morphir.csharp", "morphir.fsharp", "morphir.type-validator"]);
    }

    [Then("the F# compile follows the in-process F# frontend of the launch metadata")]
    public void ThenTheFSharpCompileFollowsTheFrontend()
    {
        using var report = JsonDocument.Parse(Result.StandardOutput);
        if (Installation.Artifact.InProcessFSharpFrontend)
        {
            Result.ExitCode.Should().Be(0, Result.Describe());
            report.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(Result.Describe());
            report.RootElement.GetProperty("frontend").GetString().Should().Be("morphir-dotnet.fsharp (in-process)");
        }
        else
        {
            // The refusal names where an F# frontend is available.
            Result.ExitCode.Should().Be(2, Result.Describe());
            var diagnostic = report.RootElement.GetProperty("diagnostics").EnumerateArray().Single();
            diagnostic.GetProperty("code").GetString().Should().Be("compile.frontend_unavailable");
            diagnostic.GetProperty("message").GetString().Should().Contain("dotnet-morphir").And.Contain("--extension");
        }
    }

    [Then("the installed artifact runs without a runtime as its launch metadata says")]
    public async Task ThenTheArtifactRunsWithoutARuntime()
    {
        var artifact = Installation.Artifact;
        if (artifact.BundledRuntime)
        {
            Result.ExitCode.Should().Be(0, Result.Describe());
            Result.StandardOutput.Trim().Split('+')[0].Should().Be(artifact.Release);
            await AssertNoMachineRuntime();
            return;
        }

        var trace = Result.HostTrace ?? throw new InvalidOperationException($"The apphost wrote no host trace.\n{Result.Describe()}");
        if (Result.ExitCode == 0)
        {
            // The apphost found a runtime although the run hid every one it knew of. If it came from a registered or
            // default location, this machine's runtime could not be hidden after all: skipped, not passed.
            var fxr = HostTrace.ResolvedFxr(trace);
            var unhideable = PackagedRun.RegisteredRuntimeRoots().Concat(PackagedRun.DefaultRuntimeRoots()).ToList();
            if (fxr is not null && unhideable.Any(root => HostTrace.IsUnder(fxr, root)))
                runtime.TestIgnore($"The apphost still found the runtime at {fxr}, a location this run could not hide.");
            throw new InvalidOperationException($"The runtime was hidden, yet the apphost started with hostfxr {fxr}.\n{Result.Describe()}");
        }

        // The application cannot start, so the apphost's message is all a user sees. The installation guide repeats
        // what to do (docs/content/docs/getting-started/installation.md).
        Result.ExitCode.Should().Be(FrameworkMissingExitCode, Result.Describe());
        Result.StandardError.Should().Contain("You must install .NET to run this application", Result.Describe())
            .And.Contain("https://aka.ms/dotnet-core-applaunch");
        if (OperatingSystem.IsWindows() && Result.Environment.GetValueOrDefault("ProgramW6432") is { } emptyRoot)
        {
            // The default location the apphost looked in is the empty folder, so the hiding reached the host.
            var location = HostTrace.DefaultLocation(trace);
            location.Should().NotBeNull(trace);
            HostTrace.IsUnder(location!, emptyRoot).Should().BeTrue($"the default location {location} is the empty {emptyRoot}");
        }
    }

    [Then("the installed artifact uses the runtime that its launch metadata declares")]
    public async Task ThenTheArtifactUsesTheDeclaredRuntime()
    {
        var artifact = Installation.Artifact;
        Result.ExitCode.Should().Be(0, Result.Describe());
        if (artifact.BundledRuntime)
        {
            // Even with a machine runtime reachable, a bundled offering never uses it.
            await AssertNoMachineRuntime();
            return;
        }

        artifact.RequiredRuntime.Should().Be("Microsoft.NETCore.App 10.0");
        var trace = Result.HostTrace ?? throw new InvalidOperationException("The apphost wrote no host trace.");
        var fxr = HostTrace.ResolvedFxr(trace);
        fxr.Should().NotBeNull(trace);
        PackagedRun.MachineRuntimeRoots().Should().Contain(root => HostTrace.IsUnder(fxr!, root),
            "a framework-dependent offering runs on the machine's shared runtime");
        // The runtimeconfig asks for Microsoft.NETCore.App 10.0.0 with the default roll-forward (Minor): the newest
        // 10.0 patch, or a higher 10.x minor when no 10.0 patch is installed, never 11.
        HostTrace.ChosenFramework(trace).Should().StartWith("10.", trace);
    }

    /// <summary>
    /// The process loaded no runtime from the machine. native-aot ran no .NET host at all: no host trace file, no host
    /// tracing on standard error (where the host writes when it cannot open the file), an executable that is Native AOT
    /// and not a single-file bundle, and a control apphost run through the same sandbox that does write a trace.
    /// self-contained resolved hostfxr and CoreCLR inside its installation only.
    /// </summary>
    private async Task AssertNoMachineRuntime()
    {
        var installation = Installation;
        if (installation.Artifact.Execution == "native")
        {
            Result.HostTrace.Should().BeNull("a Native AOT executable runs no .NET host, so there is no host trace");
            HostTrace.IsTracingOutput(Result.StandardError).Should().BeFalse(Result.Describe());
            ExecutableStructure.Of(installation.LaunchPath).Should().Be(ExecutableKind.NativeAot);
            var control = await PackagedRun.TraceControlAsync(scenario.TryGetValue(AccessKey, out RuntimeAccess access) ? access : RuntimeAccess.Visible);
            control.HostTrace.Should().NotBeNull($"a known apphost run the same way writes a host trace, so the trace variables work here\n{control.Describe()}");
            control.HostTrace.Should().Contain("--- Invoked apphost");
            return;
        }

        var trace = Result.HostTrace ?? throw new InvalidOperationException("The apphost wrote no host trace.");
        HostTrace.BundledRuntimeViolations(trace, installation.Directory, PackagedRun.MachineRuntimeRoots()).Should().BeEmpty(trace);
    }

    private static string RuntimeLibrary(string name) =>
        OperatingSystem.IsWindows() ? $"{name}.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.dylib" : $"lib{name}.so";
}
