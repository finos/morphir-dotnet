# Spec Delta

## Purpose

Writes diagrams as an OpenDocument presentation for LibreOffice and OpenOffice Impress, with native shapes and connectors glued to them, so the diagram stays editable in open office suites.

For example, the link `A --> B` becomes an Impress connector whose ends are glued to the shapes of `A` and `B`, and which follows the diagram's route.

## ADDED Requirements

### Requirement: The renderer writes presentations
The OpenDocument renderer SHALL write an OpenDocument presentation with the extension `.odp` and the media type `application/vnd.oasis.opendocument.presentation`. The package's first entry SHALL be `mimetype`, stored uncompressed with no extra field. It SHALL write one page for each layout, at the placement's slide size. The package SHALL open in LibreOffice Impress without an error.

#### Scenario: The package starts with its media type
- **WHEN** a layout is rendered
- **THEN** the raw first local header of the package names `mimetype`, is stored uncompressed, has no extra field, and holds `application/vnd.oasis.opendocument.presentation`

### Requirement: Presentations are deterministic
Within one .NET runtime version on one operating system, equal plans and options SHALL give byte-identical packages, whether or not the output stream can seek. On every runtime and operating system, the entries SHALL have the same names, every entry other than a PNG image SHALL have the same uncompressed content, and each PNG image SHALL decode to the same pixels.

#### Scenario: Equal input gives equal bytes
- **WHEN** the same plan is rendered twice
- **THEN** the two packages are byte-identical

### Requirement: Shapes have real geometry
Each shape SHALL be a custom shape whose name is its element reference, or its drawable identifier when it has no reference. Each SHALL carry its own enhanced geometry and view box:
- rectangles, rounded rectangles, ellipses, diamonds, cylinders and hexagons use a geometry for that kind, which also names the kind for Impress
- paths use their path data

Text SHALL be editable text in the shape.

#### Scenario: A cylinder has its own geometry
- **WHEN** a cylinder node is rendered
- **THEN** its custom shape carries enhanced geometry and a view box that draw the cylinder, not only a type name

### Requirement: Paths use enhanced-path commands
A path's moves, lines, cubic and quadratic curves and closes SHALL be the enhanced-path commands `M`, `L`, `C`, `Q` and `Z`. Arcs SHALL first be converted to cubic curves within 0.01 CSS pixels. An open path SHALL end with `N` and have no fill.

#### Scenario: A BPMN icon with arcs is drawn
- **WHEN** a BPMN event icon whose path has arcs is rendered
- **THEN** its enhanced path uses only `M`, `L`, `C`, `Q`, `Z`, `N` and `F`, and draws the same outline within 0.01 CSS pixels

### Requirement: Connectors are glued and keep their route
Each line SHALL be a connector. It SHALL be glued at each bound end to the glue point of its shape nearest the anchor, and it SHALL carry its exact path. Its arrowheads SHALL be markers defined in the styles, so every head follows the connector. Because markers are filled and never stroked, a head that the SVG strokes, such as the cross or an open half head, SHALL be the closed outline of that stroke at the theme's marker stroke width.

#### Scenario: A cross head is a filled outline
- **WHEN** a line with a cross head is rendered
- **THEN** its end marker is a closed outline of the cross's two strokes, and Impress shows the same cross as the SVG

#### Scenario: A connector is glued to both shapes
- **WHEN** the link `A --> B` is rendered
- **THEN** the page holds a connector whose start and end shapes are the shapes of `A` and `B`

### Requirement: The renderer declares its features
The OpenDocument renderer SHALL declare these features: rounded rectangles, ellipses, diamonds, cylinders, hexagons, paths, polylines, exact routes, attached connectors, open and circle heads, message heads, wrapped text, rotated text, interleaved painting, fill opacity, dash patterns, links, SVG images and pages. It SHALL NOT declare stroke and text opacity, because OpenDocument text has no standard opacity.

#### Scenario: Only a subgraph degrades a flowchart
- **WHEN** a flowchart with a subgraph is assessed for the OpenDocument renderer
- **THEN** the assessment is degraded only by the container falling back to a group and the bindings of any link that ends at it

### Requirement: Styles, links and images are kept
Fills, strokes and text SHALL use the paint's colours, with fill opacity as the fill's opacity, and the stroke widths and dash patterns. A drawable's first web link SHALL open when the shape is clicked. Each SVG image SHALL be a frame holding the SVG image followed by a PNG fallback, under the same SVG, PNG size and failure rules as the PowerPoint renderer.

#### Scenario: An image has a PNG fallback
- **WHEN** a degraded page is rendered under the picture-fallback policy
- **THEN** the page holds one frame whose SVG image is followed by a PNG image of the same SVG
