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

## Namespaced content and metadata

A scene can hold foreign XML, such as RDF/XML. `SvgName.Qualified(prefix,
localName, namespaceUri)` names an element or attribute in another namespace.
Unqualified names keep their meaning: SVG for elements, no namespace for
attributes. The serializer declares each namespace once, at the outermost
element that uses it, with the caller's prefix.

The prefix `xmlns` is reserved, and the prefix `xml` names only the XML
namespace. Breaking either rule is `SvgNameError.ReservedPrefix`. An element
whose name and attributes bind one namespace to two prefixes, or one prefix to
two namespaces, is an `SvgSceneError` that names them. `SvgNode.TryCreateElement`
reports it for C#, `SvgNode.ValidateElement` returns it as a `Result` for F#, and
`SvgNode.CreateElement` throws.

`SvgDocument.WithMetadata` adds a `metadata` element, written as the root's first
child. It can hold elements, text, or both. Text is escaped, so reading it back
gives the same characters, including `<`, `&` and `]]>`.

```csharp
using Morphir.Svg;

const string rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
var description = SvgNode.CreateElement(
    SvgName.Qualified("rdf", "Description", rdf),
    [SvgAttribute.Create(SvgName.Qualified("rdf", "about", rdf), "https://example.org/diagrams/checkout")],
    []);
var document = SvgDocument.Create([SvgAttribute.Create("viewBox", "0 0 100 40")], [])
    .WithMetadata([SvgNode.CreateElement(SvgName.Qualified("rdf", "RDF", rdf), [], [description])]);
var markup = document.Render();
```

## Read metadata from SVG input

`SvgMetadataReader.TryRead` reads SVG bytes and returns the content of the root
`metadata` element: its child elements, still attached to the parsed document so
they keep their namespace bindings, and its text exactly as written.
`ElementsIn(namespaceUri)` selects the elements of one namespace. A document
without metadata gives `SvgMetadataResult.NotFound`, which is not an error.

SVG input can be hostile. The reader always prohibits DTD processing and has no
XML resolver, so a document type declaration is refused as `svg.doctype` before
any entity is resolved, and no file or network access happens. Input with more
characters than the limit, 16 million by default, is `svg.limit-exceeded`.
Malformed XML is `svg.malformed-xml`, and a root other than `svg` is
`svg.not-svg`. Errors carry a line and column when the XML reader knows them.

```csharp
using System;
using System.Text;
using Morphir.Svg;

var bytes = Encoding.UTF8.GetBytes(
    "<svg xmlns=\"http://www.w3.org/2000/svg\"><metadata>&lt;urn:s&gt; &lt;urn:p&gt; \"o\" .</metadata></svg>");

if (!SvgMetadataReader.TryRead(bytes, out var result, out var error))
{
    var where = error.TryGetLocation(out var location) ? $" at {location}" : "";
    throw new InvalidOperationException($"{error.Kind.Code}{where}: {error.Message}");
}

var text = result.Match(found: content => content.Text, notFound: () => "");
Console.WriteLine(text);
```

[Morphir.Diagrams](../Morphir.Diagrams/README.md) builds a scene with connector
bindings for SVG output and offline playback.
