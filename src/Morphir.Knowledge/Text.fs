// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.CompilerServices
open System.Runtime.InteropServices

/// A zero-based position in UTF-16 code units.
[<Struct; IsReadOnly>]
type TextOffset =
    private
    | TextOffset of int

    /// The number of UTF-16 code units before this position.
    member this.Value =
        let (TextOffset value) = this
        value

    override this.ToString() = this.Value.ToString(Globalization.CultureInfo.InvariantCulture)

    /// Constructs a nonnegative offset. For F# callers.
    static member TryCreate(value: int) =
        if value >= 0 then ValueSome(TextOffset value) else ValueNone

    /// Constructs a nonnegative offset. For C# callers.
    static member TryCreate(value: int, [<Out>] result: byref<TextOffset>) =
        match TextOffset.TryCreate value with
        | ValueSome offset ->
            result <- offset
            true
        | ValueNone ->
            result <- Unchecked.defaultof<_>
            false

    /// Constructs an offset and throws if it is negative.
    static member Create(value: int) =
        match TextOffset.TryCreate value with
        | ValueSome offset -> offset
        | ValueNone -> invalidArg (nameof value) "An offset cannot be negative."

/// A half-open range in one source. An empty range represents a position.
[<Struct; IsReadOnly>]
type SourceSpan =
    private
    | SourceSpan of start: TextOffset * endExclusive: TextOffset

    /// The included start offset.
    member this.Start =
        let (SourceSpan(start, _)) = this
        start

    /// The excluded end offset.
    member this.EndExclusive =
        let (SourceSpan(_, finish)) = this
        finish

    /// The length in UTF-16 code units.
    member this.Length = this.EndExclusive.Value - this.Start.Value

    override this.ToString() = $"[{this.Start}, {this.EndExclusive})"

    /// Constructs an ordered range. For F# callers.
    static member TryCreate(start: TextOffset, endExclusive: TextOffset) =
        if start.Value <= endExclusive.Value then
            ValueSome(SourceSpan(start, endExclusive))
        else
            ValueNone

    /// Constructs an ordered range. For C# callers.
    static member TryCreate(start: TextOffset, endExclusive: TextOffset, [<Out>] result: byref<SourceSpan>) =
        match SourceSpan.TryCreate(start, endExclusive) with
        | ValueSome span ->
            result <- span
            true
        | ValueNone ->
            result <- Unchecked.defaultof<_>
            false

    /// Constructs a range and throws if its endpoints are reversed.
    static member Create(start: TextOffset, endExclusive: TextOffset) =
        match SourceSpan.TryCreate(start, endExclusive) with
        | ValueSome span -> span
        | ValueNone -> invalidArg (nameof endExclusive) "The end of a span cannot precede its start."

/// The identity of an immutable document revision.
type DocumentVersion =
    {
        /// The logical document.
        Document: DocumentId
        /// The provider's key scoped to the document.
        Revision: RevisionId
    }

    override this.ToString() = $"{this.Document} @ {this.Revision}"

/// Immutable text held by a provider for a single document revision.
[<Sealed>]
type TextSnapshot private (version: DocumentVersion, text: string) =
    /// The provider's identity for this text.
    member _.Version = version

    /// The original text for this revision.
    member _.Text = text

    override _.ToString() = version.ToString()

    /// Reads the range if it fits the snapshot. For F# callers.
    member _.TryRead(span: SourceSpan) =
        if span.EndExclusive.Value <= text.Length then
            ValueSome(text.Substring(span.Start.Value, span.Length))
        else
            ValueNone

    /// Reads the range if it fits the snapshot. For C# callers.
    member this.TryRead(span: SourceSpan, [<Out; MaybeNullWhen(false)>] result: byref<string>) =
        match this.TryRead span with
        | ValueSome value ->
            result <- value
            true
        | ValueNone ->
            result <- Unchecked.defaultof<_>
            false

    /// Binds text to a version. The provider must resolve the version to identical text on subsequent reads.
    static member Create(version: DocumentVersion, text: string) =
        ArgumentNullException.ThrowIfNull version
        ArgumentNullException.ThrowIfNull version.Document
        ArgumentNullException.ThrowIfNull version.Revision
        ArgumentNullException.ThrowIfNull text
        TextSnapshot(version, text)

/// Why a selection could not be created or read.
[<RequireQualifiedAccess>]
type SelectionError =
    | EmptySelection
    | EmptyRange of SourceSpan
    | OutOfBounds of SourceSpan
    | OverlappingRanges of first: SourceSpan * second: SourceSpan
    | VersionMismatch

    override this.ToString() =
        match this with
        | SelectionError.EmptySelection -> "A selection must contain at least one range."
        | SelectionError.EmptyRange span -> $"The range {span} is empty."
        | SelectionError.OutOfBounds span -> $"The range {span} extends beyond the source."
        | SelectionError.OverlappingRanges(first, second) -> $"The ranges {first} and {second} overlap."
        | SelectionError.VersionMismatch -> "The source is a different document revision."

/// One annotation target made of disjoint text ranges in a single revision.
[<Sealed>]
type TextSelection private (version: DocumentVersion, ranges: ImmutableArray<SourceSpan>) =
    /// The specific revision whose offsets are used.
    member _.Version = version

    /// Nonempty, non-overlapping ranges in source order.
    member _.Ranges = ranges

    override _.ToString() = $"{version} ({ranges.Length} ranges)"

    /// Reads each selected passage in source order. For F# callers.
    member _.TryRead(snapshot: TextSnapshot) =
        if version <> snapshot.Version then
            Error SelectionError.VersionMismatch
        else
            match ranges |> Seq.tryFind (fun span -> span.EndExclusive.Value > snapshot.Text.Length) with
            | Some span -> Error(SelectionError.OutOfBounds span)
            | None ->
                let text = ImmutableArray.CreateBuilder<string>(ranges.Length)

                for span in ranges do
                    text.Add(snapshot.Text.Substring(span.Start.Value, span.Length))

                Ok(text.MoveToImmutable())

    /// Reads each selected passage in source order. For C# callers.
    member this.TryRead
        (
            snapshot: TextSnapshot,
            [<Out>] result: byref<ImmutableArray<string>>,
            [<Out; MaybeNullWhen(true)>] failure: byref<SelectionError>
        ) =
        match this.TryRead snapshot with
        | Ok text ->
            result <- text
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- ImmutableArray<string>.Empty
            failure <- error
            false

    /// Groups ranges of one snapshot into a selection in source order. For F# callers.
    static member TryCreate(snapshot: TextSnapshot, ranges: IEnumerable<SourceSpan>) =
        ArgumentNullException.ThrowIfNull snapshot
        ArgumentNullException.ThrowIfNull ranges
        let sorted = ranges |> Seq.toArray
        Array.sortInPlace sorted

        if sorted.Length = 0 then
            Error SelectionError.EmptySelection
        else
            let rec check index =
                if index = sorted.Length then
                    Ok(TextSelection(snapshot.Version, ImmutableArray.CreateRange sorted))
                else
                    let span = sorted[index]

                    if span.Length = 0 then
                        Error(SelectionError.EmptyRange span)
                    elif span.EndExclusive.Value > snapshot.Text.Length then
                        Error(SelectionError.OutOfBounds span)
                    elif index > 0 && sorted[index - 1].EndExclusive.Value > span.Start.Value then
                        Error(SelectionError.OverlappingRanges(sorted[index - 1], span))
                    else
                        check (index + 1)

            check 0

    /// Groups ranges of one snapshot into a selection in source order. For C# callers.
    static member TryCreate
        (
            snapshot: TextSnapshot,
            ranges: IEnumerable<SourceSpan>,
            [<Out; MaybeNullWhen(false)>] result: byref<TextSelection>,
            [<Out; MaybeNullWhen(true)>] failure: byref<SelectionError>
        ) =
        match TextSelection.TryCreate(snapshot, ranges) with
        | Ok selection ->
            result <- selection
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false
