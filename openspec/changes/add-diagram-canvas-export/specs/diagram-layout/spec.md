# Spec Delta

## Purpose

Gives every diagram family one public, renderer-neutral laid-out form. Renderers for other formats can then draw a diagram without parsing SVG or repeating layout.

A layout describes what the SVG shows, as drawables with exact geometry and resolved colours. The drawables are shapes, lines, text, images, composite elements, containers and groups. Each drawable has an identifier that is unique on its page. A drawable that draws a diagram element also carries that element's reference.

For example, the flowchart node `A[Start]` becomes a rounded-rectangle shape with the reference `node=A`, holding a text box with `Start`. The link `A --> B` becomes a line whose ends are bound to the two shapes. The line has the routing waypoints and the exact final path, including any rounded corners or bridges, plus an arrowhead at its target end. A railroad track has no element of its own, so it is a line with no reference.

## ADDED Requirements

### Requirement: Every family has a layout
The library SHALL produce a layout for every diagram that the SVG renderer accepts, for each family: flowchart, sequence, state, swimlane, architecture, railroad, data tree and BPMN. The same render options SHALL apply. A diagram that the SVG renderer rejects SHALL be rejected with the same diagnostics.

#### Scenario: A state diagram is laid out
- **WHEN** a caller lays out a Mermaid `stateDiagram-v2` with an initial state, two states and a final state
- **THEN** the layout holds a drawable for each state and a line for each transition

#### Scenario: Invalid metadata is rejected as in SVG
- **WHEN** a caller lays out a diagram whose metadata the SVG renderer reports as errors
- **THEN** no layout is produced and the same error diagnostics are returned

### Requirement: Layouts cover what the SVG draws
Every primitive that the SVG of a page draws SHALL have a drawable in its layout. The SVG primitives are rectangles, polygons, ellipses, circles, lines, paths, text and symbol uses. The only exceptions SHALL be the page background, which is the layout's background colour, and arrowhead markers, which are line heads. The title, style sheet, metadata and player attributes have no drawable.

#### Scenario: A flowchart icon is an image
- **WHEN** a flowchart node with an icon is laid out
- **THEN** the node's element holds the node's shape, its text, and an image drawable whose SVG draws the icon

#### Scenario: A railroad track has no reference
- **WHEN** a railroad diagram is laid out
- **THEN** each track segment is a line drawable with no element reference

### Requirement: Shapes stand for the primitives of their kind
Each drawable SHALL list, in paint order, the SVG primitives it stands for. A shape of a named kind SHALL stand for every primitive that the writer paints for that kind, such as a cylinder's body and its top ellipse, so a renderer that draws the kind natively draws nothing twice. Primitives that no kind covers SHALL be drawables of their own.

#### Scenario: A cylinder stands for two primitives
- **WHEN** the flowchart node `C[(Ledger)]` is laid out
- **THEN** it is one cylinder shape that stands for the SVG's rounded rectangle and its top ellipse, and no other drawable stands for that ellipse

#### Scenario: A subroutine keeps its bars
- **WHEN** a flowchart subroutine node is laid out
- **THEN** it is a rectangle shape and a separate path drawable for its two side bars, both in the node's element

### Requirement: The layout paints what the SVG paints
The primitives that the SVG of a page paints, in document order, SHALL equal the primitives that the layout's drawables stand for, in paint order, one entry for each primitive. Each entry SHALL compare these values, formatted with the SVG's number formatting:
- the primitive's kind
- rectangle and ellipse geometry, polygon points, normalised path data, or text baselines and anchors
- its colours and stroke width

#### Scenario: Paint lists match across the pinned corpora
- **WHEN** every diagram of the pinned corpora is rendered to SVG and laid out in the same palette
- **THEN** the SVG's painted primitives and the layout's primitives in paint order are equal lists, including primitives with no reference

#### Scenario: Fractional BPMN coordinates agree
- **WHEN** a BPMN diagram whose DI bounds have two decimal places is laid out
- **THEN** each shape's formatted box equals the SVG's formatted rectangle

### Requirement: References agree with the SVG
Each reference in the SVG's `data-ref` attributes SHALL be the reference of exactly one drawable, and the layout SHALL have no other references. Each primitive SHALL belong to the innermost referenced drawable whose SVG group holds it, except where its writer documents another owner. A sequence lifeline, drawn outside its participant's group, belongs to that participant.

#### Scenario: A lifeline belongs to its participant
- **WHEN** a sequence diagram is laid out
- **THEN** each lifeline is a child of its participant's composite element, although the SVG draws it outside the participant's group

#### Scenario: Nested data-tree references are counted once
- **WHEN** a data tree whose rows hold referenced cells is laid out
- **THEN** each cell primitive belongs to the cell's drawable and not also to its row's

### Requirement: Drawables have identity and order
Every drawable SHALL have an identifier that is unique on its page and the same for equal input. It SHALL have an optional element reference and a paint-order index. Composite elements, containers and groups SHALL hold their children by containment. Paint order SHALL be the order in which the SVG paints the primitives, which can differ from containment order.

#### Scenario: Edges paint before nodes inside a subgraph
- **WHEN** a flowchart subgraph holding two linked nodes is laid out
- **THEN** the nodes are children of the subgraph's container, and the line's paint-order index is lower than both nodes'

### Requirement: Distances are exact at the SVG's precision
Every length and coordinate SHALL be a distance stored as a whole number of quarter English Metric Units (3,657,600 per inch). A distance SHALL be exact for any whole number of these units:
- CSS pixels with up to two decimal places (381 units per hundredth)
- points
- centimetres
- millimetres
- inches
- whole EMU

Other values SHALL round half to even to the nearest unit. Values outside the 64-bit range and non-finite values SHALL be refused.

#### Scenario: Pixels convert exactly
- **WHEN** a caller creates a distance of 100.02 CSS pixels
- **THEN** it is 3,810,762 quarter EMU, and converting it back gives 100.02 pixels

#### Scenario: A non-finite length is refused
- **WHEN** a caller creates a distance from NaN or infinity
- **THEN** creation fails with an argument error

### Requirement: Whole EMU rounds half to even
Conversion of a distance to whole EMU SHALL round half to even, so it differs from the distance by at most half an EMU.

#### Scenario: A half EMU rounds to even
- **WHEN** distances of 2 and 6 quarter EMU are converted to whole EMU
- **THEN** they give 0 and 2 EMU

### Requirement: Fractions and scales are parsed
A fraction SHALL be a number from 0 to 1 inclusive. A scale SHALL be a finite number greater than 0. Each SHALL be built only through:
- a parse that reports a failure
- a try-parse for C# callers
- a create for literals that throws on invalid input

#### Scenario: An out-of-range fraction is refused
- **WHEN** a caller parses 1.5 as a fraction
- **THEN** the parse fails and no fraction is produced

### Requirement: Shapes carry kind, geometry and an optional role
A shape SHALL have the following:
- a kind: rectangle, rounded rectangle with its radius, ellipse, diamond, cylinder, hexagon, or a path
- a box
- a paint
- an optional text box
- an optional family role

#### Scenario: A decision node is a diamond
- **WHEN** a flowchart node `B{Approve?}` is laid out
- **THEN** its shape kind is diamond, its reference is `node=B` and its text box holds `Approve?`

#### Scenario: An outline with no kind is a path
- **WHEN** a BPMN task icon drawn with relative SVG path commands is laid out
- **THEN** its shape kind is a path whose normalised data draws the same outline

### Requirement: Paths are normalised
Path data in a layout SHALL use only these absolute commands: move, line, cubic curve, quadratic curve, elliptical arc and close. The library SHALL offer a conversion of a path into moves, lines, cubic curves and closes, approximating arcs within 0.01 CSS pixels.

#### Scenario: Relative and smooth commands are normalised
- **WHEN** SVG path data with `a`, `c` and `s` commands is normalised
- **THEN** the result uses only absolute commands and draws the same outline

### Requirement: Lines carry ends, waypoints and their exact path
A line SHALL have the following:
- two ends
- the routing waypoints, ends included
- a route style: straight, orthogonal or curved
- the exact final path that the SVG draws, including rounded corners, curves and bridge arcs
- a paint
- an optional label text box

Each end SHALL have its point, its arrowhead and an optional binding. A binding names a drawable on the same page, such as a shape, a container or a lifeline, and an anchor on it.

#### Scenario: A link names its endpoints
- **WHEN** the flowchart link `A --> B` is laid out
- **THEN** its source end is bound to the shape of `A`, its target end is bound to the shape of `B`, and the target end has an arrowhead

#### Scenario: A link to a subgraph binds the container
- **WHEN** a flowchart link ends at a subgraph
- **THEN** that end is bound to the subgraph's container

#### Scenario: A self-message binds one lifeline twice
- **WHEN** a sequence message from a participant to itself is laid out
- **THEN** both of its ends are bound to that participant's lifeline at different anchors

#### Scenario: A bridge is kept in the exact path
- **WHEN** a flowchart with bridges enabled has two crossing links
- **THEN** the later line's exact path holds the bridge arc and its waypoints do not

#### Scenario: A creation message binds the new participant's head
- **WHEN** a sequence message that creates its receiver is laid out
- **THEN** its target end is bound to the receiver's head shape, on the side facing the sender, and not to the receiver's lifeline

### Requirement: Anchors are clamped and points are exact
A binding to a shape, image or container SHALL give its anchor as fractions across the target's box, clamped to 0 and 1. A binding to a line SHALL give its anchor as a fraction of the line's length, so a lifeline of zero width still has one. The end's point SHALL stay the exact point the SVG draws, even when it lies outside the target's box.

#### Scenario: A BPMN waypoint outside its target keeps its point
- **WHEN** a BPMN flow whose last DI waypoint lies two pixels outside its target task is laid out
- **THEN** its target end keeps that point, and its anchor is clamped to the task's edge

#### Scenario: A message anchors along the lifeline
- **WHEN** a sequence message halfway down a lifeline is laid out
- **THEN** its end's anchor is the fraction of the lifeline's length at the message's row

### Requirement: Text is laid out with baselines and boxes
A text drawable SHALL hold:
- its content lines
- the baseline origin of its first line
- the line advance
- its alignment: start, middle or end
- a font size as a distance
- a font weight
- a rotation of 0, 90, 180 or 270 degrees
- a box that holds the lines, measured with the theme's text-width estimate

Text boxes in shapes and labels SHALL say whether the text wraps within the box.

#### Scenario: A BPMN lane header is rotated
- **WHEN** a BPMN lane with a name is laid out
- **THEN** its header text has a rotation of 270 degrees and the SVG's baseline origin

### Requirement: Paints resolve each colour independently
A layout SHALL be built for one palette, dark or light, and the auto mode SHALL resolve to light. A paint SHALL hold:
- an optional fill
- an optional stroke
- an ink
- a stroke width
- a dash pattern, which is empty for a solid line
- a line cap and a line join

A missing colour is `none`. Each colour SHALL carry its source: a colour family, one of the theme's base roles, or authored in the diagram source.

#### Scenario: A BPMN icon keeps its round joins
- **WHEN** a BPMN user task's icon is laid out
- **THEN** its paint has the round line join that the SVG draws

#### Scenario: Auto mode uses the light palette
- **WHEN** a diagram is laid out with the default options, whose mode is auto
- **THEN** every theme-sourced colour is the light palette's colour for its role

#### Scenario: An authored box colour keeps its source
- **WHEN** a sequence diagram with `box Aqua` is laid out
- **THEN** the box's fill is that colour with the authored source

### Requirement: Colours carry their effective opacity
Each colour's opacity SHALL be its effective opacity: the colour's own alpha times any fill or stroke opacity that the SVG applies to it.

#### Scenario: A tinted box keeps its effective opacity
- **WHEN** a sequence diagram with a tinted box, which the SVG draws with a fill opacity of 0.12, is laid out
- **THEN** the box's fill has the opacity 0.12 times the tint's own alpha

### Requirement: Authored colours are resolved as CSS colours
An authored colour SHALL be read as a hex colour, an `rgb` or `rgba` function, or a CSS named colour, ignoring case. An authored colour that none of these reads SHALL be replaced by the colour the writer uses when none is authored, and the layout result SHALL hold a `layout/unresolved-colour` warning naming the value and the element.

#### Scenario: A named colour is resolved
- **WHEN** a sequence diagram with `box Aqua` is laid out
- **THEN** the box's fill is `#00ffff` before its opacity is applied

#### Scenario: An unknown colour is reported
- **WHEN** a sequence diagram with `box #zz Payments` is laid out
- **THEN** the box uses the writer's default box colour, and the result holds a `layout/unresolved-colour` warning naming `#zz`

#### Scenario: A dashed message keeps its dash
- **WHEN** a dashed sequence reply message is laid out
- **THEN** its paint has a non-empty dash pattern and the SVG's stroke width

### Requirement: Containers, elements and groups are distinct
The three gathering drawables SHALL differ as follows:
- A **container** holds other drawables and has a header text box, such as a subgraph, a composite state, a swimlane lane or a BPMN pool or lane.
- A **composite element** is one reference over several primitives, such as a sequence participant's head, foot and lifeline, or a data-tree row and its cells.
- A **group** gathers drawables for painting only.

#### Scenario: A subgraph is a container
- **WHEN** a flowchart with `subgraph checkout` holding two nodes is laid out
- **THEN** the layout holds a container with the header `checkout` whose children include the two nodes

#### Scenario: A participant is a composite element
- **WHEN** a sequence participant is laid out
- **THEN** one element with its reference holds its head shape, its foot shape and its lifeline

### Requirement: Family roles name element kinds
The optional family role of a shape SHALL come from a documented set. Each role names an element kind that a target may draw with a dedicated symbol:
- for sequences: actor, lifeline and activation
- for BPMN: task, gateway, event and data object
- for states: initial, final, choice, history and fork or join
- note

A renderer SHALL treat a role it does not recognise as absent, because the shape kind always stays.

#### Scenario: A BPMN gateway has a role
- **WHEN** a BPMN exclusive gateway is laid out
- **THEN** its shape is a diamond with the family role gateway

### Requirement: Links come from the diagram's metadata
Each referenced drawable SHALL carry the link items that the diagram's metadata projection gives for its reference, in the projection's order. Each link SHALL have its predicate, its target and whether the target is a web address. Property items SHALL NOT be links, even when their value is a web address.

#### Scenario: Links follow the projection's order
- **WHEN** metadata gives a node two links, one a web address and one not
- **THEN** the node's drawable holds both links in the order the projection gives them, and marks only the web address as one

#### Scenario: A web-valued property is not a link
- **WHEN** metadata gives a node a property whose value is a web address
- **THEN** the node's drawable has no link for that property

### Requirement: Images are standalone SVG documents
An image's SVG SHALL be a standalone document that draws the same picture on its own. Its root SHALL have the symbol's view box and the size of the image's box, so the symbol maps onto the viewport as the `use` does. Paint the symbol or the `use` passes down, including stroke width, caps and joins, SHALL be set on the root, and `currentColor` SHALL be resolved. Every definition it refers to SHALL be inside it.

#### Scenario: A flowchart icon renders on its own
- **WHEN** the image of a flowchart node's icon is rendered by itself
- **THEN** it draws the icon at the node's icon size in the node's ink colour, with no references outside the document

### Requirement: A layout gives the SVG of its page
Each layout SHALL give the SVG document from the drawing pass that produced it, in the layout's palette. Lowering SHALL keep that SVG.

#### Scenario: The layout's SVG matches the renderer's
- **WHEN** a diagram is laid out in the light palette
- **THEN** the layout's SVG renders to the same text as the SVG renderer's output for that diagram in the light mode

### Requirement: Layouts are deterministic and comparable
Equal diagrams and options SHALL give equal layouts on every supported operating system. Two layouts SHALL be equal when their bounds, background, drawables and the rendered text of their SVG are equal.

#### Scenario: Two runs give equal layouts
- **WHEN** the same diagram is laid out twice with the same options
- **THEN** the two layouts are equal
