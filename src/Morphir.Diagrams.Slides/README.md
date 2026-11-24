# Morphir.Diagrams.Slides

Places the layouts of [Morphir.Diagrams](../Morphir.Diagrams/README.md) on slides and writes them as presentations:
PowerPoint (`.pptx`), OpenDocument presentations (`.odp`), Google Slides `batchUpdate` requests (`.gslides.json`) and
reveal.js decks (`.reveal.html`). Each renderer is a `DiagramRenderer`, so the library assesses a layout for it and
lowers what it cannot draw before it writes. The package depends only on Morphir.Diagrams and Morphir.Diagrams.Raster,
makes no network requests, and works under Native AOT and trimming.

```csharp
using System.IO;
using System.Threading;
using Morphir.Diagrams;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Slides;

DiagramSource.TryParse("flowchart LR\n  A[Start] --> B{Approve?}", out var document, out _);
var layout = DiagramExport.Layout(document!.Diagram).Layout!;

using var pptx = File.Create("flow.pptx");
await DiagramExport.RenderAsync([layout], new PowerPointRenderer(), pptx, DegradationPolicy.AllowDegraded, CancellationToken.None);

using var odp = File.Create("flow.odp");
await DiagramExport.RenderAsync([layout], new OpenDocumentRenderer(), odp, DegradationPolicy.AllowDegraded, CancellationToken.None);
```

Each page of a plan becomes one slide. Every writer is deterministic: equal plans and options give equal bytes, whether
or not the output stream can seek.

## Placement

`SlidePlacement` puts each diagram in an area of the slide. The default is a 16:9 widescreen slide
(`SlidePlacement.Widescreen`, 13.333 × 7.5 inches) whose content area leaves a twelfth of the slide on each side;
`SlidePlacement.Standard` is 4:3. The diagram is scaled by the largest factor that keeps it inside the area and centred
there. Positions, sizes, routes and font sizes scale; stroke widths, dash lengths and arrowheads keep the theme's sizes.

```csharp
using Morphir.Diagrams.Layout;
using Morphir.Diagrams.Slides;

var placement = SlidePlacement.Standard
    .WithArea(SlideArea.FromFractions(Fraction.Create(0.1), Fraction.Create(0.2), Fraction.Create(0.8), Fraction.Create(0.7)))
    .WithMaximumScale(Scale.Create(2));
var renderer = new PowerPointRenderer(placement);
```

## PowerPoint

`PowerPointRenderer` writes a `.pptx` with one blank slide per page. Shapes are preset shapes (rectangles, rounded
rectangles, ellipses, diamonds, cans and hexagons) or custom geometry for any other outline, with their text inside as
editable text. Fills, strokes and text keep their colours and opacity, dashes their pattern, and a drawable's first web
link opens on click. An SVG image is a picture with the SVG and a PNG fallback for readers without SVG support; the SVG
carries the light palette's colours, because office applications read no CSS variables. The package passes the Open
XML SDK validator for Microsoft 365 and for Office 2010.

The connector option decides how lines are drawn:

- `PowerPointConnectors.Exact`, the default, draws each line along its exact route, with bridges and rounded corners,
  as a freeform shape. Every head is kept: arrows, open arrows and circles are line ends, and the cross, the
  asynchronous head and the half heads are shapes grouped with their line. Lines are not glued to shapes.
- `PowerPointConnectors.Glued` draws each line as a straight connector glued to the connection sites nearest its ends,
  so it follows shapes that you move. It loses the route, and message heads become arrows.

```csharp
using Morphir.Diagrams.Slides;

var glued = new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Glued);
var custom = new PowerPointRenderer(SlidePlacement.Widescreen, PowerPointConnectors.Exact, "Cascadia Mono");
```

Text uses Consolas by default, which Office installs; the last argument chooses another typeface. Office measures text
differently from the browser fonts that the layout was measured with, so a label can be a little wider or narrower than
in the SVG. Text that wraps in the SVG wraps within its box.

The packages were checked in LibreOffice Impress 26.8, which drew each family as the SVG does. LibreOffice draws the
heads of a freeform line that runs right to left at the wrong end; PowerPoint follows the file. Open the `.odp` instead
when LibreOffice is the target.

## OpenDocument

`OpenDocumentRenderer` writes an `.odp` for LibreOffice and OpenOffice Impress, one page per layout. Each shape is a
custom shape that carries the outline the SVG draws, with its text inside; its type is `non-primitive`, because Impress
replaces the geometry and text area of a preset type it recognises with its own. Each line is a connector glued at the
shape's nearest glue point and carrying its exact route, so Impress keeps the route until a glued shape moves and then
re-routes that connector. Every head is a marker that follows the connector; a stroked head, such as the cross, is the
outline of its stroke. Fills keep their opacity; strokes and text are opaque, because OpenDocument text has no standard
opacity. Text uses Liberation Mono by default, which LibreOffice installs.

```csharp
using Morphir.Diagrams.Slides;

var impress = new OpenDocumentRenderer(SlidePlacement.Widescreen, "DejaVu Sans Mono");
```

The output was checked in LibreOffice 26.8: one diagram of each family drew as the SVG does, and after a shape was moved
and the file saved, every connector stayed glued, the moved shape's connectors were re-routed with their heads, and the
others kept their routes.

## Google Slides

`GoogleSlidesRenderer` writes the request body of the Slides API `presentations.batchUpdate` method as JSON. The library
never calls the API: you send the body with your own authorised client. The options describe the target presentation:
its page size, through the placement's slide size; an object ID namespace that the presentation does not use yet; and
optionally an existing slide to draw one page on. Without an existing slide, the requests create one blank slide per page.

```csharp
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Morphir.Diagrams;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Slides;

DiagramSource.TryParse("flowchart LR\n  A[Start] --> B{Approve?}", out var document, out _);
var layout = DiagramExport.Layout(document!.Diagram).Layout!;
var options = GoogleSlidesOptions.Create(SlidePlacement.Widescreen, SlideObjectNamespace.Create("flow"))
    .WithExistingSlide(SlideObjectId.Create("p"));

using var body = new MemoryStream();
var result = await DiagramExport.RenderAsync([layout], new GoogleSlidesRenderer(options), body, DegradationPolicy.AllowDegraded, CancellationToken.None);

// Send the body with your own client and access token.
var presentationId = "your-presentation-id";
var accessToken = "your-oauth-access-token";
using var client = new HttpClient();
using var request = new HttpRequestMessage(HttpMethod.Post, $"https://slides.googleapis.com/v1/presentations/{presentationId}:batchUpdate")
{
    Content = new ByteArrayContent(body.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
};
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
using var response = await client.SendAsync(request);
```

Object IDs are `{namespace}_{page}_{kind}{index}`, so they are deterministic and never longer than 50 characters; a plan
is refused when it has more than 9,999 pages or a page more than 9,999,999 objects. Submit the same body twice with a
new namespace, because Google refuses an ID that already exists. The requests create every object in paint order, then
set text and styles, and connect lines last, at the connection sites of Google's shape types.

Google Slides draws rounded rectangles, ellipses, diamonds, cans, hexagons, attached connectors, open and circle heads,
wrapped text, fill opacity and links natively, and the renderer writes no groups. So other outlines become rectangles,
bound lines straight segments, message heads arrows, dash patterns the default dash, and text upright. Images are left
out, because `createImage` needs a public address. Text uses Roboto Mono.

The bodies of one diagram of each family were checked against the Slides API v1 discovery schema; they have not yet
been sent to a live presentation.

## reveal.js

`RevealJsRenderer` writes one HTML deck with a section per page, each holding the page's original SVG, scaled to fit
the slide. It draws the SVG rather than the drawables, so every diagram is native and the deck shows exactly what the
SVG renderer draws, including its links and metadata. Every identifier in a section's SVG gets the section's prefix, so
the same diagram can appear twice in one deck. The deck loads reveal.js 5.1.0 from jsDelivr; give a base address to use
an offline copy, and a title, which is written as text.

```csharp
using Morphir.Diagrams.Slides;

var offline = new RevealJsRenderer(RevealJsOptions.Default.WithBaseAddress("./reveal/").WithTitle("Order process"));
```

For an offline copy, put reveal.js's `dist` folder (from the `reveal.js` npm package, release 5.1.0) in a `reveal`
folder beside the deck.
