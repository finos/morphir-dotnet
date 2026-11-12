// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// <c>morphir extension runtimes</c> (task 4.2 of <c>add-wasm-extension-host</c>, design RPT-1): whether this host can
/// run WASM extensions, with the engine, the profiles and the bounds it enforces, or the reason it cannot. It loads no
/// module. The report is written to stdout, as text or one JSON document, and the command exits with 0 in both
/// states: reporting an unavailable runtime is the command's result, not its failure.
/// </summary>
public sealed class ExtensionRuntimesCommand : Command
{
    private readonly Option<bool> _json = new("--json") { Description = "Output one JSON document" };

    public ExtensionRuntimesCommand(IServiceProvider services) : base("runtimes", "Report the extension runtimes of this host and whether each is available")
    {
        Options.Add(_json);
        SetAction(result => Execute(services.GetRequiredService<HostIdentity>(), services.GetRequiredService<WasmRuntime>(), result.GetValue(_json),
            ConsoleOutputHelper.WriteLineToStdout));
    }

    /// <summary>Writes the report of <paramref name="runtime"/> to <paramref name="stdout"/>; returns the exit code, 0.</summary>
    public static int Execute(HostIdentity host, WasmRuntime runtime, bool json, Action<string> stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var report = ExtensionRuntimesReport.Of(host, runtime);
        stdout(json ? report.ToJson() : report.ToText());
        return 0;
    }
}

/// <summary>
/// The runtime report of one host. The values come from the running executable: the host from its entry point, the
/// platform and the availability from its <see cref="WasmRuntime"/>, the engine, the profiles and the bounds from the
/// libraries it was built with. No build identity of the deployment offering exists at run time yet, so the report has
/// no <c>deployment</c> member and never guesses one from the executable's name or path.
/// </summary>
public sealed class ExtensionRuntimesReport
{
    private ExtensionRuntimesReport(MorphirHost host, string platform, WasmRuntimeAvailability wasm)
    {
        Host = host;
        Platform = platform;
        Wasm = wasm;
    }

    /// <summary>The host.</summary>
    public MorphirHost Host { get; }

    /// <summary>The platform the host runs on.</summary>
    public string Platform { get; }

    /// <summary>Whether the WASM runtime can run a module.</summary>
    public WasmRuntimeAvailability Wasm { get; }

    /// <summary>The report of <paramref name="host"/> with <paramref name="runtime"/>. Reading it probes the runtime.</summary>
    public static ExtensionRuntimesReport Of(HostIdentity host, WasmRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(runtime);
        return new ExtensionRuntimesReport(host.Host, runtime.Platform, runtime.Availability);
    }

    private WasmRuntimeUnavailableReason? Reason => Wasm.Match<WasmRuntimeUnavailableReason?>(available: () => null, unavailable: reason => reason);

    /// <summary>The bytes of the memory ceiling the host gives each linear memory of a session.</summary>
    private static long MemoryBytes => SessionLimits.Default.MemoryCeilingBytes;

    /// <summary>The profiles this host runs.</summary>
    private static IEnumerable<string> Profiles => WasmHost.Profiles.Select(profile => profile.Id);

    /// <summary>
    /// One JSON document: <c>host</c>, <c>platform</c> and <c>runtimes</c>, with one <c>wasm</c> entry. An available entry
    /// has <c>engine</c>, <c>profiles</c> and <c>bounds</c>; an unavailable one has <c>reason</c> and <c>engine</c>, the
    /// engine this build carries.
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("host", Host.WireValue);
            writer.WriteString("platform", Platform);
            writer.WriteStartArray("runtimes");
            writer.WriteStartObject();
            writer.WriteString("kind", "wasm");
            if (Reason is { } reason)
            {
                writer.WriteBoolean("available", false);
                writer.WriteString("reason", reason.WireValue);
                WriteEngine(writer);
            }
            else
            {
                writer.WriteBoolean("available", true);
                WriteEngine(writer);
                writer.WriteStartArray("profiles");
                foreach (var profile in Profiles) writer.WriteStringValue(profile);
                writer.WriteEndArray();
                writer.WriteStartObject("bounds");
                writer.WriteNumber("memoryBytes", MemoryBytes);
                // The engine meters fuel and is interrupted by epochs in every session (WasmEngine), so both are enforced.
                writer.WriteString("deadline", "enforced");
                writer.WriteString("budget", "enforced");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteEngine(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("engine");
        writer.WriteString("name", WasmRuntimeIdentity.EngineName);
        writer.WriteString("version", WasmRuntimeIdentity.EngineVersion);
        writer.WriteEndObject();
    }

    /// <summary>The report as lines of text.</summary>
    public string ToText()
    {
        List<string> lines = [$"Host: {Host.WireValue}", $"Platform: {Platform}"];
        if (Reason is { } reason)
        {
            lines.Add($"wasm: unavailable ({reason.WireValue})");
            lines.Add($"  Engine: {WasmRuntimeIdentity.Engine}");
            lines.Add($"  Reason: {reason.Description}");
        }
        else
        {
            var bytes = MemoryBytes.ToString(CultureInfo.InvariantCulture);
            var mebibytes = (MemoryBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
            lines.Add("wasm: available");
            lines.Add($"  Engine: {WasmRuntimeIdentity.Engine}");
            lines.Add($"  Profiles: {string.Join(", ", Profiles)}");
            lines.Add($"  Bounds: memory ceiling {bytes} bytes ({mebibytes} MiB), call deadline enforced, instruction budget enforced");
        }

        return string.Join('\n', lines);
    }
}
