// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Mep

open System
open System.Collections.Generic
open System.Text.Json

/// Negotiation of bounded model access on the exact MEP draft.
[<RequireQualifiedAccess>]
module ModelAccess =
    let protocolVersion = ProtocolVersion.Create "0.2.0-draft.2"

    let accessVersion = SemanticVersion.Create "0.1.0-draft.1"

    let private ir31 = SemanticVersion.Create "3.1.0"
    let private ir4 = SemanticVersion.Create "4.0.0"

    let private isSupported (tuple: ModelAccessTuple) =
        tuple.ProtocolVersion = protocolVersion
        && tuple.AccessVersion = accessVersion
        && tuple.Operation = ModelOperation.GenerateModel
        && (tuple.IrVersion = ir31
            || tuple.IrVersion = ir4)
        && tuple.Profile = "morphir-ir"
        && tuple.Layout = "document-tree"

    let private validLimits (limits: ModelAccessLimits) =
        [
            int64 limits.MaxFrameBytes
            int64 limits.MaxEncodedBytesPerExchange
            int64 limits.MaxDecodedBytesPerExchange
            int64 limits.MaxDocumentsPerBatch
            int64 limits.MaxDepth
            int64 limits.MaxNodes
            int64 limits.MaxDiagnosticsPerExchange
            int64 limits.MaxOutstandingRequests
            int64 limits.MaxGuestStateBytes
            int64 limits.MaxHostCacheBytes
            int64 limits.MaxArtifactChunkBytes
            limits.MaxArtifactBytes
            limits.MaxTotalArtifactBytes
            limits.MaxSessionDiskBytes
            limits.MaxSessionWorkUnits
            int64 limits.MaxRounds
            int64 limits.MaxCallMilliseconds
            int64 limits.MaxSessionMilliseconds
        ]
        |> List.forall (fun value ->
            value > 0L
            && value
               <= 9007199254740991L
        )

    let private intersectLimits (offered: ModelAccessLimits) (supported: ModelAccessLimits) = {
        MaxFrameBytes = min offered.MaxFrameBytes supported.MaxFrameBytes
        MaxEncodedBytesPerExchange =
            min offered.MaxEncodedBytesPerExchange supported.MaxEncodedBytesPerExchange
        MaxDecodedBytesPerExchange =
            min offered.MaxDecodedBytesPerExchange supported.MaxDecodedBytesPerExchange
        MaxDocumentsPerBatch = min offered.MaxDocumentsPerBatch supported.MaxDocumentsPerBatch
        MaxDepth = min offered.MaxDepth supported.MaxDepth
        MaxNodes = min offered.MaxNodes supported.MaxNodes
        MaxDiagnosticsPerExchange =
            min offered.MaxDiagnosticsPerExchange supported.MaxDiagnosticsPerExchange
        MaxOutstandingRequests = min offered.MaxOutstandingRequests supported.MaxOutstandingRequests
        MaxGuestStateBytes = min offered.MaxGuestStateBytes supported.MaxGuestStateBytes
        MaxHostCacheBytes = min offered.MaxHostCacheBytes supported.MaxHostCacheBytes
        MaxArtifactChunkBytes = min offered.MaxArtifactChunkBytes supported.MaxArtifactChunkBytes
        MaxArtifactBytes = min offered.MaxArtifactBytes supported.MaxArtifactBytes
        MaxTotalArtifactBytes = min offered.MaxTotalArtifactBytes supported.MaxTotalArtifactBytes
        MaxSessionDiskBytes = min offered.MaxSessionDiskBytes supported.MaxSessionDiskBytes
        MaxSessionWorkUnits = min offered.MaxSessionWorkUnits supported.MaxSessionWorkUnits
        MaxRounds = min offered.MaxRounds supported.MaxRounds
        MaxCallMilliseconds = min offered.MaxCallMilliseconds supported.MaxCallMilliseconds
        MaxSessionMilliseconds = min offered.MaxSessionMilliseconds supported.MaxSessionMilliseconds
    }

    /// Agree only whole supported tuples, in host preference order.
    let internal select (offer: ModelAccessSupport) (support: ModelAccessSupport) =
        if
            not (
                validLimits offer.Limits
                && validLimits support.Limits
            )
        then
            Error(
                RpcErrors.withMessage
                    ErrorCode.InvalidParams
                    "The model-access limits must be positive and in range."
            )
        elif
            (offer.Tuples
             |> Seq.distinct
             |> Seq.length)
            <> offer.Tuples.Count
            || (support.Tuples
                |> Seq.distinct
                |> Seq.length)
               <> support.Tuples.Count
        then
            Error(
                RpcErrors.withMessage
                    ErrorCode.InvalidParams
                    "The model-access tuples must be unique."
            )
        else
            let supported = Set.ofSeq support.Tuples

            let agreed =
                offer.Tuples
                |> Seq.filter (fun tuple ->
                    isSupported tuple
                    && supported.Contains tuple
                )
                |> Seq.toArray

            Ok
                {
                    Tuples = Array.AsReadOnly agreed
                    Limits = intersectLimits offer.Limits support.Limits
                }

    /// Check the guest's answer against the exact offer that went on the wire.
    let internal verifySelection (offer: ModelAccessSupport) (selected: ModelAccessSupport) =
        match select offer selected with
        | Error error -> Error error.Message
        | Ok _ when selected.Tuples.Count = 0 ->
            Error "An empty model-access selection must omit the modelAccess member."
        | Ok agreed when
            agreed.Limits = selected.Limits
            && Seq.toList agreed.Tuples = Seq.toList selected.Tuples
            ->
            Ok agreed
        | Ok _ ->
            Error
                "The extension's model-access selection exceeds the offer or reorders or adds tuples."

    let internal validatedOffer (offered: ModelAccessSupport) =
        let snapshot =
            { offered with
                Tuples = System.Array.AsReadOnly(Seq.toArray offered.Tuples)
            }

        if snapshot.Tuples.Count = 0 then
            invalidArg "modelAccess" "A model-access offer must contain a supported tuple."

        match verifySelection snapshot snapshot with
        | Ok offer -> offer
        | Error message -> invalidArg "modelAccess" message

    /// A start may use only a live tuple, one of the extension's targets and smaller or equal limits.
    let internal admitStart (capabilities: ExtensionCapabilities) (request: ModelAccessStart) =
        let unavailable message =
            Error(RpcErrors.withMessage ErrorCode.CapabilityUnavailable message)

        let invalid message =
            Error(RpcErrors.withMessage ErrorCode.InvalidParams message)

        match capabilities.ModelAccess with
        | None -> unavailable "The session did not agree to model access."
        | Some access when
            not (
                access.Tuples
                |> Seq.contains request.Input
            )
            ->
            unavailable "The start request did not use an agreed model-access tuple."
        | Some access ->
            let offeredTarget =
                capabilities.Backend
                |> Option.exists (fun backend ->
                    backend.Targets
                    |> Seq.contains request.Target
                )

            let snapshot = request.Snapshot

            if not offeredTarget then
                unavailable "The start request did not use an advertised backend target."
            elif
                [
                    request.SessionId
                    snapshot.SnapshotId
                    snapshot.ModelId
                    snapshot.Revision
                    snapshot.Scope
                ]
                |> List.exists System.String.IsNullOrEmpty
            then
                invalid "The start request requires nonempty operation and snapshot identities."
            elif
                not (validLimits request.Limits)
                || intersectLimits request.Limits access.Limits
                   <> request.Limits
            then
                invalid
                    "The start request's limits must be positive and within the agreed ceilings."
            else
                Ok()

    let internal isMethod =
        function
        | Method.GenerateModelStart
        | Method.GenerateModelAdvance
        | Method.GenerateModelCancel -> true
        | _ -> false

    let internal validObject (forbidden: string list) (element: JsonElement) =
        element.ValueKind = JsonValueKind.Object
        && (let seen = HashSet<string>(StringComparer.Ordinal)

            element.EnumerateObject()
            |> Seq.forall (fun property ->
                seen.Add property.Name
                && not (List.contains property.Name forbidden)
            ))

    let internal validRequests (element: JsonElement) =
        element.EnumerateArray()
        |> Seq.forall (fun request ->
            let forbidden =
                if request.GetProperty("kind").GetString() = "inventory" then
                    [ "documentId" ]
                else
                    [ "selection"; "pageSize"; "cursor" ]

            validObject forbidden request
        )

    let internal validDeliveries (element: JsonElement) =
        element.EnumerateArray()
        |> Seq.forall (fun delivery ->
            if delivery.GetProperty("kind").GetString() = "inventory" then
                validObject [ "documentId"; "decodedByteLength"; "sha256"; "content" ] delivery
                && (delivery.GetProperty("entries").EnumerateArray()
                    |> Seq.forall (fun entry ->
                        match
                            entry.EnumerateObject()
                            |> Seq.toList
                        with
                        | [ item ] when item.Name = "module" -> validObject [ "name" ] item.Value
                        | [ item ] when
                            item.Name = "type"
                            || item.Name = "value"
                            ->
                            validObject [ "memberSelection" ] item.Value
                        | _ -> false
                    ))
            else
                validObject [ "entries"; "nextCursor" ] delivery
        )

type internal BoundedGenerationCall =
    | Start of ModelAccessStart
    | Advance of ModelAccessAdvance
    | Cancel of ModelAccessCancel

    member this.SessionId =
        match this with
        | Start request -> request.SessionId
        | Advance request -> request.SessionId
        | Cancel request -> request.SessionId

type private OpenGeneration = {
    SnapshotId: string
    Limits: ModelAccessLimits
    RequestIds: HashSet<string>
}

type private PendingRound = {
    Generation: OpenGeneration
    Number: int
    Requests: IReadOnlyList<ModelAccessRequest>
}

type private GenerationState =
    | InFlight
    | Awaiting of PendingRound
    | Finished

/// The operation ledger is shared in shape, but owned separately by each peer.
/// The lock protects reservations and state commits. The exchange itself runs outside it.
type internal BoundedGeneration() =
    let gate = obj ()
    let operations = Dictionary<string, GenerationState>(StringComparer.Ordinal)
    let mutable closed = false
    let invalidParams = RpcErrors.create ErrorCode.InvalidParams
    let nonempty value = not (String.IsNullOrEmpty value)

    let matches snapshot request delivery =
        match request, delivery with
        | ModelAccessRequest.Inventory(id, _, size, _),
          ModelAccessDelivery.Inventory(actualId, actualSnapshot, entries, next) ->
            id = actualId
            && snapshot = actualSnapshot
            && entries.Count
               <= size
            && Option.forall nonempty next
            && (entries
                |> Seq.forall (
                    function
                    | ModelInventoryEntry.Module entry ->
                        nonempty entry.DocumentId
                        && nonempty entry.PackageName
                        && nonempty entry.ModulePath
                        && Option.forall nonempty entry.MemberSelection
                    | ModelInventoryEntry.Type entry
                    | ModelInventoryEntry.Value entry ->
                        nonempty entry.DocumentId
                        && nonempty entry.PackageName
                        && nonempty entry.ModulePath
                        && nonempty entry.Name
                ))
        | ModelAccessRequest.Document(id, documentId),
          ModelAccessDelivery.Document(actualId, actualSnapshot, actualDocument, length, _, _) ->
            id = actualId
            && snapshot = actualSnapshot
            && documentId = actualDocument
            && length
               >= 0
        | _ -> false

    let admitDeliveries (limits: ModelAccessLimits) (request: ModelAccessAdvance) =
        let documents =
            request.Deliveries
            |> Seq.choose (
                function
                | ModelAccessDelivery.Document(_, _, _, length, digest, content) -> Some(length, digest, content)
                | ModelAccessDelivery.Inventory _ -> None
            )
            |> Seq.toArray

        // Refuse the entire batch before allocating any decoded document.
        documents.Length <= limits.MaxDocumentsPerBatch
        && (documents |> Array.sumBy (fun (_, _, content) -> int64 content.Length))
           <= int64 limits.MaxEncodedBytesPerExchange
        && (documents |> Array.sumBy (fun (length, _, _) -> int64 length))
           <= int64 limits.MaxDecodedBytesPerExchange
        && (documents
            |> Array.forall (fun (length, _, content) ->
                Base64.padding content >= 0
                && Base64.decodedLength content = int64 length))
        && (documents
            |> Array.forall (fun (_, digest, content) ->
                Base64.decodeCanonical content
                |> Option.exists (fun bytes -> Sha256Digest.Compute(ReadOnlySpan bytes) = digest)))

    let reserve (call: BoundedGenerationCall) =
        lock
            gate
            (fun () ->
                if closed then
                    Error(RpcErrors.create ErrorCode.NotInitialized)
                elif not (nonempty call.SessionId) then
                    Error invalidParams
                else
                    let previous = operations.TryGetValue call.SessionId

                    let reservation =
                        match call, previous with
                        | Start request, (false, _) ->
                            Ok(
                                {
                                    SnapshotId = request.Snapshot.SnapshotId
                                    Limits = request.Limits
                                    RequestIds = HashSet<string>(StringComparer.Ordinal)
                                },
                                None
                            )
                        | Advance request, (true, Awaiting pending) when
                            request.Round = pending.Number
                            && request.Deliveries.Count = pending.Requests.Count
                            && Seq.forall2
                                (matches pending.Generation.SnapshotId)
                                pending.Requests
                                request.Deliveries
                            ->
                            Ok(pending.Generation, Some pending)
                        | Cancel _, (true, Awaiting pending) ->
                            Ok(pending.Generation, Some pending)
                        | _ -> Error invalidParams

                    reservation
                    |> Result.map (fun reserved ->
                        operations[call.SessionId] <- InFlight
                        reserved
                    )
            )

    let finish sessionId state result =
        lock
            gate
            (fun () ->
                if closed then
                    Error(RpcErrors.create ErrorCode.NotInitialized)
                else
                    operations[sessionId] <- state
                    result
            )

    let admitRequests (generation: OpenGeneration) requests =
        requests
        |> Seq.forall (
            function
            | ModelAccessRequest.Inventory(id, selection, pageSize, cursor) ->
                nonempty id
                && nonempty selection
                && pageSize > 0
                && Option.forall nonempty cursor
                && generation.RequestIds.Add id
            | ModelAccessRequest.Document(id, documentId) ->
                nonempty id
                && nonempty documentId
                && generation.RequestIds.Add id
        )

    member _.Close() =
        lock
            gate
            (fun () ->
                closed <- true
                operations.Clear()
            )

    member _.Exchange
        (
            call: BoundedGenerationCall,
            send: unit -> System.Threading.Tasks.Task<Result<JsonElement, RpcError>>,
            decodeStep: JsonElement -> ModelAccessStep option,
            decodeCancel: JsonElement -> ModelAccessCancel option
        ) =
        task {
            match reserve call with
            | Error error -> return Error error
            | Ok(generation, pending) ->
                let sessionId = call.SessionId
                let mutable next = Finished

                try
                    let reply =
                        match call with
                        | Advance request when not (admitDeliveries generation.Limits request) ->
                            System.Threading.Tasks.Task.FromResult(Error invalidParams)
                        | _ -> send ()

                    match! reply with
                    | Error error ->
                        if error.Code = int ErrorCode.InvalidParams then
                            pending
                            |> Option.iter (fun round -> next <- Awaiting round)

                        return finish sessionId next (Error error)
                    | Ok element ->
                        let valid =
                            match call with
                            | Cancel _ ->
                                decodeCancel element
                                |> Option.exists (fun ack -> ack.SessionId = sessionId)
                            | Start _
                            | Advance _ ->
                                match decodeStep element with
                                | Some(ModelAccessStep.Continue(id, round, requests, _, diagnostics)) ->
                                    let expectedRound =
                                        pending
                                        |> Option.map (fun prior ->
                                            int64 prior.Number
                                            + 1L
                                        )
                                        |> Option.defaultValue 1L

                                    if
                                        id = sessionId
                                        && int64 round = expectedRound
                                        && round
                                           <= generation.Limits.MaxRounds
                                        && requests.Count
                                           <= generation.Limits.MaxOutstandingRequests
                                        && diagnostics.Count
                                           <= generation.Limits.MaxDiagnosticsPerExchange
                                        && admitRequests generation requests
                                    then
                                        next <-
                                            Awaiting
                                                {
                                                    Generation = generation
                                                    Number = round
                                                    Requests = requests
                                                }

                                        true
                                    else
                                        false
                                | Some(ModelAccessStep.Complete(id, _, diagnostics)) ->
                                    id = sessionId
                                    && diagnostics.Count
                                       <= generation.Limits.MaxDiagnosticsPerExchange
                                | Some(ModelAccessStep.Failed(id, diagnostics)) ->
                                    id = sessionId
                                    && diagnostics.Count > 0
                                    && diagnostics.Count
                                       <= generation.Limits.MaxDiagnosticsPerExchange
                                | None -> false

                        if valid then
                            return finish sessionId next (Ok element)
                        else
                            return
                                finish
                                    sessionId
                                    Finished
                                    (Error(
                                        RpcErrors.withMessage
                                            ErrorCode.InternalError
                                            "Invalid bounded generation result."
                                    ))
                with error ->
                    finish sessionId Finished (Ok())
                    |> ignore

                    return raise error
        }
