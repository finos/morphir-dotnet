// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Linq
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open Morphir.Knowledge

/// A parsed diagram bound to one revision of a knowledge document, the counterpart of TextSnapshot.
[<Sealed>]
type DiagramSnapshot private (version: DocumentVersion, document: DiagramDocument, elements: ImmutableArray<DiagramElementRef>) =
    let order =
        let result = Dictionary<DiagramElementRef, int>()
        elements |> Seq.iteri (fun index reference -> result[reference] <- index)
        result

    static let check (version: DocumentVersion) (document: DiagramDocument) =
        ArgumentNullException.ThrowIfNull version
        ArgumentNullException.ThrowIfNull version.Document
        ArgumentNullException.ThrowIfNull version.Revision
        ArgumentNullException.ThrowIfNull document
        let iri = version.Document.Iri

        if iri.Value.Contains '#' then
            Error(DiagramReferenceError.DocumentHasFragment iri)
        else
            Ok(DiagramSnapshot(version, document, DiagramElementRef.Enumerate document.Diagram))

    /// The document and revision.
    member _.Version = version

    /// The parsed diagram and its exact source.
    member _.Document = document

    /// Every element of the diagram, by kind and then in source order.
    member _.Elements = elements

    /// Whether the diagram has the element.
    member _.Contains(reference: DiagramElementRef) =
        ArgumentNullException.ThrowIfNull reference
        order.ContainsKey reference

    member internal _.IndexOf(reference: DiagramElementRef) = order[reference]

    /// The element IRI: the document IRI with the reference as its fragment.
    member _.ElementIri(reference: DiagramElementRef) =
        ArgumentNullException.ThrowIfNull reference
        Iri.Create(version.Document.Iri.Value + "#" + reference.ToString())

    /// The element that an IRI names, when it is an element IRI of this snapshot's document and the
    /// diagram has the element.
    member this.TryResolve(iri: Iri, [<Out; MaybeNullWhen(false)>] reference: byref<DiagramElementRef>) =
        ArgumentNullException.ThrowIfNull iri

        match DiagramElementRef.ParseIri(version.Document.Iri, iri) with
        | Ok value when this.Contains value ->
            reference <- value
            true
        | _ ->
            reference <- Unchecked.defaultof<_>
            false

    override _.ToString() = version.ToString()

    /// Binds a parsed diagram to a revision. A document IRI with a fragment is an error, because the
    /// element references own the fragment. For F# callers.
    static member TryCreate(version: DocumentVersion, document: DiagramDocument) : Result<DiagramSnapshot, DiagramReferenceError> =
        check version document

    /// Binds a parsed diagram to a revision. For C# callers.
    static member TryCreate
        (
            version: DocumentVersion,
            document: DiagramDocument,
            [<Out; MaybeNullWhen(false)>] snapshot: byref<DiagramSnapshot>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramReferenceError>
        ) =
        match check version document with
        | Ok value ->
            snapshot <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            snapshot <- Unchecked.defaultof<_>
            error <- failure
            false

    /// The revision derived from the source: sha256: and the lowercase hex SHA-256 of its UTF-8 text,
    /// including its line endings.
    static member SourceRevision(document: DiagramDocument) =
        ArgumentNullException.ThrowIfNull document
        RevisionId.Create("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes document.Source)))

    /// Binds a parsed diagram to the revision derived from its source. For F# callers.
    static member TryFromSource(document: DocumentId, diagram: DiagramDocument) : Result<DiagramSnapshot, DiagramReferenceError> =
        ArgumentNullException.ThrowIfNull document
        ArgumentNullException.ThrowIfNull diagram
        check { Document = document; Revision = DiagramSnapshot.SourceRevision diagram } diagram

    /// Binds a parsed diagram to the revision derived from its source. For C# callers.
    static member TryFromSource
        (
            document: DocumentId,
            diagram: DiagramDocument,
            [<Out; MaybeNullWhen(false)>] snapshot: byref<DiagramSnapshot>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramReferenceError>
        ) =
        match DiagramSnapshot.TryFromSource(document, diagram) with
        | Ok value ->
            snapshot <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            snapshot <- Unchecked.defaultof<_>
            error <- failure
            false

/// Why a diagram selection could not be created or read, the counterpart of SelectionError.
[<RequireQualifiedAccess>]
type DiagramSelectionError =
    /// A selection must contain at least one element.
    | Empty
    /// The diagram revision has no such element.
    | UnknownElement of DiagramElementRef
    /// The snapshot is a different document revision.
    | VersionMismatch

    /// For C# callers: every case needs a function.
    member this.Match(empty: Func<'T>, unknownElement: Func<DiagramElementRef, 'T>, versionMismatch: Func<'T>) =
        match this with
        | DiagramSelectionError.Empty -> empty.Invoke()
        | DiagramSelectionError.UnknownElement reference -> unknownElement.Invoke reference
        | DiagramSelectionError.VersionMismatch -> versionMismatch.Invoke()

    override this.ToString() =
        match this with
        | DiagramSelectionError.Empty -> "A diagram selection must contain at least one element."
        | DiagramSelectionError.UnknownElement reference -> $"The diagram has no element {reference}."
        | DiagramSelectionError.VersionMismatch -> "The snapshot is a different document revision."

/// One annotation target made of elements of one diagram revision, the counterpart of TextSelection.
/// Equality compares the version and the elements.
[<Sealed>]
type DiagramSelection private (version: DocumentVersion, elements: ImmutableArray<DiagramElementRef>) =
    /// The revision whose elements are selected.
    member _.Version = version

    /// The selected elements without duplicates, by kind and then in source order.
    member _.Elements = elements

    /// The selected elements, when the snapshot is the selection's revision. For F# callers.
    member _.TryRead(snapshot: DiagramSnapshot) : Result<ImmutableArray<DiagramElementRef>, DiagramSelectionError> =
        ArgumentNullException.ThrowIfNull snapshot

        if snapshot.Version <> version then Error DiagramSelectionError.VersionMismatch
        else Ok elements

    /// The selected elements, when the snapshot is the selection's revision. For C# callers.
    member this.TryRead
        (
            snapshot: DiagramSnapshot,
            [<Out>] result: byref<ImmutableArray<DiagramElementRef>>,
            [<Out; MaybeNullWhen(true)>] failure: byref<DiagramSelectionError>
        ) =
        match this.TryRead snapshot with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- ImmutableArray.Empty
            failure <- error
            false

    override _.ToString() = $"{version} ({elements.Length} element(s))"

    override _.Equals(other: obj | null) =
        match other with
        | :? DiagramSelection as selection -> selection.Version = version && selection.Elements.SequenceEqual elements
        | _ -> false

    override _.GetHashCode() =
        elements |> Seq.fold (fun hash reference -> HashCode.Combine(hash, reference)) (version.GetHashCode())

    /// Selects elements of one snapshot. Unknown elements and an empty selection are errors;
    /// duplicates collapse. For F# callers.
    static member TryCreate(snapshot: DiagramSnapshot, elements: IEnumerable<DiagramElementRef>) : Result<DiagramSelection, DiagramSelectionError> =
        ArgumentNullException.ThrowIfNull snapshot
        ArgumentNullException.ThrowIfNull elements
        let requested = elements |> Seq.toArray

        for reference in requested do
            ArgumentNullException.ThrowIfNull reference

        match requested |> Array.tryFind (fun reference -> not (snapshot.Contains reference)) with
        | Some unknown -> Error(DiagramSelectionError.UnknownElement unknown)
        | None when requested.Length = 0 -> Error DiagramSelectionError.Empty
        | None ->
            let ordered = requested |> Array.distinct |> Array.sortBy snapshot.IndexOf
            Ok(DiagramSelection(snapshot.Version, ImmutableArray.CreateRange ordered))

    /// Selects elements of one snapshot. For C# callers.
    static member TryCreate
        (
            snapshot: DiagramSnapshot,
            elements: IEnumerable<DiagramElementRef>,
            [<Out; MaybeNullWhen(false)>] selection: byref<DiagramSelection>,
            [<Out; MaybeNullWhen(true)>] failure: byref<DiagramSelectionError>
        ) =
        match DiagramSelection.TryCreate(snapshot, elements) with
        | Ok value ->
            selection <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            selection <- Unchecked.defaultof<_>
            failure <- error
            false
