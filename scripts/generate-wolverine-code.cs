#!/usr/bin/env dotnet run
// Generate Wolverine code using the Morphir executable
// Usage: dotnet run scripts/generate-wolverine-code.cs [CONFIGURATION]

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;

var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var config = scriptArgs.Length > 0 ? scriptArgs[0] : "Release";

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

var projectRoot = GetProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

Console.WriteLine("Building Morphir project for code generation...");
var buildExitCode = RunCommand("dotnet", "build", "src/Morphir/Morphir.csproj", "--configuration", config, "--no-restore");

if (buildExitCode != 0)
{
    Console.WriteLine("⚠ Warning: Build failed, attempting code generation anyway");
}

Console.WriteLine("Generating Wolverine code...");
var codegenExitCode = RunCommand("dotnet", "run", "--project", "src/Morphir/Morphir.csproj", "--configuration", config, "--no-build", "--", "codegen", "write");

if (codegenExitCode != 0)
{
    Console.WriteLine("⚠ Warning: Code generation failed, continuing with build");
}

var generatedDir = Path.Combine(projectRoot, "src", "Morphir.Tooling", "Internal", "Generated");
if (Directory.Exists(generatedDir))
{
    Console.WriteLine("✓ Wolverine code generated successfully");
    var fileCount = Directory.GetFiles(generatedDir, "*.cs", SearchOption.AllDirectories).Length;
    Console.WriteLine($"Generated files: {fileCount}");
}
else
{
    Console.WriteLine("⚠ Warning: Generated code directory not found");
}
