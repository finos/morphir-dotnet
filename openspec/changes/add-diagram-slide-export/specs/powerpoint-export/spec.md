# Spec Delta

## Purpose

Writes diagrams as a PowerPoint presentation whose shapes are native. Each node is a PowerPoint shape with editable text, so a presenter can restyle or adjust the diagram on the slide.

For example, the flowchart node `B{Approve?}` becomes a diamond shape holding the text `Approve?`, and the link from `A` becomes a line ending in an arrowhead at the diamond's edge.

## ADDED Requirements

### Requirement: The renderer writes presentations
The PowerPoint renderer SHALL write an Office Open XML presentation with the extension `.pptx` and the media type `application/vnd.openxmlformats-officedocument.presentationml.presentation`. It SHALL write one slide for each page, at the placement's slide size, in the order given. The package SHALL open in PowerPoint without a repair prompt.

#### Scenario: Two pages give two slides
- **WHEN** two layouts are rendered as one presentation
- **THEN** the presentation holds two slides, in the order given

### Requirement: Packages validate for current and older Office
The package SHALL pass the Open XML SDK validator for Microsoft 365 and for Office 2010. The SVG image extension SHALL be inside the blip's extension list, which Office 2010 readers skip.

#### Scenario: A package with an SVG image validates for Office 2010
- **WHEN** a page under the picture-fallback policy is rendered and validated as Office 2010
- **THEN** the validator reports no errors

### Requirement: Presentations are deterministic
Within one .NET runtime version on one operating system, equal plans and options SHALL give byte-identical packages, whether or not the output stream can seek. Entries SHALL be in a fixed order with fixed timestamps and no machine or user values. On every runtime and operating system, the entries SHALL have the same names, every entry other than a PNG image SHALL have the same uncompressed content, and each PNG image SHALL decode to the same pixels.

#### Scenario: Equal input gives equal bytes
- **WHEN** the same plan is rendered twice, once into a seekable stream and once into a forward-only stream
- **THEN** the two packages are byte-identical

### Requirement: Shapes are native
Each shape SHALL be a PowerPoint shape whose name is its element reference, or its drawable identifier when it has no reference:
- rectangles, rounded rectangles, ellipses, diamonds, cylinders and hexagons use the matching preset geometry
- paths use custom geometry, with quadratic curves as quadratic Bézier commands and arcs converted to cubic curves within 0.01 CSS pixels

Text SHALL be editable text with the layout's font size, alignment, wrapping and rotation.

#### Scenario: A decision node is a diamond with text
- **WHEN** the flowchart node `B{Approve?}` is rendered
- **THEN** the slide holds a diamond preset shape named `node=B` whose text is `Approve?`

#### Scenario: A BPMN icon is custom geometry
- **WHEN** a BPMN user task's icon path is rendered
- **THEN** the slide holds a custom-geometry shape that draws the same outline

### Requirement: The renderer declares its features
The PowerPoint renderer SHALL always declare these features: rounded rectangles, ellipses, diamonds, cylinders, hexagons, paths, open and circle heads, wrapped text, rotated text, interleaved painting, fill opacity, stroke and text opacity, dash patterns, links, SVG images and pages. It SHALL write the layout's composite elements and groups as separate shapes, so that page-wide paint order is kept. The only PowerPoint groups SHALL be a line grouped with its drawn heads.

#### Scenario: Exact connectors detach links
- **WHEN** a flowchart without subgraphs is assessed for the PowerPoint renderer with exact connectors
- **THEN** the assessment is degraded only by the removal of each link's bindings

### Requirement: The connector option decides the line features
The PowerPoint renderer's connector option SHALL decide its line features:
- **Exact connectors**, the default, SHALL add polylines, exact routes and message heads, and draw each line as its exact path, with no bindings.
- **Glued connectors** SHALL add attached connectors, and draw each line as a PowerPoint connector glued to the connection sites nearest its anchors.

#### Scenario: Glued connectors attach to shapes
- **WHEN** the link `A --> B` is rendered with glued connectors
- **THEN** the slide holds a connector whose start and end connections are the shapes of `A` and `B`

#### Scenario: Exact connectors keep their route
- **WHEN** an orthogonal link with two bends and a bridge is rendered with exact connectors
- **THEN** the slide holds a line that draws the link's exact path

### Requirement: Arrowheads are mapped
Filled arrows, open arrows and circles SHALL use DrawingML line ends at the size nearest the theme's marker. With exact connectors, cross, asynchronous and half heads SHALL be custom shapes that draw the head's outline at its end, grouped with their line. Glued connectors SHALL NOT declare message heads, because a grouped head would not follow a connector that PowerPoint re-routes.

#### Scenario: A cross head is drawn as a shape
- **WHEN** a line with a cross head is rendered with exact connectors
- **THEN** the slide holds a group of the line and a custom shape drawing the cross at its end

#### Scenario: Half heads are drawn as shapes
- **WHEN** lines with each of the four half heads are rendered with exact connectors
- **THEN** each line is grouped with a custom shape that draws its half head on the correct side

### Requirement: Colours and links are kept
Fills, strokes and text SHALL use the paint's colours, including opacity, with no fill or line where a colour is absent. Lines SHALL use the stroke width and dash pattern. A drawable's first web link SHALL be its click action in a slide show.

#### Scenario: A link opens on click
- **WHEN** a node whose first web link is `https://example.org` is rendered
- **THEN** the shape has a click action to `https://example.org`

### Requirement: PNG fallbacks report approximations
A PNG fallback SHALL have the fidelity of Morphir.Diagrams.Raster. When an image's SVG uses paint that the painter approximates, such as a colour name outside its table or a gradient, the render SHALL return a `slides/png-approximated` warning naming the image. The SVG image itself is exact.

#### Scenario: A named colour outside the painter's table is reported
- **WHEN** a sequence diagram with `box Aqua` is rendered under the picture-fallback policy
- **THEN** the result holds a `slides/png-approximated` warning naming the page's image

### Requirement: Images carry a PNG fallback
Each SVG image SHALL be stored as SVG, with a PNG drawn from the same SVG as the fallback for readers without SVG support. The PNG SHALL be drawn at two pixels per placed CSS pixel. When that exceeds the raster limits, the density SHALL be halved until it fits, but not below one. If a PNG cannot be drawn at one, the render SHALL write nothing and return the error `slides/raster-failed`. Cancellation SHALL be observed between images.

#### Scenario: A picture fallback page has both images
- **WHEN** a degraded page is rendered under the picture-fallback policy
- **THEN** the slide holds one picture whose SVG is the page's SVG and whose PNG is its raster

#### Scenario: A large image is drawn at a lower density
- **WHEN** an image whose PNG exceeds the raster limits at two pixels per CSS pixel but fits at one is rendered
- **THEN** its PNG has one pixel per placed CSS pixel and the render succeeds

#### Scenario: A raster failure writes nothing
- **WHEN** an image cannot be rasterised within the limits
- **THEN** nothing is written and the result holds `slides/raster-failed`
