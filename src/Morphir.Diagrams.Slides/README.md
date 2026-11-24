# Morphir.Diagrams.Slides

Places the layouts of [Morphir.Diagrams](../Morphir.Diagrams/README.md) on slides and writes them as presentations:
PowerPoint (`.pptx`), OpenDocument presentations (`.odp`), Google Slides `batchUpdate` requests (`.gslides.json`) and
reveal.js decks (`.reveal.html`). Each renderer is a `DiagramRenderer`, so the library assesses a layout for it and
lowers what it cannot draw before it writes. The package depends only on Morphir.Diagrams and Morphir.Diagrams.Raster,
makes no network requests, and works under Native AOT and trimming.
