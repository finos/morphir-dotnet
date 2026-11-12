// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.GuestKit

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization.Metadata
open System.Threading
open System.Threading.Tasks
open Morphir.Mep
open Morphir.Conformance.WasmHost

/// The describe and model cases of the adopted profile. Legacy remains a compatibility value and is never planned.
[<RequireQualifiedAccess>]
type KitTier =
    /// The describe case, whose claim selects the required model cases.
    | Describe
    /// A case of the model tier, on MEP 0.2.0-draft.1.
    | Model
    /// A case of the legacy tier, on MEP 0.1; this host never runs one.
    | Legacy

    /// The wire value: describe, model or legacy.
    member this.WireValue =
        match this with
        | Describe -> "describe"
        | Model -> "model"
        | Legacy -> "legacy"

    /// The wire value.
    override this.ToString() = this.WireValue

/// What happened to one step of the kit.
[<RequireQualifiedAccess>]
type StepOutcome =
    /// The step ran and its observation met the kit's checks.
    | Passed
    /// The step ran and an observation did not meet a check; Difference says which and quotes the values.
    | Failed of Difference: string
    /// The step did not run; Reason says why.
    | NotRun of Reason: string

    /// The wire value: passed, failed or not-run.
    member this.WireValue =
        match this with
        | Passed -> "passed"
        | Failed _ -> "failed"
        | NotRun _ -> "not-run"

    /// For C# callers: one function for each case.
    member this.Match<'R>(passed: Func<'R>, failed: Func<string, 'R>, notRun: Func<string, 'R>) : 'R =
        match this with
        | Passed -> passed.Invoke()
        | Failed difference -> failed.Invoke difference
        | NotRun reason -> notRun.Invoke reason

    /// Stable text for diagnostics: the wire value, then the difference or the reason, such as not-run: <reason>.
    override this.ToString() =
        match this with
        | Passed -> this.WireValue
        | Failed difference -> $"{this.WireValue}: {difference}"
        | NotRun reason -> $"{this.WireValue}: {reason}"

/// One step of the kit: a case of its plan, what was sent and returned, and its outcome. Only the runner builds one.
[<Sealed>]
type KitStep
    internal
    (
        id: string,
        tier: KitTier,
        method: string,
        requests: int,
        notifications: int,
        request: string | null,
        response: string | null,
        outcome: StepOutcome,
        elapsed: TimeSpan,
        observation: ConformanceOperationObservation option
    ) =
    /// The kit's case id, such as compile-model-vthree.
    member _.Id = id

    /// The tier of the case.
    member _.Tier = tier

    /// The MEP method of the case.
    member _.Method = method

    /// Requests with completed channel submission. Zero does not prove absence of partial I/O.
    member _.Requests = requests

    /// How many notifications the step passed to the session; the channel decides whether one reaches the guest.
    member _.Notifications = notifications

    /// The params the step sent, as compact JSON, or null when the step built none itself.
    member _.Request = request

    /// The result or error the step received, as compact JSON, or null when it received none.
    member _.Response = response

    /// The outcome.
    member _.Outcome = outcome

    /// How long the step took.
    member _.Elapsed = elapsed

    /// The operation-owned wire observation, retained internally without defining a report carrier.
    member internal _.Observation = observation

    member internal _.WithObservation(observed: ConformanceOperationObservation) =
        KitStep(id, tier, method, requests, notifications, request, response, outcome, elapsed, Some observed)

    /// Stable text for diagnostics.
    override _.ToString() = $"{id} ({tier}): {outcome}"

/// What a session is for: describe only, or the model tier.
[<RequireQualifiedAccess>]
type KitSessionPurpose =
    /// A session that only asks for morphir.extension.describe and exits. It offers no model transport.
    | Describe
    /// The model tier's session, on MEP 0.2.0-draft.1 with the kit's model-transport offer.
    | ModelTier

    /// The wire value: describe or model-tier.
    member this.WireValue =
        match this with
        | Describe -> "describe"
        | ModelTier -> "model-tier"

    /// The wire value.
    override this.ToString() = this.WireValue

/// What the runner asks of its caller when it needs a new session: its purpose and the model-transport support the
/// host must offer in it.
[<Sealed>]
type KitSessionRequest internal (purpose: KitSessionPurpose, offer: ModelTransportSupport option, lifetime: CancellationToken option) =
    let mutable terminal: (CancellationToken -> Task<Result<unit, string>>) option = None

    internal new(purpose, offer) = KitSessionRequest(purpose, offer, None)

    member internal _.ConformanceLifetime = lifetime

    member internal _.AttachTerminalObserver(observer) =
        if terminal.IsSome then invalidOp "The session already has a process owner."
        terminal <- Some observer

    member internal _.SettleAsync(cancellation) =
        match terminal with
        | Some observer -> observer cancellation
        | None -> Task.FromResult(Error "Actual process exit and stdout EOF have no process-owner observation.")

    /// The existing test/adapter factory selects the opted-in session without exposing MEP's internal API.
    member internal _.CreateSession(channel: ISingleMessageChannel, host: PeerInfo) =
        match lifetime with
        | Some token -> HostSession.CreateConformance(channel, host, ConformanceEvidenceLimits.Create(512, 268435456L), token)
        | None -> HostSession(channel, host, Option.toObj offer)
    /// What the session is for.
    member _.Purpose = purpose

    /// The model transport the session offers, for F# callers.
    member internal _.Offer = offer

    /// The model-transport support the HostSession must offer, or null for none. Pass it to the HostSession
    /// constructor.
    member _.OfferOrNull() : ModelTransportSupport | null = Option.toObj offer

    /// Stable text for diagnostics.
    override _.ToString() =
        match offer with
        | Some support -> $"{purpose} session offering {support}"
        | None -> $"{purpose} session offering no model transport"

/// Transitional execution results, without a qualification verdict or an authoritative generic report carrier.
/// Only ModelTierRunner builds one. Required NotRun cases prevent a complete generic run.
[<Sealed>]
type ModelTierRun
    internal
    (
        kitSha256: string,
        guest: string | null,
        language: string | null,
        irVersions: IReadOnlyList<string>,
        modelIrVersions: IReadOnlyList<string>,
        modelTierReason: string | null,
        steps: IReadOnlyList<KitStep>,
        legacyTier: IReadOnlyList<KitStep>
    ) =
    /// The kit profile: generic-json-library-v1, the one profile the kit reader reads.
    member _.KitProfile = KitReader.Profile

    /// The SHA-256 of the kit file.
    member _.KitSha256 = kitSha256

    /// The guest as its describe result names it, id and version, or null when describe gave no identity.
    member _.Guest = guest

    /// The kit language the plan selected, or null when none was selected.
    member _.Language = language

    /// The usable JSON IR versions selected by the frontend/backend claims and complete model tuple pairs.
    member _.IrVersions = irVersions

    /// The IR versions of the model tier; empty when the plan has no model tier.
    member _.ModelIrVersions = modelIrVersions

    /// Why the plan has no model tier, or null when it has one.
    member _.ModelTierReason = modelTierReason

    /// The describe step and the model tier's steps, in the order they ran.
    member _.Steps = steps

    /// Compatibility slot, empty for the adopted profile.
    member _.LegacyTier = legacyTier

    /// Why the compatibility legacy slot is empty.
    member _.LegacyTierReason = "The adopted generic profile has no legacy tier."

    /// Always true: this runner runs the model tier only, so a result is never a complete run of the kit.
    member _.ModelTierOnly = true

    /// The step with this id, or null.
    member _.Step(id: string) : KitStep | null =
        steps
        |> Seq.tryFind (fun step -> step.Id = id)
        |> Option.toObj

    /// For C# callers that know the step exists: the step with this id. Throws an ArgumentException that names the id
    /// when the run has no such step.
    member this.RequiredStep(id: string) : KitStep =
        match this.Step id with
        | null -> raise (ArgumentException($"The run has no step {id}.", nameof id))
        | step -> step

    /// A one-line summary. It counts the steps of describe and the model tier and says that the run is not a
    /// complete run of the kit.
    override this.ToString() =
        let guest =
            match this.Guest with
            | null -> "an unidentified guest"
            | guest -> guest

        let count wire =
            this.Steps
            |> Seq.filter (fun step -> step.Outcome.WireValue = wire)
            |> Seq.length

        let passed, failed, notRun = count "passed", count "failed", count "not-run"

        $"Generic guest kit {this.KitProfile} on {guest}, model tier only: {passed} passed, {failed} failed and {notRun} not run of {this.Steps.Count} steps; the legacy tier's {this.LegacyTier.Count} cases were not run. This is not a complete run of the generic guest kit."

    /// The summary, the plan and one line for each step, with each difference and reason.
    member this.Report() =
        let line (step: KitStep) =
            let elapsed =
                step.Elapsed.TotalMilliseconds.ToString("0.0", Globalization.CultureInfo.InvariantCulture)

            $"  {step.Id.PadRight(36)} {step.Tier.WireValue.PadRight(8)} {step.Outcome.WireValue.PadRight(8)} {elapsed.PadLeft(9)} ms"
            + (match step.Outcome with
               | StepOutcome.Passed -> ""
               | StepOutcome.Failed difference -> $"{Environment.NewLine}      {difference}"
               | StepOutcome.NotRun reason -> $"{Environment.NewLine}      {reason}")

        let language =
            match this.Language with
            | null -> "none"
            | language -> language

        let modelTier =
            match this.ModelTierReason with
            | null -> String.concat ", " this.ModelIrVersions
            | reason -> $"none: {reason}"

        String.concat
            Environment.NewLine
            [ yield this.ToString()
              yield $"  kit SHA-256 {this.KitSha256}"
              yield $"""  language {language}; IR versions {String.concat ", " this.IrVersions}; model tier {modelTier}"""
              yield! this.Steps |> Seq.map line
              yield $"  legacy tier, not run: {this.LegacyTierReason}"
              yield!
                  this.LegacyTier
                  |> Seq.map (fun step -> $"  {step.Id.PadRight(36)} {step.Tier.WireValue.PadRight(8)} not-run") ]

/// JSON helpers of the runner, over the conformance project's JsonValue.
module internal KitJson =
    let str (text: string) = JsonValue.String text

    let num (value: int64) =
        JsonValue.Number(value.ToString(Globalization.CultureInfo.InvariantCulture))

    let obj (members: (string * JsonValue) list) = JsonValue.Object members
    let arr (items: JsonValue list) = JsonValue.Array items

    /// A member of an object, or None.
    let memberOf (name: string) (value: JsonValue) =
        match value with
        | JsonValue.Object members -> members |> List.tryPick (fun (key, item) -> if key = name then Some item else None)
        | _ -> None

    /// The member at a path of member names, or None.
    let at (names: string list) (value: JsonValue) =
        names |> List.fold (fun found name -> found |> Option.bind (memberOf name)) (Some value)

    /// The value with every object's members in ordinal order, at every depth: serde_json's Value equality and its
    /// BTreeMap writer, which the Rust runner uses outside the IR comparison.
    let rec sorted (value: JsonValue) =
        match value with
        | JsonValue.Object members ->
            members
            |> List.map (fun (key, item) -> key, sorted item)
            |> List.sortWith (fun (left, _) (right, _) -> String.CompareOrdinal(left, right))
            |> JsonValue.Object
        | JsonValue.Array items -> JsonValue.Array(List.map sorted items)
        | other -> other

    /// JSON value equality with member order ignored.
    let same (left: JsonValue) (right: JsonValue) = sorted left = sorted right

    let private options = JsonDocumentOptions(MaxDepth = 256)

    /// The value as a JsonElement.
    let toElement (value: JsonValue) =
        use document = JsonDocument.Parse(Json.compact value, options)
        document.RootElement.Clone()

    /// A JsonElement as a value.
    let ofElement (element: JsonElement) = Json.ofElement "" element

    /// Strict JSON text: valid UTF-8, no repeated member.
    let parseStrict (bytes: byte array) : Result<JsonValue, string> =
        try
            let text = UTF8Encoding(false, true).GetString bytes
            use document = JsonDocument.Parse(text, options)
            Json.ofElement "" document.RootElement
        with
        | :? DecoderFallbackException -> Error "the payload is not UTF-8"
        | :? JsonException as error -> Error $"the payload is not JSON: {error.Message}"

    /// A JsonValue read as a MEP type, or None when it does not match the type.
    let toTyped<'T when 'T: not null> (info: JsonTypeInfo<'T>) (value: JsonValue) : 'T option =
        try
            match JsonSerializer.Deserialize(toElement value, info :> JsonTypeInfo) with
            | :? 'T as typed -> Some typed
            | _ -> None
        with :? JsonException ->
            None

/// The usable JSON versions selected by approved adoption SEL-1, with one version set in both projections.
type internal Selection =
    {
        Language: KitLanguage
        Extensions: string list
        Versions: IrVersion list
        ModelVersions: IrVersion list
    }

/// Selection and required plan data follow approved adoption SEL-1/TIER-1. Request/model comparisons retain the
/// pinned baseline behavior; the executor and public legacy result projection are still transitional.
module internal ModelTierPlan =
    open KitJson

    [<Literal>]
    let ModelProtocol = "0.2.0-draft.1"

    [<Literal>]
    let Compile = "morphir.frontend.compileModel"

    [<Literal>]
    let Generate = "morphir.backend.generateModel"

    let require condition (message: string) =
        if condition then Ok() else Error message

    let private major (version: IrVersion) =
        match version with
        | IrVersion.V3 -> "3"
        | IrVersion.V4 -> "4"

    /// The kit's case id suffix of a version.
    let suffix (version: IrVersion) =
        match version with
        | IrVersion.V3 -> "vthree"
        | IrVersion.V4 -> "vfour"

    let private claimsVersion (list: JsonValue option) (version: IrVersion) =
        match list with
        | Some(JsonValue.Array items) ->
            items
            |> List.exists (fun item -> item = str version.WireValue || item = str (major version))
        | _ -> false

    /// A JSON model-transport tuple.
    let jsonTuple (operation: string) (version: IrVersion) =
        obj
            [ "operation", str operation
              "profile", str "morphir-ir"
              "codec", str "morphir-json"
              "envelopeVersion", str "0.1.0-draft.1"
              "irVersion", str version.WireValue ]

    let private array (value: JsonValue option) =
        match value with
        | Some(JsonValue.Array items) -> items
        | _ -> []

    let private strings (value: JsonValue option) =
        array value
        |> List.choose (function
            | JsonValue.String text -> Some text
            | _ -> None)

    /// SEL-1 capability projection, also used to check initialize's selected capabilities. Protocol support is
    /// checked by checkDescribe because initialize supplies its chosen protocol separately.
    let select (kit: GuestKit) (claim: JsonValue) : Result<Selection, string> =
        let frontend = claim |> at [ "capabilities"; "frontend" ]
        let backend = claim |> at [ "capabilities"; "backend" ]
        let claimed = frontend |> Option.bind (memberOf "languages") |> array
        let targets = backend |> Option.bind (memberOf "targets") |> array

        let languageClaim (id: string) =
            claimed |> List.tryFind (fun language -> memberOf "id" language = Some(str id))

        match
            kit.LanguageSets
            |> List.tryFind (fun language ->
                (languageClaim language.Id).IsSome && List.contains (str language.Id) targets)
        with
        | None -> Error "guest claims no kit language as both frontend language and backend target"
        | Some language ->
            let extensions =
                languageClaim language.Id
                |> Option.bind (memberOf "fileExtensions")
                |> strings

            let tuples = claim |> at [ "capabilities"; "modelTransport"; "tuples" ] |> array

            let claims operation version =
                tuples |> List.exists (same (jsonTuple operation version))

            let versions =
                [ IrVersion.V3; IrVersion.V4 ]
                |> List.filter (fun version ->
                    claimsVersion (frontend |> Option.bind (memberOf "irVersions")) version
                    && claimsVersion (backend |> Option.bind (memberOf "irVersions")) version
                    && claims Compile version
                    && claims Generate version)

            if versions.IsEmpty then
                Error $"guest claims no JSON {Compile} and {Generate} tuple pair at IR 3 or 4 claimed by both frontend and backend"
            else
                Ok
                    { Language = language
                      Extensions = extensions
                      Versions = versions
                      ModelVersions = versions }

    /// Why a selection has no model tier.
    let noModelTier (claim: JsonValue) (selection: Selection) =
        let versions =
            selection.Versions |> List.map _.WireValue |> String.concat ", "

        if List.contains (str ModelProtocol) (array (memberOf "protocolVersions" claim)) then
            $"The guest claims MEP {ModelProtocol} but no JSON {Compile} and {Generate} tuple pair at a selected IR version ({versions}), so the kit plans no model tier."
        else
            $"The guest's describe result does not claim MEP {ModelProtocol}, so the kit plans no model tier."

    /// The describe checks of the kit, without its legacy-tier check that the guest claims MEP 0.1: the claims format,
    /// the identity and the selection.
    let checkDescribe (kit: GuestKit) (result: JsonValue) : Result<Selection, string> =
        let format =
            match memberOf "claimsVersion" result, memberOf "statementVersion" result with
            | Some(JsonValue.String "0.1.0-draft.2"), None -> Ok "claimsVersion"
            | None, Some(JsonValue.String "0.1.0-draft.1") -> Ok "statementVersion"
            | _ -> Error "unsupported capability claims format"

        let critical field =
            match memberOf "critical" result with
            | None -> Ok()
            | Some(JsonValue.Array paths) when paths |> List.forall (fun path -> path = str field) -> Ok()
            | Some _ -> Error "claim names a critical member this reader does not understand"

        let identity () =
            let text name =
                match result |> at [ "extension"; name ] with
                | Some(JsonValue.String text) -> Some text
                | _ -> None

            let version =
                text "version"
                |> Option.exists (fun version ->
                    match SemanticVersion.Parse version with
                    | Ok _ -> true
                    | Error _ -> false)

            require
                (text "id" |> Option.exists ((<>) "")
                 && text "name" |> Option.exists ((<>) "")
                 && version)
                "extension identity is incomplete"

        format
        |> Result.bind critical
        |> Result.bind identity
        |> Result.bind (fun () ->
            require
                (List.contains (str ModelProtocol) (array (memberOf "protocolVersions" result)))
                $"guest does not claim MEP {ModelProtocol}")
        |> Result.bind (fun () -> select kit result)

    /// The id and version the describe result gives, or None.
    let guest (result: JsonValue) =
        match result |> at [ "extension"; "id" ], result |> at [ "extension"; "version" ] with
        | Some(JsonValue.String id), Some(JsonValue.String version) -> Some $"{id} {version}"
        | _ -> None

    /// The kit's fixed limits of the model-transport offer.
    let limits =
        obj
            [ "maxEncodedBytes", num 1000000L
              "maxDecodedBytes", num 750000L
              "maxDepth", num 128L
              "maxNodes", num 100000L ]

    /// The model-transport offer of model-initialize: a compile and a generate tuple for each model-tier version.
    let offer (selection: Selection) =
        obj
            [ "tuples",
              arr
                  [ for version in selection.ModelVersions do
                        jsonTuple Compile version
                        jsonTuple Generate version ]
              "limits", limits
              "guarantees", arr [] ]

    let private sourceDocuments (language: string) (documents: (JsonValue * JsonValue) list) =
        documents
        |> List.map (fun (uri, text) ->
            obj [ "uri", uri; "languageId", str language; "version", num 1L; "text", text ])

    /// The params of a compileModel request.
    let compileParams (kit: GuestKit) (selection: Selection) (documents: (JsonValue * JsonValue) list) (version: IrVersion) =
        obj
            [ "languageId", str selection.Language.Id
              "sources", obj [ "documents", arr (sourceDocuments selection.Language.Id documents) ]
              "package", kit.Package
              "options", obj [ "typesOnly", JsonValue.Bool false; "irVersion", str version.WireValue ]
              "contextDigest", str kit.ContextDigest
              "output", jsonTuple Compile version ]

    /// The kit's documents for the selected language.
    let kitDocuments (selection: Selection) =
        selection.Language.Documents
        |> List.map (fun document -> str document.Uri, str document.Text)

    /// Whether a path ends in a claimed file extension of the language; with no extension claimed, every path does.
    let matchesExtension (selection: Selection) (path: string) =
        selection.Extensions.IsEmpty
        || selection.Extensions |> List.exists (fun extension -> path.EndsWith(extension, StringComparison.Ordinal))

    /// The documents of a round trip: each generated artifact whose path ends in a claimed file extension.
    let roundTripDocuments (selection: Selection) (generated: JsonValue) =
        match memberOf "artifacts" generated with
        | Some(JsonValue.Array artifacts) ->
            Ok(
                artifacts
                |> List.choose (fun artifact ->
                    match memberOf "path" artifact with
                    | Some(JsonValue.String path as uri) when matchesExtension selection path ->
                        Some(uri, memberOf "content" artifact |> Option.defaultValue JsonValue.Null)
                    | _ -> None)
            )
        | _ -> Error "generated artifacts missing"

    let private payload (bytes: byte array) =
        obj
            [ "codec", str "morphir-json"
              "layout", str "single-document"
              "decodedByteLength", num (int64 bytes.Length)
              "sha256", str (Convert.ToHexStringLower(SHA256.HashData bytes))
              "content", str (Convert.ToBase64String bytes) ]

    /// The independent model of a version: the kit's expected IR, written as serde_json writes it (members in
    /// ordinal order, no white space), in a model envelope.
    let independentModel (kit: GuestKit) (version: IrVersion) =
        let bytes = Encoding.UTF8.GetBytes(Json.compact (sorted (kit.Expected version)))

        obj
            [ "envelopeVersion", str "0.1.0-draft.1"
              "modelId", str "urn:morphir:mck:generic"
              "revision", str "independent-1"
              "irVersion", str version.WireValue
              "profile", str "morphir-ir"
              "payload", payload bytes ]

    /// The params of a generateModel request.
    let generateParams (kit: GuestKit) (selection: Selection) (model: JsonValue) (version: IrVersion) =
        obj
            [ "model", model
              "input", jsonTuple Generate version
              "target", str selection.Language.Id
              "contextDigest", str kit.ContextDigest ]

    /// The model with its payload digest replaced by 64 zeros.
    let withBadDigest (model: JsonValue) =
        match model with
        | JsonValue.Object members ->
            members
            |> List.map (fun (key, item) ->
                match key, item with
                | "payload", JsonValue.Object fields ->
                    key,
                    JsonValue.Object(
                        fields
                        |> List.map (fun (name, value) ->
                            if name = "sha256" then name, str (String('0', 64)) else name, value)
                    )
                | _ -> key, item)
            |> JsonValue.Object
        | other -> other

    let private diagnosticsWithoutError (result: JsonValue) =
        match memberOf "diagnostics" result with
        | Some(JsonValue.Array diagnostics) ->
            let wellFormed (diagnostic: JsonValue) =
                match memberOf "message" diagnostic, memberOf "severity" diagnostic with
                | Some(JsonValue.String _), Some(JsonValue.String("warning" | "info" | "hint")) -> true
                | _ -> false

            require (List.forall wellFormed diagnostics) "successful result has an error or malformed diagnostic"
        | _ -> Error "diagnostics must be an array"

    /// A successful compile: success true, no error diagnostic, and an irVersion, when given, of the version.
    let successfulCompile (result: JsonValue) (version: IrVersion) =
        require (memberOf "success" result = Some(JsonValue.Bool true)) "compile did not succeed"
        |> Result.bind (fun () -> diagnosticsWithoutError result)
        |> Result.bind (fun () ->
            match memberOf "irVersion" result with
            | None -> Ok()
            | Some reported ->
                require
                    (reported = str version.WireValue || reported = str (major version))
                    "compile result IR version differs")

    /// A generate result: success, no error diagnostic, text artifacts with unique relative paths, one of them with a
    /// claimed file extension.
    let validGenerated (selection: Selection) (result: JsonValue) =
        require (memberOf "success" result = Some(JsonValue.Bool true)) "generation did not succeed"
        |> Result.bind (fun () -> diagnosticsWithoutError result)
        |> Result.bind (fun () ->
            match memberOf "artifacts" result with
            | Some(JsonValue.Array(_ :: _ as artifacts)) ->
                let paths =
                    artifacts
                    |> List.map (fun artifact ->
                        match memberOf "path" artifact with
                        | Some(JsonValue.String path) ->
                            if not (KitReader.relativePath true path) then
                                Error "generated path is not relative"
                            else
                                match memberOf "content" artifact, memberOf "binary" artifact with
                                | Some(JsonValue.String _), (None | Some(JsonValue.Bool false)) -> Ok path
                                | _ -> Error "generated artifact must be text"
                        | _ -> Error "generated path missing")

                match paths |> List.tryPick (function Error message -> Some message | Ok _ -> None) with
                | Some message -> Error message
                | None ->
                    let found = paths |> List.choose Result.toOption

                    if (List.distinct found).Length <> found.Length then
                        Error "duplicate generated path"
                    else
                        require
                            (found |> List.exists (matchesExtension selection))
                            "no generated file has a claimed file extension of the language"
            | _ -> Error "generated artifacts missing")

    let private decodePayload (payload: JsonValue) =
        require (memberOf "layout" payload = Some(str "single-document")) "wrong payload layout"
        |> Result.bind (fun () ->
            match memberOf "content" payload with
            | Some(JsonValue.String encoded) -> Ok encoded
            | _ -> Error "payload content missing")
        |> Result.bind (fun encoded ->
            require (encoded.Length <= 1000000) "encoded payload exceeds profile limit"
            |> Result.bind (fun () ->
                try
                    Ok(Convert.FromBase64String encoded)
                with :? FormatException ->
                    Error "invalid payload base64")
            |> Result.bind (fun bytes ->
                require (Convert.ToBase64String bytes = encoded) "noncanonical base64"
                |> Result.map (fun () -> bytes)))
        |> Result.bind (fun bytes ->
            require
                (bytes.Length <= 750000
                 && memberOf "decodedByteLength" payload = Some(num (int64 bytes.Length)))
                "payload length mismatch"
            |> Result.bind (fun () ->
                require
                    (memberOf "sha256" payload = Some(str (Convert.ToHexStringLower(SHA256.HashData bytes))))
                    "payload digest mismatch")
            |> Result.map (fun () -> bytes))

    let rec private measure (pending: (JsonValue * int) list) (nodes: int) (maxDepth: int) (maxNodes: int) =
        match pending with
        | [] -> Ok()
        | (value, depth) :: rest ->
            let nodes = nodes + 1

            if depth > maxDepth || nodes > maxNodes then
                Error "decoded model exceeds the negotiated depth or node limit"
            else
                let children =
                    match value with
                    | JsonValue.Array items -> items |> List.map (fun item -> item, depth + 1)
                    | JsonValue.Object members -> members |> List.map (fun (_, item) -> item, depth + 1)
                    | _ -> []

                measure (children @ rest) nodes maxDepth maxNodes

    /// A compiled JSON model: its tuple and identity, no evidence, inside the negotiated limits, and its IR equal to
    /// the kit's expected IR under the kit's three rules.
    let validModel (kit: GuestKit) (model: JsonValue) (version: IrVersion) (limits: ModelTransportLimits) =
        require
            (memberOf "envelopeVersion" model = Some(str "0.1.0-draft.1")
             && memberOf "profile" model = Some(str "morphir-ir")
             && memberOf "irVersion" model = Some(str version.WireValue)
             && (model |> at [ "payload"; "codec" ]) = Some(str "morphir-json"))
            "compiled model tuple mismatch"
        |> Result.bind (fun () ->
            let nonEmpty name =
                match memberOf name model with
                | Some(JsonValue.String text) -> text <> ""
                | _ -> false

            require (nonEmpty "modelId" && nonEmpty "revision") "compiled logical identity missing")
        |> Result.bind (fun () ->
            let emptyOrAbsent name =
                match memberOf name model with
                | None
                | Some(JsonValue.Array []) -> true
                | _ -> false

            require
                (emptyOrAbsent "evidence" && emptyOrAbsent "critical")
                "JSON compilation must not add evidence or critical members")
        |> Result.bind (fun () ->
            match model |> at [ "payload"; "content" ] with
            | Some(JsonValue.String encoded) ->
                require (encoded.Length <= limits.MaxEncodedBytes) "encoded payload exceeds the negotiated limit"
            | _ -> Error "payload content missing")
        |> Result.bind (fun () ->
            match memberOf "payload" model with
            | Some payload -> decodePayload payload
            | None -> Error "payload content missing")
        |> Result.bind (fun bytes ->
            require (bytes.Length <= limits.MaxDecodedBytes) "decoded payload exceeds the negotiated limit"
            |> Result.bind (fun () -> parseStrict bytes))
        |> Result.bind (fun ir ->
            measure [ ir, 1 ] 0 limits.MaxDepth limits.MaxNodes
            |> Result.bind (fun () ->
                match IrComparison.difference version (kit.Expected version) ir with
                | None -> Ok()
                | Some difference -> Error $"compiled JSON model differs from the fixed semantic IR {version.WireValue} {difference}"))

    /// The tuples of each operation in the same order; operations may interleave.
    let sameTuples (actual: JsonValue list) (expected: JsonValue list) =
        let ofOperation (list: JsonValue list) (operation: string) =
            list |> List.filter (fun tuple -> memberOf "operation" tuple = Some(str operation)) |> List.map sorted

        require
            (actual.Length = expected.Length
             && [ Compile; Generate ]
                |> List.forall (fun operation -> ofOperation actual operation = ofOperation expected operation))
            "selected tuples missing, duplicated, unoffered or reordered"

    /// TIER-1 required plan data, independent of which operations the transitional executor can observe.
    /// This does not create KitSteps, record requests or assign outcomes to unexecuted lifecycle cases.
    let requiredCases (selection: Selection) =
        [ yield "describe", "morphir.extension.describe"
          yield "model-initialize", "morphir.initialize"
          yield "model-initialized-silent", "morphir.initialized"
          yield "unknown-notification-silent", "morphir.mck.unknown"
          for version in selection.Versions do
              let suffix = suffix version
              yield $"compile-model-{suffix}", Compile
              yield $"generate-model-{suffix}", Generate
              yield $"roundtrip-model-{suffix}", Compile
              yield $"generate-model-independent-{suffix}", Generate
              yield $"roundtrip-model-independent-{suffix}", Compile
          yield "bad-digest", Generate
          yield "malformed-source", Compile
          yield "unknown-method", "morphir.mck.unknown"
          yield "describe-immutable", "morphir.extension.describe"
          yield "model-shutdown", "morphir.shutdown"
          yield "after-shutdown", "morphir.ping"
          yield "model-exit", "morphir.exit" ]

    /// Transitional legacy result projection; not the adopted required plan. Retained until result migration is coordinated.
    let legacyCases (selection: Selection) =
        [ yield "initialize", "morphir.initialize"
          yield "initialized-silent", "morphir.initialized"
          yield "unknown-notification-silent", "morphir.mck.unknown"
          for version in selection.Versions do
              let suffix = suffix version
              yield $"compile-{suffix}", "morphir.frontend.compile"
              yield $"generate-{suffix}", "morphir.backend.generate"
              yield $"roundtrip-{suffix}", "morphir.frontend.compile"
              yield $"generate-independent-{suffix}", "morphir.backend.generate"
              yield $"roundtrip-independent-{suffix}", "morphir.frontend.compile"
          yield "malformed-source", "morphir.frontend.compile"
          yield "unknown-method", "morphir.mck.unknown"
          yield "describe-immutable", "morphir.extension.describe"
          yield "shutdown", "morphir.shutdown"
          yield "after-shutdown", "morphir.ping"
          yield "exit", "morphir.exit" ]

/// What a request got back through HostSession.
[<RequireQualifiedAccess>]
type internal Reply =
    /// A result.
    | Result of JsonValue
    /// A JSON-RPC error: the guest's, or HostSession's when it refused to send.
    | RpcError of RpcError
    /// A result this runner cannot read.
    | Unreadable of Reason: string
    /// The session's transport ended, so the request could not be sent: a write to the guest failed with an
    /// IOException, such as a worker that has ended.
    | TransportEnded of Reason: string

/// Drives the sessions of a model-tier run and records each step.
module internal ModelTierSession =
    open KitJson
    open ModelTierPlan

    /// How long the runner waits for a session to end after morphir.exit.
    let ExitTimeout = TimeSpan.FromSeconds 10.0

    /// The legacy tier's reason, on every legacy case.
    [<Literal>]
    let LegacyCaseReason =
        "The legacy tier negotiates MEP 0.1, which this host does not speak (design MEP-1)."

    let private step id tier method requests notifications request response outcome (elapsed: TimeSpan) =
        KitStep(id, tier, method, requests, notifications, request, response, outcome, elapsed, None)

    let private notRun id tier method reason =
        step id tier method 0 0 null null (StepOutcome.NotRun reason) TimeSpan.Zero

    let private outcomeOf (check: Result<unit, string>) =
        match check with
        | Ok() -> StepOutcome.Passed
        | Error difference -> StepOutcome.Failed difference

    let private control (session: HostSession) =
        session.Conformance |> Option.defaultWith (fun () -> invalidOp "The factory must create the requested conformance session.")

    let private failureText = function
        | ConformanceOperationFailure.ImplementationUnavailable -> "operation observation is unavailable"
        | ConformanceOperationFailure.LocalLifecycleRefusal error -> $"local lifecycle refusal {error.Code}: {error.Message}"
        | ConformanceOperationFailure.ProbeAlreadyReserved -> "the fixed probe was already reserved"
        | ConformanceOperationFailure.ChannelFailed _ -> "the MEP channel failed"
        | ConformanceOperationFailure.ConnectionEnded _ -> "the MEP connection ended"
        | ConformanceOperationFailure.TransportFailed error -> $"transport failed: {error.Message}"
        | ConformanceOperationFailure.CaptureFailed _ -> "operation capture is incomplete"
        | ConformanceOperationFailure.Cancelled _ -> "operation was cancelled"

    let private unavailable (observed: ConformanceOperationObservation) =
        match observed.Origin with
        | ConformanceOperationOrigin.AnsweredLocally cause -> Some $"Answered locally: {failureText cause}."
        | ConformanceOperationOrigin.Incomplete cause -> Some $"Incomplete operation: {failureText cause}."
        | ConformanceOperationOrigin.Submitted when observed.SubmissionCompleted && observed.Operation.IsSome && observed.RequestBody.IsSome -> None
        | _ -> Some "Operation submission is not established; prepared bytes do not prove completed I/O."

    let private requestIdValue = function
        | RequestId.Number value -> JsonValue.Number(value.ToString(Globalization.CultureInfo.InvariantCulture))
        | RequestId.Text value -> str value

    /// Only the operation's actual winning correlated bodies supply request/result data.
    let private exchange (observed: ConformanceOperationObservation) =
        match unavailable observed with
        | Some reason -> Error reason
        | None ->
            match observed.RequestId, observed.RequestBody, observed.ResponseBody with
            | Some id, Some request, Some response ->
                parseStrict request |> Result.bind (fun sent ->
                    parseStrict response |> Result.bind (fun received ->
                        require
                            (memberOf "id" sent = Some(requestIdValue id) && memberOf "id" received = Some(requestIdValue id))
                            "The operation's copied bodies do not carry its actual correlated id."
                        |> Result.map (fun () -> sent, received)))
            | _ -> Error "The operation has no complete correlated request and response bodies."

    let private requestCount (observed: ConformanceOperationObservation) =
        if observed.SubmissionCompleted && observed.RequestId.IsSome && observed.RequestBody.IsSome then 1 else 0

    let private notificationCount (observed: ConformanceOperationObservation) =
        if observed.SubmissionCompleted && observed.RequestId.IsNone && observed.RequestBody.IsSome then 1 else 0

    let private rawParams (observed: ConformanceOperationObservation) =
        observed.RequestBody |> Option.bind (parseStrict >> Result.toOption) |> Option.bind (memberOf "params")
        |> Option.map Json.compact |> Option.toObj

    let private rawResponse (observed: ConformanceOperationObservation) =
        observed.ResponseBody |> Option.bind (parseStrict >> Result.toOption)
        |> Option.bind (fun packet -> memberOf "result" packet |> Option.orElseWith (fun () -> memberOf "error" packet))
        |> Option.map Json.compact |> Option.toObj

    let private replyOf (observed: ConformanceOperationObservation) =
        match exchange observed with
        | Error reason -> Reply.TransportEnded reason
        | Ok(_, packet) ->
            match memberOf "result" packet, memberOf "error" packet with
            | Some value, None -> Reply.Result value
            | None, Some value ->
                match toTyped MepJson.Default.RpcError value with
                | Some error -> Reply.RpcError error
                | None -> Reply.Unreadable "The actual peer error cannot be decoded."
            | _ -> Reply.Unreadable "The actual peer packet must contain exactly one result or error."

    let private observedStep id tier method (observed: ConformanceOperationObservation) outcome elapsed =
        step id tier method (requestCount observed) (notificationCount observed) (rawParams observed) (rawResponse observed) outcome elapsed
        |> fun value -> value.WithObservation observed

    /// Waits for a session to end after morphir.exit: the session's run task ends with exit or the end of the
    /// stream, and the session records no failure.
    let finish (session: HostSession) (run: Task<ConnectionEnd>) (cancellation: CancellationToken) =
        task {
            let! winner = Task.WhenAny(run :> Task, Task.Delay(ExitTimeout, cancellation))
            cancellation.ThrowIfCancellationRequested()

            if not (Object.ReferenceEquals(winner, run)) then
                return Error $"the session did not end within {ExitTimeout.TotalSeconds} s of morphir.exit"
            else
                match run.Result, session.Failure with
                | _, Some failure -> return Error $"the session failed: {failure}"
                | ConnectionEnd.FramingFailed failure, None -> return Error $"the session ended with a framing failure: {failure}"
                | (ConnectionEnd.Stopped | ConnectionEnd.EndOfStream), None -> return Ok()
        }

    let private request (session: HostSession) (method: Method) (parameters: JsonValue) (cancellation: CancellationToken) =
        task {
            let watch = Diagnostics.Stopwatch.StartNew()
            let! sent = (control session).RequestAsync(method, Nullable(toElement parameters), cancellation)
            watch.Stop()
            return replyOf sent.Observation, sent.Observation, watch.Elapsed
        }

    /// The result of a reply, or why there is none.
    let private result (reply: Reply) =
        match reply with
        | Reply.Result value -> Ok value
        | Reply.RpcError error -> Error $"expected a result, got RPC error {error.Code}: {error.Message}"
        | Reply.Unreadable reason
        | Reply.TransportEnded reason -> Error reason

    /// The kit's expect_error: an RPC error with this code and no result.
    let private expectError (code: int) (reply: Reply) =
        match reply with
        | Reply.RpcError error when error.Code = code -> Ok()
        | Reply.RpcError error -> Error $"expected RPC error {code}, got RPC error {error.Code}: {error.Message}"
        | Reply.Result _ -> Error $"expected RPC error {code}, got a result"
        | Reply.Unreadable reason
        | Reply.TransportEnded reason -> Error reason

    /// Describe on the same opted-in session that will initialize and execute the complete plan.
    let describe (kit: GuestKit) (session: HostSession) cancellation =
        task {
            let watch = Diagnostics.Stopwatch.StartNew()
            let api = control session
            let! sent = api.DescribeAsync cancellation
            watch.Stop()
            // Join only these two issued observations by the opaque operation identity, never a matching-id search.
            // Keep the call's origin/completion facts even when the receipt preserves an earlier successful exchange.
            let receiptMatches =
                match api.OriginalDescribeObservation, sent.Observation.Operation with
                | Some original, Some admitted ->
                    (original.Operation |> Option.exists (fun operation -> Object.ReferenceEquals(operation, admitted)))
                    && original.RequestId = Some(RequestId.Number 1UL)
                    && original.RequestId = sent.Observation.RequestId
                    && original.RequestBody = sent.Observation.RequestBody
                    && original.ResponseBody = sent.Observation.ResponseBody
                | _ -> false
            let observed = replyOf sent.Observation

            let checkedResult =
                match sent.Result with
                | Ok _ when receiptMatches ->
                    result observed
                    |> Result.bind (fun described -> checkDescribe kit described |> Result.map (fun selection -> described, selection))
                | Error error -> Error $"Describe could not be admitted: {error.Code}: {error.Message}"
                | Ok _ -> Error "The original winning describe receipt does not belong to this first operation and request id 1."

            let outcome, planned =
                match checkedResult with
                | Ok(result, selection) -> StepOutcome.Passed, Some(result, selection)
                | Error difference -> StepOutcome.Failed difference, None

            let guest = result observed |> Result.toOption |> Option.bind guest

            let describeStep = observedStep "describe" KitTier.Describe Method.Describe.WireValue sent.Observation outcome watch.Elapsed

            return describeStep, guest, planned
        }

    /// The checks of model-initialize on what HostSession accepted.
    let private checkInitialize
        (kit: GuestKit)
        (describeResult: JsonValue)
        (selection: Selection)
        (response: JsonValue)
        =
        let extension = memberOf "extension" response |> Option.defaultValue JsonValue.Null
        let capabilities = memberOf "capabilities" response |> Option.defaultValue JsonValue.Null
        let describedExtension = memberOf "extension" describeResult

        let expectedLimits () =
            let claimed name =
                match describeResult |> at [ "capabilities"; "modelTransport"; "limits"; name ] with
                | Some(JsonValue.Number literal) ->
                    match Int64.TryParse(literal, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                    | true, value -> Some value
                    | _ -> None
                | _ -> None

            match limits with
            | JsonValue.Object members ->
                members
                |> List.map (fun (name, value) ->
                    match value, claimed name with
                    | JsonValue.Number literal, Some claimedValue -> Ok(name, num (min (int64 literal) claimedValue))
                    | _ -> Error "static model limit missing")
                |> fun results ->
                    match results |> List.tryPick (function Error message -> Some message | Ok _ -> None) with
                    | Some message -> Error message
                    | None -> Ok(obj (results |> List.choose Result.toOption))
            | _ -> Error "static model limit missing"

        let check =
            require (describedExtension |> Option.exists (same extension)) "initialized identity differs from description"
            |> Result.bind (fun () ->
                let claim = obj [ "capabilities", capabilities; "protocolVersions", arr [] ]

                match select kit claim with
                | Ok initialized when
                    initialized.Language.Id = selection.Language.Id && initialized.Versions = selection.Versions
                    ->
                    Ok()
                | _ -> Error "initialized capabilities differ from the description")
            |> Result.bind (fun () ->
                require (memberOf "protocolVersion" response = Some(str ModelProtocol)) "wrong model protocol version")
            |> Result.bind expectedLimits
            |> Result.bind (fun expected ->
                let chosen = memberOf "modelTransport" capabilities |> Option.defaultValue JsonValue.Null

                let tuples value =
                    match memberOf "tuples" value with
                    | Some(JsonValue.Array items) -> items
                    | _ -> []

                sameTuples (tuples chosen) (tuples (offer selection))
                |> Result.bind (fun () ->
                    require
                        (memberOf "limits" chosen |> Option.exists (same expected)
                         && (match memberOf "guarantees" chosen with
                             | None
                             | Some(JsonValue.Array []) -> true
                             | _ -> false))
                        "bilateral selected limits or guarantees differ"))

        check

    /// The model tier's session and steps, in the kit's order.
    let modelTier
        (kit: GuestKit)
        (describeResult: JsonValue)
        (selection: Selection)
        (session: HostSession)
        (run: Task<ConnectionEnd>)
        (sessionRequest: KitSessionRequest)
        (cancellation: CancellationToken)
        =
        task {
            let steps = Collections.Generic.List<KitStep>()

            let support =
                match toTyped MepJson.Default.ModelTransportSupport (offer selection) with
                | Some support -> support
                | None -> invalidOp "The kit's model-transport offer does not read as ModelTransportSupport."

            let control = session.Conformance |> Option.defaultWith (fun () -> invalidOp "The factory must create the requested conformance session.")
            match control.DescribeReceipt with
            | None -> invalidOp "The original same-session describe receipt is missing."
            | Some receipt ->
                match control.BindSelectedOffer(receipt, support) with
                | Ok() -> ()
                | Error reason -> invalidOp $"The selected offer could not be bound: {reason}"
            // Why the session's transport ended, once a send failed; no step after it runs.
            let transport: string option ref = ref None

            let watch = Diagnostics.Stopwatch.StartNew()
            let! initialized = control.InitializeAsync cancellation
            watch.Stop()

            let negotiated =
                let observation = initialized.Observation
                match initialized.Result, replyOf observation with
                | Ok negotiated, Reply.Result response ->
                    let check = checkInitialize kit describeResult selection response
                    steps.Add(observedStep "model-initialize" KitTier.Model Method.Initialize.WireValue observation (outcomeOf check) watch.Elapsed)
                    Some negotiated
                | _, reply ->
                    let reason =
                        match reply with
                        | Reply.TransportEnded reason | Reply.Unreadable reason -> reason
                        | Reply.RpcError error -> $"The peer refused initialization with {error.Code}: {error.Message}"
                        | Reply.Result _ -> "HostSession did not accept the actual initialization result."
                    transport.Value <- Some reason
                    let outcome =
                        match observation.Origin with
                        | ConformanceOperationOrigin.AnsweredLocally _ -> StepOutcome.NotRun reason
                        | _ -> StepOutcome.Failed reason
                    steps.Add(observedStep "model-initialize" KitTier.Model Method.Initialize.WireValue observation outcome watch.Elapsed)
                    None

            let deliveryMissing = "Actual guest notification delivery is not established by a worker write or adapter return."
            match List.ofSeq initialized.Notifications with
            | [ observation ] ->
                let reason = unavailable observation |> Option.defaultValue deliveryMissing
                steps.Add(observedStep "model-initialized-silent" KitTier.Model Method.Initialized.WireValue observation (StepOutcome.NotRun reason) TimeSpan.Zero)
            | [] ->
                steps.Add(notRun "model-initialized-silent" KitTier.Model Method.Initialized.WireValue "No initialized notification observation accompanies initialization.")
            | _ ->
                steps.Add(notRun "model-initialized-silent" KitTier.Model Method.Initialized.WireValue "Unexpected additional notification observations accompany initialization.")

            let limits =
                negotiated
                |> Option.bind (fun negotiated -> negotiated.Capabilities.ModelTransport)
                |> Option.map _.Limits

            let! unknown = control.SendUnknownNotificationObservedAsync cancellation
            let unknownReason = unavailable unknown.Observation |> Option.defaultValue deliveryMissing
            steps.Add(observedStep "unknown-notification-silent" KitTier.Model "morphir.mck.unknown" unknown.Observation (StepOutcome.NotRun unknownReason) TimeSpan.Zero)

            let results = Collections.Generic.Dictionary<string, JsonValue>(StringComparer.Ordinal)

            // Why a request step cannot run now, or None.
            let blocked (needs: string list) =
                match transport.Value, negotiated, session.FailureOrNull() with
                | Some reason, _, _ -> Some reason
                | None, None, _ -> Some "model-initialize did not succeed, so the session is not initialized."
                | None, _, null ->
                    needs
                    |> List.tryFind (fun need -> not (results.ContainsKey need))
                    |> Option.map (fun need -> $"It needs the result of {need}, which returned none.")
                | None, _, ended -> Some $"The session ended: {ended}"

            let requestStep
                id
                (method: Method)
                (needs: string list)
                (build: unit -> Result<JsonValue, string>)
                (check: Reply -> Result<unit, string>)
                =
                task {
                    match blocked needs with
                    | Some reason -> steps.Add(notRun id KitTier.Model method.WireValue reason)
                    | None ->
                        match build () with
                        | Error reason -> steps.Add(notRun id KitTier.Model method.WireValue reason)
                        | Ok parameters ->
                            let! observed, observation, elapsed = request session method parameters cancellation

                            match observed with
                            | Reply.Result value -> results[id] <- value
                            | Reply.TransportEnded reason -> transport.Value <- Some reason
                            | Reply.RpcError _
                            | Reply.Unreadable _ -> ()

                            steps.Add(
                                observedStep
                                    id
                                    KitTier.Model
                                    method.WireValue
                                    observation
                                    (outcomeOf (check observed))
                                    elapsed
                            )
                }

            let compiled version reply =
                result reply
                |> Result.bind (fun compiledResult ->
                    successfulCompile compiledResult version
                    |> Result.bind (fun () ->
                        match memberOf "model" compiledResult, limits with
                        | Some model, Some limits -> validModel kit model version limits
                        | None, _ -> Error "compiled model missing"
                        | _, None -> Error "negotiated limits missing"))

            let generated reply =
                result reply |> Result.bind (validGenerated selection)

            for version in selection.ModelVersions do
                let suffix = suffix version
                let compileId = $"compile-model-{suffix}"
                let generateId = $"generate-model-{suffix}"
                let independentId = $"generate-model-independent-{suffix}"

                do!
                    requestStep compileId Method.CompileModel [] (fun () -> Ok(compileParams kit selection (kitDocuments selection) version)) (compiled version)

                do!
                    requestStep
                        generateId
                        Method.GenerateModel
                        [ compileId ]
                        (fun () ->
                            match memberOf "model" results[compileId] with
                            | Some model -> Ok(generateParams kit selection model version)
                            | None -> Error $"It needs the model of {compileId}, which returned none.")
                        generated

                do!
                    requestStep
                        $"roundtrip-model-{suffix}"
                        Method.CompileModel
                        [ generateId ]
                        (fun () ->
                            roundTripDocuments selection results[generateId]
                            |> Result.map (fun documents -> compileParams kit selection documents version))
                        (compiled version)

                do!
                    requestStep
                        independentId
                        Method.GenerateModel
                        []
                        (fun () -> Ok(generateParams kit selection (independentModel kit version) version))
                        generated

                do!
                    requestStep
                        $"roundtrip-model-independent-{suffix}"
                        Method.CompileModel
                        [ independentId ]
                        (fun () ->
                            roundTripDocuments selection results[independentId]
                            |> Result.map (fun documents -> compileParams kit selection documents version))
                        (compiled version)

            let first = List.head selection.ModelVersions

            do!
                requestStep
                    "bad-digest"
                    Method.GenerateModel
                    []
                    (fun () -> Ok(generateParams kit selection (withBadDigest (independentModel kit first)) first))
                    (expectError -32602)

            let malformed =
                selection.Language.Malformed
                |> List.map (fun document -> str document.Uri, str document.Text)

            do!
                requestStep "malformed-source" Method.CompileModel []
                    (fun () -> Ok(compileParams kit selection malformed first))
                    (fun reply ->
                        result reply |> Result.bind (fun value ->
                            let diagnostics = memberOf "diagnostics" value
                            require
                                (memberOf "success" value = Some(JsonValue.Bool false)
                                 && (memberOf "model" value |> Option.isNone)
                                 && (match diagnostics with
                                     | Some(JsonValue.Array values) -> values |> List.exists (fun diagnostic -> memberOf "severity" diagnostic = Some(str "error"))
                                     | _ -> false))
                                "malformed source must fail with an error diagnostic and no model"))

            match blocked [] with
            | Some reason -> steps.Add(notRun "unknown-method" KitTier.Model "morphir.mck.unknown" reason)
            | None ->
                let watch = Diagnostics.Stopwatch.StartNew()
                let! unknown = control.RequestUnknownMethodAsync cancellation
                watch.Stop()
                let reply = replyOf unknown.Observation
                match reply with
                | Reply.TransportEnded reason -> transport.Value <- Some reason
                | _ -> ()
                steps.Add(observedStep "unknown-method" KitTier.Model "morphir.mck.unknown" unknown.Observation (outcomeOf (expectError -32601 reply)) watch.Elapsed)

            do!
                requestStep "describe-immutable" Method.Describe []
                    (fun () -> Ok(obj [ "protocolVersions", arr [ str ModelProtocol ] ]))
                    (fun reply -> result reply |> Result.bind (fun value -> require (same value describeResult) "describe changed during the session"))

            match blocked [] with
            | Some reason -> steps.Add(notRun "model-shutdown" KitTier.Model Method.Shutdown.WireValue reason)
            | None ->
                let watch = Diagnostics.Stopwatch.StartNew()
                let! reply = control.ShutdownAsync cancellation
                watch.Stop()

                let outcome =
                    match replyOf reply.Observation with
                    | Reply.Result _ -> StepOutcome.Passed
                    | Reply.RpcError error -> StepOutcome.Failed $"expected a result, got peer RPC error {error.Code}: {error.Message}"
                    | Reply.TransportEnded reason | Reply.Unreadable reason ->
                        transport.Value <- Some reason
                        StepOutcome.Failed reason

                steps.Add(observedStep "model-shutdown" KitTier.Model Method.Shutdown.WireValue reply.Observation outcome watch.Elapsed)

            let! afterShutdown = control.ProbeAfterShutdownObservedAsync cancellation
            let afterOutcome =
                match unavailable afterShutdown.Observation with
                | Some reason -> StepOutcome.NotRun $"No actual peer refusal was observed: {reason}"
                | None -> outcomeOf (expectError -32014 (replyOf afterShutdown.Observation))
            steps.Add(observedStep "after-shutdown" KitTier.Model Method.Ping.WireValue afterShutdown.Observation afterOutcome TimeSpan.Zero)

            match transport.Value with
            | Some reason -> steps.Add(notRun "model-exit" KitTier.Model Method.Exit.WireValue reason)
            | None ->
                let watch = Diagnostics.Stopwatch.StartNew()
                let! exited = control.ExitAsync()
                let! ended =
                    match unavailable exited.Observation, exited.Result with
                    | None, Ok() -> finish session run cancellation
                    | Some reason, _ -> Task.FromResult(Error reason)
                    | _, Error error -> Task.FromResult(Error $"Exit was refused with {error.Code}: {error.Message}")
                watch.Stop()

                let! settled = sessionRequest.SettleAsync cancellation
                let exitOutcome =
                    match ended with
                    | Ok() ->
                        match settled, run.Result with
                        | Ok(), ConnectionEnd.EndOfStream -> StepOutcome.Passed
                        | Error reason, _ -> StepOutcome.NotRun reason
                        | _, _ -> StepOutcome.NotRun "Actual process stdout EOF was not observed."
                    | Error reason -> StepOutcome.Failed $"unclean exit: {reason}"

                steps.Add(observedStep "model-exit" KitTier.Model Method.Exit.WireValue exited.Observation exitOutcome watch.Elapsed)

            return List.ofSeq steps
        }

/// Runs the model tier of the generic guest kit through HostSession.
[<AbstractClass; Sealed>]
type ModelTierRunner =
    /// Opens one explicitly opted-in conformance session. The original describe receipt binds the selected model
    /// offer before initialize; no second session or legacy tier is opened. Every selected case remains in order,
    /// including cases whose required observation is unavailable and therefore NotRun. The runner sends exit and
    /// awaits the same session and its attached process owner. When a send fails with an IOException, such as the WorkerEndedException of a
    /// worker that has ended, the session's transport has ended: that step fails, the session's later steps are not
    /// run, and the run keeps every step. Cancellation ends the run with an OperationCanceledException.
    static member RunAsync
        (
            kit: GuestKit,
            openSession: Func<KitSessionRequest, CancellationToken, Task<HostSession>>,
            cancellation: CancellationToken
        ) : Task<ModelTierRun> =
        task {
            use lifetime = CancellationTokenSource.CreateLinkedTokenSource cancellation
            let sessionRequest = KitSessionRequest(KitSessionPurpose.ModelTier, None, Some lifetime.Token)
            let! session = openSession.Invoke(sessionRequest, lifetime.Token)
            let run = session.RunAsync lifetime.Token
            let execute = task {
                let! describeStep, guest, planned = ModelTierSession.describe kit session lifetime.Token

                let reason: string | null =
                    match planned with
                    | None -> "The describe case failed, so the kit plans no required model cases."
                    | Some _ -> null

                let! modelSteps =
                    match planned with
                    | Some(described, selection) ->
                        ModelTierSession.modelTier kit described selection session run sessionRequest lifetime.Token
                    | _ -> task {
                        let! _ = (session.Conformance |> Option.get).ExitAsync()
                        return [] }

                let selection = planned |> Option.map snd

                let versions (pick: Selection -> IrVersion list) =
                    selection
                    |> Option.map (fun selection -> pick selection |> List.map _.WireValue)
                    |> Option.defaultValue []
                    |> List.toArray
                    :> IReadOnlyList<_>

                return
                    ModelTierRun(
                        kit.Sha256,
                        Option.toObj guest,
                        selection |> Option.map _.Language.Id |> Option.toObj,
                        versions _.Versions,
                        versions _.ModelVersions,
                        reason,
                        (describeStep :: modelSteps) |> List.toArray :> IReadOnlyList<_>,
                        Array.empty<KitStep> :> IReadOnlyList<_>) }
            let! outcome = task {
                try
                    let! value = execute
                    return Ok value
                with error -> return Error error }
            lifetime.Cancel()
            let! _ = sessionRequest.SettleAsync CancellationToken.None
            // Stopping our receive loop after a refused describe is cleanup, not caller cancellation.
            // External cancellation and unexpected receiver failures still propagate.
            try
                let! _ = run
                ()
            with :? OperationCanceledException when not cancellation.IsCancellationRequested -> ()
            // An observed operation may return a cancellation error while the reader ends normally.
            // Propagate the caller's cancellation only after both process and reader have settled.
            cancellation.ThrowIfCancellationRequested()
            match outcome with
            | Ok value -> return value
            | Error error -> return raise error
        }
