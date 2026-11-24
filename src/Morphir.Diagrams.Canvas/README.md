# Morphir.Diagrams.Canvas

Exports the layouts of [Morphir.Diagrams](../Morphir.Diagrams/README.md) as editable canvas files: draw.io
(`.drawio`), Excalidraw (`.excalidraw`) and Obsidian Excalidraw notes (`.excalidraw.md`). Each renderer is a
`DiagramRenderer`, so the library assesses a layout for it and lowers what it cannot draw natively before it writes.
The package depends only on Morphir.Diagrams, writes deterministic files and works under Native AOT and trimming.

```csharp
using System.IO;
using System.Threading;
using Morphir.Diagrams;
using Morphir.Diagrams.Canvas;
using Morphir.Diagrams.Export;

DiagramSource.TryParse("flowchart LR\n  A[Start] --> B{Approve?}", out var document, out _);
var layout = DiagramExport.Layout(document!.Diagram).Layout!;
CanvasRenderers.Registry.TryFind("flow.drawio", out var renderer);
using var file = File.Create("flow.drawio");
var result = await DiagramExport.RenderAsync([layout], renderer!, file, DegradationPolicy.AllowDegraded, CancellationToken.None);
```

## draw.io

`DrawIoRenderer` writes an uncompressed `.drawio` file with one diagram per page. Every shape, text, image and
container is a vertex, and every line an edge, each wrapped in a `UserObject` whose `morphir-ref` attribute keeps the
element reference (a part of a composite element, such as a lifeline, carries its participant's reference). Edges name
the vertices of their bound ends, so they stay attached when you move shapes. Containers are container cells, and a
BPMN pool or lane is a swimlane with its header band. Labels are HTML-escaped, so markup in a label stays text.

draw.io draws rounded rectangles, ellipses, diamonds, cylinders, hexagons, polylines, smooth curves, attached
connectors, open and circle heads, wrapped and rotated text, containers, family roles, fill, stroke and text opacity,
dash patterns, links, SVG images and pages natively. So:

- Any other outline, such as a parallelogram, a BPMN icon or a data object, is an SVG image.
- An exact route with rounded corners or bridges becomes its waypoints.
- Message heads (the cross, the asynchronous head and the half heads) become arrows.
- A sequence message loses its binding to the lifeline, because draw.io has no line targets; it keeps its points.
- A container paints its children together, so a link that leaves a subgraph is painted above the subgraph's nodes.
- The parts of a composite element, such as a subroutine's side bars or a node's icon, are separate cells.

Family roles use draw.io's symbols: `shape=umlActor` for sequence actors, `shape=mxgraph.bpmn.task`,
`shape=mxgraph.bpmn.gateway2` and `shape=mxgraph.bpmn.event` for BPMN tasks, gateways and events, `shape=endState` for
final states, and `shape=note` for notes. Labels use JetBrains Mono, which draw.io replaces with its default font when it
is not installed.

The renderer was checked against the draw.io web app, release 32.7.5: one diagram of each family opened, every role
style above resolved to its draw.io shape, and after every vertex that an edge is bound to was moved, each bound edge
still ended on its vertex.

## Excalidraw

`ExcalidrawRenderer` writes a `.excalidraw` scene (version 2) of one page. Shapes are rectangles, ellipses and
diamonds with their text bound inside; every line is an arrow, bound at both ends to the elements it connects and listed
in their bound elements, so arrows follow shapes that you drag. A line with no heads is an arrow with null arrowheads.
The filled arrow is Excalidraw's `triangle` head, the open arrow its `arrow` head and the circle its `dot` head. A curved
line is a rounded arrow through its waypoints. Each element keeps its element reference in `customData.morphirRef`, a
composite element's parts share a group, and images are SVG files in the scene. Text uses Excalidraw's code font. The
hand-drawn roughness is 0 by default; `new ExcalidrawRenderer(1)` or `2` makes the scene sketchier.

Excalidraw draws rounded rectangles, ellipses, diamonds, polylines, smooth curves, attached connectors, open and circle
heads, wrapped text, interleaved painting, links and SVG images natively. So:

- Containers become groups with a header text, and cylinders and hexagons become rectangles.
- Any other outline becomes an SVG image, with its label as free text in a group with it.
- Translucent colours become opaque, because Excalidraw's element opacity fades fill, stroke and text together.
- Dash patterns become Excalidraw's dashed stroke, message heads become arrows, and text is upright.
- Sequence messages lose their bindings to lifelines, because Excalidraw binds arrows only to shapes.
- Empty text is left out, because Excalidraw deletes it when it loads a scene.

The scenes were checked against Excalidraw 0.18.0 (the `@excalidraw/excalidraw` component): one diagram of each family
loaded with every element, binding, bound text, arrowhead, reference and file intact, and after each bound shape was
dragged, the ends of its arrows stayed on it.

## Obsidian Excalidraw notes

`ObsidianExcalidrawRenderer` writes the same scene as a `.excalidraw.md` note for the Obsidian Excalidraw plugin: front
matter with `excalidraw-plugin: parsed`, a `## Text Elements` section that lists each text followed by ` ^` and its
element identifier, and the scene in a `json` drawing block between `%%` markers. A text with a line that ends like a
block reference (a space, a caret and eight letters or digits) is left out of the section, and the plugin reads it from
the scene. A note is one file, so the renderer declares no SVG images: paths and icons become rectangles.

The notes follow the format of the plugin's saved files; they have not been opened in Obsidian itself, which cannot run
without its desktop application.
