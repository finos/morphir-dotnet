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
