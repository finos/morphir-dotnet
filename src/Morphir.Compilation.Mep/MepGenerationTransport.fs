// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Compilation.Mep

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.Trees
open Morphir.Mep

/// <summary>
/// The model transport of <c>morphir.backend.generateModel</c> on the host side: the JSON IR tuples a host offers, and
/// the tuple a model needs. The decision is here, in F#, with an exhaustive match on <see cref="ModelFamily"/>, so a new
/// model family fails to compile until it is decided; C# callers (the generation backend adapter) call these members
/// and do not test the family's cases.
/// </summary>
[<AbstractClass; Sealed>]
type MepGenerationTransport =

    static let json (irVersion: string) : ModelTransportTuple = {
        Operation = ModelOperation.GenerateModel
        Profile = "morphir-ir"
        Codec = ModelCodec.MorphirJson
        EnvelopeVersion = SemanticVersion.Create "0.1.0-draft.1"
        IrVersion = SemanticVersion.Create irVersion
    }

    // Functions, not values: a class `static let` value is a static field behind an F# init check. When two threads
    // start the type initialisers of two types that need each other, the CLR breaks the deadlock by letting one thread
    // see this type before its initialiser has finished, and the init check then throws (FailStaticInit). With no
    // static field the type has no initialiser to meet. The tuples are records, so each call gives an equal value.
    static let v4 () = json "4.0.0"
    static let classic () = json "3.0.0"

    /// <summary>The JSON IR 4.0.0 tuple: an IR v4 model with formatVersion 4 or "4.0.0".</summary>
    static member V4Tuple = v4 ()

    /// <summary>The JSON IR 3.0.0 tuple: a classic model with formatVersion 3 or "3.0.0".</summary>
    static member ClassicTuple = classic ()

    /// <summary>The tuples a host offers, IR 4.0.0 first.</summary>
    static member Tuples: IReadOnlyList<ModelTransportTuple> = [| v4 (); classic () |]

    /// <summary>
    /// The tuple that a model of the family and declared format version needs, for C# callers: true and the tuple, or
    /// false when the host sends no tuple for it (for example classic versions 1 and 2, or a version that its family
    /// does not have).
    /// </summary>
    static member TryTupleFor
        (
            family: ModelFamily,
            version: string,
            [<Out; MaybeNullWhen(false)>] tuple: byref<ModelTransportTuple>
        ) : bool =
        let needed =
            match family with
            | ModelFamily.V4 ->
                if
                    version = "4"
                    || version = "4.0.0"
                then
                    Some(v4 ())
                else
                    None
            | ModelFamily.Classic ->
                if
                    version = "3"
                    || version = "3.0.0"
                then
                    Some(classic ())
                else
                    None

        match needed with
        | Some found ->
            tuple <- found
            true
        | None ->
            tuple <- Unchecked.defaultof<_>
            false

/// A negotiated generation view over one caller-owned tree snapshot.
[<Sealed>]
type MepGenerationSnapshot private (snapshot: BoundedTreeSnapshot, start: ModelAccessStart, work: BoundedTreeWorkBudget) =
    static let refuse code message : Result<'value, StoreError list> =
        Result.Error [
            {
                Code = code
                File = None
                Message = message
            }
        ]

    let names address =
        let package, document =
            match address with
            | Own value -> snapshot.Package, value
            | Dependency(package, value) -> package, value
            | Manifest -> invalidOp "The manifest cannot appear as an inventory entry."

        let (PackageName packagePath) = package

        let (ModulePath path) =
            match document with
            | Module path
            | TypeDef(path, _)
            | ValueDef(path, _) -> path

        CanonicalNames.pathToString packagePath, CanonicalNames.pathToString path

    let inventoryRole =
        function
        | BoundedTreeRole.Definition -> ModelInventoryRole.Definition
        | BoundedTreeRole.Specification -> ModelInventoryRole.Specification

    let inventoryEntry =
        function
        | BoundedTreeInventoryEntry.ModuleEntry item ->
            let packageName, modulePath = names item.Address

            ModelInventoryEntry.Module
                {
                    DocumentId = item.DocumentId
                    PackageName = packageName
                    ModulePath = modulePath
                    Role = inventoryRole item.Role
                    MemberSelection = Some item.DocumentId
                }
        | BoundedTreeInventoryEntry.MemberEntry(item, role) ->
            let packageName, modulePath = names item.Address

            let entry name = {
                DocumentId = item.DocumentId
                PackageName = packageName
                ModulePath = modulePath
                Role = inventoryRole role
                Name = CanonicalNames.nameToString name
            }

            match item.Address with
            | Own(TypeDef(_, name))
            | Dependency(_, TypeDef(_, name)) -> ModelInventoryEntry.Type(entry name)
            | Own(ValueDef(_, name))
            | Dependency(_, ValueDef(_, name)) -> ModelInventoryEntry.Value(entry name)
            | _ -> invalidOp "A member inventory entry must identify a type or value."

    /// Bind the announced snapshot and selected input tuple to the admitted reader.
    static member Bind
        (
            snapshot: BoundedTreeSnapshot,
            start: ModelAccessStart,
            work: BoundedTreeWorkBudget
        ) : Result<MepGenerationSnapshot, StoreError list> =
        let codec =
            match snapshot.Syntax with
            | BoundedTreeSyntax.Json -> ModelAccessCodec.MorphirJson
            | BoundedTreeSyntax.Yaml -> ModelAccessCodec.MorphirYaml

        let encoding =
            match snapshot.NameEncoding with
            | BoundedTreeEncoding.Plain -> ModelAccessEncoding.Plain
            | BoundedTreeEncoding.ContextualDraft1
            | BoundedTreeEncoding.ContextualDraft1Streaming -> ModelAccessEncoding.ContextualDraft1

        let expected = {
            ProtocolVersion = ModelAccess.protocolVersion
            AccessVersion = ModelAccess.accessVersion
            Operation = ModelOperation.GenerateModel
            IrVersion = SemanticVersion.Create snapshot.IrVersion
            Profile = "morphir-ir"
            Codec = codec
            Layout = "document-tree"
            ContextualEncoding = encoding
        }

        if
            start.Input
            <> expected
        then
            refuse
                "tree.tuple_mismatch"
                "The selected generation tuple does not describe the admitted tree."
        elif
            start.Snapshot.Revision
            <> snapshot.RevisionId
        then
            refuse
                "tree.revision_mismatch"
                "The announced revision does not match the admitted tree."
        elif start.Limits.MaxDepth <= 0 || start.Limits.MaxNodes <= 0 then
            refuse "tree.invalid_limit" "The negotiated maxDepth and maxNodes must be positive."
        else
            let restricted = snapshot.WithDocumentLimits(start.Limits.MaxDepth, start.Limits.MaxNodes).WithWorkBudget(work)
            Ok(MepGenerationSnapshot(restricted, start, work))

    /// Bind a delivery-only view using its negotiated work allowance.
    static member Bind(snapshot: BoundedTreeSnapshot, start: ModelAccessStart) : Result<MepGenerationSnapshot, StoreError list> =
        if start.Limits.MaxSessionWorkUnits <= 0L then
            refuse "tree.invalid_limit" "The negotiated maxSessionWorkUnits must be positive."
        else
            MepGenerationSnapshot.Bind(snapshot, start, BoundedTreeWorkBudget(start.Limits.MaxSessionWorkUnits))

    /// Bind the delivery view for a host using a shared work budget.
    static member BindForHost(snapshot: BoundedTreeSnapshot, start: ModelAccessStart, work: BoundedTreeWorkBudget)
        : Result<MepGenerationSnapshot, IReadOnlyList<StoreError>> =
        MepGenerationSnapshot.Bind(snapshot, start, work)
        |> Result.mapError (fun errors -> List.toArray errors :> IReadOnlyList<StoreError>)

    /// Prepare the ordered deliveries for the supplied continuation.
    member _.PrepareAdvance
        (
            round: int,
            requests: IReadOnlyList<ModelAccessRequest>
        ) : Result<ModelAccessAdvance, StoreError list> =
        let limits = start.Limits

        if
            round < 1
            || round > limits.MaxRounds
            || requests.Count > limits.MaxOutstandingRequests
        then
            refuse
                "tree.delivery_limit"
                "The generation round or its outstanding requests exceed the admitted limit."
        else
            let deliveries = ResizeArray<ModelAccessDelivery>(requests.Count)
            let mutable encoded = int64 limits.MaxEncodedBytesPerExchange
            let mutable decoded = int64 limits.MaxDecodedBytesPerExchange
            let mutable documents = limits.MaxDocumentsPerBatch
            let mutable nodes = limits.MaxNodes
            let mutable failure = None
            let mutable index = 0
            let snapshotId = start.Snapshot.SnapshotId
            let exhausted () = refuse "extension.bounded_work_limit" "Processing exceeds the negotiated maxSessionWorkUnits."

            while index < requests.Count
                  && failure.IsNone do
                let next =
                    match work.TryCharge 1L, requests[index] with
                    | false, _ -> exhausted ()
                    | true, ModelAccessRequest.Inventory(id, selection, size, cursor) ->
                        // A delivery may have fewer entries than requested. Respect the reader's page ceiling.
                        DocumentTreeStore.pageInventory selection (min size 256) cursor snapshot
                        |> Result.map (fun page ->
                            let entries =
                                page.Entries
                                |> List.map inventoryEntry
                                |> List.toArray

                            ModelAccessDelivery.Inventory(
                                id,
                                snapshotId,
                                entries,
                                page.Continuation
                            )
                        )
                    | true, ModelAccessRequest.Document(id, documentId) ->
                        if
                            documents
                            <= 0
                        then
                            refuse
                                "tree.delivery_limit"
                                "The delivery batch exceeds its document count."
                        else
                            let available =
                                min
                                    decoded
                                    (encoded
                                     / 4L
                                     * 3L)

                            DocumentTreeStore.readDocumentWithinCounted documentId available nodes snapshot
                            |> Result.bind (fun (bytes, decodedNodes) ->
                                if not (work.TryCharge(2L * BoundedTreeWorkBudget.Blocks(int64 bytes.Length))) then
                                    exhausted ()
                                else
                                    let content = Convert.ToBase64String bytes.Span
                                    nodes <- nodes - decodedNodes

                                    documents <- documents - 1
                                    encoded <- encoded - int64 content.Length
                                    decoded <- decoded - int64 bytes.Length

                                    Ok(ModelAccessDelivery.Document(
                                        id,
                                        snapshotId,
                                        documentId,
                                        bytes.Length,
                                        Sha256Digest.Compute bytes.Span,
                                        content
                                    ))
                            )

                match next with
                | Ok delivery -> deliveries.Add delivery
                | Result.Error errors -> failure <- Some errors

                index <- index + 1

            match failure with
            | Some errors -> Result.Error errors
            | None ->
                Ok
                    {
                        SessionId = start.SessionId
                        Round = round
                        Deliveries = deliveries.ToArray()
                    }

    /// Prepare host deliveries with a read-only error collection.
    member this.PrepareHostAdvance(round: int, requests: IReadOnlyList<ModelAccessRequest>)
        : Result<ModelAccessAdvance, IReadOnlyList<StoreError>> =
        this.PrepareAdvance(round, requests)
        |> Result.mapError (fun errors -> List.toArray errors :> IReadOnlyList<StoreError>)
