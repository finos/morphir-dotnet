// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Svg

/// Collects children in the order in which a scene describes them. For F# callers.
[<Sealed>]
type SvgChildrenBuilder<'T> internal (build: SvgNode array -> 'T) =
    /// Adds a child.
    member _.Yield(node: SvgNode) = Seq.singleton node

    /// Adds multiple children.
    member _.YieldFrom(nodes: seq<SvgNode>) = nodes

    /// Adds no children.
    member _.Zero() = Seq.empty<SvgNode>

    /// Delays evaluating child expressions until the scene is built.
    member _.Delay(children: unit -> seq<SvgNode>) = Seq.delay children

    /// Appends child sequences.
    member _.Combine(left: seq<SvgNode>, right: seq<SvgNode>) = Seq.append left right

    /// Applies a child expression to every value.
    member _.For(values: seq<'Value>, body: 'Value -> seq<SvgNode>) = Seq.collect body values

    /// Builds the enclosing scene value.
    member _.Run(children: seq<SvgNode>) = children |> Seq.toArray |> build

/// Declarative scene builders for F# callers.
[<RequireQualifiedAccess>]
module Svg =
    /// Builds a document with the supplied root attributes.
    let document (attributes: SvgAttribute list) =
        let attributes = List.toArray attributes
        SvgChildrenBuilder(fun children -> SvgDocument.Create(attributes, children))

    /// Builds a named SVG element with the supplied attributes.
    let element (name: string) (attributes: SvgAttribute list) =
        let attributes = List.toArray attributes
        SvgChildrenBuilder(fun children -> SvgNode.CreateElement(name, attributes, children))

    /// Builds a group of SVG children.
    let group attributes = element "g" attributes

    /// Creates an attribute.
    let attribute (name: string) value = SvgAttribute.Create(name, value)

    /// Creates a child containing escaped text.
    let text value = SvgNode.CreateText value

    /// Sets an attribute on an element.
    let withAttribute attribute (node: SvgNode) = node.WithAttribute attribute
