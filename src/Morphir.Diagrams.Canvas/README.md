# Morphir.Diagrams.Canvas

Exports the layouts of [Morphir.Diagrams](../Morphir.Diagrams/README.md) as editable canvas files: draw.io
(`.drawio`), Excalidraw (`.excalidraw`) and Obsidian Excalidraw notes (`.excalidraw.md`). Each renderer is a
`DiagramRenderer`, so the library assesses a layout for it and lowers what it cannot draw natively before it writes.
The package depends only on Morphir.Diagrams, writes deterministic files and works under Native AOT and trimming.
