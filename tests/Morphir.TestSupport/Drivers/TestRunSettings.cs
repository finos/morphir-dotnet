// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Morphir.TestSupport.Drivers;

/// <summary>A black-box run cannot start or continue: a setting is wrong or an artifact is missing.</summary>
public sealed class TestSessionException(string message) : Exception(message);

/// <summary>
/// The tests that a run executes, by what they need beyond the development build. The build gives each test
/// process its lane.
/// </summary>
public enum TestLane
{
    /// <summary>A group on the development build: the tests with no lane.</summary>
    None,

    /// <summary>The tests that need the packed packages.</summary>
    Packages,

    /// <summary>The tests that need a Native AOT publish.</summary>
    NativeAot,

    /// <summary>A published or packaged target: the tests with no lane and the tests of the published lane.</summary>
    Published,
}

/// <summary>The names of the lanes in <c>MORPHIR_TEST_LANE</c>, in tags and in skip reasons.</summary>
public static class TestLanes
{
    public static ImmutableArray<TestLane> All { get; } = [TestLane.None, TestLane.Packages, TestLane.NativeAot, TestLane.Published];

    public static string ValidNames { get; } = string.Join(", ", All.Select(Name));

    public static string Name(this TestLane lane) => lane switch
    {
        TestLane.None => "none",
        TestLane.Packages => "packages",
        TestLane.NativeAot => "native-aot",
        TestLane.Published => "published",
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown lane."),
    };

    public static bool TryParse(string text, out TestLane lane)
    {
        var name = text.Trim().ToLowerInvariant();
        foreach (var candidate in All)
        {
            if (candidate.Name() != name) continue;
            lane = candidate;
            return true;
        }

        lane = default;
        return false;
    }
}

/// <summary>
/// The settings of a black-box run, parsed once from the environment into closed types. A wrong value stops the
/// run with the list of the valid values.
/// </summary>
public sealed record TestRunSettings(TestTarget Target, MorphirHost DefaultHost, TestLane Lane, bool KeepFailed)
{
    /// <summary>The form of the product. Default: <c>dev</c>.</summary>
    public const string TargetVariable = "MORPHIR_TEST_TARGET";

    /// <summary>The host of a test with no host tag. Default: the default host of the target.</summary>
    public const string HostVariable = "MORPHIR_TEST_HOST";

    /// <summary>The path of the executable for the target <c>executable</c>.</summary>
    public const string ExecutableVariable = "MORPHIR_TEST_EXECUTABLE";

    /// <summary>The lane of the run. Default: <c>none</c>.</summary>
    public const string LaneVariable = "MORPHIR_TEST_LANE";

    /// <summary><c>failed</c> keeps the workspace of a failed test.</summary>
    public const string KeepVariable = "MORPHIR_TEST_KEEP";

    /// <summary>The settings with no variable set: the development build, the native host, no lane.</summary>
    public static TestRunSettings Default { get; } = new(new TestTarget.DevelopmentBuild(), MorphirHost.Native, TestLane.None, KeepFailed: false);

    /// <summary>Parses the settings, or throws a <see cref="TestSessionException"/> with the reason.</summary>
    public static TestRunSettings Parse(Func<string, string?> variable, Func<string, bool> fileExists) =>
        TryParse(variable, fileExists, out var settings, out var error) ? settings : throw new TestSessionException(error);

    /// <summary>Parses the settings from <paramref name="variable"/>. <paramref name="fileExists"/> checks the path of an executable.</summary>
    public static bool TryParse(
        Func<string, string?> variable,
        Func<string, bool> fileExists,
        [NotNullWhen(true)] out TestRunSettings? settings,
        [NotNullWhen(false)] out string? error)
    {
        settings = null;
        string? Value(string name) => variable(name) is { } value && value.Trim().Length > 0 ? value.Trim() : null;

        MorphirHost? host = null;
        if (Value(HostVariable) is { } hostText)
        {
            if (!MorphirHosts.TryParse(hostText, out var parsedHost))
            {
                error = $"{HostVariable} is '{hostText}'. The valid values are: {MorphirHosts.ValidNames}.";
                return false;
            }

            host = parsedHost;
        }

        var lane = TestLane.None;
        if (Value(LaneVariable) is { } laneText && !TestLanes.TryParse(laneText, out lane))
        {
            error = $"{LaneVariable} is '{laneText}'. The valid values are: {TestLanes.ValidNames}.";
            return false;
        }

        var keepFailed = false;
        if (Value(KeepVariable) is { } keepText)
        {
            if (!keepText.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{KeepVariable} is '{keepText}'. The valid value is: failed.";
                return false;
            }

            keepFailed = true;
        }

        var targetName = Value(TargetVariable)?.ToLowerInvariant() ?? "dev";
        var executable = Value(ExecutableVariable);
        TestTarget? target;
        if (targetName == "executable")
        {
            if (executable is null)
            {
                error = $"{TargetVariable} is 'executable', so {ExecutableVariable} must give the path of the executable.";
                return false;
            }

            if (!fileExists(executable))
            {
                error = $"{ExecutableVariable} is '{executable}', and no file is at that path.";
                return false;
            }

            if (host is null)
            {
                error = $"{TargetVariable} is 'executable', so {HostVariable} must name the host that the executable is. The valid values are: {MorphirHosts.ValidNames}.";
                return false;
            }

            target = new TestTarget.Executable(executable, host.Value);
        }
        else if (!TestTarget.TryFromName(targetName, out target))
        {
            error = $"{TargetVariable} is '{targetName}'. The valid values are: {string.Join(", ", TestTarget.Names)}.";
            return false;
        }
        else if (executable is not null)
        {
            error = $"{ExecutableVariable} is set, but {TargetVariable} is '{targetName}'. Only the target 'executable' takes a path.";
            return false;
        }

        if (host is { } named && !target.Has(named))
        {
            error = $"{HostVariable} is '{named.Name()}', but the target {target.Name} has the hosts: {string.Join(", ", target.Hosts.Select(MorphirHosts.Name))}.";
            return false;
        }

        settings = new TestRunSettings(target, host ?? target.DefaultHost, lane, keepFailed);
        error = null;
        return true;
    }
}
