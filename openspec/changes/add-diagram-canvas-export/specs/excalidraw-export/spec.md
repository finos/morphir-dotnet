# Spec Delta

## Purpose

Writes any diagram as an editable Excalidraw scene, either as a plain `.excalidraw` file or as the Markdown note that the Obsidian Excalidraw plugin opens. Arrows stay bound to their shapes, and labels stay inside them where Excalidraw allows.

For example, the flowchart node `A[Start]` becomes a rectangle with the text `Start` bound inside it. The link `A --> B` becomes an arrow bound to both rectangles, so dragging `A` in Excalidraw drags the arrow's end with it.

## ADDED Requirements

### Requirement: The renderer writes Excalidraw scenes
The Excalidraw renderer SHALL write a JSON scene of type `excalidraw`, version 2, UTF-8 without a byte order mark, with the extension `.excalidraw` and the media type `application/vnd.excalidraw+json`. The scene's background SHALL be the layout's background colour. The package README SHALL name the Excalidraw release that the scenes were checked against.

#### Scenario: A flowchart becomes a scene
- **WHEN** a flowchart is rendered with the Excalidraw renderer
- **THEN** the output is a JSON object with `type` `excalidraw`, `version` 2, and elements for its shapes, texts and arrows

### Requirement: Scenes are deterministic
Element identifiers SHALL be eight characters derived from paint order. Seeds, versions and nonces SHALL be derived from the identifier, and timestamps SHALL be fixed. Equal input SHALL therefore give byte-identical output.

#### Scenario: Equal input gives equal bytes
- **WHEN** the same layout is rendered twice
- **THEN** the two scenes are byte-identical

### Requirement: Excalidraw declares its native features
The Excalidraw renderer SHALL declare these features: rounded rectangles, ellipses, diamonds, polylines, smooth curves, attached connectors, open and circle heads, wrapped text, interleaved painting, links and SVG images. Containers SHALL therefore reach it as groups, cylinders and hexagons as rectangles, and translucent colours as opaque colours, because Excalidraw's element opacity would fade the fill and stroke together.

#### Scenario: A database cylinder is degraded
- **WHEN** a flowchart with the cylinder node `C[(Ledger)]` is assessed for the Excalidraw renderer
- **THEN** the assessment is degraded and lists cylinders with the rectangle fallback for `node=C`

### Requirement: Arrows are bound and labels are contained
Each line SHALL be an arrow element, with no arrowheads when it has none, because only arrows can be bound. Each bound end SHALL be bound to its drawable's element, which SHALL list the arrow among its bound elements. A curved line SHALL be a rounded arrow that passes through its waypoints.

A text in a rectangle, ellipse, diamond or arrow SHALL be bound to it as its container. Any other text SHALL be a free text element grouped with its drawable.

#### Scenario: An arrow is bound to both shapes
- **WHEN** the flowchart link `A --> B` is rendered
- **THEN** its arrow's start binding names the element of `A`, its end binding names the element of `B`, and both elements list the arrow

#### Scenario: A line without heads can still be bound
- **WHEN** a bound line with no arrowheads is rendered
- **THEN** it is an arrow element whose start and end arrowheads are null, and its bindings are kept

#### Scenario: An image's label is free text
- **WHEN** a path shape with a label is rendered as an SVG image
- **THEN** its label is a free text element in a group with the image

### Requirement: Arrowheads use Excalidraw's heads
The filled arrow SHALL be Excalidraw's `triangle` arrowhead, the open arrow its `arrow` arrowhead and the circle its `dot` arrowhead. An end with no head SHALL have a null arrowhead.

#### Scenario: An open arrow uses the arrow head
- **WHEN** a line whose target end has an open arrow is rendered
- **THEN** its arrow element's end arrowhead is `arrow`

### Requirement: Elements keep their references
Each element of a referenced drawable, or of a composite element with a reference, SHALL carry that reference in its custom data. Composite elements and groups SHALL be written as shared group identifiers, innermost first. Elements SHALL be written in paint order. A drawable's first web link SHALL be its element's link.

#### Scenario: A node keeps its reference
- **WHEN** a flowchart node `A` is rendered
- **THEN** its element's custom data holds the reference `node=A`

### Requirement: Style follows the palette and options
Stroke and background colours SHALL be the paint's colours as hex, with `transparent` where a colour is absent. Dashed strokes SHALL be Excalidraw's dashed stroke style. Text SHALL use Excalidraw's code font. The hand-drawn roughness SHALL default to 0, and callers SHALL be able to choose 0, 1 or 2.

#### Scenario: Roughness can be chosen
- **WHEN** a caller renders with roughness 1
- **THEN** every shape and arrow element has roughness 1

### Requirement: Obsidian notes wrap the scene
The Obsidian Excalidraw renderer SHALL write a Markdown note with the extension `.excalidraw.md` and the media type `text/markdown`. The note SHALL contain front matter that marks it as an Excalidraw plugin file, a text elements section, and the same scene as the Excalidraw renderer in a JSON drawing block. The package README SHALL name the plugin release that the notes were checked against.

#### Scenario: The note holds the scene
- **WHEN** a flowchart is rendered with the Obsidian Excalidraw renderer
- **THEN** the note's drawing block holds a scene equal to the Excalidraw renderer's scene for the same lowered layout, and each label is listed under text elements with its identifier

#### Scenario: Multi-line and empty notes
- **WHEN** a layout with a two-line label, and a layout with no text, are rendered as notes
- **THEN** the two-line label is listed with both lines before its identifier, and the empty layout has an empty text elements section

### Requirement: Obsidian text elements are listed exactly
In the text elements section, each text SHALL be written as its exact content, then a space, a caret and its eight-character element identifier, then a blank line.

#### Scenario: A label is listed with its identifier
- **WHEN** a node labelled `Start` is rendered as a note
- **THEN** the text elements section holds `Start ^` followed by the label's element identifier

### Requirement: Obsidian text that looks like a block reference is left to the scene
A text with a line that ends in a space, a caret and eight letters or digits SHALL be left out of the text elements section, so that the plugin cannot read part of it as another entry. The plugin SHALL then take that text from the scene.

#### Scenario: A label that ends like a block reference
- **WHEN** a node labelled `Total ^abcd1234` is rendered as a note
- **THEN** the text elements section has no entry for that label, and the plugin shows the label from the scene

### Requirement: Obsidian notes are self-contained
The Obsidian Excalidraw renderer SHALL declare the Excalidraw features except SVG images, so that each note is one file with no separate image files in the vault.

#### Scenario: Paths degrade in Obsidian notes
- **WHEN** a layout with a path shape is assessed for the Obsidian Excalidraw renderer
- **THEN** the assessment lists paths with the rectangle fallback
