# Spec Delta

## Purpose

Writes any diagram as an editable draw.io file. Shapes, containers and attached edges behave in draw.io as if they had been drawn there, and each element keeps its Morphir element reference.

For example, the flowchart link `A --> B` becomes a draw.io edge whose source and target are the cells of `A` and `B`. Moving `A` in draw.io keeps the edge attached.

## ADDED Requirements

### Requirement: The renderer writes draw.io files
The draw.io renderer SHALL write an uncompressed `mxfile` document, UTF-8 without a byte order mark, with the extension `.drawio` and the media type `application/vnd.jgraph.mxfile`. Each page SHALL be one `diagram` element holding one graph model. The document SHALL contain no timestamps, versions of the writing application, or machine values.

#### Scenario: Two pages give two diagrams
- **WHEN** two layouts are rendered together as one draw.io file
- **THEN** the file holds two `diagram` elements, in the order given

#### Scenario: Equal input gives equal bytes
- **WHEN** the same layout is rendered twice
- **THEN** the two files are byte-identical

### Requirement: Draw.io declares its native features
The draw.io renderer SHALL declare rounded rectangles, ellipses, diamonds, cylinders, hexagons, polylines, smooth curves, attached connectors, open and circle heads, wrapped text, rotated text, containers, family roles, interleaved painting, fill opacity, stroke and text opacity, dash patterns, links, SVG images and pages. Paths SHALL therefore reach it as SVG images, exact routes as waypoints, and message heads as arrows.

#### Scenario: A flowchart with bridges is degraded only in its routes
- **WHEN** a flowchart with bridges enabled and no subgraphs is assessed for the draw.io renderer
- **THEN** the assessment is degraded only by exact routes falling back to waypoints for the bridged lines

### Requirement: Draw.io paints containers together
The draw.io renderer SHALL declare interleaved painting, because it writes no cells for composite elements and groups. It SHALL NOT declare interleaved containers, because a container cell paints its children together.

#### Scenario: A link leaving a subgraph is reported
- **WHEN** a flowchart with a subgraph and a link from a node inside it to a node outside is assessed for the draw.io renderer
- **THEN** the assessment lists interleaved containers for the subgraph

### Requirement: Cells keep identity
Each shape, text, image and container SHALL be a vertex, and each line an edge. Composite elements and groups SHALL NOT be cells, so that their children keep page-wide paint order. Each cell SHALL be wrapped in a `UserObject` that holds:
- the cell's identifier
- its label
- a `morphir-ref` attribute with its own reference, or else with the reference of the composite element that holds it
- its first web link as `link`

#### Scenario: A node keeps its reference
- **WHEN** a flowchart node `A` is rendered to draw.io
- **THEN** its vertex is wrapped in a `UserObject` with `morphir-ref="node=A"`, and its geometry is the node's box in pixels

#### Scenario: A participant's parts keep paint order
- **WHEN** a sequence diagram is rendered to draw.io
- **THEN** no degradation names paint order, each participant's lifeline cell comes before the message edges and its head cell after them, and all three carry the participant's reference

### Requirement: Vertices keep containment
A vertex's parent SHALL be the cell of its nearest container, or the layer. Each parent SHALL be written before its children, and siblings SHALL be written in paint order. Geometry SHALL be in CSS pixels, relative to the parent.

#### Scenario: A child is placed relative to its container
- **WHEN** a node inside a subgraph is rendered
- **THEN** its cell's parent is the subgraph's container cell, and its position is relative to that container

### Requirement: Edges stay attached
Each edge's parent SHALL be the nearest container that holds both of its ends, or the layer, and its geometry SHALL be relative to that parent. Each bound end SHALL name its cell as `source` or `target`, with the anchor as `exitX` and `exitY` or `entryX` and `entryY`. Each unbound end SHALL be a source or target point. Interior waypoints SHALL be the edge's points. Orthogonal and curved lines SHALL use orthogonal and curved edge styles.

#### Scenario: Every named end is a vertex
- **WHEN** every pinned corpus diagram is rendered to draw.io
- **THEN** every `source` or `target` that an edge names is a vertex on the same page, and every end without one has a point

#### Scenario: A free line has points
- **WHEN** a railroad track, which has no bindings, is rendered to draw.io
- **THEN** its edge has a source point and a target point and names no source or target cell

#### Scenario: An edge crossing container boundaries
- **WHEN** a link from a node in one subgraph to a node in another is rendered
- **THEN** the edge's parent is the layer, and its waypoints are in the layer's coordinates

### Requirement: Arrowheads use draw.io markers
The filled arrow SHALL be draw.io's `block` marker, filled. The open arrow SHALL be its `open` marker, and the circle its `oval` marker, filled. An end with no head SHALL have the marker `none`.

#### Scenario: An open arrow uses the open marker
- **WHEN** a line whose target end has an open arrow is rendered to draw.io
- **THEN** its edge style has `endArrow=open`

### Requirement: Roles use draw.io symbols
A shape with a family role SHALL use draw.io's built-in UML or BPMN symbol for that role where draw.io has one, and the style of its shape kind otherwise.

#### Scenario: A BPMN gateway uses the BPMN symbol
- **WHEN** a BPMN exclusive gateway is rendered to draw.io
- **THEN** its cell style is draw.io's BPMN gateway symbol

### Requirement: Labels are literal and styled
Each label SHALL show its text exactly, with no markup interpreted. Fill, stroke and font colours SHALL be the paint's colours as hex, with `none` where a colour is absent. Opacity SHALL be draw.io's fill, stroke and text opacity. Wrapping labels SHALL wrap within their cell. Rotated text SHALL keep its rotation.

#### Scenario: Markup in a label stays text
- **WHEN** a node labelled `<b>x</b> & y` is rendered to draw.io
- **THEN** draw.io shows the label as `<b>x</b> & y`, not as bold text
