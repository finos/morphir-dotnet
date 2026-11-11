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

/// Where a step of the kit belongs: describe, which plans both tiers; the model tier on MEP 0.2.0-draft.1; or the
/// legacy tier on MEP 0.1, which this host does not run.
[<RequireQualifiedAccess>]
type KitTier =
    /// The describe case, whose claim plans both tiers.
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
        elapsed: TimeSpan
    ) =
    /// The kit's case id, such as compile-model-vthree.
    member _.Id = id

    /// The tier of the case.
    member _.Tier = tier

    /// The MEP method of the case.
    member _.Method = method

    /// How many requests the step sent to the guest.
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
type KitSessionRequest internal (purpose: KitSessionPurpose, offer: ModelTransportSupport option) =
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

/// The result of a model-tier run of the generic guest kit. It is never a run of the whole kit: the legacy tier is
/// listed as not run, and the result holds no verdict for the kit. Only ModelTierRunner builds one.
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

    /// The legacy tier's cases of the plan, each not run.
    member _.LegacyTier = legacyTier

    /// Why the legacy tier was not run: this host does not negotiate MEP 0.1 (design MEP-1).
    member _.LegacyTierReason =
        "This host speaks MEP 0.2.0-draft.1 only and does not negotiate MEP 0.1 (design MEP-1), so the kit's legacy tier was not run. The describe case's check that the guest claims MEP 0.1 belongs to that tier and was not applied."

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

    /// The typed value written by the MEP serializer, as a JsonValue.
    let ofTyped<'T> (info: JsonTypeInfo<'T>) (value: 'T) =
        match ofElement (JsonSerializer.SerializeToElement(value, info)) with
        | Ok json -> json
        | Error message -> invalidOp $"The MEP serializer wrote JSON this reader refuses: {message}"

    /// A JsonValue read as a MEP type, or None when it does not match the type.
    let toTyped<'T when 'T: not null> (info: JsonTypeInfo<'T>) (value: JsonValue) : 'T option =
        try
            match JsonSerializer.Deserialize(toElement value, info :> JsonTypeInfo) with
            | :? 'T as typed -> Some typed
            | _ -> None
        with :? JsonException ->
            None

    /// A JSON-RPC error as compact JSON.
    let errorText (error: RpcError) =
        let data =
            match error.Data |> Option.map ofElement with
            | Some(Ok data) -> [ "data", data ]
            | _ -> []

        Json.compact (obj ([ "code", num (int64 error.Code); "message", str error.Message ] @ data))

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
        KitStep(id, tier, method, requests, notifications, request, response, outcome, elapsed)

    let private notRun id tier method reason =
        step id tier method 0 0 null null (StepOutcome.NotRun reason) TimeSpan.Zero

    let private outcomeOf (check: Result<unit, string>) =
        match check with
        | Ok() -> StepOutcome.Passed
        | Error difference -> StepOutcome.Failed difference

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

    /// Why a write to the session failed: its transport ended, such as the stdin of a worker that has ended.
    let private transportEnded (error: IO.IOException) =
        $"The session's transport ended: {error.Message}"

    /// Runs a send of the session; an IOException from its write is the end of the transport, with the reason.
    let private sending (send: unit -> Task<'T>) =
        task {
            try
                let! sent = send ()
                return Ok sent
            with :? IO.IOException as error ->
                return Error(transportEnded error)
        }

    /// Sends morphir.exit and waits for the session to end; Error when the transport has ended or the session did
    /// not end cleanly.
    let exitAndFinish (session: HostSession) (run: Task<ConnectionEnd>) (cancellation: CancellationToken) =
        task {
            match! sending (fun () -> task { do! session.ExitAsync() }) with
            | Error reason -> return Error reason
            | Ok() -> return! finish session run cancellation
        }

    let private request (session: HostSession) (method: Method) (parameters: JsonValue) (cancellation: CancellationToken) =
        task {
            let watch = Diagnostics.Stopwatch.StartNew()
            let! sent = sending (fun () -> session.RequestAsync(method, Nullable(toElement parameters), cancellation))
            watch.Stop()

            let response: string | null =
                match sent with
                | Ok(Ok element) -> element.GetRawText()
                | Ok(Error error) -> errorText error
                | Error _ -> null

            let observed =
                match sent with
                | Ok(Ok element) ->
                    match ofElement element with
                    | Ok value -> Reply.Result value
                    | Error message -> Reply.Unreadable $"the result is not JSON this runner reads: {message}"
                | Ok(Error error) -> Reply.RpcError error
                | Error reason -> Reply.TransportEnded reason

            return observed, response, watch.Elapsed
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

    /// The describe session: describe, then exit.
    let describe (kit: GuestKit) (openSession: Func<KitSessionRequest, CancellationToken, Task<HostSession>>) cancellation =
        task {
            let! session = openSession.Invoke(KitSessionRequest(KitSessionPurpose.Describe, None), cancellation)
            let run = session.RunAsync cancellation
            // The kit sends [0.2.0-draft.1, 0.1]; this host offers the 0.2 line only (design MEP-1).
            let parameters = obj [ "protocolVersions", arr [ str ModelProtocol ] ]
            let! observed, response, elapsed = request session Method.Describe parameters cancellation
            let! ended = exitAndFinish session run cancellation

            let checkedResult =
                result observed
                |> Result.bind (fun described -> checkDescribe kit described |> Result.map (fun selection -> described, selection))

            let outcome, planned =
                match checkedResult, ended with
                | Ok(result, selection), Ok() -> StepOutcome.Passed, Some(result, selection)
                | Ok(result, selection), Error reason ->
                    StepOutcome.Failed $"the describe session did not end cleanly: {reason}", Some(result, selection)
                | Error difference, _ -> StepOutcome.Failed difference, None

            let guest = result observed |> Result.toOption |> Option.bind guest

            let describeStep =
                step
                    "describe"
                    KitTier.Describe
                    Method.Describe.WireValue
                    1
                    1
                    (Json.compact parameters)
                    response
                    outcome
                    elapsed

            return describeStep, guest, planned
        }

    /// The checks of model-initialize on what HostSession accepted.
    let private checkInitialize
        (kit: GuestKit)
        (describeResult: JsonValue)
        (selection: Selection)
        (negotiated: NegotiatedSession)
        =
        let extension = ofTyped MepJson.Default.ExtensionInfo negotiated.Extension
        let capabilities = ofTyped MepJson.Default.ExtensionCapabilities negotiated.Capabilities

        let describedExtension =
            match memberOf "extension" describeResult with
            | Some described ->
                match toTyped MepJson.Default.ExtensionInfo described with
                | Some typed -> Some(ofTyped MepJson.Default.ExtensionInfo typed)
                | None -> Some described
            | None -> None

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

        let response =
            obj
                [ "protocolVersion", str negotiated.ProtocolVersion.Value
                  "extension", extension
                  "capabilities", capabilities ]

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
                require (negotiated.ProtocolVersion.Value = ModelProtocol) "wrong model protocol version")
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

        check, Json.compact response

    /// The model tier's session and steps, in the kit's order.
    let modelTier
        (kit: GuestKit)
        (describeResult: JsonValue)
        (selection: Selection)
        (openSession: Func<KitSessionRequest, CancellationToken, Task<HostSession>>)
        (cancellation: CancellationToken)
        =
        task {
            let steps = Collections.Generic.List<KitStep>()

            let support =
                match toTyped MepJson.Default.ModelTransportSupport (offer selection) with
                | Some support -> support
                | None -> invalidOp "The kit's model-transport offer does not read as ModelTransportSupport."

            let! session = openSession.Invoke(KitSessionRequest(KitSessionPurpose.ModelTier, Some support), cancellation)
            let run = session.RunAsync cancellation
            // Why the session's transport ended, once a send failed; no step after it runs.
            let transport: string option ref = ref None

            let watch = Diagnostics.Stopwatch.StartNew()
            let! initialized = sending (fun () -> session.InitializeAsync cancellation)
            watch.Stop()

            let negotiated =
                match initialized with
                | Error reason ->
                    transport.Value <- Some reason

                    steps.Add(
                        step
                            "model-initialize"
                            KitTier.Model
                            Method.Initialize.WireValue
                            1
                            0
                            null
                            null
                            (StepOutcome.Failed reason)
                            watch.Elapsed
                    )

                    steps.Add(notRun "model-initialized-silent" KitTier.Model Method.Initialized.WireValue reason)
                    None
                | Ok(Ok negotiated) ->
                    let check, response = checkInitialize kit describeResult selection negotiated
                    steps.Add(step "model-initialize" KitTier.Model Method.Initialize.WireValue 1 0 null response (outcomeOf check) watch.Elapsed)

                    steps.Add(
                        step
                            "model-initialized-silent"
                            KitTier.Model
                            Method.Initialized.WireValue
                            0
                            1
                            null
                            null
                            StepOutcome.Passed
                            TimeSpan.Zero
                    )

                    Some negotiated
                | Ok(Error failure) ->
                    let failureText =
                        match session.FailureOrNull() with
                        | null -> failure.ToString()
                        | ended -> $"{failure}; the session ended: {ended}"

                    steps.Add(
                        step
                            "model-initialize"
                            KitTier.Model
                            Method.Initialize.WireValue
                            1
                            0
                            null
                            null
                            (StepOutcome.Failed $"HostSession did not initialize: {failureText}")
                            watch.Elapsed
                    )

                    steps.Add(
                        notRun
                            "model-initialized-silent"
                            KitTier.Model
                            Method.Initialized.WireValue
                            "HostSession sends morphir.initialized only after an initialize it accepts."
                    )

                    None

            let limits =
                negotiated
                |> Option.bind (fun negotiated -> negotiated.Capabilities.ModelTransport)
                |> Option.map _.Limits

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
                            let! observed, response, elapsed = request session method parameters cancellation

                            match observed with
                            | Reply.Result value -> results[id] <- value
                            | Reply.TransportEnded reason -> transport.Value <- Some reason
                            | Reply.RpcError _
                            | Reply.Unreadable _ -> ()

                            steps.Add(
                                step
                                    id
                                    KitTier.Model
                                    method.WireValue
                                    1
                                    0
                                    (Json.compact parameters)
                                    response
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

            match blocked [] with
            | Some reason -> steps.Add(notRun "model-shutdown" KitTier.Model Method.Shutdown.WireValue reason)
            | None ->
                let watch = Diagnostics.Stopwatch.StartNew()
                let! reply = sending (fun () -> session.ShutdownAsync cancellation)
                watch.Stop()

                let (response: string | null), outcome =
                    match reply with
                    | Ok(Ok element) -> element.GetRawText(), StepOutcome.Passed
                    | Ok(Error error) -> errorText error, StepOutcome.Failed $"expected a result, got RPC error {error.Code}: {error.Message}"
                    | Error reason ->
                        transport.Value <- Some reason
                        null, StepOutcome.Failed reason

                steps.Add(step "model-shutdown" KitTier.Model Method.Shutdown.WireValue 1 0 null response outcome watch.Elapsed)

            match transport.Value with
            | Some reason -> steps.Add(notRun "model-exit" KitTier.Model Method.Exit.WireValue reason)
            | None ->
                let watch = Diagnostics.Stopwatch.StartNew()
                let! ended = exitAndFinish session run cancellation
                watch.Stop()

                let exitOutcome =
                    match ended with
                    | Ok() -> StepOutcome.Passed
                    | Error reason -> StepOutcome.Failed $"unclean exit: {reason}"

                steps.Add(step "model-exit" KitTier.Model Method.Exit.WireValue 0 1 null null exitOutcome watch.Elapsed)

            return List.ofSeq steps
        }

/// Runs the model tier of the generic guest kit through HostSession.
[<AbstractClass; Sealed>]
type ModelTierRunner =
    /// Runs the model tier of kit, as crates/morphir-mck/src/mep/generic.rs plans it, and returns every step with its
    /// outcome; the legacy tier's cases are listed as not run. openSession gives a new HostSession, on a new guest
    /// instance or worker process, for each session the runner asks for: first one that only describes, then, when
    /// the describe claim plans a model tier, one that offers the request's model transport (pass OfferOrNull() to
    /// the HostSession constructor). The runner starts each session's RunAsync, sends morphir.exit at its end and
    /// waits for it to end. When a send of a session fails with an IOException, such as the WorkerEndedException of a
    /// worker that has ended, the session's transport has ended: that step fails, the session's later steps are not
    /// run, and the run keeps every step. Cancellation ends the run with an OperationCanceledException.
    static member RunAsync
        (
            kit: GuestKit,
            openSession: Func<KitSessionRequest, CancellationToken, Task<HostSession>>,
            cancellation: CancellationToken
        ) : Task<ModelTierRun> =
        task {
            let! describeStep, guest, planned = ModelTierSession.describe kit openSession cancellation

            let reason: string | null =
                match planned with
                | None -> "The describe case failed, so the kit plans no model tier."
                | Some(described, selection) when selection.ModelVersions.IsEmpty ->
                    ModelTierPlan.noModelTier described selection
                | Some _ -> null

            let! modelSteps =
                match planned with
                | Some(described, selection) when not selection.ModelVersions.IsEmpty ->
                    ModelTierSession.modelTier kit described selection openSession cancellation
                | _ -> Task.FromResult []

            let selection = planned |> Option.map snd

            let legacy =
                match selection with
                | Some selection ->
                    ModelTierPlan.legacyCases selection
                    |> List.map (fun (id, method) ->
                        KitStep(
                            id,
                            KitTier.Legacy,
                            method,
                            0,
                            0,
                            null,
                            null,
                            StepOutcome.NotRun ModelTierSession.LegacyCaseReason,
                            TimeSpan.Zero
                        ))
                | None -> []

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
                    legacy |> List.toArray :> IReadOnlyList<_>
                )
        }
