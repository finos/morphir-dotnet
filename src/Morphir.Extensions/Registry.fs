namespace Morphir.Extensions

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.IR
open Morphir.IR.Model

/// <summary>A Morphir host. C# builds one with the generated properties <c>MorphirHost.Native</c> and <c>MorphirHost.DotnetTool</c>.</summary>
[<RequireQualifiedAccess>]
type MorphirHost =
    /// <summary>
    /// The command <c>morphir</c>: the AOT-compatible executable and the managed one. The wire value names the command,
    /// not the deployment offering.
    /// </summary>
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
        /// <summary>The kind of host.</summary>
        Host: MorphirHost
        /// <summary>The release version.</summary>
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

    /// <summary>For C# callers: availability in one host only.</summary>
    static member OnlyIn(host: MorphirHost) = Only host

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

    /// <summary>The same type as the MEP wire type.</summary>
    member this.ToMep() : Morphir.Mep.ExtensionType =
        match this with
        | Frontend -> Morphir.Mep.ExtensionType.Frontend
        | Backend -> Morphir.Mep.ExtensionType.Backend
        | Transform -> Morphir.Mep.ExtensionType.Transform
        | Validator -> Morphir.Mep.ExtensionType.Validator
        | Workspace -> Morphir.Mep.ExtensionType.Workspace

    /// <summary>The MEP wire value.</summary>
    override this.ToString() = this.WireValue

/// <summary>What a backend generates: at least one target and at least one IR version, in their declared spelling.</summary>
type BackendClaim =
    {
        /// <summary>The target ids, for example <c>csharp</c>.</summary>
        Targets: NonEmptySet<TargetId>
        /// <summary>The IR versions, in their declared spelling.</summary>
        IrVersions: NonEmptySet<SourceFormatVersion>
    }

    /// <summary>The targets and IR versions.</summary>
    override this.ToString() =
        $"targets {this.Targets}; IR versions {this.IrVersions}"

/// <summary>
/// What a validator reads: at least one IR format version, in the spelling of the model readers (<c>3</c>,
/// <c>4.0.0</c>). This is a morphir-dotnet report member; MEP has no validator capability yet.
/// </summary>
/// <remarks>F# callers use <c>ValidatorClaim.ofSeq</c>; C# callers use <c>Create</c> or <c>TryCreate</c>.</remarks>
type ValidatorClaim =
    {
        /// <summary>The IR format versions that the validator reads. Never an integer: SemVer pre-releases are allowed.</summary>
        IrVersions: NonEmptySet<Versioning.FormatVersion>
    }

    /// <summary>The IR versions as the model readers write them, in order.</summary>
    member this.IrVersionTexts: IReadOnlyList<string> = [|
        for version in this.IrVersions -> Versioning.version version
    |]

    /// <summary>True when the validator reads models of this format version.</summary>
    member this.Reads(version: Versioning.FormatVersion) = this.IrVersions.Contains version

    /// <summary>For C# callers and literals: a claim of the first version and the other versions.</summary>
    static member Create
        (
            first: Versioning.FormatVersion,
            [<ParamArray>] rest: Versioning.FormatVersion array
        ) : ValidatorClaim =
        { IrVersions = NonEmptySet.create first rest }

    /// <summary>For C# callers: a claim of the versions, or false when there is no version.</summary>
    static member TryCreate
        (
            versions: IEnumerable<Versioning.FormatVersion>,
            [<Out; MaybeNullWhen(false)>] claim: byref<ValidatorClaim>
        ) : bool =
        match NonEmptySet.ofSeq "IR versions" versions with
        | Ok set ->
            claim <- { IrVersions = set }
            true
        | Error _ ->
            claim <- Unchecked.defaultof<_>
            false

    /// <summary>
    /// Parses untrusted IR version texts, in the spelling of the model readers. For F# callers. Fails with the first
    /// text that is not a format version, or with <c>ParseFailure.Empty</c> when there is no text.
    /// </summary>
    static member Parse(texts: (string | null) seq) : Result<ValidatorClaim, ParseFailure> =
        let parse (text: string | null) =
            match text with
            | null -> Error(ParseFailure.Empty "IR version")
            | text ->
                Versioning.parse text
                |> Result.mapError (fun error -> ParseFailure.InvalidVersion("IR version", Text.versionError error))

        let parsed = texts |> Seq.map parse |> List.ofSeq

        match parsed |> List.tryPick (function Error failure -> Some failure | Ok _ -> None) with
        | Some failure -> Error failure
        | None ->
            NonEmptySet.ofSeq "IR versions" (parsed |> List.choose (function Ok version -> Some version | Error _ -> None))
            |> Result.map (fun set -> { IrVersions = set })

    /// <summary>Parses untrusted IR version texts. For C# callers.</summary>
    static member TryParse
        (
            texts: IEnumerable<string | null>,
            [<Out; MaybeNullWhen(false)>] claim: byref<ValidatorClaim>,
            [<Out; MaybeNullWhen(true)>] failure: byref<ParseFailure>
        ) : bool =
        match ValidatorClaim.Parse texts with
        | Ok parsed ->
            claim <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Error refused ->
            claim <- Unchecked.defaultof<_>
            failure <- refused
            false

    /// <summary>The IR versions.</summary>
    override this.ToString() =
        String.Join(", ", this.IrVersionTexts)

/// <summary>F# functions for <c>ValidatorClaim</c>.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ValidatorClaim =
    /// <summary>A claim of the versions, or <c>ParseFailure.Empty</c> when there is no version.</summary>
    let ofSeq (versions: Versioning.FormatVersion seq) : Result<ValidatorClaim, ParseFailure> =
        NonEmptySet.ofSeq "IR versions" versions
        |> Result.map (fun set -> { IrVersions = set })

/// <summary>A capability that an extension claims. Each case fixes the extension type it implies.</summary>
[<RequireQualifiedAccess>]
type ExtensionCapability =
    /// <summary>A backend that generates artifacts.</summary>
    | Backend of BackendClaim
    /// <summary>A validator that reads a model and reports diagnostics.</summary>
    | Validator of ValidatorClaim

    /// <summary>The extension type that this capability implies.</summary>
    member this.Type =
        match this with
        | Backend _ -> ExtensionType.Backend
        | Validator _ -> ExtensionType.Validator

    /// <summary>
    /// The short text for a table cell: the type, and for a validator the IR versions it reads, for example
    /// <c>validator (1, 2, 3)</c>.
    /// </summary>
    member this.Summary =
        match this with
        | Backend _ -> ExtensionType.Backend.WireValue
        | Validator claim -> $"{ExtensionType.Validator.WireValue} ({claim})"

    /// <summary>The capability and its claim.</summary>
    override this.ToString() =
        match this with
        | Backend claim -> $"backend ({claim})"
        | Validator claim -> $"validator ({claim})"

/// <summary>Who an extension is, in the shape of the MEP <c>ExtensionInfo</c> record.</summary>
type ExtensionIdentity =
    {
        /// <summary>The extension id.</summary>
        Id: ExtensionId
        /// <summary>The display name.</summary>
        Name: ExtensionName
        /// <summary>The release version.</summary>
        Version: ReleaseVersion
        /// <summary>The optional description. C# callers use <c>DescriptionOrNull</c>.</summary>
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
        /// <summary>Who the extension is.</summary>
        Identity: ExtensionIdentity
        /// <summary>How the extension is delivered.</summary>
        Delivery: ExtensionDelivery
        /// <summary>The hosts that the extension runs in.</summary>
        Hosts: HostAvailability
        /// <summary>The capabilities that the extension claims; at least one.</summary>
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
            | ExtensionCapability.Validator _ -> None
        )
        |> Option.toObj

    /// <summary>For C# callers: the validator claim, or null when the extension is not a validator.</summary>
    member this.ValidatorClaimOrNull =
        this.Capabilities
        |> Seq.tryPick (
            function
            | ExtensionCapability.Validator claim -> Some claim
            | ExtensionCapability.Backend _ -> None
        )
        |> Option.toObj

    /// <summary>The capability summaries for a table cell, for example <c>validator (1, 2, 3)</c>.</summary>
    member this.CapabilitySummary =
        String.Join(", ", this.Capabilities |> Seq.map (fun capability -> capability.Summary))

    /// <summary>
    /// The MEP <c>ExtensionInfo</c> of the entry: the same id, name, version, description and types. An entry with no
    /// description has no <c>description</c> member. The registry has no author, home page, license or SDK version.
    /// </summary>
    member this.ToMep() : Morphir.Mep.ExtensionInfo = {
        Id = this.Identity.Id.Value
        Name = this.Identity.Name.Value
        Version = this.Identity.Version.ToString()
        Description =
            this.Identity.Description
            |> Option.map (fun description -> description.Value)
        Types = [|
            for kind in this.Types -> kind.ToMep()
        |]
        Author = None
        Homepage = None
        License = None
        MinSdkVersion = None
    }

    /// <summary>
    /// The MEP capability claims of the entry, with only the claim kinds that the pinned contract defines. A backend
    /// claim maps to <c>backend</c>. A validator claim stays out: MEP 0.2.0-draft.1 has no validator capability.
    /// </summary>
    member this.ToMepCapabilities() : Morphir.Mep.ExtensionCapabilities =
        let backend =
            this.Capabilities
            |> Seq.tryPick (
                function
                | ExtensionCapability.Backend claim ->
                    Some(
                        {
                            Targets = [|
                                for target in claim.Targets -> target.Value
                            |]
                            IrVersions = [|
                                for version in claim.IrVersions -> version.Text
                            |]
                            // Generation is the only backend operation in MEP, so a backend claim always generates.
                            Generate = true
                        }
                        : Morphir.Mep.BackendCapability
                    )
                | ExtensionCapability.Validator _ -> None
            )

        {
            Frontend = None
            Backend = backend
            Workspace = None
            ModelTransport = None
            Streaming = false
            Incremental = false
            Cancellation = false
            Progress = false
            Extra = Dictionary<string, System.Text.Json.JsonElement>()
        }

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

    /// <summary>A built-in validator that runs in both hosts. Its type comes from the claim.</summary>
    static member BuiltInValidator
        (
            identity: ExtensionIdentity,
            claim: ValidatorClaim
        ) : ExtensionEntry =
        {
            Identity = identity
            Delivery = ExtensionDelivery.BuiltIn
            Hosts = HostAvailability.AllHosts
            Capabilities = NonEmptySet.create (ExtensionCapability.Validator claim) []
        }

    /// <summary>The identity, delivery and hosts.</summary>
    override this.ToString() =
        $"{this.Identity} ({this.Delivery}; {this.Hosts})"

/// <summary>The stable code of a registry diagnostic.</summary>
[<RequireQualifiedAccess>]
type DiagnosticCode =
    /// <summary>An extension id is registered more than once.</summary>
    | DuplicateExtensionId

    /// <summary>The code that the CLI writes.</summary>
    member this.WireValue =
        match this with
        | DuplicateExtensionId -> "extension.duplicate_id"

    /// <summary>The code that the CLI writes.</summary>
    override this.ToString() = this.WireValue

/// <summary>A diagnostic for the output edge: a stable code and a message.</summary>
type Diagnostic =
    {
        /// <summary>The stable code.</summary>
        Code: DiagnosticCode
        /// <summary>The message for people.</summary>
        Message: string
    }

    /// <summary>The code and message.</summary>
    override this.ToString() =
        $"{this.Code.WireValue}: {this.Message}"

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
                    Code = DiagnosticCode.DuplicateExtensionId
                    Message = $"Extension id '{id}' is registered more than once."
                }
          |]

    /// <summary>The diagnostics.</summary>
    override this.ToString() =
        String.Join(
            "; ",
            this.Diagnostics
            |> Seq.map (fun diagnostic -> $"{diagnostic.Code.WireValue}: {diagnostic.Message}")
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
