// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;

namespace Morphir.TestSupport;

/// <summary>How <see cref="TestSandbox.Apply"/> contains a child process.</summary>
public sealed record SandboxOptions
{
    /// <summary>Set MORPHIR_ISOLATED=1 so machine and personal configuration is ignored. Default true.</summary>
    public bool Isolated { get; init; } = true;

    /// <summary>
    /// Point HOME, USERPROFILE, APPDATA, LOCALAPPDATA, PROGRAMDATA and XDG_* into the sandbox. Default true.
    /// Turn off only for children that need the real SDK state; Morphir itself stays contained regardless.
    /// </summary>
    public bool RedirectHome { get; init; } = true;

    /// <summary>The discovery boundary to impose. Default: the sandbox root.</summary>
    public string? Boundary { get; init; }
}

/// <summary>
/// A disposable temporary tree with its own home, machine configuration locations and work area.
/// <see cref="Apply"/> confines a child process to it, so a test never reads or writes the real
/// machine's Morphir state, while the .NET SDK keeps using the warm NuGet cache.
/// </summary>
public sealed class TestSandbox : IDisposable, IAsyncDisposable
{
    /// <summary>The NuGet global packages folder of the real machine, captured before any redirect.</summary>
    public static string RealNuGetPackagesFolder { get; } =
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private TestSandbox(string root)
    {
        Root = root;
        Home = CreateDirectory("home");
        AppData = CreateDirectory("appdata");
        LocalAppData = CreateDirectory("localappdata");
        ProgramData = CreateDirectory("programdata");
        XdgConfigHome = CreateDirectory("xdg-config");
        XdgCacheHome = CreateDirectory("xdg-cache");
        DotnetCliHome = CreateDirectory("dotnet-cli-home");
        Work = CreateDirectory("work");
        MorphirHome = Path.Combine(Home, ".morphir");
    }

    public string Root { get; }
    public string Home { get; }
    public string AppData { get; }
    public string LocalAppData { get; }
    public string ProgramData { get; }
    public string XdgConfigHome { get; }
    public string XdgCacheHome { get; }
    public string DotnetCliHome { get; }

    /// <summary>Where tests put projects and workspaces.</summary>
    public string Work { get; }

    /// <summary>The sandbox's global Morphir home. Not created: each test decides whether it exists.</summary>
    public string MorphirHome { get; }

    public static TestSandbox Create(string name = "morphir-test-") =>
        new(Directory.CreateTempSubdirectory(name).FullName);

    /// <summary>A path below the sandbox root.</summary>
    public string PathOf(params string[] segments) => Path.Combine([Root, .. segments]);

    /// <summary>Confines <paramref name="info"/>'s environment to this sandbox.</summary>
    public void Apply(ProcessStartInfo info, SandboxOptions? options = null)
    {
        foreach (var (key, value) in Variables(info.Environment.Keys, options ?? new SandboxOptions()))
        {
            if (value is null) info.Environment.Remove(key);
            else info.Environment[key] = value;
        }
    }

    /// <summary>
    /// The sandboxed environment as a snapshot, for code that takes an explicit environment instead of
    /// reading the process: inherited MORPHIR_* entries are removed and the sandbox values applied.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentSnapshot(
        IReadOnlyDictionary<string, string> inherited, SandboxOptions? options = null)
    {
        var environment = new Dictionary<string, string>(inherited, KeyComparer);
        foreach (var (key, value) in Variables(environment.Keys.ToArray(), options ?? new SandboxOptions()))
        {
            if (value is null) environment.Remove(key);
            else environment[key] = value;
        }

        return environment;
    }

    public void Dispose() => TolerantDirectory.TryDelete(Root);

    public async ValueTask DisposeAsync() => await TolerantDirectory.TryDeleteAsync(Root).ConfigureAwait(false);

    private static StringComparer KeyComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private IEnumerable<(string Key, string? Value)> Variables(IEnumerable<string> existingKeys, SandboxOptions options)
    {
        foreach (var key in existingKeys.Where(key => key.StartsWith("MORPHIR_", StringComparison.OrdinalIgnoreCase)).ToArray())
            yield return (key, null);

        yield return ("MORPHIR_HOME", MorphirHome);
        yield return ("MORPHIR_DISCOVERY_BOUNDARY", options.Boundary ?? Root);
        if (options.Isolated) yield return ("MORPHIR_ISOLATED", "1");

        if (options.RedirectHome)
        {
            yield return ("HOME", Home);
            yield return ("USERPROFILE", Home);
            yield return ("APPDATA", AppData);
            yield return ("LOCALAPPDATA", LocalAppData);
            yield return ("PROGRAMDATA", ProgramData);
            yield return ("XDG_CONFIG_HOME", XdgConfigHome);
            yield return ("XDG_CACHE_HOME", XdgCacheHome);
            yield return ("DOTNET_CLI_HOME", DotnetCliHome);
        }

        yield return ("NUGET_PACKAGES", RealNuGetPackagesFolder);
        yield return ("DOTNET_NOLOGO", "1");
        yield return ("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");
        yield return ("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
        yield return ("MSBUILDDISABLENODEREUSE", "1");
    }

    private string CreateDirectory(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
}
