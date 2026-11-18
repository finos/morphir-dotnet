# Morphir.Knowledge.Okf documents

An OKF document has one original UTF-8 source and two views. The concrete
syntax tree exposes frontmatter, Markdown, tokens and trivia with exact source
spans. The abstract syntax tree exposes decoded YAML and structured Markdown,
including link definitions, tables and footnotes. Their source coordinates
are zero-based, half-open UTF-16 ranges in the full document.

```fsharp
open Morphir.Knowledge.Okf

match OkfDocument.Parse "---\ntype: Metric\n---\n# Orders\n" with
| OkfParseResult.ParsedDocument(document, _) ->
    Cursor.ast document
    |> Cursor.walk (fun cursor ->
        printfn "%s" (cursor.Node.ToString())
        VisitControl.Continue)
| OkfParseResult.UnreadableSource diagnostic ->
    eprintfn "%s" (diagnostic.ToString())
```

`Cursor.syntax` starts at the concrete root. From a cursor, inspect its children
or move to its parent and adjacent siblings. Walking stops at the end of the
starting subtree, and a visitor can skip a node's children or stop immediately.
`document.Source.Slice(span)` reads the original spelling and
`document.Reconstruct()` emits the exact UTF-8 document from its concrete leaves.

In C#, use `OkfDocument.Parse` and its result's `Match` method. The document
exposes `SyntaxCursor` and `AstCursor`, which have a `Walk` method and
nullable relations such as `FirstChildOrNull`.

Malformed YAML within an exact frontmatter fence yields a parsed document with a
diagnostic and an error region. Its Markdown body remains available. Invalid
UTF-8 yields an unreadable-source result.
