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
    /// Run unit tests (Morphir.Core.Tests, Morphir.Tooling.Tests)
    /// Tests are run using TUnit test framework
    /// </summary>
    Target Test => _ => _
        .DependsOn(Compile)
        .Description("Run all tests")
        .Executes(() =>
        {
            RunTests(Configuration);
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
    /// Generate Wolverine code for Morphir.Tooling
    /// This target replaces the GenerateWolverineCode MSBuild target that was removed from Directory.Build.targets
    /// Run this before publishing trimmed executables to ensure generated code is included
    /// </summary>
    Target GenerateWolverineCode => _ => _
        .DependsOn(Compile)
        .Description("Generate Wolverine code for Morphir.Tooling (run before trimmed publishes)")
        .Executes(() =>
        {
            Serilog.Log.Information("Generating Wolverine code...");
            
            // Run codegen write command using the compiled Morphir CLI
            var morphirDll = SourceDirectory / "Morphir" / "bin" / Configuration / "net10.0" / "morphir.dll";
            
            if (!File.Exists(morphirDll))
            {
                throw new Exception($"Morphir CLI not found at {morphirDll}. Run Compile target first.");
            }
            
            var exitCode = RunCommand("dotnet", "exec", morphirDll, "codegen", "write");
            
            if (exitCode != 0)
            {
                throw new Exception($"Wolverine code generation failed with exit code {exitCode}");
            }
            
            var generatedDir = SourceDirectory / "Morphir.Tooling" / "Internal" / "Generated";
            if (Directory.Exists(generatedDir))
            {
                var fileCount = Directory.GetFiles(generatedDir, "*.cs", System.IO.SearchOption.AllDirectories).Length;
                Serilog.Log.Information($"✓ Generated {fileCount} files in {generatedDir}");
            }
            else
            {
                Serilog.Log.Warning("⚠ Warning: Generated code directory not found");
            }
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

        // Check if any tests failed
        if (coreExitCode != 0 || toolingExitCode != 0)
        {
            Serilog.Log.Error("");
            Serilog.Log.Error("================================================");
            Serilog.Log.Error("Tests FAILED");
            Serilog.Log.Error($"  Morphir.Core.Tests: exit code {coreExitCode}");
            Serilog.Log.Error($"  Morphir.Tooling.Tests: exit code {toolingExitCode}");
            throw new Exception("Tests failed");
        }

        Serilog.Log.Information("");
        Serilog.Log.Information("================================================");
        Serilog.Log.Information("All tests PASSED");
    }
}
