#!/usr/bin/env dotnet run
// Run end-to-end tests against Morphir executables
// Usage: dotnet run scripts/run-e2e-tests.cs [EXECUTABLE_TYPE] [CONFIGURATION]

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
    var baseName = isAot ? "Morphir" : "morphir";
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
var executableTypes = new List<string>();
if (executableType == "all")
{
    executableTypes.AddRange(new[] { "aot", "trimmed", "untrimmed" });
}
else
{
    executableTypes.Add(executableType);
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

// E2E test project should already be built via BuildE2ETests target
Console.WriteLine("");
Console.WriteLine("Verifying E2E test project is built...");
var testDllPath = Path.Combine(projectRoot, "tests", "Morphir.E2E.Tests", "bin", config, "net10.0", "Morphir.E2E.Tests.dll");
if (!File.Exists(testDllPath))
{
    Console.Error.WriteLine("✗ Error: E2E test project not built. Run 'build.cmd --target BuildE2ETests' first.");
    Environment.Exit(1);
}
Console.WriteLine("✓ E2E test project is built");

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
    
    // Run tests
    var exitCode = RunCommand("dotnet", "exec", testDllPath);
    
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
