#!/usr/bin/env dotnet run
// Run end-to-end tests against Morphir executables
// Usage: dotnet run scripts/run-e2e-tests.cs [EXECUTABLE_TYPE] [CONFIGURATION]
// EXECUTABLE_TYPE: aot, trimmed, untrimmed, tool, all (those four), one of the packaged offerings native-aot,
// self-contained, framework-dependent or dotnet-tool, or offerings (all four). The packaged offerings run from the
// artifacts that the PackRelease target writes to artifacts/release (or MORPHIR_RELEASE_DIR). An offering with no
// artifact there is packed first. An artifact packed from another commit than HEAD is refused, unless
// MORPHIR_REUSE_PACKAGED_ARTIFACTS=true says to test it anyway.

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;

var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var executableType = scriptArgs.Length > 0 ? scriptArgs[0] : "all";
var config = scriptArgs.Length > 1 ? scriptArgs[1] : "Release";

static string GetProjectRoot()
{
    var currentDir = Directory.GetCurrentDirectory();
    var scriptsDir = Path.Combine(currentDir, "scripts");
    
    if (Directory.Exists(scriptsDir))
    {
        return currentDir;
    }
    
    if (Path.GetFileName(currentDir) == "scripts")
    {
        return Directory.GetParent(currentDir)!.FullName;
    }
    
    return currentDir;
}

static string GetCurrentRid()
{
    var os = RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" :
                           RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64" : "linux-x64",
        Architecture.Arm64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-arm64" :
                            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-arm64" : "linux-arm64",
        _ => throw new NotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
    };
    return os;
}

static string GetExecutableName(string rid, bool isAot = false)
{
    // Every morphir executable is named morphir, AOT included; "Morphir" never matched on Linux and macOS.
    var baseName = "morphir";
    if (rid.StartsWith("win-"))
    {
        return $"{baseName}.exe";
    }
    return baseName;
}

static int RunCommand(string command, params string[] args)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }
    };

    process.OutputDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();

    return process.ExitCode;
}

static int RunCommandWithEnv(string command, Dictionary<string, string>? envVars, params string[] args)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }
    };

    if (envVars != null)
    {
        foreach (var kvp in envVars)
        {
            process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }
    }

    process.OutputDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();

    return process.ExitCode;
}

var projectRoot = GetProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

var rid = GetCurrentRid();

Console.WriteLine($"Detected platform RID: {rid}");
Console.WriteLine($"Executable type: {executableType}");
Console.WriteLine($"Configuration: {config}");

// Determine which executable types to test
var packagedTypes = new[] { "native-aot", "self-contained", "framework-dependent", "dotnet-tool" };
var executableTypes = new List<string>();
if (executableType == "all")
{
    executableTypes.AddRange(new[] { "aot", "trimmed", "untrimmed", "tool" });
}
else if (executableType == "offerings")
{
    executableTypes.AddRange(packagedTypes);
}
else
{
    executableTypes.Add(executableType);
}

// The one release folder of this run: PackRelease writes into it (--release-dir) and the tests read from it
// (MORPHIR_RELEASE_DIR, which ExecutableLocator.ReleaseDirectory resolves the same way). No trailing separator, so the
// quoted argument of a path with spaces cannot end in \".
var releaseDir = Path.TrimEndingDirectorySeparator(Environment.GetEnvironmentVariable("MORPHIR_RELEASE_DIR") is { Length: > 0 } customRelease
    ? Path.GetFullPath(customRelease)
    : Path.Combine(projectRoot, "artifacts", "release"));
var packCommand = (string deployment) =>
    $"build --target PackRelease --deployment {deployment}{(deployment == "dotnet-tool" ? "" : $" --rid {rid}")} --release-dir \"{releaseDir}\"";

// The launch metadata files that PackRelease wrote for this deployment on this platform (or the portable tool).
List<string> PackagedLaunchFiles(string deployment) =>
    !Directory.Exists(releaseDir) ? new List<string>() : Directory.GetFiles(releaseDir, "*.build-launch.json").Where(file =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var platform = json.RootElement.GetProperty("platform").GetString();
        return json.RootElement.GetProperty("deployment").GetString() == deployment && (platform == rid || platform == "any");
    }).ToList();

static string GitHead(string root)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add("rev-parse");
    start.ArgumentList.Add("HEAD");
    using var process = Process.Start(start)!;
    var head = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return process.ExitCode == 0 ? head : "";
}

var reusePackaged = Environment.GetEnvironmentVariable("MORPHIR_REUSE_PACKAGED_ARTIFACTS") == "true";

// Prints what is under test, and refuses an artifact that was packed from another commit than HEAD: after a code
// change its old bytes would be tested and reported as found.
bool CheckPackagedArtifact(string deployment, string launchFile)
{
    using var launch = JsonDocument.Parse(File.ReadAllText(launchFile));
    var artifact = launch.RootElement.GetProperty("artifact").GetString()!;
    var contentsFile = Path.Combine(releaseDir, artifact + ".build-contents.json");
    string? commit = null, packedAt = null;
    bool? clean = null;
    if (File.Exists(contentsFile))
    {
        using var contents = JsonDocument.Parse(File.ReadAllText(contentsFile));
        if (contents.RootElement.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.Object)
        {
            commit = origin.GetProperty("commit").GetString();
            clean = origin.GetProperty("workingTreeClean").GetBoolean();
            packedAt = origin.GetProperty("packedAt").GetString();
        }
    }

    Console.WriteLine($"Packaged {deployment} under test: {artifact}");
    Console.WriteLine($"  release {launch.RootElement.GetProperty("release").GetString()}, SHA-256 {launch.RootElement.GetProperty("sha256").GetString()}");
    Console.WriteLine($"  packed from commit {commit ?? "(not recorded)"}{(clean == false ? " with uncommitted changes" : "")} at {packedAt ?? "(not recorded)"}");

    var head = GitHead(projectRoot);
    if (commit is not null && commit == head)
    {
        // Same commit. Uncommitted changes at pack time cannot be compared, so they are only reported.
        if (clean == false) Console.WriteLine("⚠ It was packed with uncommitted changes, which this check cannot compare.");
        return true;
    }

    var why = commit is null ? "its pack commit is not recorded" : $"it was packed from {commit}, and HEAD is {head}";
    if (reusePackaged)
    {
        Console.WriteLine($"⚠ Testing it anyway, as MORPHIR_REUSE_PACKAGED_ARTIFACTS=true says: {why}.");
        return true;
    }

    Console.Error.WriteLine($"✗ Error: the packaged {deployment} artifact may be stale: {why}. Pack it again " +
        $"({packCommand(deployment)}) " +
        "or set MORPHIR_REUSE_PACKAGED_ARTIFACTS=true to test it as it is.");
    return false;
}

// Build all executables that will be tested
foreach (var type in executableTypes)
{
    string? exePath = null;
    
    if (type == "aot")
    {
        var exeName = GetExecutableName(rid, isAot: true);
        exePath = Path.Combine(projectRoot, "artifacts", "executables", rid, exeName);

        if (!File.Exists(exePath))
        {
            Console.WriteLine($"Building AOT executable for {rid}...");
            var buildScript = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(projectRoot, "build.cmd")
                : Path.Combine(projectRoot, "build.sh");
            var exitCode = RunCommand(buildScript, "--target", "PublishExecutable", "--configuration", config, "--rid", rid);
            if (exitCode != 0)
            {
                Console.Error.WriteLine($"✗ Error: Failed to build AOT executable");
                Environment.Exit(exitCode);
            }
        }
        else
        {
            Console.WriteLine($"✓ AOT executable found: {exePath}");
        }
    }
    else if (type == "trimmed")
    {
        var exeName = GetExecutableName(rid, isAot: false);
        exePath = Path.Combine(projectRoot, "artifacts", "single-file", rid, exeName);

        if (!File.Exists(exePath))
        {
            Console.WriteLine($"Building trimmed single-file executable for {rid}...");
            var buildScript = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(projectRoot, "build.cmd")
                : Path.Combine(projectRoot, "build.sh");
            var exitCode = RunCommand(buildScript, "--target", "PublishSingleFile", "--configuration", config, "--rid", rid);
            if (exitCode != 0)
            {
                Console.WriteLine("⚠ Warning: Failed to build trimmed executable");
            }
        }
        else
        {
            Console.WriteLine($"✓ Trimmed executable found: {exePath}");
        }
    }
    else if (packagedTypes.Contains(type))
    {
        if (PackagedLaunchFiles(type).Count == 0)
        {
            Console.WriteLine($"Packing the {type} release profile for {rid}...");
            var buildScript = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(projectRoot, "build.cmd")
                : Path.Combine(projectRoot, "build.sh");
            Console.WriteLine($"  into {releaseDir}");
            var packArgs = new List<string> { "--target", "PackRelease", "--deployment", type, "--configuration", config, "--release-dir", releaseDir };
            if (type != "dotnet-tool") packArgs.AddRange(new[] { "--rid", rid });
            var exitCode = RunCommand(buildScript, packArgs.ToArray());
            if (exitCode != 0)
            {
                Console.Error.WriteLine($"✗ Error: Failed to pack the {type} release profile");
                Environment.Exit(exitCode);
            }
        }

        var launchFiles = PackagedLaunchFiles(type);
        if (launchFiles.Count != 1)
        {
            Console.Error.WriteLine($"✗ Error: {launchFiles.Count} packaged {type} artifacts for {rid} in {releaseDir}; keep exactly one.");
            Environment.Exit(1);
        }

        if (!CheckPackagedArtifact(type, launchFiles[0]))
            Environment.Exit(1);
    }
    else if (type == "untrimmed")
    {
        var exeName = GetExecutableName(rid, isAot: false);
        exePath = Path.Combine(projectRoot, "artifacts", "single-file-untrimmed", rid, exeName);

        if (!File.Exists(exePath))
        {
            Console.WriteLine($"Building untrimmed single-file executable for {rid}...");
            var buildScript = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(projectRoot, "build.cmd")
                : Path.Combine(projectRoot, "build.sh");
            var exitCode = RunCommand(buildScript, "--target", "PublishSingleFileUntrimmed", "--configuration", config, "--rid", rid);
            if (exitCode != 0)
            {
                Console.WriteLine("⚠ Warning: Failed to build untrimmed executable");
            }
        }
        else
        {
            Console.WriteLine($"✓ Untrimmed executable found: {exePath}");
        }
    }
}

// The dotnet tool build is cheap. Every run builds it, so the scenarios that compare both hosts
// (and the "tool" executable type) can run it next to the executable under test.
Console.WriteLine("");
Console.WriteLine("Building the dotnet-morphir tool...");
if (RunCommand("dotnet", "build", Path.Combine(projectRoot, "src", "Morphir.Tool", "Morphir.Tool.csproj"), "--configuration", config) != 0)
{
    Console.Error.WriteLine("✗ Error: Failed to build src/Morphir.Tool");
    Environment.Exit(1);
}
Console.WriteLine("✓ dotnet-morphir tool is built");

// E2E test project should already be built via BuildE2ETests target
Console.WriteLine("");
Console.WriteLine("Verifying E2E test project is built...");
var testDllPath = Path.Combine(projectRoot, "tests", "Morphir.E2E.Tests", "bin", config, "net11.0", "Morphir.E2E.Tests.dll");
if (!File.Exists(testDllPath))
{
    Console.Error.WriteLine("✗ Error: E2E test project not built. Run 'build.cmd --target BuildE2ETests' first.");
    Environment.Exit(1);
}
Console.WriteLine("✓ E2E test project is built");

// Check if we should exclude manual-only tests (AOT tests)
// These should only run in the manual-aot-test workflow
var includeManualTests = Environment.GetEnvironmentVariable("INCLUDE_MANUAL_TESTS") == "true";
if (!includeManualTests)
{
    Console.WriteLine("ℹ Excluding @manual-only tests (use INCLUDE_MANUAL_TESTS=true to include them)");
}

// Track results for each executable type
var failedTypes = new List<string>();
var passedTypes = new List<string>();

// Run E2E tests for each executable type
foreach (var type in executableTypes)
{
    Console.WriteLine("");
    Console.WriteLine("================================================");
    Console.WriteLine($"Testing executable type: {type}");
    Console.WriteLine("================================================");
    
    // Set environment variable for executable type
    Environment.SetEnvironmentVariable("MORPHIR_EXECUTABLE_TYPE", type);
    
    // Run tests (exclude manual-only tests unless explicitly included)
    // Reqnroll turns tags into TUnit categories. The filter needs the full /assembly/namespace/class/test path: a
    // shorter one selects no tests at all. The @packaged scenarios run only for the packaged offerings, which install
    // their release artifact. The @manual-only scenarios (the AOT and trimming features) build their own executables
    // from the source tree, so they never run for a packaged type, even with INCLUDE_MANUAL_TESTS=true.
    var testArgs = new List<string> { "exec", testDllPath, "--treenode-filter" };
    if (packagedTypes.Contains(type))
    {
        if (includeManualTests)
            Console.WriteLine($"ℹ INCLUDE_MANUAL_TESTS=true does not apply to the packaged type {type}: @manual-only scenarios build from source");
        testArgs.Add("/*/*/*/*[Category!=manual-only]");
    }
    else
    {
        testArgs.Add(includeManualTests ? "/*/*/*/*[Category!=packaged]" : "/*/*/*/*[(Category!=manual-only)&(Category!=packaged)]");
    }
    
    var exitCode = RunCommand("dotnet", testArgs.ToArray());
    
    if (exitCode == 0)
    {
        Console.WriteLine("");
        Console.WriteLine($"✓ E2E tests PASSED for {type}");
        passedTypes.Add(type);
    }
    else
    {
        Console.WriteLine("");
        Console.WriteLine($"✗ E2E tests FAILED for {type} (exit code: {exitCode})");
        failedTypes.Add(type);
    }
}

// Report summary
Console.WriteLine("");
Console.WriteLine("================================================");
Console.WriteLine("E2E Test Summary");
Console.WriteLine("================================================");
if (passedTypes.Count > 0)
{
    Console.WriteLine($"✓ Passed: {string.Join(" ", passedTypes)}");
}
if (failedTypes.Count > 0)
{
    Console.WriteLine($"✗ Failed: {string.Join(" ", failedTypes)}");
}
Console.WriteLine("================================================");

// Exit with failure if any executable type failed
if (failedTypes.Count > 0)
{
    Environment.Exit(1);
}
else
{
    Environment.Exit(0);
}
