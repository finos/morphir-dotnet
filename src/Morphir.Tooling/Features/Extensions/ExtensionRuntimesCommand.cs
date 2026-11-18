// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;
using Morphir.Tooling.Cli;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// <c>morphir extension runtimes</c> (task 4.2 of <c>add-wasm-extension-host</c>, design RPT-1): whether this host can
/// run WASM extensions, with the engine, the profiles and the bounds it enforces, or the reason it cannot. It loads no
/// module. The report is written to stdout, as text, as the one <c>completed</c> record of <c>--json</c> or as one JSON
/// document with <c>--json=document</c>, and the command exits with 0 in both states: reporting an unavailable runtime
/// is the command's result, not its failure.
/// </summary>
public sealed class ExtensionRuntimesCommand : Command, IMorphirParseErrorHandler
{
    private readonly IServiceProvider _services;
    private readonly Option<string> _json = JsonOutputOption.Create();

    public ExtensionRuntimesCommand(IServiceProvider services) : base("runtimes", "Report the extension runtimes of this host and whether each is available")
    {
        _services = services;
        Options.Add(_json);
        SetAction(result => Execute(services.GetRequiredService<HostIdentity>(), services.GetRequiredService<WasmRuntime>(), JsonOutputOption.ModeOf(result),
            ConsoleOutputHelper.Stdout));
    }

    /// <summary>Writes the report of <paramref name="runtime"/> to <paramref name="stdout"/>; returns the exit code, 0.</summary>
    public static int Execute(HostIdentity host, WasmRuntime runtime, JsonOutputMode mode, TextWriter stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var report = ExtensionRuntimesReport.Of(host, runtime);
        if (mode != JsonOutputMode.None) JsonReportOutput.Write(stdout, mode, report, ExtensionRuntimesReport.JsonInfo);
        else stdout.WriteLine(report.ToText());
        return 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// In a JSON mode the report of a usage error has no runtime and one diagnostic for each message, with exit code
    /// 2; the runtime is not probed. In text mode each message goes to stderr.
    /// </remarks>
    public int HandleParseErrors(ParseResult result, IReadOnlyList<string> messages)
    {
        var mode = JsonOutputOption.ScanTokens([.. result.Tokens.Select(token => token.Value)]);
        if (mode == JsonOutputMode.None)
        {
            foreach (var message in messages) Console.Error.WriteLine($"Error: {ExtensionListCommand.InvalidArgumentsCode}: {message}");
            return ExtensionRuntimesReport.UsageExitCode;
        }

        var report = ExtensionRuntimesReport.Usage(_services.GetRequiredService<HostIdentity>(), _services.GetRequiredService<WasmRuntime>().Platform,
            ExtensionListCommand.InvalidArgumentsCode, messages);
        JsonReportOutput.Write(ConsoleOutputHelper.Stdout, mode, report, ExtensionRuntimesReport.JsonInfo);
        return ExtensionRuntimesReport.UsageExitCode;
    }
}

/// <summary>
/// The runtime report of one host. The values come from the running executable: the host from its entry point, the
/// platform and the availability from its <see cref="WasmRuntime"/>, the engine, the profiles and the bounds from the
/// libraries it was built with. No build identity of the deployment offering exists at run time yet, so the report has
/// no <c>deployment</c> member and never guesses one from the executable's name or path.
/// </summary>
[JsonConverter(typeof(ExtensionRuntimesReportConverter))]
public sealed class ExtensionRuntimesReport
{
    /// <summary>The exit code of a usage error.</summary>
    public const int UsageExitCode = 2;

    private readonly IReadOnlyList<(string Code, string Message)>? _usage;

    private ExtensionRuntimesReport(MorphirHost host, string platform, WasmRuntimeAvailability wasm, IReadOnlyList<(string Code, string Message)>? usage = null)
    {
        Host = host;
        Platform = platform;
        Wasm = wasm;
        _usage = usage;
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

    /// <summary>
    /// The report of a usage error: <c>host</c>, <c>platform</c>, no runtime, one diagnostic <paramref name="code"/> for
    /// each message and <c>exitCode</c> 2. The runtime is not probed.
    /// </summary>
    public static ExtensionRuntimesReport Usage(HostIdentity host, string platform, string code, IEnumerable<string> messages)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(messages);
        // A usage report writes no runtime, so its availability is never read.
        return new ExtensionRuntimesReport(host.Host, platform, null!, [.. messages.Select(message => (code, message))]);
    }

    /// <summary>The source-generated contract of the report, for the shared JSON output writer.</summary>
    public static JsonTypeInfo<ExtensionRuntimesReport> JsonInfo => ExtensionRuntimesJsonContext.Default.ExtensionRuntimesReport;

    private WasmRuntimeUnavailableReason? Reason => Wasm.Match<WasmRuntimeUnavailableReason?>(available: () => null, unavailable: reason => reason);

    /// <summary>The bytes of the memory ceiling the host gives each linear memory of a session.</summary>
    private static long MemoryBytes => SessionLimits.Default.MemoryCeilingBytes;

    /// <summary>The profiles this host runs.</summary>
    private static IEnumerable<string> Profiles => WasmHost.Profiles.Select(profile => profile.Id);

    /// <summary>
    /// One JSON document: <c>host</c>, <c>platform</c> and <c>runtimes</c>, with one <c>wasm</c> entry. An available entry
    /// has <c>engine</c>, <c>profiles</c> and <c>bounds</c>; an unavailable one has <c>reason</c> and <c>engine</c>, the
    /// engine this build carries. The report of a usage error has no runtime, and has <c>diagnostics</c> and
    /// <c>exitCode</c>.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonInfo);

    /// <summary>Writes the report of <see cref="ToJson"/> with <paramref name="writer"/>, which sets the indentation.</summary>
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("host", Host.WireValue);
        writer.WriteString("platform", Platform);
        writer.WriteStartArray("runtimes");
        if (_usage is not null)
        {
            writer.WriteEndArray();
            writer.WriteStartArray("diagnostics");
            foreach (var (code, message) in _usage)
            {
                writer.WriteStartObject();
                writer.WriteString("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("exitCode", UsageExitCode);
            writer.WriteEndObject();
            return;
        }

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

/// <summary>Writes an <see cref="ExtensionRuntimesReport"/> with its own writer code; the report is never read back.</summary>
internal sealed class ExtensionRuntimesReportConverter : JsonConverter<ExtensionRuntimesReport>
{
    public override ExtensionRuntimesReport Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("An extension runtimes report is written, never read.");

    public override void Write(Utf8JsonWriter writer, ExtensionRuntimesReport value, JsonSerializerOptions options) => value.Write(writer);
}

/// <summary>The indented JSON contract of <c>extension runtimes --json=document</c>; the record of <c>--json</c> is compact.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExtensionRuntimesReport))]
internal partial class ExtensionRuntimesJsonContext : JsonSerializerContext;
