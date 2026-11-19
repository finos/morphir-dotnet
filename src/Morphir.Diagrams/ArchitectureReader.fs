// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Text.RegularExpressions

/// Reads a Mermaid `architecture-beta` body, as Mermaid 12.2.0 does.
[<RequireQualifiedAccess>]
module internal ArchitectureReader =
    let private id = @"[\w-]+"
    let private label = @"\[(?<label>""(?:\\.|[^""])*""|'(?:\\.|[^'])*'|[^\]]*)\]"

    let private declaration =
        Regex($@"^(?<kind>group|service)\s+(?<id>{id})\s*(?:\((?<icon>[^)]*)\)|(?<text>""(?:\\.|[^""])*""|'(?:\\.|[^'])*'))?\s*(?:{label})?\s*(?:in\s+(?<parent>{id}))?$", RegexOptions.CultureInvariant)

    let private junction = Regex($@"^junction\s+(?<id>{id})\s*(?:in\s+(?<parent>{id}))?$", RegexOptions.CultureInvariant)

    let private edge =
        Regex(
            $@"^(?<s>{id})(?<sg>\{{group\}})?\s*:\s*(?<ss>[LRTB])\s*(?<lh><)?\s*-\s*(?:\[(?<label>""[^""]*""|[^\]]*)\]\s*-\s*|-\s*)(?<rh>>)?\s*(?<ts>[LRTB])\s*:\s*(?<t>{id})(?<tg>\{{group\}})?$",
            RegexOptions.CultureInvariant
        )

    let private alignment = Regex($@"^align\s+(?<kind>row|column)\s+(?<members>{id}(?:\s+{id})+)$", RegexOptions.CultureInvariant)

    /// The text of a label: quotes removed and escapes resolved.
    let private text (value: string) =
        let value = value.Trim()

        if value.Length >= 2 && (value[0] = '"' || value[0] = '\'') && value[value.Length - 1] = value[0] then
            Regex.Unescape(value.Substring(1, value.Length - 2))
        else
            value

    let read (statements: seq<Reading.Line * int>) : Result<Diagram, Diagnostic list> =
        let errors = ResizeArray<Diagnostic>()
        let groups = ResizeArray<ArchitectureGroup>()
        let services = ResizeArray<ArchitectureService>()
        let junctions = ResizeArray<ArchitectureJunction>()
        let edges = ResizeArray<ArchitectureEdge * SourceSpan>()
        let alignments = ResizeArray<ArchitectureAlignment * SourceSpan>()
        let parents = ResizeArray<DiagramId * DiagramId * SourceSpan>()
        let ids = HashSet<DiagramId>()
        let mutable title = None
        let mutable inDescription = false
        let fail code message span = errors.Add(Reading.error $"mermaid/{code}" message (Some span))

        for line, start in statements do
            let value = line.Text.Substring(start).Trim()
            let span = line.Span(start, line.Text.Length)

            let declare (identity: string) =
                let identity = DiagramId.Create identity

                if identity.Value.Length = 1 && "LRTB".Contains identity.Value then
                    fail "architecture-reserved-id" $"{identity} is a port name and cannot name an element." span
                elif not (ids.Add identity) then
                    fail "architecture-duplicate-id" $"{identity} is declared twice." span

                identity

            let parent (found: Match) identity =
                if found.Groups["parent"].Success then
                    let parentId = DiagramId.Create found.Groups["parent"].Value
                    parents.Add(identity, parentId, span)
                    Some parentId
                else
                    None

            if inDescription then
                if value.EndsWith '}' then inDescription <- false
            elif value.StartsWith("accDescr", StringComparison.Ordinal) && value.Contains '{' && not (value.Contains '}') then
                inDescription <- true
                Reading.warn "mermaid/unapplied-presentation" "Accessibility text is kept in the source but not drawn." (Some span)
            elif value.StartsWith("accTitle", StringComparison.Ordinal) || value.StartsWith("accDescr", StringComparison.Ordinal) then
                Reading.warn "mermaid/unapplied-presentation" "Accessibility text is kept in the source but not drawn." (Some span)
            elif value.StartsWith("title ", StringComparison.Ordinal) then
                title <- Some(value.Substring(6).Trim())
            elif declaration.IsMatch value then
                let found = declaration.Match value
                let identity = declare found.Groups["id"].Value
                let hasLabel = found.Groups["label"].Success
                let name = if hasLabel then text found.Groups["label"].Value else identity.Value
                let icon = if found.Groups["icon"].Success then Some found.Groups["icon"].Value else None

                if hasLabel && found.Groups["label"].Value.Length = 0 then
                    fail "architecture-empty-label" $"The label of {identity} is empty." span

                match icon with
                | Some key ->
                    let bare = let colon = key.IndexOf ':' in if colon >= 0 then key.Substring(colon + 1) else key

                    if not (Icons.exists bare) then
                        Reading.warn "mermaid/unknown-icon" $"No icon is called {key}; a marked fallback is drawn." (Some span)
                | None -> ()

                if found.Groups["kind"].Value = "group" then
                    groups.Add(ArchitectureGroup(identity, name, icon, parent found identity))
                else
                    let iconText = if found.Groups["text"].Success then Some(text found.Groups["text"].Value) else None
                    services.Add(ArchitectureService(identity, name, icon, iconText, parent found identity))
            elif junction.IsMatch value then
                let found = junction.Match value
                let identity = declare found.Groups["id"].Value
                junctions.Add(ArchitectureJunction(identity, parent found identity))
            elif edge.IsMatch value then
                let found = edge.Match value
                let side (name: string) = found.Groups[name].Value[0]
                let edgeLabel = if found.Groups["label"].Success then Some(text found.Groups["label"].Value) else None

                edges.Add(
                    ArchitectureEdge(
                        DiagramId.Create found.Groups["s"].Value,
                        side "ss",
                        found.Groups["sg"].Success,
                        found.Groups["lh"].Success,
                        DiagramId.Create found.Groups["t"].Value,
                        side "ts",
                        found.Groups["tg"].Success,
                        found.Groups["rh"].Success,
                        edgeLabel
                    ),
                    span
                )
            elif alignment.IsMatch value then
                let found = alignment.Match value
                let members = found.Groups["members"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.map DiagramId.Create
                alignments.Add(ArchitectureAlignment(found.Groups["kind"].Value = "row", Reading.immutable members), span)
            else
                fail "architecture-statement" "Expected a group, a service, a junction, an edge `a:R -- L:b` or an alignment." span

        // Constraints that need every declaration: endpoints, parents and alignment members.
        let groupIds = groups |> Seq.map (fun group -> group.Id) |> HashSet
        let nodeIds = Seq.append (services |> Seq.map (fun service -> service.Id)) (junctions |> Seq.map (fun junction -> junction.Id)) |> HashSet

        for child, parentId, span in parents do
            if not (groupIds.Contains parentId) then fail "architecture-missing-group" $"{child} is in {parentId}, which is not a group." span

        for item, span in edges do
            for endpoint in [ item.Source; item.Target ] do
                if groupIds.Contains endpoint then
                    fail "architecture-group-endpoint" $"{endpoint} is a group; an edge attaches to a group through a service with {{group}}." span
                elif not (nodeIds.Contains endpoint) then
                    fail "architecture-missing-endpoint" $"{endpoint} is not a service or junction." span

        // A group boundary leaves the group: both ends in one group cannot use it.
        let parentOf = services |> Seq.choose (fun service -> match service.Parent with | null -> None | parent -> Some(service.Id, parent)) |> dict

        for item, span in edges do
            if item.SourceGroup || item.TargetGroup then
                match parentOf.TryGetValue item.Source, parentOf.TryGetValue item.Target with
                | (true, first), (true, second) when first = second ->
                    fail "architecture-group-boundary" $"{item.Source} and {item.Target} share the group {first}; a group boundary edge must leave it." span
                | _ -> ()

        for item, span in alignments do
            for memberId in item.Members do
                if not (nodeIds.Contains memberId) then fail "architecture-alignment" $"{memberId} cannot be aligned: only services and junctions can." span

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok(
                Diagram.Architecture(
                    ArchitectureDiagram(
                        title,
                        Reading.immutable groups,
                        Reading.immutable services,
                        Reading.immutable junctions,
                        edges |> Seq.map fst |> Reading.immutable,
                        alignments |> Seq.map fst |> Reading.immutable
                    )
                )
            )
