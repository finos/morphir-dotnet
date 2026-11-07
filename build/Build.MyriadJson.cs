// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.IO;

/// <summary>
/// Build targets for Morphir.Myriad.SystemTextJson.
/// </summary>
partial class Build
{
    AbsolutePath MyriadJsonAotProbeProject =>
        TestsDirectory / "Morphir.Myriad.SystemTextJson.AotProbe" / "Morphir.Myriad.SystemTextJson.AotProbe.fsproj";

    /// <summary>
    /// Publish the generated-converter probe with Native AOT for this machine, check that the only IL warnings are the
    /// two FSharp.Core summary warnings that every F# AOT application has, and check that the native output equals the
    /// JIT output byte for byte. The F# compiler does not run the trim analyzers, so only a publish finds AOT problems
    /// in generated F# code.
    /// </summary>
    Target TestMyriadJsonAot => _ => _
        .Description("Publish the Morphir.Myriad.SystemTextJson probe with Native AOT and compare it with JIT")
        .Executes(() =>
        {
            var work = TemporaryDirectory / "myriad-json-aot";
            work.CreateOrCleanDirectory();
            var rid = RuntimeInformation.RuntimeIdentifier;
            var configuration = Configuration.ToString();

            var (buildExit, buildLog) = Capture("dotnet", "build", MyriadJsonAotProbeProject, "-c", configuration, "-nologo");
            if (buildExit != 0)
            {
                throw new Exception($"The probe did not build:{Environment.NewLine}{buildLog}");
            }

            var jitDll = MyriadJsonAotProbeProject.Parent / "bin" / configuration / "net10.0" / "Morphir.Myriad.SystemTextJson.AotProbe.dll";
            var (jitExit, jitOutput) = Capture("dotnet", "exec", jitDll);
            if (jitExit != 0)
            {
                throw new Exception($"The probe failed under JIT:{Environment.NewLine}{jitOutput}");
            }

            var output = work / "publish";
            var (publishExit, publishLog) = Capture("dotnet", "publish", MyriadJsonAotProbeProject, "-c", configuration, "-r", rid,
                "-p:MorphirProbeAot=true", "-o", output, "-nologo");
            File.WriteAllText(work / "publish.log", publishLog);
            if (publishExit != 0)
            {
                throw new Exception($"The Native AOT publish of the probe failed. See {work / "publish.log"}.");
            }

            var unexpected = publishLog.Split('\n')
                .Where(line => line.Contains("warning IL") || line.Contains("error IL"))
                .Where(line => !(line.Contains("FSharp.Core") && (line.Contains("IL2104") || line.Contains("IL3053"))))
                .Distinct()
                .ToArray();
            if (unexpected.Length > 0)
            {
                throw new Exception($"The probe publish has IL warnings:{Environment.NewLine}{string.Join(Environment.NewLine, unexpected)}");
            }

            var native = output / (OperatingSystem.IsWindows() ? "Morphir.Myriad.SystemTextJson.AotProbe.exe" : "Morphir.Myriad.SystemTextJson.AotProbe");
            var (nativeExit, nativeOutput) = Capture(native);
            if (nativeExit != 0)
            {
                throw new Exception($"The Native AOT probe failed:{Environment.NewLine}{nativeOutput}");
            }

            if (nativeOutput != jitOutput)
            {
                File.WriteAllText(work / "jit.txt", jitOutput);
                File.WriteAllText(work / "native.txt", nativeOutput);
                throw new Exception($"The Native AOT output differs from the JIT output. Compare {work / "jit.txt"} and {work / "native.txt"}.");
            }

            Serilog.Log.Information("Morphir.Myriad.SystemTextJson AOT probe: {Lines} lines equal under JIT and Native AOT ({Rid}); binary size {Size:N0} bytes",
                jitOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, rid, new FileInfo(native).Length);
        });

    /// <summary>Runs a command with no shell and returns its exit code and its stdout (stderr is appended).</summary>
    static (int ExitCode, string Output) Capture(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {command}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
