# Morphir.Svg

Build and serialize immutable SVG scenes on .NET 10 and .NET 11. F# callers
can compose nested elements with computation expressions. C# callers can use
the same scene types through factories and immutable modifiers.

```fsharp
open Morphir.Svg

let badge =
    Svg.document [ Svg.attribute "viewBox" "0 0 100 40" ] {
        yield
            Svg.group [ Svg.attribute "transform" "translate(10 10)" ] {
                yield
                    Svg.element "text" [ Svg.attribute "y" "14" ] {
                        yield Svg.text "Orders & returns"
                    }
            }
    }

let markup = badge.Render()
```

```csharp
using Morphir.Svg;

var label = SvgNode.CreateElement("text", [SvgAttribute.Create("y", "24")],
    [SvgNode.CreateText("Orders & returns")]);
var document = SvgDocument.Create([SvgAttribute.Create("viewBox", "0 0 100 40")], [label]);
var markup = document.Render();
```

The serializer writes SVG elements and unqualified attributes in authored
order, escaping text and values as XML. Callers select the SVG elements and
attributes that belong in their output.

[Morphir.Diagrams](../Morphir.Diagrams/README.md) builds a scene with connector
bindings for SVG output and offline playback.
