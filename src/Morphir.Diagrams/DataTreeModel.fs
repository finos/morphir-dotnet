// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable

/// What a data value is.
[<RequireQualifiedAccess>]
type DataValueKind =
    /// Members in source order; a key may repeat.
    | Object
    /// Items in order.
    | Array
    /// Text. YAML scalars such as `true` and `3` are text too, as PlantUML reads them.
    | String
    /// A JSON number, kept as its exact lexeme.
    | Number
    /// `true` or `false`.
    | Boolean
    /// `null`.
    | Null

    /// The kind as documentation writes it.
    member this.WireValue =
        match this with
        | DataValueKind.Object -> "object"
        | DataValueKind.Array -> "array"
        | DataValueKind.String -> "string"
        | DataValueKind.Number -> "number"
        | DataValueKind.Boolean -> "boolean"
        | DataValueKind.Null -> "null"

    override this.ToString() = this.WireValue

/// A JSON or YAML value. Each value is a distinct occurrence, even when its text repeats.
[<Sealed>]
type DataValue internal (kind: DataValueKind, text: string, members: ImmutableArray<DataMember>, items: ImmutableArray<DataValue>) =
    /// What the value is.
    member _.Kind = kind

    /// A scalar's text: the string, the number's lexeme, `true`, `false` or `null`; empty for containers.
    member _.Text = text

    /// An object's members in source order; empty for other kinds.
    member _.Members = members

    /// An array's items in order; empty for other kinds.
    member _.Items = items

    /// The children of a container, in order.
    member internal _.Children =
        match kind with
        | DataValueKind.Object -> members |> Seq.map (fun item -> item.Value) |> Seq.toList
        | DataValueKind.Array -> List.ofSeq items
        | _ -> []

    override _.ToString() =
        match kind with
        | DataValueKind.Object -> $"object with {members.Length} members"
        | DataValueKind.Array -> $"array with {items.Length} items"
        | _ -> $"{kind} {text}"

/// One member of an object.
and [<Sealed>] DataMember internal (key: string, value: DataValue) =
    /// The member's key.
    member _.Key = key

    /// The member's value.
    member _.Value = value

    override _.ToString() = key

/// Which syntax a data tree was read from.
[<RequireQualifiedAccess>]
type DataFormat =
    /// A PlantUML `@startjson` block.
    | Json
    /// A PlantUML `@startyaml` block.
    | Yaml

    /// The format as documentation writes it.
    member this.WireValue =
        match this with
        | DataFormat.Json -> "json"
        | DataFormat.Yaml -> "yaml"

    override this.ToString() = this.WireValue

/// A `#highlight` selector: keys, array positions, `*` for any one member and `**` for any depth.
[<Sealed>]
type DataHighlight internal (path: ImmutableArray<string>, stereotype: string option) =
    /// The path segments, unquoted. A quoted segment keeps a `/` inside it.
    member _.Path = path

    /// The stereotype after the path, such as `<<bold>>`, or null. It names a style that is not applied.
    member _.Stereotype: string | null = Option.toObj stereotype

    override _.ToString() = String.Join(" / ", path)

/// A data tree of a PlantUML JSON or YAML document.
[<Sealed>]
type DataTreeDiagram internal (format: DataFormat, root: DataValue, title: string option, highlights: ImmutableArray<DataHighlight>) =
    static let span = SourceSpan(SourcePosition(0, 1, 1), SourcePosition(0, 1, 1))

    /// A segment of a JSON pointer: `~` and `/` escaped as RFC 6901 writes them.
    static let escape (key: string) = key.Replace("~", "~0").Replace("/", "~1")

    /// Each value with its identifier: `$` and the JSON pointer of the value. A repeated key's later members add `~2`,
    /// `~3` and so on, which no escaped key contains.
    let occurrences =
        lazy
            [
                let rec walk (id: string) (key: string) (value: DataValue) =
                    seq {
                        yield id, key, value

                        match value.Kind with
                        | DataValueKind.Object ->
                            let seen = Dictionary<string, int>(StringComparer.Ordinal)

                            for item in value.Members do
                                let count = (match seen.TryGetValue item.Key with | true, count -> count | _ -> 0) + 1
                                seen[item.Key] <- count
                                let suffix = if count = 1 then "" else $"~{count}"
                                yield! walk $"{id}/{escape item.Key}{suffix}" item.Key item.Value
                        | DataValueKind.Array ->
                            for index in 0 .. value.Items.Length - 1 do
                                yield! walk $"{id}/{index}" (string index) value.Items[index]
                        | _ -> ()
                    }

                yield! walk "$" "" root
            ]

    let identities =
        lazy
            (let found = Dictionary<DataValue, string>(HashIdentity.Reference)
             for id, _, value in occurrences.Value do found[value] <- id
             found)

    /// The values each selector picks, in document order; a selector that picks nothing is unresolved.
    let resolved =
        lazy
            (highlights
             |> Seq.map (fun highlight ->
                 let rec pick (segments: string list) (value: DataValue) : DataValue seq =
                     seq {
                         match segments with
                         | [] -> yield value
                         | "**" :: rest ->
                             yield! pick rest value
                             for child in value.Children do yield! pick segments child
                         | "*" :: rest -> for child in value.Children do yield! pick rest child
                         | segment :: rest ->
                             match value.Kind with
                             | DataValueKind.Object ->
                                 for item in value.Members do
                                     if item.Key = segment then yield! pick rest item.Value
                             | DataValueKind.Array ->
                                 match Int32.TryParse(segment, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                                 | true, index when index < value.Items.Length -> yield! pick rest value.Items[index]
                                 | _ -> ()
                             | _ -> ()
                     }

                 let picked = pick (List.ofSeq highlight.Path) root |> Seq.map (fun value -> identities.Value[value]) |> Seq.distinct |> Seq.toList
                 highlight, picked)
             |> Seq.toList)

    let projection =
        lazy
            (let label (key: string) (value: DataValue) =
                match value.Kind with
                | DataValueKind.Object
                | DataValueKind.Array -> if key = "" then value.Kind.WireValue else key
                | _ -> if key = "" then value.Text else $"{key}: {value.Text}"

             let nodes =
                 occurrences.Value |> List.map (fun (id, key, value) -> FlowNode(DiagramId.Create id, label key value, FlowShape.Rectangle, span))

             // Hierarchy: each container links to the containers inside it.
             let edges =
                 [
                     for id, _, value in occurrences.Value do
                         for child in value.Children do
                             if child.Kind = DataValueKind.Object || child.Kind = DataValueKind.Array then
                                 FlowEdge(DiagramId.Create id, DiagramId.Create identities.Value[child], None, FlowLine.Solid, EdgeHead.None, EdgeHead.Arrow, span)
                 ]

             Flowchart(FlowDirection.LeftToRight, ImmutableArray.CreateRange nodes, ImmutableArray.CreateRange edges, ImmutableArray.Empty))

    /// The syntax the tree was read from.
    member _.Format = format

    /// The root value; a scalar or empty container is kept as it is.
    member _.Root = root

    /// The title, or null.
    member _.Title: string | null = Option.toObj title

    /// The `#highlight` selectors in source order.
    member _.Highlights = highlights

    /// The identifier of every value in document order: `$` for the root, then `$` and the value's JSON pointer, such as
    /// `$/server/port` or `$/items/0`. A repeated key's later members add `~2`, `~3` and so on.
    member _.OccurrenceIds = occurrences.Value |> List.map (fun (id, _, _) -> id) |> ImmutableArray.CreateRange

    /// The identifier of a value of this tree, or null for a value of another tree.
    member _.OccurrenceOf(value: DataValue | null) : string | null =
        match value with
        | null -> null
        | value ->
            match identities.Value.TryGetValue value with
            | true, id -> id
            | false, _ -> null

    /// The identifiers of the values that the highlights select, in document order.
    member _.HighlightedIds =
        let picked = resolved.Value |> List.collect snd |> HashSet
        occurrences.Value |> List.map (fun (id, _, _) -> id) |> List.filter picked.Contains |> ImmutableArray.CreateRange

    member internal _.Unresolved = resolved.Value |> List.filter (snd >> List.isEmpty) |> List.map fst

    member internal _.Occurrences = occurrences.Value

    member internal _.Projection = projection.Value

    override _.ToString() = $"{format} data tree, {root}"
