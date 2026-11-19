# Spec Delta

## Purpose

Let callers attach comments, links to text and code, links between diagrams, and arbitrary RDF statements to the elements of a Morphir diagram. The data is kept in a sidecar dataset or embedded in the rendered SVG, and is presented by the offline player.

For example, the edge from `Cart` to `Billing` in the diagram `https://example.org/diagrams/checkout` has the IRI `https://example.org/diagrams/checkout#edge=Cart/Billing/0`. A reviewer's comment targets that IRI. The rendered SVG carries the comment in its `<metadata>`, and the player pins it to the edge.

## ADDED Requirements

### Requirement: Every diagram element has a stable reference

Each node, edge, group, participant, message, note, frame and playback step SHALL have a reference that is unique within its diagram and depends only on the authored identifiers and on source order among elements of the same kind. Rendered SVG SHALL mark each element's outermost group with its reference.

#### Scenario: Unrelated edits keep a node's reference
- **WHEN** a node `Billing` exists in two revisions, and the second adds an unrelated node before it
- **THEN** `Billing` has the same reference in both revisions

#### Scenario: Parallel edges are distinguished
- **WHEN** a flowchart has two edges from `Cart` to `Billing`
- **THEN** they receive distinct references, ordered by their position in the source

#### Scenario: Rendered elements carry references
- **WHEN** a flowchart and a sequence diagram are rendered to SVG
- **THEN** every node, edge, group, participant, message, note and frame group has a reference attribute, and no two share a value

### Requirement: An element IRI joins the document IRI and the reference

When a caller supplies a diagram document identity, each element SHALL have an absolute IRI formed from the document IRI and the element reference as its fragment. The reference SHALL be parseable back into the element kind and key. Characters outside the IRI fragment grammar SHALL be percent-encoded. A document IRI that already has a fragment SHALL be rejected.

#### Scenario: Round trip from IRI to element
- **WHEN** a caller formats the IRI of the node `Billing` in `https://example.org/diagrams/checkout` and parses it back against the same diagram
- **THEN** the result identifies the node `Billing`

#### Scenario: Identifier with reserved characters
- **WHEN** a node identifier contains characters that are not allowed in an IRI fragment
- **THEN** its IRI percent-encodes them, and parsing restores the original identifier

#### Scenario: Document IRI with a fragment
- **WHEN** a caller supplies `https://example.org/diagrams/checkout#main` as the diagram document identity
- **THEN** the caller receives a typed failure, and no element IRIs are produced

### Requirement: A diagram selection targets elements of one revision

A diagram selection SHALL contain at least one element of one diagram document version, and SHALL be checked against that version's model. Unknown elements and empty selections SHALL be typed failures. Duplicate elements SHALL collapse, and elements SHALL be kept in a deterministic order. When no revision is supplied, it SHALL be derived from the source text.

#### Scenario: Select two nodes and the edge between them
- **WHEN** a caller selects `Billing`, `Cart` and the edge from `Cart` to `Billing`
- **THEN** the selection contains the three elements in a deterministic order, and carries the diagram's document version

#### Scenario: Unknown element
- **WHEN** a caller selects a node that the diagram does not contain
- **THEN** the caller receives a typed failure that names the missing reference

#### Scenario: Resolve against another revision
- **WHEN** a selection made on one revision is resolved against a different revision of the same document
- **THEN** the caller receives a version mismatch failure, and the selection is unchanged

#### Scenario: Derived revision
- **WHEN** a caller parses the same source text twice without supplying a revision
- **THEN** both parses have the same revision, and changing one character of the source changes it

### Requirement: Diagram selections are annotation targets and link endpoints

The shared annotation and link types SHALL accept a diagram selection as an annotation target, and as either endpoint of a link. A link SHALL be able to join a diagram selection to a text selection, an IRI, or another diagram selection.

#### Scenario: Comment on an edge
- **WHEN** a reviewer annotates the selection of one edge with a text body and provenance
- **THEN** a consumer can read the edge, the body, the author and the creation time from the annotation

#### Scenario: Link a node to a code snippet
- **WHEN** a caller links the node `Billing` to a text selection of a source file revision, with an "implemented by" predicate
- **THEN** traversal yields the node, the predicate, the source file identity and revision, and the selected range

#### Scenario: Link a step to another diagram
- **WHEN** a caller links a flowchart node to a selection of all participants in a sequence diagram
- **THEN** both endpoints resolve against their own documents and revisions

### Requirement: Annotations and links map to and from RDF

Comments SHALL map to W3C Web Annotation statements: target, body value, motivation, creator and creation time. Links SHALL map to a statement with the link's predicate. Revision-pinned and text endpoints SHALL map to Web Annotation specific resources and selectors. Reading the statements back SHALL reproduce equal values, with text positions converted between UTF-16 and code points.

#### Scenario: Comment round trip
- **WHEN** an annotation on a diagram selection is written to a dataset and read back
- **THEN** the result equals the original annotation, including its identity, body, provenance and every selected element

#### Scenario: Snippet range with astral characters
- **WHEN** a link targets a text range that follows a character outside the Basic Multilingual Plane
- **THEN** the RDF selector counts code points, and reading it back restores the original UTF-16 range

#### Scenario: Unknown vocabulary is preserved
- **WHEN** a dataset contains statements about diagram elements with predicates the library does not interpret
- **THEN** reading annotations ignores those statements, and every carriage of the dataset keeps them unchanged

### Requirement: A sidecar dataset and embedded metadata are interchangeable

A caller SHALL be able to render a diagram with a dataset read from any supported RDF format. When a dataset is supplied, the SVG SHALL embed it in its metadata element. Extracting the metadata from that SVG SHALL produce a dataset isomorphic to the supplied one, except for named graphs that the chosen embedding format cannot hold.

#### Scenario: Sidecar to SVG and back
- **WHEN** a diagram is rendered with a dataset read from a sidecar N-Quads file that uses only the default graph, and the metadata is extracted from the SVG
- **THEN** the extracted dataset is isomorphic to the sidecar dataset

#### Scenario: No dataset supplied
- **WHEN** a diagram is rendered without a dataset
- **THEN** the SVG has element references but no metadata element and no RDF namespace declarations

#### Scenario: Dataset without a document identity
- **WHEN** a caller supplies a dataset but no diagram document identity
- **THEN** rendering reports a typed error, and produces no output

### Requirement: Embedding uses RDF/XML by default and reports format loss

Embedded metadata SHALL use RDF/XML unless the caller selects N-Quads or N-Triples. When the dataset has named graphs that the selected format cannot hold, rendering SHALL report each affected graph as a typed error and produce no output, unless the caller explicitly allows dropping those graphs.

#### Scenario: Named graph with RDF/XML embedding
- **WHEN** a dataset with a named review graph is rendered with the default embedding
- **THEN** rendering reports the review graph and produces no output

#### Scenario: Named graph with N-Quads embedding
- **WHEN** the same dataset is rendered with N-Quads embedding
- **THEN** the SVG embeds every graph, and extraction restores the named review graph

#### Scenario: Caller allows dropping named graphs
- **WHEN** the same dataset is rendered with RDF/XML embedding and dropping explicitly allowed
- **THEN** the SVG embeds the default graph, and the result names the dropped graphs

### Requirement: Statements about missing elements produce diagnostics

When a dataset refers to an element IRI under the diagram's document IRI that does not identify an element of the rendered revision, rendering SHALL report a warning diagnostic naming that IRI. It SHALL still embed the statement unchanged.

#### Scenario: Comment on a removed node
- **WHEN** a dataset holds a comment on the node `Legacy`, and the diagram no longer contains `Legacy`
- **THEN** rendering succeeds with a warning that names the node's IRI, and the comment remains in the embedded metadata

### Requirement: Output with metadata is deterministic

Rendering the same source, document version, dataset and options SHALL produce byte-identical SVG and player output, independent of blank-node identities and the order in which statements were added.

#### Scenario: Same statements in another order
- **WHEN** two datasets contain the same statements added in different orders, with fresh blank-node identities
- **THEN** rendering the same diagram with either dataset produces identical bytes

### Requirement: The player presents comments and links on their targets

The offline player SHALL mark each element that has comments, links or other statements. Its panel SHALL list them with author, time and body where present, and SHALL highlight an annotation's elements when the annotation is focused. Bodies SHALL be shown as plain text. Only http and https targets SHALL be links. Everything SHALL work offline and by keyboard.

#### Scenario: Open a comment from its marker
- **WHEN** a viewer activates the marker on the commented edge by keyboard
- **THEN** the panel shows the comment's author, time and body, and the edge is highlighted

#### Scenario: Statement with unknown vocabulary
- **WHEN** a dataset states that a node has an owner, using a predicate the library does not interpret
- **THEN** the panel for that node lists the predicate IRI and its value

#### Scenario: Markup in a comment body
- **WHEN** a comment body contains `<script>` and HTML markup
- **THEN** the player shows the markup as literal text and runs no script

#### Scenario: Link with a non-web scheme
- **WHEN** a link targets a `javascript:` or `file:` IRI
- **THEN** the player shows the IRI as text, not as a navigable link
