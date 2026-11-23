# Spec Delta

## Purpose

Lets renderers for other formats, including renderers in users' own assemblies, draw diagram layouts through one contract. Before anything is written, a caller can learn three things: which parts of a layout a renderer draws natively, how the library approximates the rest, and what the output will contain.

For example, Excalidraw has no cylinder. A renderer that does not declare cylinders receives each cylinder as a path, or as a rectangle when it does not declare paths either. The caller can see this from an assessment before rendering, and the result reports it as a degradation.

## ADDED Requirements

### Requirement: Layout features are a closed set
The library SHALL define a closed set of layout features: the drawing features below and the presentation features of the next requirement. A layout SHALL report exactly the features its drawables use. The drawing features are:
- **Shapes:** rounded rectangles, ellipses, diamonds, cylinders, hexagons, paths.
- **Lines:** polylines, exact routes, smooth curves, attached connectors, line targets.
- **Heads:** open and circle heads, message heads.

#### Scenario: A flowchart reports what it uses
- **WHEN** a flowchart with a decision node, a subgraph and orthogonal links is laid out
- **THEN** its features include diamonds, containers, attached connectors and polylines, and do not include cylinders

### Requirement: The closed set includes presentation features
The presentation features of the closed set SHALL be:
- **Text:** wrapped text, rotated text.
- **Structure:** containers, family roles, interleaved painting, interleaved containers.
- **Style:** fill opacity, stroke and text opacity, dash patterns.
- **Other:** links, SVG images, pages.

#### Scenario: A sequence diagram reports interleaved painting
- **WHEN** a sequence diagram whose lifelines are painted before its messages is laid out
- **THEN** its features include interleaved painting and dash patterns

### Requirement: Line features have defined meanings
A renderer that declares a line feature SHALL draw it as defined here:
- **Polylines:** one line through all of its waypoints.
- **Exact routes:** the line's exact final path.
- **Smooth curves:** a smooth curve through the waypoints of a curved line. The curve may differ from the exact path.
- **Attached connectors:** line ends that stay bound to shapes, images or containers when those move.
- **Line targets:** line ends bound to another line, at a fraction of its length.

#### Scenario: Smooth curves need not match the exact path
- **WHEN** a curved line is rendered by a renderer that declares smooth curves but not exact routes
- **THEN** the renderer receives the line's waypoints with the curved route style and no exact path

### Requirement: Head features have defined meanings
A renderer that declares a head feature SHALL draw these arrowheads at the theme's marker size:
- **Open and circle heads:** the open arrow and the filled circle.
- **Message heads:** the cross, the asynchronous head and the four half heads, upper or lower, open or filled.

#### Scenario: A lost message keeps its cross
- **WHEN** a sequence message with a cross head is rendered by a renderer that declares message heads
- **THEN** the renderer receives the line with its cross head

### Requirement: Text and structure features have defined meanings
A renderer that declares one of these features SHALL draw it as defined here:
- **Wrapped text:** text that wraps within its box.
- **Rotated text:** text at its rotation.

Every feature without a definition in these requirements means drawing it as described.

#### Scenario: Rotated text arrives rotated
- **WHEN** a BPMN lane header is rendered by a renderer that declares rotated text
- **THEN** the renderer receives the header text with its rotation of 270 degrees

### Requirement: Paint-order features have defined meanings
A renderer that declares one of these features SHALL paint as defined here:
- **Interleaved painting:** the drawables of composite elements and groups in page-wide paint order, not as units.
- **Interleaved containers:** a native container's children in page-wide paint order relative to drawables outside it.

#### Scenario: A participant's lifeline stays under the messages
- **WHEN** a sequence diagram is rendered by a renderer that declares interleaved painting
- **THEN** it paints each lifeline before the messages and each participant's head after them

### Requirement: Style and image features have defined meanings
A renderer that declares one of these features SHALL draw it as defined here:
- **Fill opacity:** a fill's opacity without changing its stroke.
- **Stroke and text opacity:** a stroke's or text's opacity without changing the fill.
- **SVG images:** an image drawn from an SVG document.

#### Scenario: A translucent fill keeps an opaque stroke
- **WHEN** a shape with a fill at 50 percent opacity and an opaque stroke is rendered by a renderer that declares fill opacity
- **THEN** the renderer receives the fill with its opacity and the stroke opaque

### Requirement: Every renderer receives the baseline
Every renderer SHALL be able to draw the baseline, which is outside the feature set and always native:
- rectangles
- single straight line segments with no head or a filled arrowhead at each end
- upright text of one or more lines
- composite elements and groups, each painted together in containment order
- opaque solid colours
- solid strokes, and dashed strokes with the theme's default dash

#### Scenario: A renderer that declares no features still renders
- **WHEN** a diagram is rendered with a renderer that declares no features
- **THEN** it receives only drawables from the baseline

### Requirement: Shape kinds fall back to simpler shapes
When a renderer does not declare a shape feature, the shape SHALL fall back as follows:
- a rounded rectangle becomes a rectangle
- an ellipse, diamond, cylinder or hexagon becomes a path that draws the same outline, or a rectangle over the same box when paths are not declared, and never an image
- a path from the layout becomes an SVG image of its outline, or a rectangle over its box when SVG images are not declared

#### Scenario: A cylinder falls back to a path
- **WHEN** a layout with a cylinder is rendered with a renderer that declares paths but not cylinders
- **THEN** the renderer receives a path shape that draws the cylinder's outline in the same box

#### Scenario: A cylinder falls back to a rectangle
- **WHEN** a layout with a cylinder is rendered with a renderer that declares SVG images but neither cylinders nor paths
- **THEN** the renderer receives a rectangle with the cylinder's box, paint, text, identifier and reference

### Requirement: Structure, text, style and links are simplified
When a renderer does not declare them, these features SHALL fall back as follows:
- A container becomes a group with its header as a text drawable.
- A family role is removed.
- Rotated text becomes upright in the same box.
- Wrapped text becomes its explicit lines.
- A translucent fill, stroke or ink becomes its colour composited over the page background, opaque.
- A dash pattern becomes the theme's default dash.
- A link is removed.
- An image is removed when SVG images are not declared.

#### Scenario: A subgraph becomes a group
- **WHEN** a flowchart with a subgraph is rendered by a renderer that does not declare containers
- **THEN** the renderer receives a group, with the subgraph's identifier and reference, that holds its header text and its nodes

#### Scenario: A translucent fill becomes opaque
- **WHEN** a shape with a fill at 50 percent opacity is rendered by a renderer that does not declare fill opacity
- **THEN** the renderer receives that fill composited over the page background as an opaque colour

#### Scenario: A custom theme's translucent stroke becomes opaque
- **WHEN** a diagram drawn with a custom theme whose family stroke has 50 percent opacity is rendered by a renderer that declares fill opacity but not stroke and text opacity
- **THEN** the renderer receives every such stroke as an opaque colour, and the assessment lists stroke and text opacity

### Requirement: Arrowheads fall back to the filled arrow
When a renderer does not declare message heads, asynchronous and open half heads SHALL become open arrows, and filled half heads and cross heads SHALL become filled arrows. When it does not declare open and circle heads, open arrows and circles SHALL then become filled arrows.

#### Scenario: A half head reaches the baseline
- **WHEN** a line with an open upper half head is rendered by a renderer that declares no head features
- **THEN** the renderer receives that end with a filled arrow, and the assessment lists message heads for the line

### Requirement: Bindings are resolved across the page
After every drawable has been replaced, each binding SHALL be kept only when the renderer declares attached connectors and its target is still on the page and eligible:
- a shape or an image
- a container, when the renderer declares containers
- a line, when the renderer declares line targets and the line can take a binding

Every other binding SHALL be removed. The end SHALL keep its point, and the removal SHALL be a degradation of the feature that the binding needed.

#### Scenario: A binding to a lowered container is removed
- **WHEN** a link that ends at a subgraph is rendered by a renderer that declares attached connectors but not containers
- **THEN** that end has no binding and keeps its point, and the assessment lists containers for the line

#### Scenario: Lifeline bindings need line targets
- **WHEN** a sequence message is rendered by a renderer that declares attached connectors but not line targets
- **THEN** its ends have no bindings, and the assessment lists line targets for the message

#### Scenario: Every surviving binding resolves
- **WHEN** any pinned corpus diagram is rendered with a renderer that declares an arbitrary subset of the features
- **THEN** every binding the renderer receives names an eligible drawable on the same page

### Requirement: A line takes a binding only while it stays one line
A line SHALL be able to take a binding only when the route rules keep it as one line, judged without its own bindings so that no cycle arises.

#### Scenario: A binding to a line that would be split is removed
- **WHEN** a line bound to an orthogonal line with two bends is rendered by a renderer that declares attached connectors and line targets but neither exact routes nor polylines
- **THEN** that end has no binding, because its target becomes a group of segments

### Requirement: Each line gets one route
After bindings are resolved, each line SHALL get the first route that applies:
1. Exact routes: its exact path, waypoints and route style.
2. Smooth curves, for a curved line: its waypoints and the curved style.
3. Polylines: a polyline through its waypoints.
4. A bound end or only two waypoints: one straight segment between its ends.
5. Otherwise: a group of straight segments, heads on the first and last.

#### Scenario: A curve becomes a polyline
- **WHEN** a curved line is rendered by a renderer that declares polylines but neither exact routes nor smooth curves
- **THEN** the renderer receives a polyline through the line's waypoints

#### Scenario: Exact routes keep a curve exact
- **WHEN** a curved line is rendered by a renderer that declares exact routes but not smooth curves
- **THEN** the renderer receives the line's exact path

#### Scenario: A smooth curve is not split
- **WHEN** a curved line is rendered by a renderer that declares smooth curves but not polylines
- **THEN** the renderer receives the line's waypoints with the curved style, not separate segments

#### Scenario: A bound line keeps its binding
- **WHEN** an orthogonal link with two bends is rendered by a renderer that declares attached connectors but not polylines
- **THEN** the renderer receives one straight segment whose ends are still bound to the two shapes

#### Scenario: An unbound polyline is split
- **WHEN** the same link is rendered by a renderer that declares neither attached connectors nor polylines
- **THEN** the renderer receives a group of three straight segments with the line's identifier and reference, and an arrowhead on the last segment

### Requirement: A route change is a degradation only when it shows
A route from the route rules SHALL be a degradation only when it draws differently from the line's exact path, such as a polyline that loses rounded corners, curves or bridge arcs. A smooth curve from the second rule SHALL NOT be a degradation.

#### Scenario: A straight link without exact routes is native
- **WHEN** a flowchart whose links are straight segments without bridges is assessed for a renderer that declares polylines but not exact routes
- **THEN** no degradation names exact routes

### Requirement: Paint order falls back to containment
After containers have become groups where they are not declared, each unit whose drawables are not consecutive in paint order SHALL be a degradation, because the renderer paints it as a unit:
- a composite element or group, when the renderer does not declare interleaved painting
- a container, when the renderer does not declare interleaved containers

Nothing else changes.

#### Scenario: A participant is painted together
- **WHEN** a sequence diagram is assessed for a renderer that does not declare interleaved painting
- **THEN** the assessment lists interleaved painting for each participant whose lifeline is painted before the messages and whose head is painted after them

#### Scenario: A native container is painted together
- **WHEN** a flowchart with a subgraph and a link from inside it to outside is assessed for a renderer that declares containers and interleaved painting but not interleaved containers
- **THEN** the assessment lists interleaved containers for the subgraph, and nothing for paint order of elements

### Requirement: Lowering applies the fallbacks in a fixed order
Lowering SHALL be a pure function of the pages and the declared features. It SHALL apply the fallbacks in this order:
1. containers
2. family roles
3. shape kinds and paths
4. images
5. bindings
6. routes
7. arrowheads
8. text rotation and wrapping
9. fill opacity, then stroke and text opacity
10. dash patterns
11. links
12. paint order

Several fallbacks on one drawable SHALL be one degradation, naming its first feature and its final form.

#### Scenario: A cylinder becomes a rectangle in one degradation
- **WHEN** a cylinder is lowered for a renderer that declares neither cylinders, paths nor SVG images
- **THEN** the result is a rectangle, and one degradation names cylinders with the rectangle fallback

### Requirement: Lowering keeps identity
After lowering:
- every drawable SHALL use only declared features and the baseline
- each surviving drawable SHALL keep its identifier, reference, box and paint-order index
- a drawable that becomes a group SHALL give its identifier and reference to that group

#### Scenario: A renderer never sees an undeclared feature
- **WHEN** any pinned corpus diagram is rendered with a renderer that declares an arbitrary subset of the features
- **THEN** every drawable it receives uses only that subset and the baseline, and every reference that survives keeps its identifier

### Requirement: A layout is assessed before rendering
For a list of pages and a renderer, an assessment SHALL give one of three answers:
- **native**, when no fallback applies
- **degraded**, listing each fallback applied, with its feature and the identifiers and references it affects
- **unsupported**, when several pages are given to a renderer that does not declare pages

Equal inputs SHALL give equal assessments.

#### Scenario: A BPMN data object loses its role
- **WHEN** a BPMN process with a data object is assessed for a renderer that declares no family roles
- **THEN** the assessment is degraded and lists the removal of the family role for the data object's reference

#### Scenario: Several pages need pages
- **WHEN** two layouts are assessed together for a renderer that does not declare pages
- **THEN** the assessment is unsupported and names pages

### Requirement: Strict renders refuse degradation
Under the strict policy, a degraded assessment SHALL write nothing and return the degradations as errors. Under every policy, an unsupported assessment SHALL write nothing and return the unsupported features as errors.

#### Scenario: Strict refuses a degraded render
- **WHEN** a degraded layout is rendered under the strict policy
- **THEN** nothing is written to the output and the result holds the degradations as errors

### Requirement: Degraded renders report what changed
Under the allow-degraded policy, a degraded assessment SHALL write the lowered layout and return each degradation as a warning with the code `layout/degraded`.

#### Scenario: A degradation is a warning
- **WHEN** a layout with a cylinder is rendered under the allow-degraded policy by a renderer without cylinders
- **THEN** the output is written and the result holds a `layout/degraded` warning naming cylinders and the element

### Requirement: Picture fallback replaces degraded pages
Under the picture-fallback policy, each degraded page SHALL be replaced by a page holding one SVG image of that page's SVG over the page's bounds, when the renderer declares SVG images. The result SHALL hold one `layout/pictured` warning naming the page. When the renderer does not declare SVG images, the policy SHALL behave as allow degraded.

#### Scenario: Picture fallback embeds the page
- **WHEN** a degraded layout is rendered under the picture-fallback policy by a renderer that declares SVG images
- **THEN** the renderer receives one page holding one image of the page's SVG, and the result holds a `layout/pictured` warning

### Requirement: Some renderers draw the original SVG
A renderer SHALL declare one of two modes:
- **Drawables**, the default: it receives lowered pages and draws their drawables.
- **Original SVG**: it receives the pages unlowered and draws only each page's SVG. It declares no features, and its assessment is native for any pages it accepts.

#### Scenario: An original-SVG renderer gets the SVG unchanged
- **WHEN** a flowchart with a cylinder and a subgraph is rendered by an original-SVG renderer
- **THEN** the assessment is native, and the plan's page has the layout's SVG and its original drawables

### Requirement: Renderers render only plans
A renderer SHALL state its media type, its file extension, its mode and its native features. Its render operation SHALL take a render plan, which only the library can create, together with an output stream and a cancellation token, and return the renderer's diagnostics. A plan SHALL record its mode and the features it was lowered to. A renderer from any assembly SHALL be usable through this contract.

#### Scenario: A renderer from another assembly renders
- **WHEN** a test assembly defines a renderer and renders a flowchart with it
- **THEN** the renderer receives a plan with the lowered layout, and its output is written to the caller's stream

### Requirement: A plan fits only matching renderers
The library SHALL offer a check that a plan fits a renderer: the plan's mode is the renderer's mode, and the features it was lowered to are among the renderer's native features. The check SHALL throw an argument error otherwise. Every renderer in the library's packages SHALL make this check before writing.

#### Scenario: A plan made for another renderer is refused
- **WHEN** a plan lowered for draw.io is given directly to the Excalidraw renderer
- **THEN** the render fails with an argument error and writes nothing

### Requirement: Renderers can report diagnostics
The library SHALL offer public factories that create an error or a warning from a code and a message. A code SHALL be a prefix and a name separated by one slash, each of lowercase letters, digits and hyphens. Other codes and blank messages SHALL be refused with an argument error. The factories SHALL take the code and message as nullable text, so C# can pass what it has.

#### Scenario: A renderer in another assembly reports
- **WHEN** a renderer defined in a test assembly returns an error `test/failed` and a warning `test/approximated`
- **THEN** the render result holds both, with their codes, severities and messages

#### Scenario: A malformed code is refused
- **WHEN** a caller creates a diagnostic with the code `Bad Code`
- **THEN** creation fails with an argument error

### Requirement: Output streams are the caller's
The output stream SHALL need only to be writable. The bytes written SHALL NOT depend on whether it can seek. The library SHALL flush the stream after the renderer completes and SHALL NOT close it.

#### Scenario: A forward-only stream gets the same bytes
- **WHEN** the same plan is rendered into a seekable stream and into a forward-only stream
- **THEN** both receive the same bytes and both remain open

### Requirement: Cancellation and failures are reported
A render cancelled before it starts SHALL write nothing and return a cancelled task. A render cancelled while writing SHALL return a cancelled task, and any partial output SHALL be the caller's to discard. An exception thrown by a renderer SHALL reach the caller unchanged. The result SHALL hold the assessment and every diagnostic from the assessment, the policy and the renderer.

#### Scenario: A renderer that throws after writing
- **WHEN** a renderer writes some bytes and then throws
- **THEN** the caller receives that exception and the stream holds the partial bytes

#### Scenario: Cancellation before the start writes nothing
- **WHEN** a render is started with a token that is already cancelled
- **THEN** the task is cancelled and nothing is written

### Requirement: Layouts can be visited
The library SHALL offer a visitor over drawables with one method for each drawable kind. Composite elements, containers and groups SHALL visit their children by default.

#### Scenario: Default visiting reaches nested shapes
- **WHEN** a visitor that overrides only the shape method visits a layout with a container of two shapes
- **THEN** the shape method is called for both shapes

### Requirement: New layout vocabulary does not break renderers
These rules SHALL keep earlier renderers working as the vocabulary grows:
- A new drawable kind, shape kind, path command or arrowhead SHALL arrive only with a new feature, so a renderer that does not declare it never receives it.
- An existing `Match` member called on a newer case SHALL throw an invalid-operation error.
- Layout types SHALL have no public constructors, and the public API SHALL be checked against an approved listing.

#### Scenario: An earlier renderer is unaffected by a new feature
- **WHEN** a renderer that declares the current features renders a layout that uses a feature added later
- **THEN** it receives that feature's fallback and no drawable of the new kind

### Requirement: Advisory values can be ignored
Family roles, colour sources, route styles, line caps and line joins SHALL be advisory. A renderer SHALL treat a value it does not recognise as absent, and the drawable SHALL still be drawable without it.

#### Scenario: An unknown role is ignored
- **WHEN** a renderer that knows no family roles receives a gateway shape with its role
- **THEN** it can draw the shape from its diamond kind alone

### Requirement: Renderers are registered explicitly
A caller SHALL register renderers in an immutable registry and look one up by file name. Lookup SHALL match the longest registered extension ordinally and ignore case, so that `.excalidraw.md` is found before `.md`. Registering an extension again SHALL replace the earlier renderer. A C# caller's nullable analysis SHALL treat the found renderer as set only when lookup succeeds. Registration and lookup SHALL work under Native AOT and trimming with no warnings.

#### Scenario: The longest extension wins
- **WHEN** renderers for `.md` and `.excalidraw.md` are registered and `flow.excalidraw.md` is looked up
- **THEN** the `.excalidraw.md` renderer is returned

#### Scenario: A user's renderer replaces a built-in one
- **WHEN** a caller registers its own renderer for `.drawio` after the built-in one
- **THEN** looking up `flow.drawio` returns the caller's renderer
