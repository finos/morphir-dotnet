# Morphir.Knowledge

## Attach a comment to two passages

Bind the exact text to its identity and revision first. A document's IRI identifies it across storage path changes. The source provider's revision key identifies its immutable text within that document.

Offset values count UTF-16 code units. Spans have an inclusive start and an exclusive end. A selection checks bounds, sorts its ranges in source order, and retains them together as one target.

```fsharp
open System
open Morphir.Knowledge

let document = DocumentId.Create(Iri.Create "https://example.org/metrics/revenue")
let version = { Document = document; Revision = RevisionId.Create "review-1" }
let source = TextSnapshot.Create(version, "net revenue and gross revenue")
let span start finish = SourceSpan.Create(TextOffset.Create start, TextOffset.Create finish)

let review =
    TextSelection.TryCreate(source, [ span 0 11; span 16 29 ])
    |> Result.map (fun selection ->
        { Id = AnnotationId.Create(Iri.Create "urn:review:revenue:001")
          Target = selection
          Body = "Explain how these amounts differ."
          Provenance =
              { Author = Iri.Create "urn:reviewer:one"
                CreatedAt = DateTimeOffset.UnixEpoch } })
```

C# callers use the `TryCreate` overload with a selected value and a typed failure.

```csharp
using Morphir.Knowledge;

var document = DocumentId.Create(Iri.Create("https://example.org/metrics/revenue"));
var version = new DocumentVersion(document, RevisionId.Create("review-1"));
var source = TextSnapshot.Create(version, "net revenue and gross revenue");
var span = SourceSpan.Create(TextOffset.Create(0), TextOffset.Create(11));

if (TextSelection.TryCreate(source, [span], out var selection, out var error))
    Console.WriteLine(selection.Version);
else
    Console.WriteLine(error);
```

Call `TryRead` with the original snapshot to get the passages. A different document or revision returns a version mismatch.

## Link a phrase to a dictionary concept

Use the selection as the source, a relationship IRI as the predicate, and the dictionary concept IRI as the target. The example assumes the `selection` has already been created.

```fsharp
let mention: KnowledgeLink<TextSelection, Iri> =
    { Id = LinkId.Create(Iri.Create "urn:link:revenue:001")
      Source = selection
      Predicate = Iri.Create "https://example.org/vocabulary/mentions"
      Target = Iri.Create "https://example.org/dictionary/net-revenue" }
```

Annotation bodies and link endpoints are typed by the caller. Store them with their own identities and keep the original document revision available for readers.
