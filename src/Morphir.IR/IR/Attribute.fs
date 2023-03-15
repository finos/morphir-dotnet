module Morphir.IR.Attribute

open Morphir.IR.NodePath

/// <summary>
/// Compact representation of a set of optional attributes on some nodes of an expression tree.
/// </summary>
type AttributeTree<'A> =
    | AttributeTree of attributes: 'A * nodes: (NodePath * AttributeTree<'A>) list
