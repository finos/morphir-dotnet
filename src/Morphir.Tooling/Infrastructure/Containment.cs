// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Configuration;

namespace Morphir.Tooling.Infrastructure;

/// <summary>Where an effective containment control came from.</summary>
public enum ContainmentSource
{
    /// <summary>No control was supplied: the filesystem root, or not isolated.</summary>
    Default,

    /// <summary>A command-line flag.</summary>
    Flag,

    /// <summary>An environment variable.</summary>
    Environment,
}

/// <summary>A containment control that could not be honored. Commands report it as a usage failure (exit 2).</summary>
public sealed record ContainmentFailure(string Code, string Message);

/// <summary>Either the effective containment or the reason it could not be resolved.</summary>
public sealed record ContainmentResolution(Containment? Value, ContainmentFailure? Failure);

/// <summary>
/// The effective discovery boundary and isolation for one command invocation. A flag wins over its
/// environment variable, which wins over the default (the start path's filesystem root, not isolated).
/// Resolution is pure: callers pass the environment snapshot and the current directory.
/// </summary>
public sealed record Containment(string Boundary, ContainmentSource BoundarySource, bool Isolated, ContainmentSource IsolationSource)
{
    public const string BoundaryVariable = "MORPHIR_DISCOVERY_BOUNDARY";
    public const string IsolatedVariable = "MORPHIR_ISOLATED";
    public const string BoundaryFlag = "--discovery-boundary";
    public const string IsolatedFlag = "--isolated";
    public const string BoundaryCode = "discovery.boundary";
    public const string InvalidValueCode = "containment.invalid_value";

    /// <param name="flagBoundary">The <c>--discovery-boundary</c> value, or null when the flag was not given.</param>
    /// <param name="flagIsolated">The <c>--isolated</c> value, or null when the flag was not given.</param>
    /// <param name="environment">The command's environment snapshot.</param>
    /// <param name="currentDirectory">Relative values, and the default boundary, are based on this directory.</param>
    public static ContainmentResolution Resolve(
        string? flagBoundary,
        bool? flagIsolated,
        IReadOnlyDictionary<string, string> environment,
        string currentDirectory)
    {
        var environmentBoundary = Nonempty(environment, BoundaryVariable);
        var (boundary, boundarySource) = flagBoundary is not null
            ? (Canonical(currentDirectory, flagBoundary), ContainmentSource.Flag)
            : environmentBoundary is not null
                ? (Canonical(currentDirectory, environmentBoundary), ContainmentSource.Environment)
                : (Canonical(currentDirectory, Path.GetPathRoot(Path.GetFullPath(currentDirectory))!), ContainmentSource.Default);

        bool isolated;
        ContainmentSource isolationSource;
        if (flagIsolated is { } flag)
        {
            (isolated, isolationSource) = (flag, ContainmentSource.Flag);
        }
        else if (Nonempty(environment, IsolatedVariable) is { } value)
        {
            if (!TryParseSwitch(value, out isolated))
            {
                return new ContainmentResolution(null, new ContainmentFailure(InvalidValueCode,
                    $"{IsolatedVariable} must be true, false, 1 or 0 (any case); got '{value}'."));
            }

            isolationSource = ContainmentSource.Environment;
        }
        else
        {
            (isolated, isolationSource) = (false, ContainmentSource.Default);
        }

        return new ContainmentResolution(new Containment(boundary, boundarySource, isolated, isolationSource), null);
    }

    /// <summary>The process environment as an explicit snapshot, for passing to <see cref="Resolve"/>.</summary>
    public static IReadOnlyDictionary<string, string> CurrentEnvironment() =>
        System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(pair => pair.Key is string && pair.Value is string)
            .ToDictionary(pair => (string)pair.Key, pair => (string)pair.Value!, StringComparer.Ordinal);

    /// <summary>The boundary in native form, for filesystem hosts.</summary>
    public string NativeBoundary => Path.GetFullPath(Boundary);

    /// <summary>True when <paramref name="path"/> is the boundary or below it.</summary>
    public bool Contains(string path) => ProjectPath.contains(Boundary, Canonical(Boundary, path));

    /// <summary>A <c>discovery.boundary</c> failure when <paramref name="path"/> is outside the boundary; otherwise null.</summary>
    public ContainmentFailure? RequireInside(string path)
    {
        if (Contains(path)) return null;
        return new ContainmentFailure(BoundaryCode,
            $"{Canonical(Boundary, path)} is outside the discovery boundary {Boundary} (from {DescribeBoundarySource()}).");
    }

    /// <summary>Names where the boundary came from, for diagnostics and reports.</summary>
    public string DescribeBoundarySource() => BoundarySource switch
    {
        ContainmentSource.Flag => BoundaryFlag,
        ContainmentSource.Environment => BoundaryVariable,
        _ => "the filesystem root",
    };

    /// <summary>Names where the isolation setting came from, for reports.</summary>
    public string DescribeIsolationSource() => IsolationSource switch
    {
        ContainmentSource.Flag => IsolatedFlag,
        ContainmentSource.Environment => IsolatedVariable,
        _ => "default",
    };

    private static string? Nonempty(IReadOnlyDictionary<string, string> environment, string name) =>
        environment.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    private static bool TryParseSwitch(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "1" or "true":
                result = true;
                return true;
            case "0" or "false":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    /// <summary>
    /// Absolute, normalized and with an upper-case drive letter, so it compares with discovery paths.
    /// Native separators, drive paths and UNC paths are accepted; relative paths resolve against <paramref name="basePath"/>.
    /// </summary>
    private static string Canonical(string basePath, string path)
    {
        var full = ProjectPath.normalize(Path.GetFullPath(path, Path.GetFullPath(basePath)));
        return full.Length >= 2 && full[1] == ':' ? char.ToUpperInvariant(full[0]) + full[1..] : full;
    }
}
