// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Configuration;

namespace Morphir.Tooling.Features.Configuration;

public sealed record ConfigurationSourceDto(string Path, string Format, string Role, int Order, string? Profile);
public sealed record ConfigurationOriginDto(string Key, ConfigurationSourceDto Source, int? Line, int? Column);
public sealed record ConfigurationEntryDto(string Key, JsonElement Value, ConfigurationOriginDto[] Origins);
public sealed record ConfigurationSourceReportDto(ConfigurationSourceDto Source, string Status, bool Contributed, string Detail);
public sealed record ConfigurationContributionDto(ConfigurationSourceDto Source, JsonElement? Value, bool Winner, ConfigurationOriginDto[] Origins);
public sealed record ConfigurationDiagnosticDto(string Code, string Severity, string Message, string? Path, string? Key, int? Line, int? Column);
public sealed record ConfigurationValueTypeDto(string Pointer, string Kind, string Encoding);
public sealed record ConfigurationReport(
    string Operation, string Scope, string? Key, bool Found, JsonElement? Value,
    ConfigurationEntryDto[] Entries, ConfigurationSourceReportDto[] Sources,
    ConfigurationContributionDto[] Contributions, ConfigurationDiagnosticDto[] Diagnostics, int ExitCode, ConfigurationValueTypeDto[] ValueTypes)
{
    // Readers accept 128 levels; retain room for typed tags and report envelopes.
    internal const int MaxJsonDepth = 256;

    public string ToJson() => JsonSerializer.Serialize(this, ConfigurationJsonContext.Default.ConfigurationReport);

    internal static ConfigurationReport From(InspectionResult result, bool origin, bool scope)
    {
        var valueTypes = new List<ConfigurationValueTypeDto>();
        var errors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        var exit = errors ? 2 : result.Found ? 0 : 1;
        return new(result.Operation, ScopeName(result.Scope), result.Key?.Value, result.Found,
            result.Value is null ? null : JsonValue(result.Value.Value, "/value", valueTypes),
            result.Entries.Select((e, index) => new ConfigurationEntryDto(e.Key, JsonValue(e.Value, $"/entries/{index}/value", valueTypes),
                Origins(e.Origins, origin, scope))).ToArray(),
            result.Sources.Select(s => new ConfigurationSourceReportDto(Source(s.Source), Status(s.Status), s.Contributed, s.Detail)).ToArray(),
            result.Contributions.Select((c, index) => new ConfigurationContributionDto(Source(c.Source), c.Value is null ? null : JsonValue(c.Value.Value, $"/contributions/{index}/value", valueTypes), c.Winner,
                Origins(c.Origins, true, true))).ToArray(),
            result.Diagnostics.Select(Diagnostic).ToArray(), exit, valueTypes.ToArray());
    }

    internal static ConfigurationReport Failure(string operation, string code, string message) => new(operation, "effective", null, false, null,
        [], [], [], [new(code, "error", message, null, null, null, null)], 2, []);

    private static ConfigurationOriginDto[] Origins(IEnumerable<InspectionOrigin> origins, bool showOrigin, bool showScope) =>
        !showOrigin && !showScope ? [] : origins.Select(o => new ConfigurationOriginDto(o.Key,
            new(showOrigin ? o.Origin.Source.Path : "", showOrigin ? o.Origin.Source.Format.ToString().ToLowerInvariant() : "",
                showScope ? o.Origin.Source.Role.ToString() : "", o.Origin.Source.Order, o.Origin.Source.Profile?.Value),
            showOrigin ? o.Origin.Position?.Value.Line : null, showOrigin ? o.Origin.Position?.Value.Column : null)).ToArray();

    internal static ConfigurationDiagnosticDto Diagnostic(ConfigDiagnostic diagnostic) => new(diagnostic.Code,
        diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning", diagnostic.Message, diagnostic.Source.Path,
        diagnostic.KeyPath.IsEmpty ? null : KeyPath.format(diagnostic.KeyPath), diagnostic.Position?.Value.Line, diagnostic.Position?.Value.Column);

    internal static ConfigurationSourceDto Source(SourceIdentity source) => new(source.Path, source.Format.ToString().ToLowerInvariant(), source.Role.ToString(), source.Order, source.Profile?.Value);

    private static string ScopeName(QueryScope scope) => scope switch
    {
        QueryScope.File file => "file:" + file.Item,
        _ when scope.IsWorkspace => "workspace",
        _ when scope.IsProject => "project",
        _ when scope.IsUser => "user",
        _ when scope.IsGlobal => "global",
        _ when scope.IsSystem => "system",
        _ => "effective"
    };

    private static string Status(SourceStatus status) => status.IsLoaded ? "loaded" : status.IsMissing ? "missing" : status.IsSkipped ? "skipped" : "failed";

    internal static JsonElement JsonValue(ConfigValue value, string pointer, List<ConfigurationValueTypeDto> valueTypes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = MaxJsonDepth })) WriteValue(writer, value, pointer, valueTypes);
        using var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        return document.RootElement.Clone();
    }

    private static void WriteValue(Utf8JsonWriter writer, ConfigValue value, string pointer, List<ConfigurationValueTypeDto> valueTypes)
    {
        switch (value)
        {
            case ConfigValue.String text: writer.WriteStringValue(text.Item); break;
            case ConfigValue.Boolean boolean: writer.WriteBooleanValue(boolean.Item); break;
            case ConfigValue.Integer integer: writer.WriteNumberValue(integer.Item); break;
            case ConfigValue.BigInteger integer: writer.WriteRawValue(integer.Item.ToString(CultureInfo.InvariantCulture)); break;
            case ConfigValue.Number number when double.IsFinite(number.Item): writer.WriteNumberValue(number.Item); break;
            case ConfigValue.Number number:
                writer.WriteStartObject();
                writer.WriteString("$number", double.IsNaN(number.Item) ? "NaN" : double.IsPositiveInfinity(number.Item) ? "Infinity" : "-Infinity");
                writer.WriteEndObject();
                valueTypes.Add(new(pointer, "number", "nonfinite"));
                break;
            case ConfigValue.Temporal temporal:
                writer.WriteStringValue(temporal.text);
                valueTypes.Add(new(pointer, "temporal", temporal.kind));
                break;
            case ConfigValue.Array array:
                writer.WriteStartArray();
                var index = 0;
                foreach (var child in array.Item) WriteValue(writer, child, pointer + "/" + (index++).ToString(CultureInfo.InvariantCulture), valueTypes);
                writer.WriteEndArray();
                break;
            case ConfigValue.Object obj:
                writer.WriteStartObject();
                foreach (var child in obj.Item)
                {
                    writer.WritePropertyName(child.Key);
                    var segment = child.Key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                    WriteValue(writer, child.Value, pointer + "/" + segment, valueTypes);
                }
                writer.WriteEndObject();
                break;
            case ConfigValue.SecretReference: writer.WriteStringValue("[REDACTED]"); break;
            case ConfigValue nullValue when nullValue.IsNull: writer.WriteNullValue(); break;
            default: throw new InvalidOperationException("Unsupported canonical configuration value");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = global::Morphir.Tooling.Features.Configuration.ConfigurationReport.MaxJsonDepth)]
[JsonSerializable(typeof(ConfigurationReport))]
internal partial class ConfigurationJsonContext : JsonSerializerContext;
