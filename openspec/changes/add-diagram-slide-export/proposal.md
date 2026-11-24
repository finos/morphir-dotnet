# Proposal

## Why

Diagrams kept next to Morphir models are often presented, and today they reach a slide only as a pasted picture. That picture can't be edited, restyled or read by assistive technology, and it goes stale when the model changes. `add-diagram-canvas-export` gives every diagram a renderer-neutral layout and a renderer contract. This change uses that contract to write the diagram straight into the presentation formats teams use: PowerPoint, OpenDocument presentations, Google Slides and reveal.js. Where the format allows, the shapes are native and editable.

For example, a Mermaid flowchart becomes a 16:9 PowerPoint slide. Its nodes are PowerPoint shapes with real text, centred in the slide's content area, and its labels are large enough to read when projected:

```csharp
var placement = SlidePlacement.Widescreen.WithArea(SlideArea.Content);
await DiagramExport.RenderAsync([layout], new PowerPointRenderer(placement), file, DegradationPolicy.AllowDegraded, ct);
```

## What Changes

- **Slide placement:** a shared description of the following:
  - the slide size (16:9 by default, or 4:3, or custom)
  - the area the diagram goes in, as fractions of the slide or as exact distances
  - how it fits there: scaled uniformly to fit inside, centred

  Placement fits everything the layout draws, including BPMN shapes at negative coordinates, and rounds each edge once onto the target's grid, so nothing leaves the area. Text and geometry scale together, but stroke widths and arrowheads keep their theme sizes. A diagram enlarged for a slide is then readable without heavy lines.
- **PowerPoint (`.pptx`):** a new presentation with one slide per page, written as OpenXML with no third-party dependency.
  - Shapes use DrawingML preset geometry, and paths use custom geometry.
  - Text is real, editable text.
  - Images, such as icons and pages under the picture fallback, are SVG with PNG fallbacks.
  - Lines are exact paths by default, with every arrowhead kept. An option makes them PowerPoint connectors glued to their shapes instead, which follow moved shapes but lose the exact route and the sequence-diagram heads.
- **OpenDocument presentation (`.odp`):** a new presentation for LibreOffice and OpenOffice Impress, with custom shapes that carry real geometry, connectors glued to their shapes that keep their routes and arrowheads, text, links, and SVG images with PNG fallbacks.
- **Google Slides:** a JSON body for the Slides API `presentations.batchUpdate` method. The caller gives the target presentation's page size and an unused object ID namespace. The requests create slides, shapes, text and lines with deterministic object IDs, either on one existing slide or on new slides. The page, object and ID limits are checked before anything is written. The library makes no network calls and holds no credentials; the caller sends the requests with their own client.
- **reveal.js (`.reveal.html`):** a deck with one section per page, holding the page's original SVG inline, with identifiers made unique for each section. It uses the contract's original-SVG mode, so it is native for every diagram without claiming editable connectors. reveal.js is loaded from a configurable base address that defaults to a pinned version.
- **Determinism:** each format gives byte-identical output for equal input within one .NET runtime version on one operating system, because the runtime's ZIP writer records the platform. Across runtimes, the ZIP formats have the same entries, the same contents for every entry other than PNG images, and PNG images with the same pixels, because PNG compression comes from the runtime. A PNG fallback that cannot be drawn fails the render before anything is written.
- All four renderers live in one new package, `Morphir.Diagrams.Slides`, which depends only on Morphir.Diagrams and Morphir.Diagrams.Raster.
- Nothing existing changes.

## Capabilities

### New Capabilities

- `slide-placement`: slide sizes, areas, fitting and which values scale when a layout is placed on a slide.
- `powerpoint-export`: the PowerPoint renderer.
- `opendocument-presentation-export`: the OpenDocument presentation renderer.
- `google-slides-export`: the Google Slides request renderer.
- `revealjs-export`: the reveal.js deck renderer.

### Modified Capabilities

None. The layout and renderer contract come from `add-diagram-canvas-export`, which this change requires.

## Tracking

Issue: [#706](https://github.com/finos/morphir-dotnet/issues/706). Per AGENTS.md, the change needs explicit approval before merge. This change depends on `add-diagram-canvas-export` and starts once that change has merged.

## Impact

- **Code:** new `src/Morphir.Diagrams.Slides` and `tests/Morphir.Diagrams.Slides.Tests`. No change to existing projects.
- **APIs:** additive only.
- **Build and packaging:** add the package to `LibraryPackages` after Morphir.Diagrams.Raster. Add the projects to `Morphir.slnx`, the suite to `build/Helpers/TestPlan.cs`, and the package to the diagrams smoke test.
- **Dependencies:** none from third parties. ZIP packages are written with `System.IO.Compression`, and XML and JSON with `System.Xml` and `System.Text.Json`. reveal.js is not bundled; the deck refers to it.
- **Documentation:** the package README, the diagrams documentation page and the changelog.
