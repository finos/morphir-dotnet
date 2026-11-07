// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;

namespace Morphir.Myriad.Hosting;

/// <summary>The result of one Myriad host run.</summary>
/// <param name="ExitCode">The exit code of the host process.</param>
/// <param name="StandardOutput">Everything the host wrote to stdout.</param>
/// <param name="StandardError">Everything the host wrote to stderr.</param>
public sealed record MyriadRun(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Prepares and runs the Myriad host that a Morphir Myriad package carries.</summary>
public static class MyriadHost
{
    /// <summary>The longest time one host run may take.</summary>
    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Copies the packaged host into <paramref name="workDirectory"/> and replaces its FSharp.Core with
    /// <paramref name="fsharpCorePath"/>. Returns the path of the copied host.
    /// </summary>
    /// <remarks>
    /// Myriad 0.85.0 bundles FSharp.Core 9. The plugins use a later FSharp.Core, and the host and the plugin
    /// share Fantomas async types, so one FSharp.Core must load before both. The restored tool in the NuGet
    /// cache is never changed; the copy is private to one generation.
    /// </remarks>
    public static string Prepare(string hostPath, string fsharpCorePath, string workDirectory)
    {
        if (!File.Exists(hostPath))
        {
            throw new InvalidOperationException($"Myriad host could not be found: '{hostPath}'.");
        }

        var sourceRoot = Path.GetDirectoryName(hostPath)!;
        var destinationRoot = Path.Combine(workDirectory, ".host");
        Directory.CreateDirectory(destinationRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        File.Copy(fsharpCorePath, Path.Combine(destinationRoot, "FSharp.Core.dll"), true);
        return Path.Combine(destinationRoot, Path.GetFileName(hostPath));
    }

    /// <summary>
    /// Runs a prepared host with major-version roll forward, no shell and the given arguments.
    /// Throws <see cref="TimeoutException"/> and stops the process tree when the run exceeds <see cref="RunLimit"/>.
    /// </summary>
    public static MyriadRun Run(string dotnetHost, string preparedHost, string workingDirectory, IEnumerable<string> arguments)
    {
        var host = string.IsNullOrWhiteSpace(dotnetHost) ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet" : dotnetHost;
        var start = new ProcessStartInfo(host)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--roll-forward");
        start.ArgumentList.Add("LatestMajor");
        start.ArgumentList.Add(preparedHost);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Myriad host.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(RunLimit))
        {
            process.Kill(true);
            throw new TimeoutException("Myriad exceeded two minutes.");
        }

        return new MyriadRun(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
