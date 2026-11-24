# Proposal

## Why

`Morphir.Diagrams` turns every diagram family into SVG, and from SVG into PNG, JPEG and GIF. A team that wants the same diagram in another tool cannot get there today. Examples are an editable draw.io file, an Excalidraw sketch, a TikZ figure or, later, a PowerPoint slide. The only shared forms are the eight family models, which carry no geometry, and the SVG tree, whose styling is CSS that every consumer would have to re-implement. Nothing in the public API lets a renderer from another assembly plug in, or tells it which parts of a diagram it can draw faithfully.

This change adds a public, renderer-neutral layout of every diagram and a contract for renderers built on it, including renderers in users' own assemblies. It also ships the first two editable targets. For example:

```csharp
var pages = new[] { DiagramExport.Layout(document.Diagram, SvgRenderOptions.Default).Layout! };
var assessment = DiagramExport.Assess(pages, new ExcalidrawRenderer());   // Native, Degraded or Unsupported
await DiagramExport.RenderAsync(pages, new DrawIoRenderer(), file, DegradationPolicy.AllowDegraded, ct);
```

It comes now because the diagram families reached parity with diagrams.mbt in #692. The slide formats need this foundation, and they follow in `add-diagram-slide-export`.

## What Changes

- **Diagram layout:** a public, immutable model of a laid-out diagram, one per page. Its drawables are shapes, lines, text, images, composite elements, containers and groups. Each drawable has:
  - an identifier that is unique on its page
  - an optional element reference
  - a paint-order index
  - containment, which can differ from paint order

  Shapes have a kind (rectangle, rounded rectangle, ellipse, diamond, cylinder, hexagon, or a normalised path) and an optional family role, such as lifeline, BPMN gateway or final state.

  Lines have ends that can each bind to any drawable, including containers and lifelines. Arrowheads are independent of the bindings. Each line keeps its routing waypoints and its exact final path, including rounded corners and bridges.

  Text has baselines, alignment, rotation and a measured box. Each paint resolves fill, stroke and ink independently and records where each colour came from. Links come from the diagram's metadata.

  Every family produces the layout from the same drawing pass as its SVG. A test checks that the layout paints the same primitives as the SVG, in the same order, and that its references and owners agree with the SVG's `data-ref` attributes.
- **Units:** `Distance`, stored as whole quarter English Metric Units. It is exact for CSS pixels with two decimal places, as BPMN writes them, and for points, centimetres, millimetres, inches and EMU. `Fraction` (0 to 1) holds anchors, and `Scale` (greater than 0) holds scale factors.
- **Capabilities:** a closed set of layout features, each with a defined meaning. A renderer declares the features it draws natively. The library then:
  - assesses a list of pages for that renderer as native, degraded (with the list of approximations) or unsupported
  - lowers the layout to the declared features through fallbacks that apply in a fixed order and keep each drawable's identity
  - resolves every binding after the fallbacks, so none names a drawable that was removed or cannot take a binding
  - gives each line one route from a single table, so exact, smooth and polyline renderers each get the most faithful route they can draw
  - applies a strict, allow-degraded or picture-fallback policy

  Arrowheads beyond the filled arrow, translucent strokes and text, and paint order across containers are features too, so a renderer never receives something it did not declare.
- **Renderer contract:** a public renderer base type with a media type, a file extension, its native features and an asynchronous render of a plan into a stream. Only the library can create a plan, after assessment and lowering, so the assessment cannot be bypassed, and a plan records what it was lowered for, so it cannot be reused with a renderer it does not fit. Renderers in other assemblies return diagnostics created through new public factories. A renderer can also choose to draw each page's original SVG instead of its drawables. The contract fixes the stream, cancellation and failure rules. A layout visitor has default methods, and a compatibility policy lets the vocabulary grow without breaking compiled renderers. Renderers are registered explicitly; nothing is discovered by reflection, so the library stays AOT- and trim-clean.
- **`Morphir.Diagrams.Canvas` package:** draw.io (`.drawio`), Excalidraw (`.excalidraw`) and Obsidian Excalidraw (`.excalidraw.md`) renderers. They write deterministic files, and the package has no dependencies beyond Morphir.Diagrams.
- Existing SVG, raster, GIF and player output does not change, byte for byte. Digest baselines taken before the refactor prove it: SVG for every pinned corpus diagram, and the player, raster and GIF output of representative diagrams.

## Capabilities

### New Capabilities

- `diagram-layout`: the public laid-out model of every family, its units, and its agreement with the SVG of the same diagram.
- `diagram-renderers`: layout features, assessment, lowering and degradation policies, the renderer contract and explicit registration.
- `drawio-export`: the draw.io renderer.
- `excalidraw-export`: the Excalidraw renderer and its Obsidian Markdown form.

### Modified Capabilities

None. SVG, raster export and the player keep their requirements and their exact output.

## Tracking

Issue: [#705](https://github.com/finos/morphir-dotnet/issues/705). Per AGENTS.md, the change needs explicit approval before merge. Related: #692 (diagrams.mbt parity, merged), the follow-up change `add-diagram-slide-export`, and the migration PRD at `docs/content/contributing/design/prds/migrate-diagrams.md`.

## Impact

- **Code:** `src/Morphir.Diagrams`.
  - The family writers draw through one drawing surface that builds both the SVG scene and the layout.
  - New public namespaces `Morphir.Diagrams.Layout` and `Morphir.Diagrams.Export`.
  - New project `src/Morphir.Diagrams.Canvas` and its test project `tests/Morphir.Diagrams.Canvas.Tests`.
- **APIs:** additive only. `SvgRenderer`, `DiagramRaster` and `AnimatedRenderer` keep their signatures and output.
- **Build and packaging:**
  - Add the new package to `LibraryPackages` in `build/Build.cs`, after Morphir.Diagrams.
  - Add the new projects to `Morphir.slnx`, the test suite to `build/Helpers/TestPlan.cs`, and the package to the diagrams smoke test.
- **Dependencies:** none new. JSON is written with `System.Text.Json`'s `Utf8JsonWriter`, and XML with `System.Xml`'s `XmlWriter`.
- **Documentation:** package READMEs, the diagrams documentation page and the changelog.
