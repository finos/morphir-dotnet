// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;

namespace Morphir.TestSupport.Drivers;

/// <summary>One entry of the report of <c>extension list --json</c>.</summary>
/// <param name="Reason">The reason of an entry, or null when the entry has no member <c>reason</c>.</param>
/// <param name="HasReason">True when the entry has the member <c>reason</c>, also with a null value.</param>
public sealed record ExtensionEntry(
    string Id,
    ImmutableArray<string> Types,
    string Delivery,
    ImmutableArray<string> Hosts,
    bool Available,
    string? Reason,
    bool HasReason,
    ImmutableArray<string>? ValidatorIrVersions,
    string RawJson);

/// <summary>The report of <c>extension list --json</c>: the host, the deployment and the execution of the build, and the entries.</summary>
/// <param name="Deployment">The deployment, or null when the report has no member <c>deployment</c>.</param>
/// <param name="HasDeployment">True when the report has the member <c>deployment</c>, also with a null value.</param>
public sealed record ExtensionListReport(
    CommandResult Result, string Host, string? Deployment, bool HasDeployment, string Execution, ImmutableArray<ExtensionEntry> Extensions)
{
    /// <summary>The arguments of the command.</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["extension", "list", "--json"];

    public static ExtensionListReport Of(CommandResult result)
    {
        var root = result.ShouldWriteOneJsonDocument();
        try
        {
            return new ExtensionListReport(
                result,
                root.GetProperty("host").GetString()!,
                root.TryGetProperty("deployment", out var deployment) ? deployment.GetString() : null,
                root.TryGetProperty("deployment", out _),
                root.GetProperty("execution").GetString()!,
                [.. root.GetProperty("extensions").EnumerateArray().Select(entry => new ExtensionEntry(
                    entry.GetProperty("id").GetString()!,
                    [.. entry.GetProperty("types").EnumerateArray().Select(type => type.GetString()!)],
                    entry.GetProperty("delivery").GetString()!,
                    [.. entry.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()!)],
                    entry.GetProperty("available").GetBoolean(),
                    entry.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    entry.TryGetProperty("reason", out _),
                    entry.TryGetProperty("capabilities", out var capabilities) && capabilities.TryGetProperty("validator", out var validator)
                        ? [.. validator.GetProperty("irVersions").EnumerateArray().Select(version => version.GetString()!)]
                        : null,
                    entry.GetRawText()))]);
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException)
        {
            throw new CommandAssertionException($"Standard output is not an extension list report: {error.Message}", result);
        }
    }

    public ExtensionEntry Extension(string id) =>
        Extensions.SingleOrDefault(entry => entry.Id == id)
        ?? throw new CommandAssertionException($"The extension list has no entry '{id}'.", Result);

    /// <summary>The executable name of a host in a report: <c>morphir</c>, or <c>dotnet-morphir</c> for the tool host.</summary>
    public static string HostName(MorphirHost host) => host == MorphirHost.DotnetTool ? "dotnet-morphir" : "morphir";

    /// <summary>
    /// The deployment and the execution that the build of a target reports. The Native AOT executable is published
    /// with the settings of the native-aot profile, and each packaged offering is the output of its own release
    /// profile. The development build, the single-file executables and an executable at a path name no profile.
    /// </summary>
    public static (string? Deployment, string Execution) ExpectedDeploymentOf(TestTarget target) => target switch
    {
        TestTarget.NativeAot => ("native-aot", "native-aot"),
        TestTarget.Packaged packaged => (packaged.Name, packaged.Offering == PackagedOffering.NativeAot ? "native-aot" : "jit"),
        _ => (null, "jit"),
    };
}

/// <summary>
/// The WASM runtime of the report of <c>extension runtimes --json</c>. A runtime that is not available can have no
/// profiles and no bounds: <paramref name="Profiles"/> is then empty and <paramref name="MemoryBytes"/> null.
/// </summary>
public sealed record WasmRuntime(string Kind, bool Available, string? Reason, string EngineName, string EngineVersion, ImmutableArray<string> Profiles, long? MemoryBytes);

/// <summary>The report of <c>extension runtimes --json</c>: the host, the platform and its one runtime.</summary>
public sealed record ExtensionRuntimesReport(CommandResult Result, string Host, string Platform, WasmRuntime Wasm)
{
    /// <summary>The arguments of the command.</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["extension", "runtimes", "--json"];

    /// <summary>The reason of a host that cannot run guests on Windows x64 (#609).</summary>
    public const string WindowsX64Reason = "win_x64_trap_unsupported";

    public static ExtensionRuntimesReport Of(CommandResult result)
    {
        var root = result.ShouldWriteOneJsonDocument();
        try
        {
            var runtimes = root.GetProperty("runtimes").EnumerateArray().ToArray();
            if (runtimes.Length != 1)
                throw new CommandAssertionException($"The runtime report has {runtimes.Length} runtimes, not one.", result);
            var wasm = runtimes[0];
            return new ExtensionRuntimesReport(
                result,
                root.GetProperty("host").GetString()!,
                root.GetProperty("platform").GetString()!,
                new WasmRuntime(
                    wasm.GetProperty("kind").GetString()!,
                    wasm.GetProperty("available").GetBoolean(),
                    wasm.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    wasm.GetProperty("engine").GetProperty("name").GetString()!,
                    wasm.GetProperty("engine").GetProperty("version").GetString()!,
                    wasm.TryGetProperty("profiles", out var profiles) ? [.. profiles.EnumerateArray().Select(profile => profile.GetString()!)] : [],
                    wasm.TryGetProperty("bounds", out var bounds) ? bounds.GetProperty("memoryBytes").GetInt64() : null));
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException)
        {
            throw new CommandAssertionException($"Standard output is not a runtime report: {error.Message}", result);
        }
    }

    /// <summary>True on Windows x64, where the host reports that it cannot run guests (#609).</summary>
    public bool IsWindowsX64 => Platform == "win-x64";
}

/// <summary>The versions that <c>Directory.Packages.props</c> pins, for a check of a reported version.</summary>
public static class PinnedPackages
{
    /// <summary>The version of <paramref name="package"/> in the text of a <c>Directory.Packages.props</c> file.</summary>
    public static string VersionOf(string props, string package) =>
        System.Xml.Linq.XDocument.Parse(props).Descendants("PackageVersion")
            .Single(element => (string?)element.Attribute("Include") == package).Attribute("Version")!.Value;
}
