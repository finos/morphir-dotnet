using System;
using System.Diagnostics;
using System.IO;
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
    [Parameter("Time with no test progress after which a Microsoft.Testing.Platform test run takes a hang dump and stops - Default is '10m'")]
    readonly string TestHangTimeout = "10m";

    /// <summary>Results of the last Test run (reports, hang logs and hang dumps), one directory for each test assembly.</summary>
    AbsolutePath TestResultsDirectory => RootDirectory / "artifacts" / "test-results";

    /// <summary>The test assemblies that run on Microsoft.Testing.Platform and reference its HangDump extension.</summary>
    static readonly string[] HangDumpProjects =
    [
        "Morphir.Core.Tests",
        "Morphir.Tooling.Tests",
        "Morphir.Configuration.IO.Tests",
        "Morphir.Configuration.Microsoft.Tests",
        "Morphir.TestSupport.Tests",
        "Morphir.SDK.Conformance.Tests",
        "Morphir.SDK.CSharp.Tests",
        "Morphir.Tests",
        "Morphir.Managed.Tests",
        "Morphir.Myriad.Integration.Tests",
    ];

    /// <summary>
    /// Run unit tests (Morphir.Core.Tests, Morphir.Tooling.Tests, Morphir.Internal.CodeGeneration.Tests, Morphir.Models.Tests, Morphir.IR.Pipeline.Tests, Morphir.SDK.Tests)
    /// Tests are run using TUnit and Expecto test frameworks
    /// </summary>
    Target Test => _ => _
        .DependsOn(Compile)
        .Description("Run unit suites, Myriad BDD consumers and cold package lifecycle checks")
        .Executes(() =>
        {
            RunTests(Configuration);
            RunMyriadPackageTests();
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
        .Description("Run unit, BDD and package-consumer suites plus build/package validation")
        .Executes(() =>
        {
            Serilog.Log.Information("All tests completed successfully");
        });

    /// <summary>
    /// Exercise cold package consumers, module publication and rebuild behavior.
    /// </summary>
    Target TestMyriadPackages => _ => _
        .DependsOn(Compile)
        .Description("Run Myriad package-consumer lifecycle checks")
        .Executes(RunMyriadPackageTests);

    void RunMyriadPackageTests()
    {
        var script = TestsDirectory / "Morphir.Myriad.Tests" / "package-tests.fsx";
        if (RunCommand("dotnet", "fsi", script) != 0)
        {
            throw new Exception("Myriad package-consumer checks failed");
        }

        var jsonScript = TestsDirectory / "Morphir.Myriad.SystemTextJson.Tests" / "package-tests.fsx";
        if (RunCommand("dotnet", "fsi", jsonScript) != 0)
        {
            throw new Exception("Morphir.Myriad.SystemTextJson package-consumer checks failed");
        }
    }

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

        // A retry must not see the hang logs of the attempt before it.
        TestResultsDirectory.CreateOrCleanDirectory();

        // Run Morphir.Core.Tests
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.Core.Tests...");
        var coreTestDll = TestsDirectory / "Morphir.Core.Tests" / "bin" / configuration / "net10.0" / "Morphir.Core.Tests.dll";
        var coreExitCode = RunTestAssembly("Morphir.Core.Tests", coreTestDll);

        // Run Morphir.Tooling.Tests
        Serilog.Log.Information("");
        Serilog.Log.Information("Running Morphir.Tooling.Tests...");
        var toolingTestDll = TestsDirectory / "Morphir.Tooling.Tests" / "bin" / configuration / "net10.0" / "Morphir.Tooling.Tests.dll";
        var toolingExitCode = RunTestAssembly("Morphir.Tooling.Tests", toolingTestDll);

        // Reuse compiled outputs and invoke each configuration suite's configured runner.
        var configurationTestDll = TestsDirectory / "Morphir.Configuration.Tests" / "bin" / configuration / "net10.0" / "Morphir.Configuration.Tests.dll";
        var configurationExitCode = RunCommand("dotnet", "exec", configurationTestDll, "--sequenced");
        var configurationIoTestDll = TestsDirectory / "Morphir.Configuration.IO.Tests" / "bin" / configuration / "net10.0" / "Morphir.Configuration.IO.Tests.dll";
        var configurationIoExitCode = RunTestAssembly("Morphir.Configuration.IO.Tests", configurationIoTestDll, "--maximum-parallel-tests", "1");

        var configurationMicrosoftTestDll = TestsDirectory / "Morphir.Configuration.Microsoft.Tests" / "bin" / configuration / "net10.0" / "Morphir.Configuration.Microsoft.Tests.dll";
        var configurationMicrosoftExitCode = RunTestAssembly("Morphir.Configuration.Microsoft.Tests", configurationMicrosoftTestDll, "--maximum-parallel-tests", "1");

        // Exercise the actual generated CLI DTO source without loading the CLI host.
        var configurationInspectionSmokeDll = TestsDirectory / "Morphir.Configuration.Inspection.Smoke" / "bin" / configuration / "net10.0" / "Morphir.Configuration.Inspection.Smoke.dll";
        var configurationInspectionExitCode = RunCommand("dotnet", "exec", configurationInspectionSmokeDll);

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

        var myriadExitCodes = new[]
        {
            "Morphir.TestSupport.Tests",
            "Morphir.Frontend.FSharp.Tests",
            "Morphir.SDK.Conformance.Tests",
            "Morphir.SDK.CSharp.Tests",
            "Morphir.IR.Pipeline.Plugins.Tests",
            "Morphir.Extensions.Tests",
            "Morphir.Analyzers.Tests",
            "Morphir.Mep.Tests",
            "Morphir.Toolchain.Tests",
            "Morphir.Compilation.Tests",
            "Morphir.Compilation.Mep.Tests",
            "Morphir.Frontend.FSharp.Extension.Tests",
            "Morphir.Tests",
            "Morphir.Managed.Tests",
            "Morphir.CodeGeneration.FSharp.Tests",
            "Morphir.CodeGeneration.FSharp.Extension.Tests",
            "Morphir.CodeGeneration.CSharp.Tests",
            "Morphir.Ion.Tests",
            "Morphir.Myriad.Tests",
            "Morphir.Myriad.Integration.Tests",
            "Morphir.Myriad.SystemTextJson.Tests",
        }.Select(project =>
        {
            Serilog.Log.Information("Running {Project}...", project);
            var testDll = TestsDirectory / project / "bin" / configuration / "net10.0" / $"{project}.dll";
            var exitCode = project == "Morphir.CodeGeneration.CSharp.Tests"
                ? RunCommand("dotnet", "exec", testDll, "--sequenced")
                : RunTestAssembly(project, testDll);
            if (exitCode != 0)
            {
                Serilog.Log.Error("{Project}: exit code {ExitCode}", project, exitCode);
            }
            return exitCode;
        }).ToArray();

        ReportHangs();

        // Check if any tests failed
        if (coreExitCode != 0 || toolingExitCode != 0 || configurationExitCode != 0 || configurationIoExitCode != 0 || configurationMicrosoftExitCode != 0 || configurationInspectionExitCode != 0 || codeGenExitCode != 0 || modelsExitCode != 0 || pipelineExitCode != 0 || sdkExitCode != 0 || myriadExitCodes.Any(code => code != 0))
        {
            Serilog.Log.Error("");
            Serilog.Log.Error("================================================");
            Serilog.Log.Error("Tests FAILED");
            Serilog.Log.Error($"  Morphir.Core.Tests: exit code {coreExitCode}");
            Serilog.Log.Error($"  Morphir.Tooling.Tests: exit code {toolingExitCode}");
            Serilog.Log.Error($"  Morphir.Configuration.Tests: exit code {configurationExitCode}");
            Serilog.Log.Error($"  Morphir.Configuration.IO.Tests: exit code {configurationIoExitCode}");
            Serilog.Log.Error($"  Morphir.Configuration.Microsoft.Tests: exit code {configurationMicrosoftExitCode}");
            Serilog.Log.Error($"  Morphir.Configuration.Inspection.Smoke: exit code {configurationInspectionExitCode}");
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

    /// <summary>
    /// Runs one test assembly. A Microsoft.Testing.Platform assembly gets a hang dump: after
    /// <see cref="TestHangTimeout"/> with no test progress, the platform writes the names of the running tests
    /// and a mini dump to <see cref="TestResultsDirectory"/>, then stops the test process.
    /// </summary>
    int RunTestAssembly(string project, AbsolutePath testDll, params string[] options)
    {
        string[] hangDump = HangDumpProjects.Contains(project)
            ? ["--hangdump", "--hangdump-timeout", TestHangTimeout, "--hangdump-type", "Mini", "--results-directory", TestResultsDirectory / project]
            : [];
        return RunCommand("dotnet", ["exec", testDll, .. options, .. hangDump]);
    }

    /// <summary>
    /// Logs each hang that the HangDump extension recorded. In GitHub Actions it also sets the step output
    /// <c>hang=true</c> and adds the hang to the job summary, so the workflow can tell a hang from a test failure.
    /// </summary>
    void ReportHangs()
    {
        var logs = TestResultsDirectory.GlobFiles("**/*_hang.log");
        if (logs.Count == 0) return;

        var summary = new System.Text.StringBuilder("### Test hang\n\n");
        foreach (var log in logs)
        {
            var project = log.Parent.Name;
            var running = File.ReadAllText(log).Trim();
            Serilog.Log.Error("Test hang in {Project} after {Timeout} with no test progress. Tests still running:{NewLine}{Tests}",
                project, TestHangTimeout, Environment.NewLine, running);
            summary.Append($"{project} stopped after {TestHangTimeout} with no test progress. Tests still running:\n\n```text\n{running}\n```\n\n");
        }

        var output = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(output)) File.AppendAllText(output, "hang=true\n");
        var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(stepSummary)) File.AppendAllText(stepSummary, summary.ToString());
    }
}
