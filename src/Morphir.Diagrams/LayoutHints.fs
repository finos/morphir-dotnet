// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Collections.Generic
open Morphir.Diagrams.Layout
open Morphir.Svg

/// What a writer tells the layout sink about an element it emits, where the SVG itself does not say.
[<RequireQualifiedAccess>]
type internal LayoutHint =
    /// The path is a line routed through these waypoints, in pixels, in this style.
    | Route of waypoints: (float * float) list * style: RouteStyle
    /// The group is a container: its first rectangle is its frame and its first text its header.
    | Container
    /// The element belongs to the drawable with this reference, not to the group that holds it in the SVG.
    | Owner of reference: string
    /// The path is the lifeline of a participant.
    | Lifeline of participant: string
    /// The shape has this family role.
    | Role of role: FamilyRole
    /// The text was wrapped by the writer to fit its box.
    | Wraps
    /// The writer could not paint an authored colour, named with the element it belongs to, and used its default.
    | Unresolved of color: string * element: string

/// What the final drawing pass recorded for the layout sink: its SVG tree, the hints for its elements, and the
/// containment the model gives, as (child reference, owner reference) pairs.
[<NoComparison; NoEquality>]
type internal LayoutCapture =
    {
        Root: SvgNode
        Hints: IReadOnlyDictionary<SvgNode, LayoutHint list>
        Owners: (string * string) list
        Roles: (string * FamilyRole) list
        OnFills: bool
    }
