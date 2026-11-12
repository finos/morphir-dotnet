using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using Morphir.Build.Helpers;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for testing (unit tests, E2E tests)
/// </summary>
partial class Build
{
    [Parameter("Time with no test progress after which a Microsoft.Testing.Platform test run takes a hang dump and stops - Default is '10m'")]
    readonly string TestHangTimeout = "10m";

    [Parameter("Test: 'all' runs every test; 'platform' runs only the tests with the PlatformSpecific marker - Default is 'all'")]
    readonly string TestScope = "all";

    [Parameter("Test: the number of test assemblies that run at the same time - Default is half the processors, rounded up, from 1 to 4")]
    readonly int? TestParallelism;

    /// <summary>The variable that names the published self-contained F# frontend folder for its process test.</summary>
    const string FSharpFrontendProcessDirVariable = "MORPHIR_FSHARP_FRONTEND_PROCESS_DIR";

    /// <summary>Results of the last Test run (reports, hang logs and hang dumps), one directory for each test assembly.</summary>
    AbsolutePath TestResultsDirectory => RootDirectory / "artifacts" / "test-results";

    /// <summary>
    /// Run the test assemblies of <see cref="Morphir.Build.Helpers.TestPlan.Suites"/> (TUnit and Expecto), then the
    /// Myriad package-consumer checks. <c>--test-scope platform</c> runs only the tests with the PlatformSpecific
    /// marker: CI uses it on the operating systems other than Linux, where the managed tests already ran.
    /// <c>--test-parallelism</c> runs more than one assembly at the same time.
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
            var buildTestDll = TestsDirectory / "Morphir.Build.Tests" / "bin" / Configuration / CurrentTargetFramework / "Morphir.Build.Tests.dll";
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
        // --no-build: Compile made the assemblies. A pack that builds with the PackageVersion of the scripts changes
        // the assembly information of the packed projects, and then the next Compile builds the solution again.
        // The scripts pack the Release configuration, so with another configuration they must build.
        string[] noBuild = Configuration == "Release" ? ["--no-build"] : [];
        var script = TestsDirectory / "Morphir.Myriad.Tests" / "package-tests.fsx";
        if (RunCommand("dotnet", ["fsi", script, .. noBuild]) != 0)
        {
            throw new Exception("Myriad package-consumer checks failed");
        }

        var jsonScript = TestsDirectory / "Morphir.Myriad.SystemTextJson.Tests" / "package-tests.fsx";
        if (RunCommand("dotnet", ["fsi", jsonScript, .. noBuild]) != 0)
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
    /// Parameters: --executable-type (aot, trimmed, untrimmed, tool, all - default: all; or a packaged release profile:
    /// native-aot, self-contained, framework-dependent, dotnet-tool, or offerings for all four, which run from PackRelease artifacts)
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

    /// <summary>The outcome of one test assembly.</summary>
    readonly record struct SuiteResult(TestSuite Suite, int ExitCode, TimeSpan Duration);

    // Helper method for running tests
    void RunTests(string configuration)
    {
        if (!Morphir.Build.Helpers.TestScope.TryParse(TestScope, out var scope))
            throw new Exception($"The test scope '{TestScope}' is not all or platform.");
        var parallelism = TestParallelism ?? TestPlan.DefaultParallelism(Environment.ProcessorCount);
        if (parallelism < 1)
            throw new Exception($"The test parallelism {parallelism} is not 1 or more.");

        var runs = TestPlan.Select(scope, TestPlan.Suites, HasPlatformTests);
        Serilog.Log.Information("Running tests with configuration: {Configuration}", configuration);
        Serilog.Log.Information("Test scope: {Scope}. {Count} of {Total} test assemblies, {Parallelism} at the same time.",
            scope, runs.Length, TestPlan.Suites.Length, parallelism);
        Serilog.Log.Information("================================================");

        // A scope that selects nothing must not pass: the marker or its filter is then wrong.
        if (runs.IsEmpty)
            throw new Exception($"The test scope '{scope}' selects no test assembly: no test source below {TestsDirectory} has the {TestPlan.PlatformMarker} marker.");

        // A retry must not see the hang logs of the attempt before it.
        TestResultsDirectory.CreateOrCleanDirectory();

        // The self-contained process test of the F# frontend runs against a folder published once here, not in the
        // test. Only that assembly gets the variable.
        const string frontendTests = "Morphir.Frontend.FSharp.Extension.Tests";
        var frontendProcess = runs.Any(run => run.Suite.Project == frontendTests)
            ? PublishFSharpFrontendProcessForTests()
            : null;

        // The link-refusal tests need a temporary directory with no link in its path. The default one of macOS is
        // below /var, which is a link, so every test process gets the physical path.
        var temporary = PhysicalPath(Path.GetTempPath());

        SuiteResult Run(TestRun run, bool buffered)
        {
            var environment = new Dictionary<string, string> { ["TMPDIR"] = temporary, ["TEMP"] = temporary, ["TMP"] = temporary };
            if (run.Suite.Project == frontendTests && frontendProcess is not null)
                environment[FSharpFrontendProcessDirVariable] = frontendProcess.ToString();
            return RunSuite(run, configuration, environment, buffered);
        }

        var results = new List<SuiteResult>();
        if (parallelism == 1)
        {
            foreach (var run in runs) results.Add(Run(run, buffered: false));
        }
        else
        {
            // Each worker starts the next assembly that can start (TestPlan.NextRunnable), so the long assemblies do
            // not wait for each other. The assemblies that run MSBuild on source projects run one at a time, and an
            // assembly with time budgets runs alone.
            // The output of an assembly is one block, written when the assembly completes.
            var gate = new object();
            var pending = runs.ToList();
            var running = new List<TestSuite>();

            void Work()
            {
                while (true)
                {
                    TestRun? run;
                    lock (gate)
                    {
                        while ((run = TestPlan.NextRunnable(pending, running)) is null)
                        {
                            if (pending.Count == 0) return;
                            Monitor.Wait(gate);
                        }

                        pending.Remove(run);
                        running.Add(run.Suite);
                    }

                    SuiteResult result;
                    try
                    {
                        result = Run(run, buffered: true);
                    }
                    finally
                    {
                        lock (gate)
                        {
                            running.Remove(run.Suite);
                            Monitor.PulseAll(gate);
                        }
                    }

                    lock (gate) results.Add(result);
                }
            }

            Task.WaitAll(Enumerable.Range(0, Math.Min(parallelism, runs.Length)).Select(_ => Task.Run(Work)).ToArray());
        }

        ReportHangs();
        ReportSuites(scope, results);

        var failed = results.Where(result => result.ExitCode != 0).ToArray();
        if (failed.Length > 0)
        {
            Serilog.Log.Error("");
            Serilog.Log.Error("================================================");
            Serilog.Log.Error("Tests FAILED");
            foreach (var result in failed)
                Serilog.Log.Error("  {Project}: exit code {ExitCode}", result.Suite.Project, result.ExitCode);
            throw new Exception("Tests failed");
        }

        Serilog.Log.Information("");
        Serilog.Log.Information("================================================");
        Serilog.Log.Information("All tests PASSED");
    }

    /// <summary>
    /// True when a test source file or feature file of the suite has the PlatformSpecific marker. The sources are the
    /// one record of the marked tests, so the build holds no second list that can differ from them.
    /// </summary>
    bool HasPlatformTests(TestSuite suite)
    {
        var directory = TestsDirectory / suite.Project;
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".cs" or ".fs" or ".feature")
            .Where(file => !Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Any(file => TestPlan.MentionsPlatformMarker(File.ReadAllText(file)));
    }

    /// <summary>
    /// Runs one test assembly. A Microsoft.Testing.Platform assembly gets a hang dump: after
    /// <see cref="TestHangTimeout"/> with no test progress, the platform writes the names of the running tests
    /// and a mini dump to <see cref="TestResultsDirectory"/>, then stops the test process.
    /// </summary>
    SuiteResult RunSuite(TestRun run, string configuration, IReadOnlyDictionary<string, string> environment, bool buffered)
    {
        var project = run.Suite.Project;
        var testDll = TestsDirectory / project / "bin" / configuration / CurrentTargetFramework / $"{project}.dll";
        string[] hangDump = run.Suite.TakesHangDump
            ? ["--hangdump", "--hangdump-timeout", TestHangTimeout, "--hangdump-type", "Mini", "--results-directory", TestResultsDirectory / project]
            : [];

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])["exec", testDll, .. run.Arguments, .. hangDump]) start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment) start.Environment[name] = value;

        // (isError, line) in the order of arrival, when the output is one block.
        var lines = new List<(bool IsError, string Text)>();
        var ranNoTests = false;
        void Write(bool isError, string? line)
        {
            if (line is null) return;
            if (TestPlan.ReportsNoTests(line)) ranNoTests = true;
            if (buffered) lock (lines) lines.Add((isError, line));
            else if (isError) Serilog.Log.Error(line);
            else Serilog.Log.Information(line);
        }

        if (!buffered)
        {
            Serilog.Log.Information("");
            Serilog.Log.Information("Running {Project}...", project);
        }

        var watch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Write(false, e.Data);
        process.ErrorDataReceived += (_, e) => Write(true, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        // A suite runs because it has tests, or in the platform scope because it has the marker. A run that selects
        // no test means that the marker or its filter is wrong. TUnit exits with 8 then; Expecto exits with 0.
        var exitCode = process.ExitCode == 0 && ranNoTests ? NoTestsExitCode : process.ExitCode;
        var result = new SuiteResult(run.Suite, exitCode, watch.Elapsed);

        lock (SuiteOutputLock)
        {
            foreach (var (isError, text) in lines)
            {
                if (isError) Serilog.Log.Error(text);
                else Serilog.Log.Information(text);
            }

            if (result.ExitCode == 0)
                Serilog.Log.Information("{Project}: passed in {Seconds:0}s", project, result.Duration.TotalSeconds);
            else if (result.ExitCode == NoTestsExitCode)
                Serilog.Log.Error("{Project}: the run selected no test (exit code {ExitCode})", project, result.ExitCode);
            else
                Serilog.Log.Error("{Project}: exit code {ExitCode} after {Seconds:0}s", project, result.ExitCode, result.Duration.TotalSeconds);
        }

        return result;
    }

    /// <summary>
    /// The path with each directory that is a link replaced by its target, for example /private/var/folders/x on
    /// macOS for /var/folders/x.
    /// </summary>
    static string PhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full) ?? throw new Exception($"{full} has no root.");
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target) current = target.FullName;
        }

        return current;
    }

    /// <summary>The exit code of a test assembly that ran no test. Microsoft.Testing.Platform uses the same number.</summary>
    const int NoTestsExitCode = 8;

    /// <summary>One assembly writes its buffered output at a time.</summary>
    static readonly object SuiteOutputLock = new();

    /// <summary>Logs the time of each test assembly, longest first. In GitHub Actions it also adds them to the job summary.</summary>
    static void ReportSuites(Morphir.Build.Helpers.TestScope scope, IReadOnlyCollection<SuiteResult> results)
    {
        var summary = new System.Text.StringBuilder($"### Test assemblies (scope: {scope})\n\n| Assembly | Result | Seconds |\n| --- | --- | ---: |\n");
        foreach (var result in results.OrderByDescending(result => result.Duration))
        {
            var outcome = result.ExitCode == 0 ? "passed" : $"exit code {result.ExitCode}";
            Serilog.Log.Information("  {Seconds,5:0}s  {Project} ({Outcome})", result.Duration.TotalSeconds, result.Suite.Project, outcome);
            summary.Append($"| {result.Suite.Project} | {outcome} | {result.Duration.TotalSeconds:0} |\n");
        }

        var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(stepSummary)) File.AppendAllText(stepSummary, summary.Append('\n').ToString());
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
