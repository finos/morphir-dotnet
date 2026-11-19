# Morphir.Diagrams.Raster

Converts the SVG that [Morphir.Diagrams](../Morphir.Diagrams/README.md) writes to PNG or JPEG,
and a playback plan to an animated GIF, in managed code: no browser, native library or process.
Equal SVG, options and samples give identical bytes on every platform, under JIT and Native AOT.

```csharp
using System;
using Morphir.Diagrams;
using Morphir.Diagrams.Raster;

var svg = Mermaid.RenderSvg("flowchart LR\n  A[Order] --> B{Valid?}\n  B -->|yes| C[Ship]",
    SvgRenderOptions.Create(DiagramThemes.Monokai, ThemeMode.Dark)).Output;

var png = DiagramRaster.Render(svg, RasterOptions.Png.WithScale(2));
Console.WriteLine(png.Succeeded ? $"{png.MediaType} {png.Width}x{png.Height}" : png.Diagnostics[0].ToString());

var jpeg = DiagramRaster.Render(svg, RasterOptions.Jpeg(85).WithBackground(RasterColor.Create(255, 255, 255)));
Console.WriteLine(jpeg.Bytes.Length);

if (Mermaid.TryParse("sequenceDiagram\n  A->>B: one\n  B-->>A: two", out var document, out _))
{
    var gif = DiagramRaster.RenderGif(SvgRenderer.BuildScene(document.Diagram), GifOptions.Create(2000, 10));
    Console.WriteLine(gif.Width);
}
```

## Still images

`DiagramRaster.Render(svg, options)` returns a `RasterImage` with its media type, actual width
and height, and bytes, or diagnostics and no bytes. `RasterOptions.Png` (the default, with alpha)
and `RasterOptions.Jpeg(quality)` (1 to 100, on white unless `WithBackground` sets another
opaque colour) take `WithWidth`, `WithHeight` or `WithScale` (above 0, at most 16); each keeps
the aspect ratio and the image is never cropped. An 800 by 400 diagram at scale 2 is 1600 by 800.
`WithTheme(RasterTheme.Light)` picks the light palette of an adaptive (`ThemeMode.Auto`) SVG; a
fixed-mode SVG has one palette.

The rasteriser reads what the library writes: rectangles, circles, ellipses, lines, polygons,
paths with every command, text with `tspan` lines, transforms, markers, symbols drawn with `use`,
opacity, dashes, round caps, the even-odd rule and the CSS variables of the theme. Gradients
paint their first stop; clip paths, masks and filters are not applied. Shapes fill with 4x4
supersampled coverage in fixed-point arithmetic, and curves and arcs use deterministic
trigonometry, so no platform library changes a pixel. PNG is 8-bit RGBA with the Sub filter and
zlib at a fixed level; JPEG is baseline 4:4:4 with an integer DCT.

## Animated GIF

`DiagramRaster.RenderGif(scene, steps, options)` samples the playback at logical times: frame
*n* shows the time *n* / frames-per-second. Each active step draws the travelled part of its
connector and a dot at its front, so repeated and concurrent steps all show. `GifOptions.Create`
takes the interval (1 to 60,000 ms) and the frame rate (1 to 50); 2 seconds at 10 frames per
second is 20 frames. `WithScale`, `WithBackground`, `WithTheme` and `PlayOnce` adjust it. The
palette is a fixed 6x7x6 colour cube with four greys, shared by every frame.

## Fonts and glyphs

Text is drawn with embedded subsets of Noto Sans Mono (regular and bold, for weights of 600 and
above) and Noto Sans Symbols 2, under the SIL Open Font License 1.1 (`licenses/fonts/` in the
package). A character that no font covers stops the export with `raster/missing-glyph`; nothing
is substituted.

## Limits

Sizes are validated before allocation: more than 40 million pixels gives `raster/pixel-limit`,
more than 600 GIF frames gives `raster/frame-limit`, and painting that needs too much work gives
`raster/work-limit`. An unreadable SVG gives `raster/unreadable-svg`.

## Player downloads

`DiagramRaster.RenderPlayer(scene)` writes the offline player with a format picker beside its
download button: SVG, PNG, JPEG and GIF. The images are made when the page is written, at the
diagram's natural size, so a download ignores zoom and pan and never changes the playback
position, speed or route.
