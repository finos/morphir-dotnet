// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Bpmn

open System
open System.Collections.Generic
open System.Text

/// How a document is written.
[<RequireQualifiedAccess>]
type BpmnWriteFormat =
    /// One line per top-level node, no white space between elements, attributes and children in source order, and
    /// `<?xml version="1.0" encoding="UTF-8"?>` first. Writing a canonical document again gives the same bytes.
    | Canonical
    /// Canonical content with each element of element-only content on its own line, indented by two spaces.
    | Pretty

    /// The format as documentation writes it.
    member this.WireValue =
        match this with
        | BpmnWriteFormat.Canonical -> "canonical"
        | BpmnWriteFormat.Pretty -> "pretty"

    override this.ToString() = this.WireValue

[<RequireQualifiedAccess>]
module internal XmlWriting =
    let private escape (text: string) (inAttribute: bool) =
        let builder = StringBuilder(text.Length)

        for c in text do
            match c with
            | '&' -> builder.Append "&amp;" |> ignore
            | '<' -> builder.Append "&lt;" |> ignore
            | '>' when not inAttribute -> builder.Append "&gt;" |> ignore
            | '"' when inAttribute -> builder.Append "&quot;" |> ignore
            | '\t' when inAttribute -> builder.Append "&#9;" |> ignore
            | '\n' when inAttribute -> builder.Append "&#10;" |> ignore
            | '\r' -> builder.Append "&#13;" |> ignore
            | c -> builder.Append c |> ignore

        builder.ToString()

    type private Step =
        | Open of BpmnNode * int
        | Close of BpmnNode * int * bool
        | Break of int

    /// Writes the top-level nodes. The walk keeps its own stack, so depth is bounded only by memory.
    let write (format: BpmnWriteFormat) (top: BpmnNode seq) =
        let output = StringBuilder()
        let pretty = format = BpmnWriteFormat.Pretty
        output.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n") |> ignore

        // Element-only content drops white space between elements; mixed content keeps every text node.
        let kept (node: BpmnNode) =
            let elementOnly = node.Children |> Seq.forall (fun child -> child.Kind <> BpmnNodeKind.Text && child.Kind <> BpmnNodeKind.CData)
            node.Children |> Seq.filter (fun child -> not (elementOnly && child.Kind = BpmnNodeKind.Whitespace)) |> Seq.toList

        let steps = Stack<Step>()

        let leaf (node: BpmnNode) =
            match node.Kind with
            | BpmnNodeKind.Text
            | BpmnNodeKind.Whitespace -> output.Append(escape node.Value false) |> ignore
            | BpmnNodeKind.CData -> output.Append("<![CDATA[").Append(node.Value.Replace("]]>", "]]]]><![CDATA[>")).Append("]]>") |> ignore
            | BpmnNodeKind.Comment -> output.Append("<!--").Append(node.Value).Append("-->") |> ignore
            | BpmnNodeKind.ProcessingInstruction ->
                output.Append("<?").Append(node.LocalName).Append(if node.Value = "" then "" else " " + node.Value).Append("?>") |> ignore
            | _ -> ()

        for root in top do
            if root.Kind <> BpmnNodeKind.Whitespace && root.Kind <> BpmnNodeKind.Declaration then
                steps.Push(Open(root, 0))

                while steps.Count > 0 do
                    match steps.Pop() with
                    | Open(node, depth) when node.Kind = BpmnNodeKind.Element ->
                        output.Append('<').Append(node.Name) |> ignore

                        for attribute in node.Attributes do
                            output.Append(' ').Append(attribute.Name).Append("=\"").Append(escape attribute.Value true).Append('"') |> ignore

                        let children = kept node

                        if children.IsEmpty then
                            output.Append("/>") |> ignore
                        else
                            output.Append('>') |> ignore
                            let indent = pretty && children |> List.forall (fun child -> child.Kind = BpmnNodeKind.Element || child.Kind = BpmnNodeKind.Comment || child.Kind = BpmnNodeKind.ProcessingInstruction)
                            steps.Push(Close(node, depth, indent))

                            for child in List.rev children do
                                steps.Push(Open(child, depth + 1))
                                if indent then steps.Push(Break(depth + 1))
                    | Open(node, _) -> leaf node
                    | Break depth -> output.Append('\n').Append(String(' ', 2 * depth)) |> ignore
                    | Close(node, depth, indent) ->
                        if indent then output.Append('\n').Append(String(' ', 2 * depth)) |> ignore
                        output.Append("</").Append(node.Name).Append('>') |> ignore

                output.Append('\n') |> ignore

        output.ToString()
