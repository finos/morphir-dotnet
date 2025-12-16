#!/usr/bin/env dotnet run
// Publish single-file executable without AOT and without trimming (baseline)
// Usage: dotnet run scripts/publish-single-file-untrimmed.cs [RID] [CONFIGURATION] [VERSION] [OUTPUT_DIR]

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (scriptArgs.Length == 0 || string.IsNullOrEmpty(scriptArgs[0]))
{
    Console.Error.WriteLine("Error: RID is required");
    Console.Error.WriteLine($"Usage: dotnet run {Path.GetFileName(Environment.GetCommandLineArgs()[0])} <RID> [CONFIGURATION] [VERSION] [OUTPUT_DIR]");
    Environment.Exit(1);
}

var rid = scriptArgs[0];
var config = scriptArgs.Length > 1 ? scriptArgs[1] : "Release";
var version = scriptArgs.Length > 2 ? scriptArgs[2] : null;
var outputDir = scriptArgs.Length > 3 ? scriptArgs[3] : "./artifacts/single-file-untrimmed";

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

static string GetExecutableName(string rid, bool isAot = false)
{
    var baseName = isAot ? "Morphir" : "morphir";
    if (rid.StartsWith("win-"))
    {
        return $"{baseName}.exe";
    }
    return baseName;
}

static string GetFileSize(string filePath)
{
    var fileInfo = new FileInfo(filePath);
    var bytes = fileInfo.Length;
    
    string[] sizes = { "B", "KB", "MB", "GB" };
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len = len / 1024;
    }
    
    return $"{len:0.##} {sizes[order]}";
}

var projectRoot = GetProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

// Generate Wolverine code before publishing
Console.WriteLine("Generating Wolverine code...");
var buildExitCode = RunCommand("dotnet", "build", "src/Morphir/Morphir.csproj", "--configuration", config, "--no-restore");

if (buildExitCode != 0)
{
    Console.WriteLine("⚠ Warning: Build failed, attempting code generation anyway");
}

var codegenExitCode = RunCommand("dotnet", "run", "--project", "src/Morphir/Morphir.csproj", "--configuration", config, "--no-build", "--", "codegen", "write");

if (codegenExitCode != 0)
{
    Console.WriteLine("⚠ Warning: Code generation failed, continuing with build");
}

var generatedDir = Path.Combine(projectRoot, "src", "Morphir.Tooling", "Internal", "Generated");
if (Directory.Exists(generatedDir))
{
    Console.WriteLine("✓ Found generated code directory");
    var fileCount = Directory.GetFiles(generatedDir, "*.cs", SearchOption.AllDirectories).Length;
    Console.WriteLine($"  Generated files: {fileCount}");
}
else
{
    Console.WriteLine("⚠ Warning: Generated code directory not found at src/Morphir.Tooling/Internal/Generated");
}

var ridOutputDir = Path.Combine(outputDir, rid);
EnsureDirectoryExists(ridOutputDir);

var publishArgs = new List<string>
{
    "publish",
    "src/Morphir/Morphir.csproj",
    "--configuration", config,
    "--self-contained", "true",
    "--property:PublishSingleFile=true",
    "--runtime", rid,
    "--output", Path.GetFullPath(ridOutputDir)
};

if (!string.IsNullOrEmpty(version))
{
    publishArgs.Add($"--property:Version={version}");
}

Console.WriteLine($"Publishing single-file executable (managed, untrimmed) for {rid}...");
var publishExitCode = RunCommand("dotnet", publishArgs.ToArray());

if (publishExitCode != 0)
{
    Console.Error.WriteLine($"✗ Error: Publish failed with exit code {publishExitCode}");
    Environment.Exit(publishExitCode);
}

var exeName = GetExecutableName(rid, isAot: false);
var exePath = Path.Combine(ridOutputDir, exeName);

if (File.Exists(exePath))
{
    var size = GetFileSize(exePath);
    Console.WriteLine($"✓ Created: {exePath}");
    var fileInfo = new FileInfo(exePath);
    Console.WriteLine($"  Size: {size}");
    Console.WriteLine($"  Last modified: {fileInfo.LastWriteTime}");
    Console.WriteLine("");
    Console.WriteLine($"Executable size: {size}");
}
else
{
    Console.Error.WriteLine($"✗ Error: Executable not found at {exePath}");
    Console.Error.WriteLine($"Contents of {ridOutputDir}:");
    if (Directory.Exists(ridOutputDir))
    {
        foreach (var item in Directory.GetFileSystemEntries(ridOutputDir))
        {
            Console.Error.WriteLine($"  {item}");
        }
    }
    else
    {
        Console.Error.WriteLine("Directory does not exist");
    }
    Environment.Exit(1);
}
