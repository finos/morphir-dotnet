#!/usr/bin/env dotnet run
// Pack the Morphir CLI as a dotnet tool (managed DLL only, no platform-specific executables)
// Usage: dotnet run scripts/pack-tool-platform.cs [CONFIGURATION] [VERSION] [OUTPUT_DIR]
// Note: Since we're using managed DLL (not AOT), we don't need platform-specific executables
// The managed DLL will run on any platform with .NET installed

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Text;

var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var config = scriptArgs.Length > 0 ? scriptArgs[0] : "Release";
var version = scriptArgs.Length > 1 ? scriptArgs[1] : null;
var outputDir = scriptArgs.Length > 2 ? scriptArgs[2] : "./artifacts/packages";

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

static string GetScriptsDirectory()
{
    return Path.Combine(GetProjectRoot(), "scripts");
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

var projectRoot = GetProjectRoot();
Directory.SetCurrentDirectory(projectRoot);

EnsureDirectoryExists(outputDir);

// First, build the managed DLL entry point
Console.WriteLine("Building managed DLL entry point...");
var scriptsDir = GetScriptsDirectory();
var buildDllExitCode = RunCommand("dotnet", "run", Path.Combine(scriptsDir, "build-tool-dll.cs"), config, "./artifacts/tool-dll");

if (buildDllExitCode != 0)
{
    Console.Error.WriteLine($"✗ Error: Failed to build managed DLL entry point");
    Environment.Exit(buildDllExitCode);
}

var dllDir = "./artifacts/tool-dll";

// Create temporary directory for package structure
var packageRoot = Path.Combine(Path.GetTempPath(), $"MorphirPack_{Guid.NewGuid()}");
var toolsDir = Path.Combine(packageRoot, "tools", "net10.0");
var anyDir = Path.Combine(toolsDir, "any");
EnsureDirectoryExists(anyDir);

// Copy the managed DLL to the 'any' folder (check both uppercase and lowercase)
var dllPath = Path.Combine(dllDir, "Morphir.dll");
if (!File.Exists(dllPath))
{
    dllPath = Path.Combine(dllDir, "morphir.dll");
}

if (File.Exists(dllPath))
{
    // Copy all DLLs and dependencies to the package
    Console.WriteLine("Copying DLLs and dependencies...");
    var dllFiles = Directory.GetFiles(dllDir, "*.dll", SearchOption.TopDirectoryOnly);
    var depsFiles = Directory.GetFiles(dllDir, "*.deps.json", SearchOption.TopDirectoryOnly);
    var pdbFiles = Directory.GetFiles(dllDir, "*.pdb", SearchOption.TopDirectoryOnly);
    
    foreach (var file in dllFiles)
    {
        var fileName = Path.GetFileName(file);
        // Rename main DLL to match tool command name
        if (fileName.Equals("morphir.dll", StringComparison.OrdinalIgnoreCase) || 
            fileName.Equals("Morphir.dll", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = Path.Combine(anyDir, "dotnet-morphir.dll");
            File.Copy(file, targetPath, overwrite: true);
        }
        else
        {
            var targetPath = Path.Combine(anyDir, fileName);
            File.Copy(file, targetPath, overwrite: true);
        }
    }
    
    // Copy deps.json files (rename main one)
    foreach (var file in depsFiles)
    {
        var fileName = Path.GetFileName(file);
        if (fileName.Equals("morphir.deps.json", StringComparison.OrdinalIgnoreCase) || 
            fileName.Equals("Morphir.deps.json", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = Path.Combine(anyDir, "dotnet-morphir.deps.json");
            File.Copy(file, targetPath, overwrite: true);
        }
        else
        {
            var targetPath = Path.Combine(anyDir, fileName);
            File.Copy(file, targetPath, overwrite: true);
        }
    }
    
    // Copy PDB files (rename main one)
    foreach (var file in pdbFiles)
    {
        var fileName = Path.GetFileName(file);
        if (fileName.Equals("morphir.pdb", StringComparison.OrdinalIgnoreCase) || 
            fileName.Equals("Morphir.pdb", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = Path.Combine(anyDir, "dotnet-morphir.pdb");
            File.Copy(file, targetPath, overwrite: true);
        }
        else
        {
            var targetPath = Path.Combine(anyDir, fileName);
            File.Copy(file, targetPath, overwrite: true);
        }
    }
    
    // Copy runtimeconfig.json if it exists, otherwise create it
    var runtimeConfigSource = Path.Combine(dllDir, "morphir.runtimeconfig.json");
    if (!File.Exists(runtimeConfigSource))
    {
        runtimeConfigSource = Path.Combine(dllDir, "Morphir.runtimeconfig.json");
    }
    
    var runtimeConfigPath = Path.Combine(anyDir, "dotnet-morphir.runtimeconfig.json");
    if (File.Exists(runtimeConfigSource))
    {
        File.Copy(runtimeConfigSource, runtimeConfigPath, overwrite: true);
    }
    else
    {
        // Create runtimeconfig.json (required for framework-dependent execution)
        var runtimeConfig = @"{
  ""runtimeOptions"": {
    ""tfm"": ""net10.0"",
    ""framework"": {
      ""name"": ""Microsoft.NETCore.App"",
      ""version"": ""10.0.0""
    }
  }
}";
        File.WriteAllText(runtimeConfigPath, runtimeConfig);
    }
    Console.WriteLine("✓ Copied DLLs and dependencies");
    
    // Create DotnetToolSettings.xml (required for dotnet tools)
    // Note: This must match the format that dotnet pack expects
    var settingsXmlPath = Path.Combine(anyDir, "DotnetToolSettings.xml");
    var settingsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<DotNetCliTool Version=""1"">
  <Commands>
    <Command Name=""dotnet-morphir"" EntryPoint=""dotnet-morphir.dll"" Runner=""dotnet"" />
  </Commands>
</DotNetCliTool>";
    File.WriteAllText(settingsXmlPath, settingsXml);
    Console.WriteLine("✓ Created DotnetToolSettings.xml");
}
else
{
    Console.Error.WriteLine($"✗ Error: Managed DLL not found at {Path.Combine(dllDir, "Morphir.dll")} or {Path.Combine(dllDir, "morphir.dll")}");
    Directory.Delete(packageRoot, recursive: true);
    Environment.Exit(1);
}

// Note: Since we're using managed DLL (not AOT), we don't need platform-specific executables
// The managed DLL will run on any platform with .NET installed
// Standalone executables are still built for direct download/installation, but not packaged here

// Create a minimal .csproj for packaging
var packProjPath = Path.Combine(packageRoot, "Morphir.Tool.Pack.csproj");
var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <PackageId>Morphir</PackageId>
        <PackageType>DotnetTool</PackageType>
        <ToolCommandName>dotnet-morphir</ToolCommandName>
        <PackAsTool>true</PackAsTool>
        <NoBuild>true</NoBuild>
        <IncludeBuildOutput>false</IncludeBuildOutput>
        <GenerateDocumentationFile>false</GenerateDocumentationFile>
        <GenerateDependencyFile>false</GenerateDependencyFile>
        <ToolEntryPoint>dotnet-morphir.dll</ToolEntryPoint>
    </PropertyGroup>
    <ItemGroup>
        <None Include=""tools/**/*"" Pack=""true"" PackagePath=""tools/"" />
    </ItemGroup>
</Project>";

File.WriteAllText(packProjPath, csprojContent);

// Create empty build outputs to satisfy pack requirements (even with NoBuild=true)
var objDir = Path.Combine(packageRoot, "obj", "Release", "net10.0");
EnsureDirectoryExists(objDir);
File.WriteAllText(Path.Combine(objDir, "Morphir.Tool.Pack.dll"), "");
File.WriteAllText(Path.Combine(objDir, "Morphir.Tool.Pack.pdb"), "");

// Set version if provided
var packArgs = new List<string>
{
    "pack",
    packProjPath,
    "--output", Path.GetFullPath(outputDir),
    "--no-build"
};

if (!string.IsNullOrEmpty(version))
{
    packArgs.Add($"/p:Version={version}");
}

Console.WriteLine("Packing Morphir CLI tool (managed DLL only)...");
var originalDir = Directory.GetCurrentDirectory();
Directory.SetCurrentDirectory(packageRoot);

// Restore dependencies first
var restoreExitCode = RunCommand("dotnet", "restore", packProjPath);
if (restoreExitCode != 0)
{
    Console.Error.WriteLine($"✗ Error: Restore failed with exit code {restoreExitCode}");
    Directory.SetCurrentDirectory(originalDir);
    Directory.Delete(packageRoot, recursive: true);
    Environment.Exit(restoreExitCode);
}

var packExitCode = RunCommand("dotnet", packArgs.ToArray());

Directory.SetCurrentDirectory(originalDir);

// Cleanup
try
{
    Directory.Delete(packageRoot, recursive: true);
}
catch
{
    // Ignore cleanup errors
}

if (packExitCode != 0)
{
    Console.Error.WriteLine($"✗ Error: Pack failed with exit code {packExitCode}");
    Environment.Exit(packExitCode);
}

Console.WriteLine($"✓ Tool package created in {outputDir}");
