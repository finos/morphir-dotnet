// Shared utilities for C# build scripts
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.Collections.Generic;

public static class ScriptUtilities
{
    /// <summary>
    /// Get the current Runtime Identifier (RID) based on the current platform
    /// </summary>
    public static string GetCurrentRid()
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

    /// <summary>
    /// Get the executable name based on RID and whether it's AOT
    /// </summary>
    public static string GetExecutableName(string rid, bool isAot = false)
    {
        var baseName = isAot ? "Morphir" : "morphir";
        if (rid.StartsWith("win-"))
        {
            return $"{baseName}.exe";
        }
        return baseName;
    }

    /// <summary>
    /// Get the project root directory (parent of scripts directory)
    /// </summary>
    public static string GetProjectRoot()
    {
        // Get the directory where this script is located
        var scriptPath = Environment.GetCommandLineArgs()[0];
        var scriptDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
        
        // If running via dotnet run, the script path might be different
        // Try to find the scripts directory
        var currentDir = Directory.GetCurrentDirectory();
        var scriptsDir = Path.Combine(currentDir, "scripts");
        
        if (Directory.Exists(scriptsDir))
        {
            return currentDir;
        }
        
        // If we're in the scripts directory, go up one level
        if (Path.GetFileName(currentDir) == "scripts")
        {
            return Directory.GetParent(currentDir)!.FullName;
        }
        
        // Fallback: assume we're in project root
        return currentDir;
    }

    /// <summary>
    /// Get the scripts directory
    /// </summary>
    public static string GetScriptsDirectory()
    {
        return Path.Combine(GetProjectRoot(), "scripts");
    }

    /// <summary>
    /// Ensure a directory exists, creating it if necessary
    /// </summary>
    public static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <summary>
    /// Run a command and return the exit code
    /// </summary>
    public static int RunCommand(string command, params string[] args)
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

    /// <summary>
    /// Run a command and return the exit code, with environment variables
    /// </summary>
    public static int RunCommandWithEnv(string command, Dictionary<string, string>? envVars, params string[] args)
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

    /// <summary>
    /// Find an executable in the artifacts directory
    /// </summary>
    public static string? FindExecutable(string basePath, string rid, string exeName)
    {
        // Try direct path first
        var directPath = Path.Combine(basePath, rid, exeName);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        // Try to find in subdirectories
        if (Directory.Exists(basePath))
        {
            var files = Directory.GetFiles(basePath, exeName, SearchOption.AllDirectories)
                .Where(f => f.Contains(rid))
                .FirstOrDefault();
            
            if (files != null)
            {
                return files;
            }
        }

        return null;
    }

    /// <summary>
    /// Get file size in human-readable format
    /// </summary>
    public static string GetFileSize(string filePath)
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
}

