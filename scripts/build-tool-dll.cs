#!/usr/bin/env dotnet run
// Build managed DLL for tool entry point (without AOT)
// Usage: dotnet run scripts/build-tool-dll.cs [CONFIGURATION] [OUTPUT_DIR]

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;

// Top-level statements must come first in .NET 10 file-based execution
var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var config = scriptArgs.Length > 0 ? scriptArgs[0] : "Release";
var outputDir = scriptArgs.Length > 1 ? scriptArgs[1] : "./artifacts/tool-dll";

// Utility functions (defined after top-level statements start)
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

static void EnsureDirectoryExists(string path)
{
    if (!Directory.Exists(path))
    {
        Directory.CreateDirectory(path);
    }
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

// Main script logic
var projectRoot = GetProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

EnsureDirectoryExists(outputDir);

Console.WriteLine("Building managed DLL for tool entry point (without AOT)...");

var exitCode = RunCommand("dotnet", 
    "build", 
    "src/Morphir/Morphir.csproj",
    "--configuration", config,
    "--no-restore",
    "/p:PublishAot=false",
    $"/p:OutputPath={Path.GetFullPath(outputDir)}");

if (exitCode != 0)
{
    Console.Error.WriteLine($"✗ Error: Build failed with exit code {exitCode}");
    Environment.Exit(exitCode);
}

// Check for both uppercase and lowercase DLL names (project name vs assembly name)
var dllPath = Path.Combine(outputDir, "Morphir.dll");
if (!File.Exists(dllPath))
{
    dllPath = Path.Combine(outputDir, "morphir.dll");
}

if (File.Exists(dllPath))
{
    Console.WriteLine($"✓ Managed DLL created: {dllPath}");
}
else
{
    Console.Error.WriteLine($"✗ Error: Morphir.dll or morphir.dll not found in {outputDir}");
    Console.Error.WriteLine($"Contents of {outputDir}:");
    if (Directory.Exists(outputDir))
    {
        foreach (var item in Directory.GetFileSystemEntries(outputDir))
        {
            Console.Error.WriteLine($"  {item}");
        }
    }
    Environment.Exit(1);
}
