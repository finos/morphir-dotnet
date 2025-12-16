#!/usr/bin/env dotnet run
// Run tests script for .NET 10 TUnit with Microsoft Testing Platform
// Usage: dotnet run scripts/run-tests.cs [CONFIGURATION]

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;

var scriptArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var configuration = scriptArgs.Length > 0 ? scriptArgs[0] : "Debug";

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

Console.WriteLine($"Running tests with configuration: {configuration}");
Console.WriteLine("================================================");

// Run Morphir.Core.Tests
Console.WriteLine("");
Console.WriteLine("Running Morphir.Core.Tests...");
var coreExitCode = RunCommand("dotnet", "exec", $"tests/Morphir.Core.Tests/bin/{configuration}/net10.0/Morphir.Core.Tests.dll");

// Run Morphir.Tooling.Tests
Console.WriteLine("");
Console.WriteLine("Running Morphir.Tooling.Tests...");
var toolingExitCode = RunCommand("dotnet", "exec", $"tests/Morphir.Tooling.Tests/bin/{configuration}/net10.0/Morphir.Tooling.Tests.dll");

// Check if any tests failed
if (coreExitCode != 0 || toolingExitCode != 0)
{
    Console.WriteLine("");
    Console.WriteLine("================================================");
    Console.WriteLine("Tests FAILED");
    Console.WriteLine($"  Morphir.Core.Tests: exit code {coreExitCode}");
    Console.WriteLine($"  Morphir.Tooling.Tests: exit code {toolingExitCode}");
    Environment.Exit(1);
}

Console.WriteLine("");
Console.WriteLine("================================================");
Console.WriteLine("All tests PASSED");
Environment.Exit(0);
