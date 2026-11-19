// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.IO
open System.IO.Compression
open System.Text.Json
open System.Text.RegularExpressions
open Morphir.Svg

/// The art of an icon: a provider or UML symbol copied unchanged, or a symbol drawn for this library.
type internal IconArt =
    | Copied of tree: JsonElement * family: string * attribution: string * terms: string
    | Drawn of elements: (string * (string * string) list) list

/// The symbol catalogue: AWS, Azure, Google Cloud and UML symbols from diagrams.mbt (see Icons/PROVENANCE.md), and
/// original drawings for technology keys, `externalsystem`, the architecture defaults and the fallback.
[<RequireQualifiedAccess>]
module internal Icons =
    let private catalog =
        lazy
            (use stream =
                match typeof<IconArt>.Assembly.GetManifestResourceStream "Morphir.Diagrams.Icons.json.gz" with
                 | null -> invalidOp "The icon catalogue resource is missing."
                 | stream -> stream

             use gzip = new GZipStream(stream, CompressionMode.Decompress)
             use buffer = new MemoryStream()
             gzip.CopyTo buffer
             JsonDocument.Parse(buffer.ToArray()).RootElement)

    let private path data = "path", [ "d", data ]
    let private circle x y r = "circle", [ "cx", x; "cy", y; "r", r ]

    /// A badge with initials: a neutral mark for a product whose own logo has no recorded licence. It does not
    /// imitate the product's logo; the trademark notice names the owner.
    let private badge (initials: string) =
        [ "rect", [ "x", "2"; "y", "4"; "width", "20"; "height", "16"; "rx", "4" ]; "text", [ "x", "12"; "y", "15"; "#text", initials ] ]

    let private drawn =
        dict
            [
                "client", [ "rect", [ "x", "3"; "y", "4"; "width", "18"; "height", "12"; "rx", "2" ]; path "M8 20h8M12 16v4" ]
                "server", [ path "M4 4h16v6H4zM4 14h16v6H4zM7 7h.01M7 17h.01" ]
                "browser", [ path "M3 5h18v14H3zM3 9h18M6 7h.01M8.5 7h.01" ]
                "service", [ circle "12" "12" "3"; path "M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7 7M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1" ]
                "controller", [ path "M4 6h16M4 12h16M4 18h16"; circle "9" "6" "2"; circle "15" "12" "2"; circle "7" "18" "2" ]
                "api", [ path "M8 4C5 4 5 6 5 8s-1 4-3 4c2 0 3 2 3 4s0 4 3 4M16 4c3 0 3 2 3 4s1 4 3 4c-2 0-3 2-3 4s0 4-3 4" ]
                "ui", [ path "M3 4h18v16H3zM3 9h18M9 9v11" ]
                "mobile", [ "rect", [ "x", "7"; "y", "2"; "width", "10"; "height", "20"; "rx", "2" ]; path "M11 18h2" ]
                "externalsystem", [ path "M14 3h7v7M21 3l-9 9M19 14v6H4V5h6" ]
                "application", [ path "M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z" ]
                "loadbalancer", [ path "M12 3v6M12 9l-7 7M12 9l7 7M12 9v7"; circle "5" "18" "2"; circle "12" "18" "2"; circle "19" "18" "2" ]
                "network", [ circle "12" "5" "2.5"; circle "5" "19" "2.5"; circle "19" "19" "2.5"; path "M11 7.5l-5 9M13 7.5l5 9M7.5 19h9" ]
                "cache", [ "ellipse", [ "cx", "12"; "cy", "5"; "rx", "8"; "ry", "3" ]; path "M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5M13 9l-3 5h4l-3 5" ]
                "webserver", [ path "M4 3h16v7H4zM7 6.5h.01M8.5 17.5h7"; circle "12" "17.5" "3.5" ]
                "messagequeue", [ path "M3 7h18v10H3zM8 7v10M13 7v10M18 7v10" ]
                "scheduler", [ circle "12" "12" "9"; path "M12 7v5l3 3" ]
                "gateway", [ path "M4 21V10l8-6 8 6v11M9 21v-6h6v6" ]
                "authenticationservice", [ path "M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6zM9 12l2 2 4-4" ]
                "mailserver", [ path "M3 6h18v12H3zM3 6l9 7 9-7" ]
                "cloud", [ path "M7 18a4.5 4.5 0 0 1-.5-9 6 6 0 0 1 11.5 1.5A3.75 3.75 0 0 1 17.5 18z" ]
                "disk", [ path "M3 15h18v5H3zM3 15l3-9h12l3 9M17 17.5h.01" ]
                "internet", [ circle "12" "12" "9"; path "M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18" ]
                "github", badge "GH"
                "docker", badge "DK"
                "gitlab", badge "GL"
                "jenkins", badge "JK"
                "postgresql", badge "PG"
                "mongodb", badge "MG"
                "kubernetes", badge "K8s"
                "apachekafka", badge "KF"
                "elasticsearch", badge "ES"
                "auth0", badge "A0"
                "redis", badge "RD"
            ]

    let private fallback =
        [ "rect", [ "x", "4"; "y", "4"; "width", "16"; "height", "16"; "rx", "2"; "stroke-dasharray", "3 2" ]; path "M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5V14M12 17h.01" ]

    /// The art of a key, ignoring case, or none.
    let tryFind (key: string) =
        let key = key.Trim().ToLowerInvariant()

        match drawn.TryGetValue key with
        | true, elements -> Some(Drawn elements)
        | false, _ ->
            match catalog.Value.GetProperty("keys").TryGetProperty key with
            | true, record ->
                let field index = record[index].GetString() |> Option.ofObj |> Option.defaultValue ""
                Some(Copied(catalog.Value.GetProperty("assets").GetProperty(field 0), field 1, field 2, field 3))
            | false, _ -> None

    /// Whether a key names an icon.
    let exists key = (tryFind key).IsSome

    /// Every key, copied or drawn.
    let keys () =
        Seq.append (catalog.Value.GetProperty("keys").EnumerateObject() |> Seq.map (fun property -> property.Name)) drawn.Keys
        |> Seq.distinct
        |> Seq.sort
        |> Seq.toList

    let private attribute (name: string) (value: string) = SvgAttribute.Create(name, value)

    /// A symbol definition for a key, with every internal identifier scoped by `id`, so two diagrams in one page
    /// share no identifiers. Unknown keys get the fallback drawing.
    let symbol (id: string) (key: string) =
        let title = SvgNode.CreateElement("title", [||], [| SvgNode.CreateText key |])

        match tryFind key with
        | Some(Copied(tree, family, attribution, terms)) ->
            let scope (value: string) =
                Regex.Replace(value, @"url\(#([^)]+)\)", (fun (found: Match) -> $"url(#{id}-{found.Groups[1].Value})"))

            let rec convert (node: JsonElement) =
                let attributes =
                    node.GetProperty("attrs").EnumerateArray()
                    |> Seq.map (fun pair ->
                        let name = pair[0].GetString() |> Option.ofObj |> Option.defaultValue ""
                        let value = pair[1].GetString() |> Option.ofObj |> Option.defaultValue ""

                        match name with
                        | "id" -> attribute name $"{id}-{value}"
                        | "href"
                        | "xlink:href" when value.StartsWith '#' -> attribute "href" $"#{id}-{value.Substring 1}"
                        | _ -> attribute name (scope value))
                    |> Seq.toArray

                let children = node.GetProperty("children").EnumerateArray() |> Seq.map convert |> Seq.toArray
                SvgNode.CreateElement(node.GetProperty("tag").GetString() |> Option.ofObj |> Option.defaultValue "g", attributes, children)

            let root = convert tree
            let description = SvgNode.CreateElement("desc", [||], [| SvgNode.CreateText $"{attribution}, {family} icon. {terms}" |])

            SvgNode.CreateElement(
                "symbol",
                Seq.append [ attribute "id" id ] (root.Attributes |> Seq.filter (fun pair -> pair.Name.Value <> "id")) |> Seq.toArray,
                Seq.append [ title; description ] root.Children |> Seq.toArray
            )
        | found ->
            let elements =
                match found with
                | Some(Drawn elements) -> elements
                | _ -> fallback

            let children =
                elements
                |> List.map (fun (tag, pairs) ->
                    let text = pairs |> List.tryFind (fst >> (=) "#text") |> Option.map snd

                    let pairs =
                        pairs
                        |> List.filter (fst >> (<>) "#text")
                        |> List.map (fun (name, value) -> attribute name value)

                    match text with
                    | Some value ->
                        let style =
                            [ attribute "text-anchor" "middle"; attribute "font-size" "7"; attribute "font-weight" "700"; attribute "font-family" "ui-monospace, monospace"; attribute "fill" "currentColor"; attribute "stroke" "none" ]

                        SvgNode.CreateElement(tag, List.toArray (pairs @ style), [| SvgNode.CreateText value |])
                    | None -> SvgNode.CreateElement(tag, List.toArray pairs, [||]))

            SvgNode.CreateElement(
                "symbol",
                [|
                    attribute "id" id
                    attribute "viewBox" "0 0 24 24"
                    attribute "fill" "none"
                    attribute "stroke" "currentColor"
                    attribute "stroke-width" "1.6"
                    attribute "stroke-linecap" "round"
                    attribute "stroke-linejoin" "round"
                |],
                List.toArray (title :: children)
            )

    /// Whether the key's art keeps its provider colours, and so needs a light plate to stay visible on dark themes.
    let keepsColours key =
        match tryFind key with
        | Some(Copied(_, family, _, _)) -> family <> "uml"
        | _ -> false

/// The icon keys that services, groups and participants can name.
[<AbstractClass; Sealed>]
type DiagramIcons private () =
    /// Every key, in ordinal order: AWS, Azure, Google Cloud and UML symbols copied unchanged, and the symbols drawn
    /// for this library. Keys are matched without regard to case.
    static member Keys = Icons.keys () |> System.Collections.Immutable.ImmutableArray.CreateRange

    /// Whether a key names an icon, ignoring case. An unknown key draws a marked fallback.
    static member Contains(key: string | null) =
        match key with
        | null -> false
        | key -> Icons.exists key
