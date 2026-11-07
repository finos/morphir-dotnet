namespace Morphir.Extensions

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.IR.Model

/// <summary>A Morphir host. C# builds one with the generated properties <c>MorphirHost.Native</c> and <c>MorphirHost.DotnetTool</c>.</summary>
[<RequireQualifiedAccess>]
type MorphirHost =
    /// <summary>The Native AOT executable <c>morphir</c>.</summary>
    | Native
    /// <summary>The JIT dotnet tool <c>dotnet-morphir</c>.</summary>
    | DotnetTool

    /// <summary>The command name that the CLI writes.</summary>
    member this.WireValue =
        match this with
        | Native -> "morphir"
        | DotnetTool -> "dotnet-morphir"

    /// <summary>The command name.</summary>
    override this.ToString() = this.WireValue

/// <summary>The host that runs this process, with its release version.</summary>
type HostIdentity =
    {
        Host: MorphirHost
        Version: ReleaseVersion
    }

    /// <summary>The host and its version.</summary>
    override this.ToString() = $"{this.Host.WireValue} {this.Version}"

/// <summary>The hosts that an extension runs in. An empty set of hosts cannot exist.</summary>
[<RequireQualifiedAccess>]
type HostAvailability =
    /// <summary>Both hosts.</summary>
    | AllHosts
    /// <summary>One host only.</summary>
    | Only of MorphirHost

    /// <summary>True when the extension runs in the host.</summary>
    member this.IsAvailableIn(host: MorphirHost) =
        match this with
        | AllHosts -> true
        | Only only -> only = host

    /// <summary>The command names of the hosts, in a fixed order.</summary>
    member this.WireValues: IReadOnlyList<string> =
        match this with
        | AllHosts -> [| MorphirHost.Native.WireValue; MorphirHost.DotnetTool.WireValue |]
        | Only host -> [| host.WireValue |]

    /// <summary>The command names of the hosts.</summary>
    override this.ToString() = String.Join(", ", this.WireValues)

/// <summary>How an extension is delivered. Process and WASM deliveries come in later steps.</summary>
[<RequireQualifiedAccess>]
type ExtensionDelivery =
    /// <summary>Compiled into the host.</summary>
    | BuiltIn

    /// <summary>The text that the CLI writes.</summary>
    member this.WireValue =
        match this with
        | BuiltIn -> "built-in"

    /// <summary>The text that the CLI writes.</summary>
    override this.ToString() = this.WireValue

/// <summary>A MEP extension type.</summary>
[<RequireQualifiedAccess>]
type ExtensionType =
    /// <summary>Reads source code and produces a model.</summary>
    | Frontend
    /// <summary>Generates artifacts from a model.</summary>
    | Backend
    /// <summary>Changes a model.</summary>
    | Transform
    /// <summary>Checks a model.</summary>
    | Validator
    /// <summary>Discovers workspace content.</summary>
    | Workspace

    /// <summary>The MEP wire value.</summary>
    member this.WireValue =
        match this with
        | Frontend -> "frontend"
        | Backend -> "backend"
        | Transform -> "transform"
        | Validator -> "validator"
        | Workspace -> "workspace"

    /// <summary>The MEP wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>What a backend generates: at least one target and at least one IR version, in their declared spelling.</summary>
type BackendClaim =
    {
        Targets: NonEmptySet<TargetId>
        IrVersions: NonEmptySet<SourceFormatVersion>
    }

    /// <summary>The targets and IR versions.</summary>
    override this.ToString() =
        $"targets {this.Targets}; IR versions {this.IrVersions}"

/// <summary>A capability that an extension claims. Each case fixes the extension type it implies.</summary>
[<RequireQualifiedAccess>]
type ExtensionCapability =
    /// <summary>A backend that generates artifacts.</summary>
    | Backend of BackendClaim

    /// <summary>The extension type that this capability implies.</summary>
    member this.Type =
        match this with
        | Backend _ -> ExtensionType.Backend

    /// <summary>The capability and its claim.</summary>
    override this.ToString() =
        match this with
        | Backend claim -> $"backend ({claim})"

/// <summary>Who an extension is, in the shape of the MEP <c>ExtensionInfo</c> record.</summary>
type ExtensionIdentity =
    {
        Id: ExtensionId
        Name: ExtensionName
        Version: ReleaseVersion
        Description: ExtensionDescription option
    }

    /// <summary>For C# callers: the description, or null when there is none.</summary>
    member this.DescriptionOrNull = Option.toObj this.Description

    /// <summary>For C# callers: an identity with an optional description.</summary>
    static member Create
        (id: ExtensionId,
         name: ExtensionName,
         version: ReleaseVersion,
         description: ExtensionDescription | null)
        =
        {
            Id = id
            Name = name
            Version = version
            Description = Option.ofObj description
        }

    /// <summary>The id and version.</summary>
    override this.ToString() = $"{this.Id} {this.Version}"

/// <summary>One extension that a host knows.</summary>
type ExtensionEntry =
    {
        Identity: ExtensionIdentity
        Delivery: ExtensionDelivery
        Hosts: HostAvailability
        Capabilities: NonEmptySet<ExtensionCapability>
    }

    /// <summary>The MEP extension types, derived from the capabilities. They cannot disagree with the claims.</summary>
    member this.Types =
        this.Capabilities
        |> NonEmptySet.map (fun capability -> capability.Type)

    /// <summary>True when the extension runs in the host.</summary>
    member this.IsAvailableIn(host: MorphirHost) = this.Hosts.IsAvailableIn host

    /// <summary>For C# callers: the backend claim, or null when the extension is not a backend.</summary>
    member this.BackendClaimOrNull =
        this.Capabilities
        |> Seq.tryPick (
            function
            | ExtensionCapability.Backend claim -> Some claim
        )
        |> Option.toObj

    /// <summary>For C# callers, which cannot use <c>with</c> on an F# record: the same entry with other hosts.</summary>
    member this.WithHosts(hosts: HostAvailability) = { this with Hosts = hosts }

    /// <summary>A built-in backend that runs in both hosts.</summary>
    static member BuiltInBackend
        (
            identity: ExtensionIdentity,
            claim: BackendClaim
        ) : ExtensionEntry =
        {
            Identity = identity
            Delivery = ExtensionDelivery.BuiltIn
            Hosts = HostAvailability.AllHosts
            Capabilities = NonEmptySet.create (ExtensionCapability.Backend claim) []
        }

    /// <summary>The identity, delivery and hosts.</summary>
    override this.ToString() =
        $"{this.Identity} ({this.Delivery}; {this.Hosts})"

/// <summary>A diagnostic for the output edge: a stable code and a message.</summary>
type Diagnostic =
    {
        Code: string
        Message: string
    }

    /// <summary>The code and message.</summary>
    override this.ToString() = $"{this.Code}: {this.Message}"

/// <summary>Why a host cannot build its extension catalog.</summary>
[<RequireQualifiedAccess>]
type RegistryError =
    /// <summary>The composition registers these ids more than once.</summary>
    | DuplicateIds of NonEmptySet<ExtensionId>

    /// <summary>One diagnostic for each problem, for the output edge.</summary>
    member this.Diagnostics: IReadOnlyList<Diagnostic> =
        match this with
        | DuplicateIds ids -> [|
            for id in ids ->
                {
                    Code = "extension.duplicate_id"
                    Message = $"Extension id '{id}' is registered more than once."
                }
          |]

    /// <summary>The diagnostics.</summary>
    override this.ToString() =
        String.Join(
            "; ",
            this.Diagnostics
            |> Seq.map string
        )

/// <summary>
/// A source of extension entries. Hosts register sources statically through dependency injection; there is no
/// assembly scanning. An entry for one host only is declared in shared code, and only its implementation is bound
/// in that host's composition.
/// </summary>
type IExtensionSource =
    /// <summary>The entries that this source contributes.</summary>
    abstract Entries: IReadOnlyList<ExtensionEntry>

/// <summary>The extensions that one host knows, ordered by id. A catalog never holds two entries with the same id.</summary>
[<Sealed>]
type ExtensionCatalog private (host: HostIdentity, entries: ExtensionEntry array) =
    /// <summary>The host that built the catalog.</summary>
    member _.Host = host

    /// <summary>The entries, ordered by id with ordinal comparison.</summary>
    member _.Entries: IReadOnlyList<ExtensionEntry> = entries

    /// <summary>The entries for the host, or the ids that are registered more than once. For F# callers.</summary>
    static member Build
        (
            host: HostIdentity,
            entries: ExtensionEntry seq
        ) : Result<ExtensionCatalog, RegistryError> =
        let all = Array.ofSeq entries

        let duplicates =
            all
            |> Array.countBy (fun entry -> entry.Identity.Id)
            |> Array.filter (fun (_, count) -> count > 1)
            |> Array.map fst

        match NonEmptySet.ofSeq "duplicate ids" duplicates with
        | Ok ids -> Error(RegistryError.DuplicateIds ids)
        | Error _ ->
            Ok(
                ExtensionCatalog(
                    host,
                    all
                    |> Array.sortWith (fun a b -> compare a.Identity.Id b.Identity.Id)
                )
            )

    /// <summary>The entries for the host, or the error. For C# callers.</summary>
    static member TryCreate
        (
            host: HostIdentity,
            entries: IEnumerable<ExtensionEntry>,
            [<Out; MaybeNullWhen(false)>] catalog: byref<ExtensionCatalog>,
            [<Out; MaybeNullWhen(true)>] error: byref<RegistryError>
        ) : bool =
        match ExtensionCatalog.Build(host, entries) with
        | Ok built ->
            catalog <- built
            error <- Unchecked.defaultof<_>
            true
        | Error refused ->
            catalog <- Unchecked.defaultof<_>
            error <- refused
            false

    /// <summary>The host and the number of entries.</summary>
    override _.ToString() = $"{host}: {entries.Length} extensions"

/// <summary>F# functions for <c>ExtensionCatalog</c>.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ExtensionCatalog =
    /// <summary>The entries for the host, or the ids that are registered more than once.</summary>
    let create (host: HostIdentity) (entries: ExtensionEntry seq) =
        ExtensionCatalog.Build(host, entries)
