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

/// <summary>
/// The host that runs this process (HOST-1): the command, the release version, the offerings that this entry point can
/// be published as, the release profile that built it, and how it runs. The host string keeps its meaning as the
/// command name; the deployment is a separate value. Only <see cref="HostIdentity.Create"/> builds one.
/// </summary>
[<CustomEquality; NoComparison>]
type HostIdentity =
    private
        {
            host: MorphirHost
            version: ReleaseVersion
            entryPoint: NonEmptySet<Deployment>
            profile: BuildProfile
            execution: ExecutionMode
        }

    /// <summary>
    /// The identity of an entry point. <paramref name="entryPoint"/> holds the offerings that the entry point can be
    /// published as. Throws <see cref="ArgumentException"/> when <paramref name="profile"/> names another offering:
    /// the build refuses that combination too.
    /// </summary>
    static member Create
        (
            host: MorphirHost,
            version: ReleaseVersion,
            entryPoint: NonEmptySet<Deployment>,
            profile: BuildProfile,
            execution: ExecutionMode
        ) : HostIdentity =
        match profile.Deployment with
        | Some deployment when not (entryPoint.Contains deployment) ->
            let allowed = String.Join(", ", entryPoint |> Seq.map (fun candidate -> candidate.WireValue))
            invalidArg (nameof profile) $"This entry point is published as {allowed}, not as {deployment}."
        | _ ->
            {
                host = host
                version = version
                entryPoint = entryPoint
                profile = profile
                execution = execution
            }

    /// <summary>The command that runs: <c>morphir</c> or <c>dotnet-morphir</c>.</summary>
    member this.Host = this.host

    /// <summary>The release version.</summary>
    member this.Version = this.version

    /// <summary>The offerings that this entry point can be published as.</summary>
    member this.EntryPoint = this.entryPoint

    /// <summary>The release profile that built the executable, or no profile.</summary>
    member this.Profile = this.profile

    /// <summary>How this process runs.</summary>
    member this.Execution = this.execution

    /// <summary>The deployment that the build states, or none when no named profile built it. C# uses <see cref="DeploymentOrNull"/>.</summary>
    member internal this.Deployment = this.profile.Deployment

    /// <summary>For C# callers: the deployment, or null when no named profile built the executable.</summary>
    member this.DeploymentOrNull: Deployment | null = Option.toObj this.profile.Deployment

    /// <summary>For C# callers: the platform the profile built for, or null for a portable artifact or no profile.</summary>
    member this.PlatformOrNull: Platform | null = Option.toObj this.profile.Platform

    /// <summary>
    /// The deployments that availability is judged against: the profile's deployment, or without a profile every
    /// offering that the entry point can be published as.
    /// </summary>
    member this.CandidateDeployments: IReadOnlyList<Deployment> =
        match this.profile.Deployment with
        | Some deployment -> [| deployment |]
        | None -> [| for deployment in Deployment.All do if this.entryPoint.Contains deployment then deployment |]

    /// <summary>
    /// False when the build claims a deployment that this process contradicts: a <c>native-aot</c> claim in a process
    /// that supports dynamic code, or a managed claim in native code. True when there is no claim.
    /// </summary>
    member this.ExecutionMatchesDeployment =
        match this.profile.Deployment with
        | Some deployment -> deployment.Execution = this.execution
        | None -> true

    /// <summary>For C# callers: the prerequisites of the running offering, or null when no named profile built it.</summary>
    member this.PrerequisitesOrNull: Prerequisites | null =
        this.profile.Deployment |> Option.map (fun deployment -> deployment.Prerequisites) |> Option.toObj

    override this.Equals(other: obj | null) =
        match other with
        | :? HostIdentity as identity ->
            this.host = identity.host
            && this.version = identity.version
            && this.entryPoint = identity.entryPoint
            && this.profile = identity.profile
            && this.execution = identity.execution
        | _ -> false

    override this.GetHashCode() =
        HashCode.Combine(this.host, this.version, this.profile, this.execution)

    /// <summary>The host, version and profile.</summary>
    override this.ToString() =
        $"{this.host.WireValue} {this.version} ({this.profile}; {this.execution})"

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

/// <summary>Why the WASM runtime of a host is unavailable: the three reasons of the runtime report. Nothing turns the runtime off, so no reason says it is.</summary>
[<RequireQualifiedAccess>]
type WasmRuntimeUnavailableReason =
    /// <summary>The engine has no build for this platform.</summary>
    | PlatformUnsupported
    /// <summary>The host was built or started without its engine library.</summary>
    | EngineMissing
    /// <summary>The engine library is present but did not load.</summary>
    | EngineLoadFailed

    /// <summary>The text that the runtime report writes.</summary>
    member this.WireValue =
        match this with
        | PlatformUnsupported -> "platform_unsupported"
        | EngineMissing -> "engine_missing"
        | EngineLoadFailed -> "engine_load_failed"

    /// <summary>What the reason means, as a clause the runtime report and its diagnostics write after the wire value.</summary>
    member this.Description =
        match this with
        | PlatformUnsupported -> "the engine has no build for this platform"
        | EngineMissing -> "the engine's native library is not in this installation"
        | EngineLoadFailed -> "the engine's native library is present but did not load"

    /// <summary>The common registry reason; the runtime report keeps the separate wire code.</summary>
    member internal this.CommonReason =
        UnavailableReason.Create $"The WASM runtime is unavailable: {this.Description}."

    /// <summary>The text that the runtime report writes.</summary>
    override this.ToString() = this.WireValue

/// <summary>
/// Whether the WASM runtime of this host can run a module. The caller passes the value of the runtime report of the
/// same executable, so that the registry and the report cannot disagree. C# reads <c>CanRunModules</c>, builds the
/// unavailable case with <c>UnavailableBecause</c> and branches with <c>Match</c>.
/// </summary>
[<RequireQualifiedAccess>]
type WasmRuntimeAvailability =
    /// <summary>The runtime can run a module.</summary>
    | Available
    /// <summary>The runtime cannot run a module, for this reason.</summary>
    | Unavailable of Reason: WasmRuntimeUnavailableReason

    /// <summary>For C# callers: an unavailable runtime with its reason.</summary>
    static member UnavailableBecause(reason: WasmRuntimeUnavailableReason) = Unavailable reason

    /// <summary>True when the runtime can run a module.</summary>
    member this.CanRunModules =
        match this with
        | Available -> true
        | Unavailable _ -> false

    /// <summary>For C# code that must branch: one function for each case.</summary>
    member this.Match<'R>(available: Func<'R>, unavailable: Func<WasmRuntimeUnavailableReason, 'R>) : 'R =
        match this with
        | Available -> available.Invoke()
        | Unavailable reason -> unavailable.Invoke reason

    /// <summary><c>available</c>, or <c>unavailable</c> with the reason.</summary>
    override this.ToString() =
        match this with
        | Available -> "available"
        | Unavailable reason -> $"unavailable ({reason.WireValue})"

/// <summary>An extension's availability, with a common validated reason whenever it is unavailable.</summary>
[<RequireQualifiedAccess>]
type ExtensionAvailability =
    /// <summary>The extension can run in the host.</summary>
    | Available
    /// <summary>The extension cannot run in the host, for this reason.</summary>
    | Unavailable of Reason: UnavailableReason

    /// <summary>True when the extension can run.</summary>
    member this.CanRun =
        match this with
        | Available -> true
        | Unavailable _ -> false

    /// <summary>The common reason, or none when available. F# callers use this; C# reads <see cref="ReasonOrNull"/>.</summary>
    member this.Reason =
        match this with
        | Available -> None
        | Unavailable reason -> Some reason

    /// <summary>For C# callers: the common reason, or null when available.</summary>
    member this.ReasonOrNull: UnavailableReason | null = Option.toObj this.Reason

    /// <summary>For C# code that must branch: one function for each case.</summary>
    member this.Match<'R>(available: Func<'R>, unavailable: Func<UnavailableReason, 'R>) : 'R =
        match this with
        | Available -> available.Invoke()
        | Unavailable reason -> unavailable.Invoke reason

    /// <summary>The availability and, when unavailable, its reason.</summary>
    override this.ToString() =
        match this with
        | Available -> "available"
        | Unavailable reason -> $"unavailable ({reason.Value})"

/// <summary>How an extension is delivered. A process delivery comes in a later step.</summary>
[<RequireQualifiedAccess>]
type ExtensionDelivery =
    /// <summary>Compiled into the host.</summary>
    | BuiltIn
    /// <summary>A WASM module that the host's WASM runtime runs.</summary>
    | Wasm

    /// <summary>The text that the CLI writes.</summary>
    member this.WireValue =
        match this with
        | BuiltIn -> "built-in"
        | Wasm -> "wasm"

    /// <summary>True when an extension of this delivery can run in a host with this WASM runtime.</summary>
    member this.CanRunWith(wasmRuntime: WasmRuntimeAvailability) =
        match this with
        | BuiltIn -> true
        | Wasm -> wasmRuntime.CanRunModules

    /// <summary>For C# code that must branch: one function for each case.</summary>
    member this.Match<'R>(builtIn: Func<'R>, wasm: Func<'R>) : 'R =
        match this with
        | BuiltIn -> builtIn.Invoke()
        | Wasm -> wasm.Invoke()

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
    /// <summary>A frontend that compiles source code to a model.</summary>
    | Frontend of FrontendClaim
    /// <summary>A backend that generates artifacts.</summary>
    | Backend of BackendClaim
    /// <summary>A validator that reads a model and reports diagnostics.</summary>
    | Validator of ValidatorClaim

    /// <summary>The extension type that this capability implies.</summary>
    member this.Type =
        match this with
        | Frontend _ -> ExtensionType.Frontend
        | Backend _ -> ExtensionType.Backend
        | Validator _ -> ExtensionType.Validator

    /// <summary>
    /// The short text for a table cell: the type, and for a validator the IR versions it reads, for example
    /// <c>validator (1, 2, 3)</c>.
    /// </summary>
    member this.Summary =
        match this with
        | Frontend claim ->
            let languages = String.Join(", ", claim.Languages |> Seq.map (fun language -> language.Language.Value))
            $"{ExtensionType.Frontend.WireValue} ({languages})"
        | Backend _ -> ExtensionType.Backend.WireValue
        | Validator claim -> $"{ExtensionType.Validator.WireValue} ({claim})"

    /// <summary>The capability and its claim.</summary>
    override this.ToString() =
        match this with
        | Frontend claim -> $"frontend ({claim})"
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
        /// <summary>The hosts that the extension runs in: command names, never the deployment.</summary>
        Hosts: HostAvailability
        /// <summary>The offerings of the release that provide the extension.</summary>
        Deployments: EntryDeployments
        /// <summary>The capabilities that the extension claims; at least one.</summary>
        Capabilities: NonEmptySet<ExtensionCapability>
    }

    /// <summary>The MEP extension types, derived from the capabilities. They cannot disagree with the claims.</summary>
    member this.Types =
        this.Capabilities
        |> NonEmptySet.map (fun capability -> capability.Type)

    /// <summary>
    /// Static host and deployment eligibility. This overload does not inspect the WASM runtime.
    /// </summary>
    member this.AvailabilityIn(host: HostIdentity) : ExtensionAvailability =
        if not (this.Hosts.IsAvailableIn host.Host) then
            ExtensionAvailability.Unavailable(UnavailableReason.Create $"{this.Identity.Id} runs only in {this.Hosts}.")
        else
            match this.Deployments with
            | EntryDeployments.Every -> ExtensionAvailability.Available
            | EntryDeployments.Only(deployments, reason) ->
                if host.CandidateDeployments |> Seq.forall deployments.Contains then
                    ExtensionAvailability.Available
                else
                    ExtensionAvailability.Unavailable reason

    /// <summary>
    /// The combined availability. Static refusals take precedence; only eligible WASM entries ask the lazy runtime.
    /// For C# callers, the outcome supplies both <c>CanRun</c> and the common <c>ReasonOrNull</c>.
    /// </summary>
    member this.AvailabilityIn(host: HostIdentity, wasmRuntime: Func<WasmRuntimeAvailability>) : ExtensionAvailability =
        match this.AvailabilityIn host with
        | ExtensionAvailability.Unavailable reason -> ExtensionAvailability.Unavailable reason
        | ExtensionAvailability.Available ->
            match this.Delivery with
            | ExtensionDelivery.BuiltIn -> ExtensionAvailability.Available
            | ExtensionDelivery.Wasm ->
                match wasmRuntime.Invoke() with
                | WasmRuntimeAvailability.Available -> ExtensionAvailability.Available
                | WasmRuntimeAvailability.Unavailable reason -> ExtensionAvailability.Unavailable reason.CommonReason

    /// <summary>The combined availability with an already known runtime outcome.</summary>
    member this.AvailabilityIn(host: HostIdentity, wasmRuntime: WasmRuntimeAvailability) : ExtensionAvailability =
        this.AvailabilityIn(host, Func<WasmRuntimeAvailability>(fun () -> wasmRuntime))

    /// <summary>True when the static host and deployment declarations allow the extension. Does not probe the runtime.</summary>
    member this.IsAvailableIn(host: HostIdentity) : bool = (this.AvailabilityIn host).CanRun

    /// <summary>True when the static declarations and the WASM runtime allow the extension.</summary>
    member this.IsAvailableIn(host: HostIdentity, wasmRuntime: WasmRuntimeAvailability) : bool =
        (this.AvailabilityIn(host, wasmRuntime)).CanRun

    /// <summary>True when available. Only a statically eligible WASM entry asks the lazy runtime.</summary>
    member this.IsAvailableIn(host: HostIdentity, wasmRuntime: Func<WasmRuntimeAvailability>) : bool =
        (this.AvailabilityIn(host, wasmRuntime)).CanRun

    /// <summary>The static refusal, or none when the host and deployment declarations allow the extension.</summary>
    member this.ReasonIn(host: HostIdentity) : UnavailableReason option = (this.AvailabilityIn host).Reason

    /// <summary>The common reason from the combined availability, or none when available.</summary>
    member this.ReasonIn(host: HostIdentity, wasmRuntime: WasmRuntimeAvailability) : UnavailableReason option =
        (this.AvailabilityIn(host, wasmRuntime)).Reason

    /// <summary>The common reason. Only a statically eligible WASM entry asks the lazy runtime.</summary>
    member this.ReasonIn(host: HostIdentity, wasmRuntime: Func<WasmRuntimeAvailability>) : UnavailableReason option =
        (this.AvailabilityIn(host, wasmRuntime)).Reason

    /// <summary>For C# callers: the static refusal, or null when statically eligible.</summary>
    member this.ReasonOrNullIn(host: HostIdentity) : UnavailableReason | null = Option.toObj (this.ReasonIn host)

    /// <summary>For C# callers: the common reason from the combined availability, or null when available.</summary>
    member this.ReasonOrNullIn(host: HostIdentity, wasmRuntime: WasmRuntimeAvailability) : UnavailableReason | null =
        Option.toObj (this.ReasonIn(host, wasmRuntime))

    /// <summary>For C# callers: the common reason, probing only for a statically eligible WASM entry.</summary>
    member this.ReasonOrNullIn(host: HostIdentity, wasmRuntime: Func<WasmRuntimeAvailability>) : UnavailableReason | null =
        Option.toObj (this.ReasonIn(host, wasmRuntime))

    /// <summary>For C# callers: the frontend claim, or null when the extension is not a frontend.</summary>
    member this.FrontendClaimOrNull =
        this.Capabilities
        |> Seq.tryPick (
            function
            | ExtensionCapability.Frontend claim -> Some claim
            | ExtensionCapability.Backend _
            | ExtensionCapability.Validator _ -> None
        )
        |> Option.toObj

    /// <summary>For C# callers: the backend claim, or null when the extension is not a backend.</summary>
    member this.BackendClaimOrNull =
        this.Capabilities
        |> Seq.tryPick (
            function
            | ExtensionCapability.Backend claim -> Some claim
            | ExtensionCapability.Frontend _
            | ExtensionCapability.Validator _ -> None
        )
        |> Option.toObj

    /// <summary>For C# callers: the validator claim, or null when the extension is not a validator.</summary>
    member this.ValidatorClaimOrNull =
        this.Capabilities
        |> Seq.tryPick (
            function
            | ExtensionCapability.Validator claim -> Some claim
            | ExtensionCapability.Frontend _
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
    /// claim maps to <c>backend</c>. A validator claim stays out: MEP 0.2.0-draft.1 has no validator capability. A
    /// built-in frontend claim stays out too: an in-process frontend is not offered to a MEP peer.
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
                | ExtensionCapability.Frontend _
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

    /// <summary>For C# callers, which cannot use <c>with</c> on an F# record: the same entry with another delivery.</summary>
    member this.WithDelivery(delivery: ExtensionDelivery) = { this with Delivery = delivery }

    /// <summary>For C# callers: the same entry with other deployments.</summary>
    member this.WithDeployments(deployments: EntryDeployments) = { this with Deployments = deployments }

    /// <summary>
    /// A built-in in-process frontend that runs in both hosts, in the given offerings. Its type comes from the claim.
    /// </summary>
    static member BuiltInFrontend
        (
            identity: ExtensionIdentity,
            claim: FrontendClaim,
            deployments: EntryDeployments
        ) : ExtensionEntry =
        {
            Identity = identity
            Delivery = ExtensionDelivery.BuiltIn
            Hosts = HostAvailability.AllHosts
            Deployments = deployments
            Capabilities = NonEmptySet.create (ExtensionCapability.Frontend claim) []
        }

    /// <summary>A built-in backend that runs in both hosts and every offering.</summary>
    static member BuiltInBackend
        (
            identity: ExtensionIdentity,
            claim: BackendClaim
        ) : ExtensionEntry =
        {
            Identity = identity
            Delivery = ExtensionDelivery.BuiltIn
            Hosts = HostAvailability.AllHosts
            Deployments = EntryDeployments.Every
            Capabilities = NonEmptySet.create (ExtensionCapability.Backend claim) []
        }

    /// <summary>A built-in validator that runs in both hosts and every offering. Its type comes from the claim.</summary>
    static member BuiltInValidator
        (
            identity: ExtensionIdentity,
            claim: ValidatorClaim
        ) : ExtensionEntry =
        {
            Identity = identity
            Delivery = ExtensionDelivery.BuiltIn
            Hosts = HostAvailability.AllHosts
            Deployments = EntryDeployments.Every
            Capabilities = NonEmptySet.create (ExtensionCapability.Validator claim) []
        }

    /// <summary>The identity, delivery, hosts and deployments.</summary>
    override this.ToString() =
        $"{this.Identity} ({this.Delivery}; {this.Hosts}; {this.Deployments})"

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
