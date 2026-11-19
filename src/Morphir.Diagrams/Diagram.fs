// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable

/// A diagram understood by the shared renderer.
[<RequireQualifiedAccess>]
type Diagram =
    /// A flowchart with nodes and links.
    | Flowchart of Flowchart
    /// A sequence diagram with lifelines and messages, from Mermaid or ZenUML.
    | Sequence of SequenceDiagram
    /// A state diagram from Mermaid or PlantUML.
    | State of StateDiagram
    /// A swimlane diagram from Mermaid swimlanes or a PlantUML activity diagram.
    | Swimlane of SwimlaneDiagram
    /// A Mermaid architecture diagram.
    | Architecture of ArchitectureDiagram
    /// A railroad diagram of a PlantUML regular expression or EBNF grammar.
    | Railroad of RailroadDiagram
    /// A data tree of a PlantUML JSON or YAML document.
    | DataTree of DataTreeDiagram
    /// A BPMN process or collaboration view.
    | Bpmn of BpmnDiagram

    /// Exhaustive dispatch over the supported diagram families for C# callers.
    member this.Match<'T>
        (
            flowchart: Func<Flowchart, 'T>,
            sequence: Func<SequenceDiagram, 'T>,
            state: Func<StateDiagram, 'T>,
            swimlane: Func<SwimlaneDiagram, 'T>,
            architecture: Func<ArchitectureDiagram, 'T>,
            railroad: Func<RailroadDiagram, 'T>,
            dataTree: Func<DataTreeDiagram, 'T>,
            bpmn: Func<BpmnDiagram, 'T>
        ) : 'T =
        match this with
        | Diagram.Flowchart model -> flowchart.Invoke model
        | Diagram.Sequence model -> sequence.Invoke model
        | Diagram.State model -> state.Invoke model
        | Diagram.Swimlane model -> swimlane.Invoke model
        | Diagram.Architecture model -> architecture.Invoke model
        | Diagram.Railroad model -> railroad.Invoke model
        | Diagram.DataTree model -> dataTree.Invoke model
        | Diagram.Bpmn model -> bpmn.Invoke model

    /// The graph whose nodes, groups and edges are the elements of a family drawn as a graph; none for sequences.
    member internal this.Graph =
        match this with
        | Diagram.Flowchart model -> Some model
        | Diagram.Sequence _ -> None
        | Diagram.State model -> Some model.Projection
        | Diagram.Swimlane model -> Some model.Projection
        | Diagram.Architecture model -> Some model.Projection
        | Diagram.Railroad model -> Some model.Projection
        | Diagram.DataTree model -> Some model.Projection
        | Diagram.Bpmn model -> Some model.Projection

    override this.ToString() =
        match this with
        | Diagram.Flowchart model -> model.ToString()
        | Diagram.Sequence model -> model.ToString()
        | Diagram.State model -> model.ToString()
        | Diagram.Swimlane model -> model.ToString()
        | Diagram.Architecture model -> model.ToString()
        | Diagram.Railroad model -> model.ToString()
        | Diagram.DataTree model -> model.ToString()
        | Diagram.Bpmn model -> model.ToString()

/// A parsed diagram together with the exact text supplied to the reader.
[<Sealed>]
type DiagramDocument internal (source: string, diagram: Diagram) =
    /// Original source, including trivia and line endings.
    member _.Source = source

    /// Model ready for inspection and rendering.
    member _.Diagram = diagram

    override _.ToString() = diagram.ToString()

/// A rendering outcome and its diagnostics.
[<Sealed>]
type RenderedDiagram
    internal (mediaType: string, output: string option, diagnostics: ImmutableArray<Diagnostic>) =
    /// Media type of the requested output.
    member _.MediaType = mediaType

    /// Rendered text, or null when rendering could not produce an output.
    member _.Output: string | null = Option.toObj output

    /// Diagnostics in encounter order.
    member _.Diagnostics = diagnostics

    override _.ToString() =
        match output with
        | Some text -> $"{mediaType}, {text.Length} UTF-16 code units"
        | None -> $"{mediaType}, {diagnostics.Length} diagnostics"
