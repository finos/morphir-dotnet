# Morphir.Knowledge.Rdf

`Morphir.Knowledge.Rdf` models RDF 1.1 terms, graphs and datasets. It uses the shared identity, annotation and link types
from `Morphir.Knowledge`.

A dataset always has a default graph. Named graphs admit either an IRI or a
blank node as their name and can be explicitly empty. Triples have set membership
within each graph. A quad exposes the subject, predicate, object and graph as
individual fields, so the same triple can have a distinct quad in each graph.

A `GraphRef` identifies a graph in a specific `DatasetVersion`, including an
empty graph. A `StatementRef` identifies a quad in a specific version.
`DatasetSnapshot` resolves references only for the version it represents.
An `AuthoredClaim` adds a claim IRI and provenance to a statement reference so
independent claims about the same quad can carry their own annotations.
