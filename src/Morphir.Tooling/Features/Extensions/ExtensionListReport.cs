using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The serialization shape of <c>morphir extension list --json</c>. It is built only by <see cref="From"/> and
/// <see cref="Failure"/>, which are the one place that turns typed values into text. No code reads it back.
/// </summary>
public sealed record ExtensionListReport(string Host, IReadOnlyList<ExtensionListEntry> Extensions, IReadOnlyList<ExtensionListDiagnostic>? Diagnostics)
{
    public static ExtensionListReport From(ExtensionCatalog catalog) =>
        new(catalog.Host.Host.WireValue, catalog.Entries.Select(entry => Entry(entry, catalog.Host.Host)).ToArray(), null);

    public static ExtensionListReport Failure(HostIdentity host, RegistryError error) =>
        new(host.Host.WireValue, [], error.Diagnostics.Select(diagnostic => new ExtensionListDiagnostic(diagnostic.Code.WireValue, diagnostic.Message)).ToArray());

    private static ExtensionListEntry Entry(ExtensionEntry entry, MorphirHost host)
    {
        var backend = entry.BackendClaimOrNull;
        var validator = entry.ValidatorClaimOrNull;
        return new ExtensionListEntry(
            entry.Identity.Id.Value,
            entry.Identity.Name.Value,
            entry.Identity.Version.ToString(),
            entry.Identity.DescriptionOrNull?.Value,
            entry.Types.Select(type => type.WireValue).ToArray(),
            entry.Delivery.WireValue,
            entry.Hosts.WireValues,
            entry.IsAvailableIn(host),
            new ExtensionListCapabilities(backend is null
                ? null
                // Generation is the only backend operation in MEP, so a backend claim always generates.
                : new ExtensionListBackend(backend.Targets.Select(target => target.Value).ToArray(), backend.IrVersions.Select(version => version.Text).ToArray(), true),
                validator is null ? null : new ExtensionListValidator(validator.IrVersionTexts)),
            entry.CapabilitySummary);
    }

    public string ToJson() => JsonSerializer.Serialize(this, ExtensionListJsonContext.Default.ExtensionListReport);
}

public sealed record ExtensionListEntry(string Id, string Name, string Version, string? Description, IReadOnlyList<string> Types,
    string Delivery, IReadOnlyList<string> Hosts, bool Available, ExtensionListCapabilities Capabilities,
    [property: JsonIgnore] string CapabilitySummary);

/// <summary>
/// Capability claims under their MEP wire names. <c>validator</c> is a morphir-dotnet report member: MEP
/// 0.2.0-draft.1 has no validator capability, so it is never written into MEP messages.
/// </summary>
public sealed record ExtensionListCapabilities(ExtensionListBackend? Backend, ExtensionListValidator? Validator);

public sealed record ExtensionListBackend(IReadOnlyList<string> Targets, IReadOnlyList<string> IrVersions, bool Generate);

/// <summary>The IR versions that a validator reads, in the spelling of the model readers.</summary>
public sealed record ExtensionListValidator(IReadOnlyList<string> IrVersions);

public sealed record ExtensionListDiagnostic(string Code, string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExtensionListReport))]
internal partial class ExtensionListJsonContext : JsonSerializerContext;
