// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics.CodeAnalysis;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Mck;

/// <summary>
/// The Morphir Compatibility Kit, <c>morphir-mck</c>, which <c>morphir mck</c> runs. The kit is a separately installed,
/// development-only tool from finos/morphir; this CLI links no kit of its own. <c>mck</c> is the only command that is
/// routed to another executable: this is not a plugin mechanism.
/// </summary>
public static class MckTool
{
    /// <summary>The command that routes to the tool.</summary>
    public const string Command = "mck";

    /// <summary>The tool's executable name, without the Windows <c>.exe</c>.</summary>
    public const string ExecutableName = "morphir-mck";

    /// <summary>The environment variable that names the tool's absolute path.</summary>
    public const string PathVariable = "MORPHIR_MCK";

    /// <summary>The exit code when there is no tool to run: the shell's "command not found".</summary>
    public const int NotFoundExitCode = 127;

    /// <summary>
    /// Runs the tool with <paramref name="arguments"/>, unchanged, in this process's working directory, environment and
    /// standard streams, and returns its exit code. With no tool to run, writes why to <paramref name="diagnostics"/>
    /// (standard error by default) and returns <see cref="NotFoundExitCode"/>. Writes nothing to standard output.
    /// </summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter? diagnostics = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var errors = diagnostics ?? Console.Error;
        if (!TryResolve(Environment.GetEnvironmentVariable(PathVariable), Environment.GetEnvironmentVariable("PATH"), out var executable, out var error))
        {
            errors.WriteLine(error);
            return NotFoundExitCode;
        }

        return MckProcess.Run(executable, arguments, errors, cancellationToken);
    }

    /// <summary>
    /// The tool's absolute path, or why there is none (MCK-3):
    /// <list type="bullet">
    /// <item>A non-empty <paramref name="explicitPath"/> (the value of <c>MORPHIR_MCK</c>) must be an absolute path to
    /// an existing file. It is never read against the current directory.</item>
    /// <item>Otherwise the absolute entries of <paramref name="searchPath"/> are searched in order. Empty and relative
    /// entries are skipped, and the current directory and this executable's folder are never searched.</item>
    /// <item>On Windows the file must be an <c>.exe</c> (<c>morphir-mck.exe</c> on PATH), because a script would run
    /// through a shell. Elsewhere it must have an execute bit.</item>
    /// </list>
    /// </summary>
    public static bool TryResolve(string? explicitPath, string? searchPath,
        [NotNullWhen(true)] out string? executable, [NotNullWhen(false)] out string? error)
    {
        executable = null;
        if (!string.IsNullOrEmpty(explicitPath))
        {
            error = Refusal(explicitPath);
            if (error is not null) return false;
            executable = Path.GetFullPath(explicitPath);
            return true;
        }

        executable = MepExtensionCommand.FindOnPath(ExecutableName, searchPath, ".EXE");
        error = executable is null ? NotFound : null;
        return executable is not null;
    }

    private static string? Refusal(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return $"{PathVariable} must be an absolute path, and it is {path}. A relative path would depend on the current directory, so it is not used.";
        if (!File.Exists(path))
            return $"{PathVariable} names {path}, which does not exist.";
        if (OperatingSystem.IsWindows() && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return $"{PathVariable} names {path}, which is not an .exe file. morphir mck starts no script, because a script would run through a shell.";
        if (!MepExtensionCommand.IsExecutable(path))
            return $"{PathVariable} names {path}, which has no execute permission.";
        return null;
    }

    private static string NotFound =>
        $"morphir mck runs the Morphir Compatibility Kit, {ExecutableName}, which is installed separately: it is a development " +
        $"tool from finos/morphir, and this CLI does not include it. {PathVariable} is not set, and no absolute folder on PATH " +
        $"holds {ExecutableName}{(OperatingSystem.IsWindows() ? ".exe" : "")}. The current directory and relative PATH entries are " +
        $"not searched. Put the folder of {ExecutableName} on PATH, or set {PathVariable} to its absolute path.";
}
