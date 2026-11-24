# Spec Delta

## Purpose

Describes where a diagram goes on a slide and how it is sized there, so that every slide renderer places a layout the same way. A diagram enlarged for projection then stays readable without heavy lines.

For example, a flowchart 600 px wide placed in the content area of a 16:9 slide is scaled to fill that area's width and centred vertically. Its labels grow by the same factor, but its 1.5 px strokes stay 1.5 px.

## ADDED Requirements

### Requirement: Slides have a size
A placement SHALL have a slide size: widescreen 16:9 (40/3 by 7.5 inches) by default, standard 4:3 (10 by 7.5 inches), or custom distances greater than zero.

#### Scenario: The default slide is widescreen
- **WHEN** a caller uses the default placement
- **THEN** the slide is 12,192,000 by 6,858,000 EMU

### Requirement: Diagrams go in an area
A placement SHALL place the diagram in an area of the slide, given either as fractions of the slide's width and height or as exact distances. The area SHALL have a positive width and height and lie within the slide. The predefined content area SHALL leave a margin of one twelfth of the slide's width and height on each side, and it SHALL be the default area.

#### Scenario: An area outside the slide is refused
- **WHEN** a caller gives an area that extends past the slide's right edge
- **THEN** creating the placement fails with an argument error

### Requirement: Placement fits the drawn extent
The fitted extent SHALL be the smallest box that holds the layout's bounds and the geometry of every drawable: boxes, line points, path points and control points. Stroke widths and arrowheads are not part of it. An extent with zero width or height SHALL be refused with an argument error.

#### Scenario: Negative BPMN coordinates are fitted
- **WHEN** a BPMN diagram whose DI places a task at x = -40 is placed
- **THEN** the fitted extent starts at x = -40, and the task lies inside the area

### Requirement: Placement rounds to the target's grid
Each renderer SHALL give placement its grid: whole EMU for PowerPoint and Google Slides, and a thousandth of a millimetre for OpenDocument. Placement SHALL round the area inwards onto the grid, and refuse with an argument error an area that is empty after rounding. It SHALL map each absolute coordinate with one exact scale and one rounding, half to even, using checked arithmetic. A size SHALL be the difference of its two rounded edges.

#### Scenario: Rounding never leaves the area
- **WHEN** layouts whose scaled edges fall exactly on a rounding boundary are placed in fractional areas of a 4:3 slide, for each grid
- **THEN** every placed edge lies on the grid and within the rounded area

### Requirement: Diagrams fit inside their area
The fitted extent SHALL be scaled uniformly by the largest exact factor that keeps it inside the rounded area, and then centred in the area. A caller SHALL be able to cap the factor with a maximum scale.

#### Scenario: A wide diagram fills the width
- **WHEN** a layout twice as wide as it is tall is placed in a square area
- **THEN** its width equals the area's width, and it is centred vertically to within one grid step

#### Scenario: A maximum scale caps the growth
- **WHEN** a small layout is placed with a maximum scale of 1
- **THEN** it keeps its size and is centred in the area

### Requirement: Strokes keep their size
Placement SHALL scale positions, sizes, waypoints, path points and control points, corner radii and font sizes by the fit factor. It SHALL keep stroke widths, dash lengths and arrowhead sizes as the theme set them. Strokes, joins and arrowheads can therefore extend past the area, by at most half the stroke width times the stroke's miter limit, plus the arrowhead's length.

#### Scenario: Text grows and strokes do not
- **WHEN** a layout is placed with a fit factor of 2
- **THEN** each font size is doubled, and each stroke width and dash length is unchanged
