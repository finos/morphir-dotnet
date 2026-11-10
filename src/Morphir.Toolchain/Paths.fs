// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Text

/// One step of a document member path.
[<RequireQualifiedAccess>]
type PathSegment =
    | Member of name: string
    | Index of index: int

/// <summary>
/// A member path in a toolchain document, as the stable codes report it: dotted member names with <c>[index]</c> for
/// array elements, for example <c>platforms.win-x64.providers[0].artifactTarget</c>. The empty path means the document
/// itself.
/// </summary>
[<CustomEquality; NoComparison>]
type DocumentPath =
    private
    | DocumentPath of reversed: PathSegment list

    /// The segments from the document root.
    member this.Segments =
        let (DocumentPath reversed) = this
        List.rev reversed

    /// The path of a member of this path's object.
    member this.Child(name: string) =
        let (DocumentPath reversed) = this
        DocumentPath(PathSegment.Member name :: reversed)

    /// The path of an element of this path's array.
    member this.At(index: int) =
        let (DocumentPath reversed) = this
        DocumentPath(PathSegment.Index index :: reversed)

    /// The rendered path, such as <c>toolset.providers[0].delivery</c>; empty for the document itself.
    override this.ToString() =
        let text = StringBuilder()

        for segment in this.Segments do
            match segment with
            | PathSegment.Member name ->
                if text.Length > 0 then
                    text.Append('.') |> ignore

                text.Append(name) |> ignore
            | PathSegment.Index index -> text.Append('[').Append(index).Append(']') |> ignore

        text.ToString()

    override this.Equals(other) =
        match other with
        | :? DocumentPath as path -> String.Equals(this.ToString(), path.ToString(), StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode(this.ToString())

/// Constructors for document paths.
module DocumentPath =
    /// The document itself.
    let root = DocumentPath []

    /// <summary>A path exactly as a document writes it, such as a <c>critical</c> entry or a result's <c>path</c>
    /// member. It is one segment, never split at dots, so it renders as written: <c>.host</c> stays <c>.host</c>.
    /// </summary>
    let verbatim (text: string) =
        if String.IsNullOrEmpty text then root else root.Child text

    /// The rendered text.
    let render (path: DocumentPath) = path.ToString()
