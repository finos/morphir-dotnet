// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The WASM runtime of this process: its platform, and whether it can run a module (task 4.2 of
/// <c>add-wasm-extension-host</c>, design RPT-1). The registry, <c>extension list</c>, the module loader, the worker
/// and <c>extension runtimes</c> all read <see cref="Process"/>, so they cannot disagree: the composition registers that
/// one instance, and the worker, which runs before the composition exists, reads it directly.
/// </summary>
/// <remarks>
/// The availability is decided on first need and remembered for the process: creating this value, starting the CLI,
/// <c>--version</c>, <c>--help</c> and every command that never reads <see cref="Availability"/> leave the engine's
/// native library unloaded. Reading it runs the probe once, also when several threads read it at the same time.
/// Nothing here writes to stdout or stderr.
/// </remarks>
public sealed class WasmRuntime
{
    private readonly Lazy<WasmRuntimeAvailability> availability;

    /// <summary>A runtime on <paramref name="platform"/> whose availability <paramref name="probe"/> decides, once, when it is first read.</summary>
    /// <param name="platform">The platform, as <see cref="WasmRuntimeProbe.PlatformOf"/> names it, for example <c>win-arm64</c>.</param>
    /// <param name="probe">Decides the availability. Called at most once.</param>
    public WasmRuntime(string platform, Func<WasmRuntimeAvailability> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(probe);
        Platform = platform;
        availability = new Lazy<WasmRuntimeAvailability>(probe, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The runtime of this process: <see cref="WasmRuntimeProbe.Probe"/> on <see cref="WasmRuntimeProbe.CurrentPlatform"/>,
    /// starting the process's engine (<see cref="WasmEngine.Shared"/>).
    /// </summary>
    public static WasmRuntime Process { get; } = new(WasmRuntimeProbe.CurrentPlatform,
        () => WasmRuntimeProbe.Probe(WasmRuntimeProbe.CurrentPlatform, WasmRuntimeProbe.StartEngine, WasmRuntimeProbe.EngineLibraryIsPresent));

    /// <summary>The platform this process runs on, for example <c>win-arm64</c> or <c>linux-musl-x64</c>.</summary>
    public string Platform { get; }

    /// <summary>Whether the probe has run. Reading it does not run the probe.</summary>
    public bool HasProbed => availability.IsValueCreated;

    /// <summary>Whether this process can run a WASM module, and why not. The first read runs the probe.</summary>
    public WasmRuntimeAvailability Availability => availability.Value;
}

/// <summary>
/// Decides whether a process can run WASM modules, and if not, why (the reasons of design RPT-1). The decision comes
/// from what starting the engine did, not from the file system alone:
/// <list type="bullet">
/// <item><c>platform_unsupported</c>: the Wasmtime package has no native library for the platform
/// (<see cref="EnginePlatforms"/>). The engine is not started.</item>
/// <item><c>engine_missing</c>: starting the engine threw <see cref="DllNotFoundException"/> and no engine library file
/// is where the runtime looks for it.</item>
/// <item><c>engine_load_failed</c>: starting the engine threw anything else (a library of another architecture, a
/// missing entry point, an engine that cannot be created), or <see cref="DllNotFoundException"/> while the library file
/// is there. .NET on Linux and macOS reports every <c>dlopen</c> failure as <see cref="DllNotFoundException"/>, and on
/// Windows a library whose own dependency is missing; the file then decides between the two.</item>
/// </list>
/// <c>disabled</c> is a reason of the runtime report, but no switch that turns the runtime off is defined yet, so this
/// probe never gives it.
/// </summary>
public static class WasmRuntimeProbe
{
    /// <summary>The platforms the Wasmtime package ships a native library for, in ordinal order.</summary>
    public static IReadOnlyList<string> EnginePlatforms { get; } = ["linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-arm64", "win-x64"];

    /// <summary>The platform of this process, from its operating system, its architecture and its C library.</summary>
    public static string CurrentPlatform { get; } =
        PlatformOf(CurrentOperatingSystem(), RuntimeInformation.ProcessArchitecture, RuntimeInformation.RuntimeIdentifier);

    /// <summary>
    /// The platform name of an operating system and a process architecture: <c>win</c>, <c>osx</c>, <c>linux</c> or
    /// <c>linux-musl</c>, or the operating system's own name, then the architecture, as the Wasmtime package names its
    /// native folders. The runtime identifier of the running .NET runtime says only whether Linux uses musl, so a
    /// distribution's own identifier, such as <c>ubuntu.24.04-x64</c>, still names <c>linux-x64</c>.
    /// </summary>
    public static string PlatformOf(OSPlatform operatingSystem, Architecture architecture, string runtimeIdentifier)
    {
        ArgumentNullException.ThrowIfNull(runtimeIdentifier);
        var system = operatingSystem == OSPlatform.Windows ? "win"
            : operatingSystem == OSPlatform.OSX ? "osx"
            : operatingSystem == OSPlatform.Linux ? (UsesMusl(runtimeIdentifier) ? "linux-musl" : "linux")
            : operatingSystem.ToString().ToLowerInvariant();
        return $"{system}-{ArchitectureName(architecture)}";
    }

    /// <summary>The file name of the engine's native library on <paramref name="platform"/>, or null for a platform without an engine.</summary>
    public static string? EngineLibraryFileName(string platform) =>
        !EnginePlatforms.Contains(platform, StringComparer.Ordinal) ? null
        : platform.StartsWith("win-", StringComparison.Ordinal) ? "wasmtime.dll"
        : platform.StartsWith("osx-", StringComparison.Ordinal) ? "libwasmtime.dylib"
        : "libwasmtime.so";

    /// <summary>
    /// Whether the engine can run on <paramref name="platform"/>: the reason it cannot, or available.
    /// <paramref name="startEngine"/> loads the native library and creates the engine, and throws when either fails;
    /// <paramref name="engineLibraryIsPresent"/> is asked only when it threw <see cref="DllNotFoundException"/>. Every
    /// exception of <paramref name="startEngine"/> becomes a reason; none leaves this method.
    /// </summary>
    public static WasmRuntimeAvailability Probe(string platform, Action startEngine, Func<bool> engineLibraryIsPresent)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(startEngine);
        ArgumentNullException.ThrowIfNull(engineLibraryIsPresent);
        if (!EnginePlatforms.Contains(platform, StringComparer.Ordinal))
            return WasmRuntimeAvailability.UnavailableBecause(WasmRuntimeUnavailableReason.PlatformUnsupported);

        try
        {
            startEngine();
            return WasmRuntimeAvailability.Available;
        }
        catch (Exception error)
        {
            // Turning a failed start into a reason is this method's purpose: a native library loading error must
            // never reach a user as the diagnostic (requirement "Unavailable runtime fails by name").
            return WasmRuntimeAvailability.UnavailableBecause(Reason(error, engineLibraryIsPresent));
        }
    }

    private static WasmRuntimeUnavailableReason Reason(Exception error, Func<bool> engineLibraryIsPresent)
    {
        while (error is TypeInitializationException { InnerException: { } inner }) error = inner;
        return error is DllNotFoundException && !engineLibraryIsPresent()
            ? WasmRuntimeUnavailableReason.EngineMissing
            : WasmRuntimeUnavailableReason.EngineLoadFailed;
    }

    /// <summary>Loads the engine's native library and creates the process's engine.</summary>
    internal static void StartEngine() => _ = WasmEngine.Shared;

    /// <summary>
    /// Whether the engine's library file of <see cref="CurrentPlatform"/> is beside the application or in a native
    /// search directory the .NET host resolved from the application's dependencies.
    /// </summary>
    internal static bool EngineLibraryIsPresent()
    {
        if (EngineLibraryFileName(CurrentPlatform) is not { } name) return false;
        var searched = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ((IEnumerable<string>)[AppContext.BaseDirectory, .. searched]).Any(directory => File.Exists(Path.Combine(directory, name)));
    }

    private static bool UsesMusl(string runtimeIdentifier) =>
        runtimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase) || runtimeIdentifier.StartsWith("alpine", StringComparison.OrdinalIgnoreCase);

    private static OSPlatform CurrentOperatingSystem() =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsLinux() ? OSPlatform.Linux
        : OperatingSystem.IsFreeBSD() ? OSPlatform.FreeBSD
        : OSPlatform.Create("UNKNOWN");

    private static string ArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        Architecture.Armv6 => "armv6",
        Architecture.Wasm => "wasm",
        Architecture.S390x => "s390x",
        Architecture.LoongArch64 => "loongarch64",
        Architecture.Ppc64le => "ppc64le",
        Architecture.RiscV64 => "riscv64",
        _ => "unknown",
    };
}

/// <summary>The failure <c>extension.runtime_unavailable</c> (requirement "Unavailable runtime fails by name").</summary>
public static class WasmRuntimeUnavailable
{
    /// <summary>The code of a command that needs the WASM runtime in a host without it.</summary>
    public const string Code = "extension.runtime_unavailable";

    /// <summary>
    /// The clause that names the host, its platform and the reason, for example <c>the WASM runtime of morphir 0.4.0 on
    /// linux-musl-x64 is unavailable (platform_unsupported): the engine has no build for this platform</c>. No build
    /// identity of the deployment offering exists at run time yet, so the host stands for it.
    /// </summary>
    public static string Clause(HostIdentity host, string platform, WasmRuntimeUnavailableReason reason)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(reason);
        return $"the WASM runtime of {host} on {platform} is unavailable ({reason.WireValue}): {reason.Description}";
    }
}
