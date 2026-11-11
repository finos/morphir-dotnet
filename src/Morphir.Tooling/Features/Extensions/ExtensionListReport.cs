using System.Text.Json;
using System.Text.Json.Serialization;
using Morphir.Extensions;

namespace Morphir.Tooling.Features.Extensions;

/// <summary>
/// The serialization shape of <c>morphir extension list --json</c>. It is built only by <see cref="From"/> and
/// <see cref="Failure"/>, which are the one place that turns typed values into text. No code reads it back.
/// <c>host</c> is the command name. <c>deployment</c>, <c>platform</c> and <c>prerequisites</c> describe the running
/// offering and are absent from a build that no named release profile produced; <c>execution</c> says how the process
/// runs.
/// </summary>
public sealed record ExtensionListReport(
    string Host,
    string? Deployment,
    string Execution,
    string? Platform,
    ExtensionListPrerequisites? Prerequisites,
    IReadOnlyList<ExtensionListEntry> Extensions,
    IReadOnlyList<ExtensionListDiagnostic>? Diagnostics)
{
    public static ExtensionListReport From(ExtensionCatalog catalog) =>
        Identity(catalog.Host, catalog.Entries.Select(entry => Entry(entry, catalog.Host)).ToArray(), null);

    public static ExtensionListReport Failure(HostIdentity host, RegistryError error) =>
        Identity(host, [], error.Diagnostics.Select(diagnostic => new ExtensionListDiagnostic(diagnostic.Code.WireValue, diagnostic.Message)).ToArray());

    private static ExtensionListReport Identity(HostIdentity host, IReadOnlyList<ExtensionListEntry> entries, IReadOnlyList<ExtensionListDiagnostic>? diagnostics)
    {
        var prerequisites = host.PrerequisitesOrNull;
        return new(host.Host.WireValue, host.DeploymentOrNull?.WireValue, host.Execution.WireValue, host.PlatformOrNull?.WireValue,
            prerequisites is null ? null : Prerequisite(prerequisites), entries, diagnostics);
    }

    /// <summary>The runtime, SDK and native-library prerequisites as separate members, each in the recipe's prerequisite shape.</summary>
    private static ExtensionListPrerequisites Prerequisite(Morphir.Extensions.Prerequisites prerequisites) =>
        new(prerequisites.Runtime.Select(runtime => new ExtensionListPrerequisite("runtime", runtime.Name, runtime.Versions, null)).ToArray(),
            prerequisites.Sdk.Select(sdk => new ExtensionListPrerequisite("sdk", sdk.Name, sdk.Versions, sdk.Phase.WireValue)).ToArray(),
            // No native-library prerequisite is declared: operating-system prerequisites are not established yet.
            []);

    private static ExtensionListEntry Entry(ExtensionEntry entry, HostIdentity host)
    {
        var frontend = entry.FrontendClaimOrNull;
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
            entry.Deployments.Deployments.Select(deployment => deployment.WireValue).ToArray(),
            entry.IsAvailableIn(host),
            entry.ReasonOrNullIn(host)?.Value,
            // A built-in has no prerequisite beyond those of its offering.
            Prerequisite(Morphir.Extensions.Prerequisites.None),
            new ExtensionListCapabilities(
                frontend is null
                    ? null
                    : new ExtensionListFrontend(
                        frontend.Languages.Select(language => new ExtensionListLanguage(language.Language.Value, language.FileExtensions)).ToArray(),
                        frontend.IrVersionTexts),
                backend is null
                    ? null
                    // Generation is the only backend operation in MEP, so a backend claim always generates.
                    : new ExtensionListBackend(backend.Targets.Select(target => target.Value).ToArray(), backend.IrVersions.Select(version => version.Text).ToArray(), true),
                validator is null ? null : new ExtensionListValidator(validator.IrVersionTexts)),
            entry.CapabilitySummary);
    }

    public string ToJson() => JsonSerializer.Serialize(this, ExtensionListJsonContext.Default.ExtensionListReport);
}

public sealed record ExtensionListEntry(string Id, string Name, string Version, string? Description, IReadOnlyList<string> Types,
    string Delivery, IReadOnlyList<string> Hosts, IReadOnlyList<string> Deployments, bool Available, string? Reason,
    ExtensionListPrerequisites Prerequisites, ExtensionListCapabilities Capabilities,
    [property: JsonIgnore] string CapabilitySummary);

/// <summary>Prerequisites by kind, as separate members.</summary>
public sealed record ExtensionListPrerequisites(
    IReadOnlyList<ExtensionListPrerequisite> Runtime,
    IReadOnlyList<ExtensionListPrerequisite> Sdk,
    IReadOnlyList<ExtensionListPrerequisite> Native);

/// <summary>One prerequisite in the toolset recipe's shape: <c>kind</c>, <c>name</c>, <c>versions</c>, and <c>phase</c> for an SDK.</summary>
public sealed record ExtensionListPrerequisite(string Kind, string Name, IReadOnlyList<string> Versions, string? Phase);

/// <summary>
/// Capability claims under their MEP wire names. <c>validator</c> is a morphir-dotnet report member: MEP
/// 0.2.0-draft.1 has no validator capability, so it is never written into MEP messages.
/// </summary>
public sealed record ExtensionListCapabilities(ExtensionListFrontend? Frontend, ExtensionListBackend? Backend, ExtensionListValidator? Validator);

/// <summary>What a frontend reads and produces, under the MEP wire names <c>languages</c> and <c>irVersions</c>.</summary>
public sealed record ExtensionListFrontend(IReadOnlyList<ExtensionListLanguage> Languages, IReadOnlyList<string> IrVersions);

/// <summary>A language and its file extensions, under the MEP wire names <c>id</c> and <c>fileExtensions</c>.</summary>
public sealed record ExtensionListLanguage(string Id, IReadOnlyList<string> FileExtensions);

public sealed record ExtensionListBackend(IReadOnlyList<string> Targets, IReadOnlyList<string> IrVersions, bool Generate);

/// <summary>The IR versions that a validator reads, in the spelling of the model readers.</summary>
public sealed record ExtensionListValidator(IReadOnlyList<string> IrVersions);

public sealed record ExtensionListDiagnostic(string Code, string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExtensionListReport))]
internal partial class ExtensionListJsonContext : JsonSerializerContext;
