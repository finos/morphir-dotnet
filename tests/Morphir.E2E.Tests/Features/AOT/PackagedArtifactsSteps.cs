// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using FluentAssertions;
using Morphir.E2E.Tests.Infrastructure;
using Morphir.TestSupport;

namespace Morphir.E2E.Tests.Features.AOT;

/// <summary>Steps of PackagedArtifacts.feature: the four offerings, run from their packaged release artifacts.</summary>
[Binding]
public sealed class PackagedArtifactsSteps(ScenarioContext scenario)
{
    private const string ResultKey = "PackagedRunResult";

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
                // One native file. Not a .NET single-file bundle: there is no managed runtime or assembly to extract.
                files.Should().Equal(installation.Artifact.LaunchPath);
                names.Should().NotContain(runtime);
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
        scenario[ResultKey] = await PackagedRun.RunAsync(Installation, arguments.Split(' '), RuntimeAccess.Visible);

    [When("I run the installed artifact with \"(.*)\" with no .NET runtime reachable")]
    public async Task WhenIRunWithoutARuntime(string arguments) =>
        scenario[ResultKey] = await PackagedRun.RunAsync(Installation, arguments.Split(' '), RuntimeAccess.Hidden);

    [When("I run the installed artifact with \"(.*)\" with the machine's .NET runtime reachable")]
    public async Task WhenIRunWithTheMachineRuntime(string arguments) =>
        scenario[ResultKey] = await PackagedRun.RunAsync(Installation, arguments.Split(' '), RuntimeAccess.Visible);

    [When("I compile an F# source file with the installed artifact")]
    public async Task WhenICompileFSharp() =>
        scenario[ResultKey] = await PackagedRun.RunAsync(Installation,
            ["compile", "--language", "fsharp", "--input", "src/Rates.fs", "--package-name", "acme/demo", "--json"], RuntimeAccess.Visible,
            work => File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(work, "src")).FullName, "Rates.fs"),
                "module Acme.Demo.Rates\n\nlet dailyRate (days: int) : int = days * 40\n"));

    [When("I validate the model fixture \"(.*)\" with the installed artifact")]
    public async Task WhenIValidate(string fixture)
    {
        var path = new TestDataResolver().ResolvePath($"<model-fixtures>/{fixture}");
        scenario[ResultKey] = await PackagedRun.RunAsync(Installation, ["validate", "--input", path, "--json"], RuntimeAccess.Visible);
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
    public void ThenTheArtifactRunsWithoutARuntime()
    {
        var artifact = Installation.Artifact;
        if (artifact.BundledRuntime)
        {
            Result.ExitCode.Should().Be(0, Result.Describe());
            Result.StandardOutput.Trim().Split('+')[0].Should().Be(artifact.Release);
            AssertNoMachineRuntime();
        }
        else if (PackagedRun.UnhideableRuntimeRoots() is { Count: > 0 } unhideable && Result.ExitCode == 0)
        {
            // This machine has a runtime where every apphost looks, so it cannot be hidden. The run is still proof
            // that the hidden variables and PATH played no part: the host found the runtime only there.
            var fxr = HostTrace.ResolvedFxr(Result.HostTrace ?? "");
            fxr.Should().NotBeNull(Result.Describe());
            unhideable.Should().Contain(root => HostTrace.IsUnder(fxr!, root), $"with the runtime hidden, {fxr} must come from a default location");
        }
        else
        {
            // The application cannot start, so the apphost's message is all a user sees. The installation guide
            // repeats what to do (docs/content/docs/getting-started/installation.md).
            Result.ExitCode.Should().NotBe(0, Result.Describe());
            Result.StandardError.Should().Contain("You must install .NET to run this application", Result.Describe())
                .And.Contain("https://aka.ms/dotnet-core-applaunch");
        }
    }

    [Then("the installed artifact uses the runtime that its launch metadata declares")]
    public void ThenTheArtifactUsesTheDeclaredRuntime()
    {
        var artifact = Installation.Artifact;
        Result.ExitCode.Should().Be(0, Result.Describe());
        if (artifact.BundledRuntime)
        {
            // Even with a machine runtime reachable, a bundled offering never uses it.
            AssertNoMachineRuntime();
            return;
        }

        artifact.RequiredRuntime.Should().Be("Microsoft.NETCore.App 10.0");
        var trace = Result.HostTrace ?? throw new InvalidOperationException("The apphost wrote no host trace.");
        var fxr = HostTrace.ResolvedFxr(trace);
        fxr.Should().NotBeNull(trace);
        PackagedRun.MachineRuntimeRoots().Should().Contain(root => HostTrace.IsUnder(fxr!, root),
            "a framework-dependent offering runs on the machine's shared runtime");
        HostTrace.ChosenFramework(trace).Should().StartWith("10.", "the runtimeconfig asks for Microsoft.NETCore.App 10.0");
    }

    /// <summary>
    /// The process loaded no runtime from the machine: native-aot ran no .NET host at all (no host trace, and no
    /// runtime files in the installation); self-contained resolved hostfxr and CoreCLR inside its installation only.
    /// </summary>
    private void AssertNoMachineRuntime()
    {
        var installation = Installation;
        if (installation.Artifact.Execution == "native")
        {
            Result.HostTrace.Should().BeNull("a Native AOT executable runs no .NET host, so there is no host trace");
            return;
        }

        var trace = Result.HostTrace ?? throw new InvalidOperationException("The apphost wrote no host trace.");
        trace.Should().Contain("Using app-local location", "the host runs in self-contained mode");
        var loaded = HostTrace.LoadedRuntimePaths(trace);
        loaded.Should().NotBeEmpty(trace);
        loaded.Should().OnlyContain(path => HostTrace.IsUnder(path, installation.Directory), "hostfxr and CoreCLR come from the installation");
        foreach (var root in PackagedRun.MachineRuntimeRoots())
            loaded.Should().NotContain(path => HostTrace.IsUnder(path, root), $"nothing is loaded from the machine runtime at {root}");
    }

    private static string RuntimeLibrary(string name) =>
        OperatingSystem.IsWindows() ? $"{name}.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.dylib" : $"lib{name}.so";
}
