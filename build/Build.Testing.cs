using System;
using System.Diagnostics;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for testing (unit tests, E2E tests)
/// </summary>
partial class Build
{
    /// <summary>
    /// Run unit tests (Morphir.Core.Tests, Morphir.Tooling.Tests, Morphir.Internal.CodeGeneration.Tests, Morphir.Models.Tests, Morphir.IR.Pipeline.Tests, Morphir.SDK.Tests)
    /// Tests are run using TUnit and Expecto test frameworks
    /// </summary>
    Target Test => _ => _
        .DependsOn(Compile)
        .Description("Run unit tests (Morphir.Core.Tests, Morphir.Tooling.Tests, Morphir.Internal.CodeGeneration.Tests, Morphir.Models.Tests, Morphir.IR.Pipeline.Tests, Morphir.SDK.Tests)")
        .Executes(() =>
        {
            RunTests(Configuration);
        });

    /// <summary>
    /// Run build tests (Morphir.Build.Tests)
    /// Validates package structure, metadata, and local installation
    /// NOTE: Requires packages to be built first (run PackAll target)
    /// </summary>
    Target TestBuild => _ => _
        .DependsOn(Compile)
        .Description("Run build/package validation tests (requires packages to be built)")
        .Executes(() =>
        {
            Serilog.Log.Information("Running build/package validation tests...");

            // Check if packages exist
            var packagesDir = OutputDir;
            if (!System.IO.Directory.Exists(packagesDir) ||
                !System.IO.Directory.GetFiles(packagesDir, "*.nupkg").Any())
            {
                Serilog.Log.Warning("⚠ No packages found in {0}", packagesDir);
                Serilog.Log.Warning("⚠ Build tests require packages to be built first");
                Serilog.Log.Warning("⚠ Run: ./build.sh PackAll --configuration {0}", Configuration);
                Serilog.Log.Warning("⚠ Skipping build tests");
                return;
            }

            Serilog.Log.Information("Running Morphir.Build.Tests...");

            // Use dotnet exec to run the test assembly directly (required for .NET 10 SDK + TUnit)
            var buildTestDll = TestsDirectory / "Morphir.Build.Tests" / "bin" / Configuration / "net10.0" / "Morphir.Build.Tests.dll";
            var exitCode = RunCommand("dotnet", "exec", buildTestDll);

            // Exit code 0 = success, 8 = all tests skipped (expected when packages don't exist)
            if (exitCode != 0 && exitCode != 8)
            {
                throw new Exception($"Build tests failed with exit code {exitCode}");
            }

            if (exitCode == 8)
            {
                Serilog.Log.Warning("✓ Build tests SKIPPED (packages not built)");
            }
            else
            {
                Serilog.Log.Information("✓ Build tests PASSED");
            }
        });

    /// <summary>
    /// Run all tests (unit tests and build tests)
    /// Equivalent to running Test and TestBuild targets
    /// NOTE: TestBuild requires packages to be built first (run PackAll target)
    /// </summary>
    Target TestAll => _ => _
        .DependsOn(Test, TestBuild)
        .Description("Run all tests (unit tests and build tests)")
        .Executes(() =>
        {
            Serilog.Log.Information("All tests completed successfully");
        });

    /// <summary>
    /// Build the E2E test project (Morphir.E2E.Tests)
    /// E2E tests use Reqnroll (BDD/Gherkin) to test executables
    /// </summary>
    Target BuildE2ETests => _ => _
        .DependsOn(Compile)
        .Description("Build the E2E test project")
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(MorphirE2ETestsProject)
                .SetConfiguration(Configuration)
                .SetNoRestore(false));
        });

    /// <summary>
    /// Run end-to-end tests against Morphir executables (BDD/Gherkin using Reqnroll)
    /// Parameters: --executable-type (aot, trimmed, untrimmed, or all - default: all)
    /// Note: Requires executables to be published first (PublishExecutable, PublishSingleFile, etc.)
    /// </summary>
    Target TestE2E => _ => _
        .DependsOn(BuildE2ETests)
        .Description("Run end-to-end tests against Morphir executables (BDD/Gherkin)")
        .Executes(() =>
        {
            Serilog.Log.Information($"Running E2E tests for executable type: {ExecutableType}");
            var exitCode = RunCommand("dotnet",
                ScriptsDirectory / "run-e2e-tests.cs", ExecutableType, Configuration);

            if (exitCode != 0)
            {
                throw new Exception($"E2E tests failed with exit code {exitCode}");
            }
        });

    // Helper method for running tests
    void RunTests(string configuration)
    {
        Serilog.Log.Information($"Running tests with configuration: {configuration}");
        Serilog.Log.Information("================================================");

        // Run Morphir.Core.Tests
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.Core.Tests...");
        var coreTestDll = TestsDirectory / "Morphir.Core.Tests" / "bin" / configuration / "net10.0" / "Morphir.Core.Tests.dll";
        var coreExitCode = RunCommand("dotnet", "exec", coreTestDll);

        // Run Morphir.Tooling.Tests
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.Tooling.Tests...");
        var toolingTestDll = TestsDirectory / "Morphir.Tooling.Tests" / "bin" / configuration / "net10.0" / "Morphir.Tooling.Tests.dll";
        var toolingExitCode = RunCommand("dotnet", "exec", toolingTestDll);

        // Run Morphir.Internal.CodeGeneration.Tests
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.Internal.CodeGeneration.Tests...");
        var codeGenTestDll = TestsDirectory / "Morphir.Internal.CodeGeneration.Tests" / "bin" / configuration / "net10.0" / "Morphir.Internal.CodeGeneration.Tests.dll";
        var codeGenExitCode = RunCommand("dotnet", "exec", codeGenTestDll);

        // Run Morphir.Models.Tests (Expecto)
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.Models.Tests...");
        var modelsTestDll = TestsDirectory / "Morphir.Models.Tests" / "bin" / configuration / "net10.0" / "Morphir.Models.Tests.dll";
        var modelsExitCode = RunCommand("dotnet", "exec", modelsTestDll);

        // Run Morphir.IR.Pipeline.Tests (Expecto)
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.IR.Pipeline.Tests...");
        var pipelineTestDll = TestsDirectory / "Morphir.IR.Pipeline.Tests" / "bin" / configuration / "net10.0" / "Morphir.IR.Pipeline.Tests.dll";
        var pipelineExitCode = RunCommand("dotnet", "exec", pipelineTestDll);

        // Run Morphir.SDK.Tests (Expecto)
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.SDK.Tests...");
        var sdkTestDll = TestsDirectory / "Morphir.SDK.Tests" / "bin" / configuration / "net10.0" / "Morphir.SDK.Tests.dll";
        var sdkExitCode = RunCommand("dotnet", "exec", sdkTestDll);

        // Check if any tests failed
        if (coreExitCode != 0 || toolingExitCode != 0 || codeGenExitCode != 0 || modelsExitCode != 0 || pipelineExitCode != 0 || sdkExitCode != 0)
        {
            Serilog.Log.Error("");
            Serilog.Log.Error("================================================");
            Serilog.Log.Error("Tests FAILED");
            Serilog.Log.Error($"  Morphir.Core.Tests: exit code {coreExitCode}");
            Serilog.Log.Error($"  Morphir.Tooling.Tests: exit code {toolingExitCode}");
            Serilog.Log.Error($"  Morphir.Internal.CodeGeneration.Tests: exit code {codeGenExitCode}");
            Serilog.Log.Error($"  Morphir.Models.Tests: exit code {modelsExitCode}");
            Serilog.Log.Error($"  Morphir.IR.Pipeline.Tests: exit code {pipelineExitCode}");
            Serilog.Log.Error($"  Morphir.SDK.Tests: exit code {sdkExitCode}");
            throw new Exception("Tests failed");
        }

        Serilog.Log.Information("");
        Serilog.Log.Information("================================================");
        Serilog.Log.Information("All tests PASSED");
    }
}
