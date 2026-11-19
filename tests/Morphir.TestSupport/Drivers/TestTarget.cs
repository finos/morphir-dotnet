// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Morphir.TestSupport.Drivers;

/// <summary>An entry point of the product. The set is closed.</summary>
public enum MorphirHost
{
    /// <summary>The <c>morphir</c> executable of <c>src/Morphir</c>. It reaches the F# frontend through the extension process.</summary>
    Native,

    /// <summary>The <c>morphir</c> executable of <c>src/Morphir.Managed</c>. The F# frontend is in the host.</summary>
    Managed,

    /// <summary>The <c>dotnet-morphir</c> tool of <c>src/Morphir.Tool</c>. The F# frontend is in the host.</summary>
    DotnetTool,
}

/// <summary>The names of the hosts in settings, tags and skip reasons.</summary>
public static class MorphirHosts
{
    /// <summary>The hosts in the order of the design table.</summary>
    public static ImmutableArray<MorphirHost> All { get; } = [MorphirHost.Native, MorphirHost.Managed, MorphirHost.DotnetTool];

    /// <summary>The valid names, for a message.</summary>
    public static string ValidNames { get; } = string.Join(", ", All.Select(Name));

    /// <summary>The name of the host: <c>native</c>, <c>managed</c> or <c>tool</c>.</summary>
    public static string Name(this MorphirHost host) => host switch
    {
        MorphirHost.Native => "native",
        MorphirHost.Managed => "managed",
        MorphirHost.DotnetTool => "tool",
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, "Unknown host."),
    };

    /// <summary>Parses a host name. <c>dotnet-morphir</c>, the name of the tool executable, is also the tool host.</summary>
    public static bool TryParse(string text, out MorphirHost host)
    {
        MorphirHost? parsed = text.Trim().ToLowerInvariant() switch
        {
            "native" => MorphirHost.Native,
            "managed" => MorphirHost.Managed,
            "tool" or "dotnet-morphir" => MorphirHost.DotnetTool,
            _ => null,
        };
        host = parsed ?? default;
        return parsed is not null;
    }
}

/// <summary>A packaged offering of a release.</summary>
public enum PackagedOffering
{
    NativeAot,
    SelfContained,
    FrameworkDependent,
    DotnetTool,
}

/// <summary>
/// The form of the product that a black-box run tests. The set is closed. A target states the hosts that it has;
/// a run never uses the artifact of another form.
/// </summary>
public abstract record TestTarget
{
    private TestTarget() { }

    /// <summary>The build output of the three entry point projects, in the configuration of the test assembly.</summary>
    public sealed record DevelopmentBuild : TestTarget;

    /// <summary>The trimmed single-file executable in <c>artifacts/single-file</c>.</summary>
    public sealed record Trimmed : TestTarget;

    /// <summary>The untrimmed single-file executable in <c>artifacts/single-file-untrimmed</c>.</summary>
    public sealed record Untrimmed : TestTarget;

    /// <summary>The Native AOT executable in <c>artifacts/executables</c>.</summary>
    public sealed record NativeAot : TestTarget;

    /// <summary>A packaged offering in <c>artifacts/release</c>.</summary>
    public sealed record Packaged(PackagedOffering Offering) : TestTarget;

    /// <summary>One executable at a path, which is the host that the run names.</summary>
    public sealed record Executable(string Path, MorphirHost Host) : TestTarget;

    /// <summary>The names of the targets in <c>MORPHIR_TEST_TARGET</c>, in the order of the design table.</summary>
    public static ImmutableArray<string> Names { get; } =
        ["dev", "trimmed", "untrimmed", "aot", "native-aot", "self-contained", "framework-dependent", "dotnet-tool", "executable"];

    /// <summary>The name of the target in <c>MORPHIR_TEST_TARGET</c> and in a skip reason.</summary>
    public string Name => this switch
    {
        DevelopmentBuild => "dev",
        Trimmed => "trimmed",
        Untrimmed => "untrimmed",
        NativeAot => "aot",
        Packaged { Offering: PackagedOffering.NativeAot } => "native-aot",
        Packaged { Offering: PackagedOffering.SelfContained } => "self-contained",
        Packaged { Offering: PackagedOffering.FrameworkDependent } => "framework-dependent",
        Packaged { Offering: PackagedOffering.DotnetTool } => "dotnet-tool",
        Executable => "executable",
        _ => throw new InvalidOperationException($"Unknown target {GetType().Name}."),
    };

    /// <summary>The hosts that the target has. The first is its default host.</summary>
    public ImmutableArray<MorphirHost> Hosts => this switch
    {
        DevelopmentBuild => MorphirHosts.All,
        Trimmed or Untrimmed or NativeAot or Packaged { Offering: PackagedOffering.NativeAot } => [MorphirHost.Native],
        Packaged { Offering: PackagedOffering.SelfContained or PackagedOffering.FrameworkDependent } => [MorphirHost.Managed],
        Packaged { Offering: PackagedOffering.DotnetTool } => [MorphirHost.DotnetTool],
        Executable executable => [executable.Host],
        _ => throw new InvalidOperationException($"Unknown target {GetType().Name}."),
    };

    /// <summary>The host of a test with no host tag, when the run gives no other.</summary>
    public MorphirHost DefaultHost => Hosts[0];

    public bool Has(MorphirHost host) => Hosts.Contains(host);

    /// <summary>The skip reason of a test that names a host that this target does not have.</summary>
    public string MissingHostReason(MorphirHost host) => $"host: {host.Name()} is not in target {Name}";

    /// <summary>The target of a name with no other setting. <c>executable</c> needs a path and a host, so it is not here.</summary>
    internal static bool TryFromName(string name, [NotNullWhen(true)] out TestTarget? target)
    {
        target = name switch
        {
            "dev" => new DevelopmentBuild(),
            "trimmed" => new Trimmed(),
            "untrimmed" => new Untrimmed(),
            "aot" => new NativeAot(),
            "native-aot" => new Packaged(PackagedOffering.NativeAot),
            "self-contained" => new Packaged(PackagedOffering.SelfContained),
            "framework-dependent" => new Packaged(PackagedOffering.FrameworkDependent),
            "dotnet-tool" => new Packaged(PackagedOffering.DotnetTool),
            _ => null,
        };
        return target is not null;
    }
}
